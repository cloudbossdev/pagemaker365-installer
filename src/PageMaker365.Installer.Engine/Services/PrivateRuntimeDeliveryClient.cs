using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

/// <summary>
/// Acquires already-approved runtime ZIPs from the PageMaker365 control plane.
/// It never accepts an artifact URL, SAS, redirect, storage locator, or direct
/// storage credential, and it deliberately does not invoke deployment.
/// </summary>
public sealed class PrivateRuntimeDeliveryClient : IDisposable
{
    public const string DeliveryReferenceHeader = "X-PM365-Runtime-Delivery-Ref";
    public const string DeliverySessionHeader = "X-PM365-Runtime-Delivery-Session";
    public const string PackageHashHeader = "X-PM365-Package-Hash";
    private const string RuntimeDeliveryReceiptResponseContract = "pagemaker365.runtime-delivery-receipt.v1";
    private const int BufferSize = 81_920;
    private static readonly HashSet<string> PrivateControlPlaneApiHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.pagemaker365.com",
        "api-staging.pagemaker365.com"
    };

    private readonly PrivateRuntimeDeliveryOptions _options;
    private readonly HttpClient _httpClient;

    public PrivateRuntimeDeliveryClient(PrivateRuntimeDeliveryOptions? options = null)
        : this(options, new HttpClientHandler { AllowAutoRedirect = false })
    {
    }

    // This constructor is restricted to the contract-test assembly. Production
    // callers cannot supply a transport that might forward session credentials
    // or delivery references on a redirect.
    internal PrivateRuntimeDeliveryClient(PrivateRuntimeDeliveryOptions? options, HttpMessageHandler transport)
    {
        _options = options ?? new PrivateRuntimeDeliveryOptions();
        ArgumentNullException.ThrowIfNull(transport);
        if (_options.Timeout <= TimeSpan.Zero || _options.Timeout > TimeSpan.FromMinutes(15))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Private runtime delivery timeout must be between zero and fifteen minutes.");
        }
        _httpClient = new HttpClient(transport, disposeHandler: true);
        _httpClient.Timeout = _options.Timeout;
    }

    public Task<PrivateRuntimeDeliveryResult> AcquireAsync(
        string packageJson,
        PackageTrustOptions trustOptions,
        OnboardingBootstrapSession onboardingSession,
        string outputRoot,
        string installerVersion,
        CancellationToken cancellationToken = default) =>
        AcquireAsync(
            new PrivateRuntimeDeliveryPackageService().ValidateJson(packageJson, trustOptions),
            onboardingSession,
            outputRoot,
            installerVersion,
            cancellationToken);

    internal async Task<PrivateRuntimeDeliveryResult> AcquireAsync(
        PrivateRuntimeDeliveryPackage package,
        OnboardingBootstrapSession onboardingSession,
        string outputRoot,
        string installerVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(onboardingSession);
        ValidateSessionBinding(package, onboardingSession);
        ValidateInstallerVersion(installerVersion);
        var artifactRoot = GetArtifactRoot(outputRoot);
        var controlPlaneBase = GetControlPlaneBaseUri(onboardingSession);
        var verified = new List<PrivateRuntimeDeliveryArtifactResult>();
        PrivateRuntimeDeliverySession? deliverySession = null;

        try
        {
            deliverySession = await CreateDeliverySessionAsync(package, onboardingSession, controlPlaneBase, cancellationToken);
            foreach (var artifactKind in new[] { "api", "portal" })
            {
                verified.Add(await AcquireArtifactAsync(package, deliverySession, onboardingSession, controlPlaneBase, artifactRoot, artifactKind, cancellationToken));
            }

            var receipt = CreateReceipt(package, deliverySession, installerVersion, "passed", verified, null);
            var receiptResult = await SubmitOrStageReceiptAsync(package, onboardingSession, controlPlaneBase, artifactRoot, receipt, cancellationToken);
            return new PrivateRuntimeDeliveryResult
            {
                Outcome = receiptResult.Submitted ? "passed" : "verified_receipt_pending",
                ReceiptStatus = receiptResult.Submitted ? "submitted" : "outbox_pending",
                DeliverySessionId = deliverySession.DeliverySessionId,
                ReceiptOutboxPath = receiptResult.OutboxPath,
                Artifacts = verified
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var error = ToSafeError(exception);
            if (deliverySession is null)
            {
                return new PrivateRuntimeDeliveryResult
                {
                    Outcome = "failed",
                    ReceiptStatus = "not_submitted",
                    SafeErrorCode = error.Code,
                    SafeErrorMessage = error.Message,
                    Artifacts = verified
                };
            }

            var receipt = CreateReceipt(package, deliverySession, installerVersion, "failed", verified, error);
            var receiptResult = await TrySubmitOrStageFailureReceiptAsync(package, onboardingSession, controlPlaneBase, artifactRoot, receipt, cancellationToken);
            return new PrivateRuntimeDeliveryResult
            {
                Outcome = "failed",
                ReceiptStatus = receiptResult.Submitted ? "submitted" : "outbox_pending",
                SafeErrorCode = error.Code,
                SafeErrorMessage = error.Message,
                DeliverySessionId = deliverySession.DeliverySessionId,
                ReceiptOutboxPath = receiptResult.OutboxPath,
                Artifacts = verified
            };
        }
    }

    private async Task<PrivateRuntimeDeliverySession> CreateDeliverySessionAsync(
        PrivateRuntimeDeliveryPackage package,
        OnboardingBootstrapSession onboardingSession,
        Uri controlPlaneBase,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildRelativeEndpoint(controlPlaneBase, PrivateRuntimeDeliveryPackage.SessionPathValue);
        var requestBody = new
        {
            contractVersion = PrivateRuntimeDeliveryPackage.AcquisitionContractVersionValue,
            packageHash = package.PackageHash,
            onboardingSessionId = package.OnboardingSessionId,
            deploymentExportId = package.DeploymentExportId,
            installerCapability = PrivateRuntimeDeliveryPackage.CapabilityValue,
            manifestSha256 = package.ManifestSha256
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };
        ApplyInstallerSessionAuthorization(request, onboardingSession);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        RejectRedirectOrOriginChange(response, endpoint);
        var body = await ReadBoundedJsonAsync(response, 32 * 1024, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PrivateRuntimeDeliveryException(MapPreHeaderFailure(response.StatusCode), "The runtime delivery session was not accepted.");
        }
        return ParseDeliverySession(body, package, DateTimeOffset.UtcNow);
    }

    private async Task<PrivateRuntimeDeliveryArtifactResult> AcquireArtifactAsync(
        PrivateRuntimeDeliveryPackage package,
        PrivateRuntimeDeliverySession deliverySession,
        OnboardingBootstrapSession onboardingSession,
        Uri controlPlaneBase,
        string artifactRoot,
        string artifactKind,
        CancellationToken cancellationToken)
    {
        var artifact = package.Artifact(artifactKind);
        var targetPath = GetContainedPath(artifactRoot, artifact.FileName);
        var partialPath = GetContainedPath(artifactRoot, $"{artifactKind}-{artifact.Sha256}.partial");
        var existingLength = GetSafePartialLength(partialPath, artifact.SizeBytes);
        var isRangeRequest = existingLength > 0;
        var endpoint = BuildRelativeEndpoint(controlPlaneBase, PrivateRuntimeDeliveryPackage.ArtifactPathValue.Replace("{artifactKind}", artifactKind, StringComparison.Ordinal));
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        ApplyInstallerSessionAuthorization(request, onboardingSession);
        request.Headers.TryAddWithoutValidation(DeliveryReferenceHeader, package.DeliveryReference(artifactKind));
        request.Headers.TryAddWithoutValidation(DeliverySessionHeader, deliverySession.DeliverySessionId);
        request.Headers.TryAddWithoutValidation(PackageHashHeader, package.PackageHash);
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{artifact.Sha256}\""));
        if (isRangeRequest)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, artifact.SizeBytes - 1);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        RejectRedirectOrOriginChange(response, endpoint);
        if (!response.IsSuccessStatusCode)
        {
            throw new PrivateRuntimeDeliveryException(MapPreHeaderFailure(response.StatusCode), "The requested runtime artifact was unavailable.");
        }

        ValidateArtifactResponse(response, artifact, existingLength, isRangeRequest);
        long bytesReceived;
        try
        {
            bytesReceived = await AppendResponseAsync(response, partialPath, existingLength, artifact.SizeBytes, cancellationToken);
        }
        catch
        {
            throw new PrivateRuntimeDeliveryException("runtime_artifact_incomplete_transfer", "The runtime artifact transfer did not complete.");
        }

        var finalLength = new FileInfo(partialPath).Length;
        if (finalLength != artifact.SizeBytes || bytesReceived != artifact.SizeBytes - existingLength)
        {
            throw new PrivateRuntimeDeliveryException("runtime_artifact_incomplete_transfer", "The runtime artifact transfer did not complete.");
        }

        var hash = await ComputeFileSha256Async(partialPath, cancellationToken);
        if (!FixedTimeEquals(hash, artifact.Sha256))
        {
            TryDelete(partialPath);
            throw new PrivateRuntimeDeliveryException("runtime_artifact_integrity_failed", "The runtime artifact integrity verification failed.");
        }

        try
        {
            PrivateRuntimeArchiveVerifier.Verify(partialPath, artifact, package);
        }
        catch (InvalidDataException)
        {
            TryDelete(partialPath);
            throw new PrivateRuntimeDeliveryException("runtime_artifact_archive_invalid", "The runtime artifact archive verification failed.");
        }

        File.Move(partialPath, targetPath, overwrite: true);
        return new PrivateRuntimeDeliveryArtifactResult
        {
            ArtifactKind = artifactKind,
            FileName = artifact.FileName,
            VerifiedPath = targetPath,
            Sha256 = artifact.Sha256,
            SizeBytes = artifact.SizeBytes,
            BytesReceived = bytesReceived,
            RangeRequestCount = isRangeRequest ? 1 : 0,
            VerificationStatus = "passed"
        };
    }

    private async Task<ReceiptSubmissionResult> SubmitOrStageReceiptAsync(
        PrivateRuntimeDeliveryPackage package,
        OnboardingBootstrapSession onboardingSession,
        Uri controlPlaneBase,
        string artifactRoot,
        PrivateRuntimeDeliveryReceipt receipt,
        CancellationToken cancellationToken)
    {
        try
        {
            await SubmitReceiptAsync(package, onboardingSession, controlPlaneBase, receipt, cancellationToken);
            return new ReceiptSubmissionResult(true, "");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new ReceiptSubmissionResult(false, await TryStageReceiptAsync(artifactRoot, receipt, cancellationToken));
        }
    }

    private async Task<ReceiptSubmissionResult> TrySubmitOrStageFailureReceiptAsync(
        PrivateRuntimeDeliveryPackage package,
        OnboardingBootstrapSession onboardingSession,
        Uri controlPlaneBase,
        string artifactRoot,
        PrivateRuntimeDeliveryReceipt receipt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SubmitOrStageReceiptAsync(package, onboardingSession, controlPlaneBase, artifactRoot, receipt, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A cancellation must not be converted into a network callback.
            return new ReceiptSubmissionResult(false, await TryStageReceiptAsync(artifactRoot, receipt, CancellationToken.None));
        }
    }

    private async Task SubmitReceiptAsync(
        PrivateRuntimeDeliveryPackage package,
        OnboardingBootstrapSession onboardingSession,
        Uri controlPlaneBase,
        PrivateRuntimeDeliveryReceipt receipt,
        CancellationToken cancellationToken)
    {
        ValidateReceipt(receipt);
        var endpoint = BuildRelativeEndpoint(controlPlaneBase, PrivateRuntimeDeliveryPackage.ReceiptPathValue);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(receipt), Encoding.UTF8, "application/json")
        };
        ApplyInstallerSessionAuthorization(request, onboardingSession);
        request.Headers.TryAddWithoutValidation(DeliverySessionHeader, receipt.DeliverySessionId);
        request.Headers.TryAddWithoutValidation(PackageHashHeader, package.PackageHash);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", receipt.IdempotencyKey);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        RejectRedirectOrOriginChange(response, endpoint);
        var body = await ReadBoundedJsonAsync(response, 16 * 1024, cancellationToken);
        if (!response.IsSuccessStatusCode || !IsAcceptedReceiptResponse(body, receipt))
        {
            throw new PrivateRuntimeDeliveryException("runtime_delivery_receipt_not_accepted", "The runtime delivery receipt was not accepted.");
        }
    }

    private static PrivateRuntimeDeliveryReceipt CreateReceipt(
        PrivateRuntimeDeliveryPackage package,
        PrivateRuntimeDeliverySession session,
        string installerVersion,
        string outcome,
        IReadOnlyCollection<PrivateRuntimeDeliveryArtifactResult> artifacts,
        PrivateRuntimeDeliverySafeError? error)
    {
        var receiptArtifacts = new[] { "api", "portal" }.Select(kind =>
        {
            var matched = artifacts.FirstOrDefault(item => item.ArtifactKind.Equals(kind, StringComparison.Ordinal));
            var contract = package.Artifact(kind);
            return new PrivateRuntimeDeliveryReceiptArtifact
            {
                ArtifactKind = kind,
                Sha256 = contract.Sha256,
                SizeBytes = contract.SizeBytes,
                BytesReceived = matched?.BytesReceived ?? 0,
                RangeRequestCount = matched?.RangeRequestCount ?? 0,
                VerificationStatus = matched?.VerificationStatus ?? "not_verified"
            };
        }).ToArray();
        var idempotencySource = $"{session.DeliverySessionId}:{package.PackageHash}:{outcome}";
        var idempotencyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencySource))).ToLowerInvariant();
        var receipt = new PrivateRuntimeDeliveryReceipt
        {
            DeliverySessionId = session.DeliverySessionId,
            PackageHash = package.PackageHash,
            ReleaseId = package.ReleaseId,
            EventId = Guid.NewGuid().ToString("D"),
            IdempotencyKey = $"runtime-delivery:{idempotencyHash}",
            Outcome = outcome,
            OccurredAt = DateTimeOffset.UtcNow,
            InstallerVersion = installerVersion,
            Artifacts = receiptArtifacts,
            SafeError = error
        };
        ValidateReceipt(receipt);
        return receipt;
    }

    private static PrivateRuntimeDeliverySafeError ToSafeError(Exception exception) => exception switch
    {
        PrivateRuntimeDeliveryException privateError => new PrivateRuntimeDeliverySafeError { Code = privateError.Code, Message = privateError.SafeMessage },
        HttpRequestException => new PrivateRuntimeDeliverySafeError { Code = "runtime_delivery_transport_failed", Message = "The runtime delivery request could not be completed." },
        IOException => new PrivateRuntimeDeliverySafeError { Code = "runtime_delivery_local_io_failed", Message = "The runtime delivery files could not be handled safely." },
        _ => new PrivateRuntimeDeliverySafeError { Code = "runtime_delivery_failed", Message = "The runtime delivery could not be completed." }
    };

    private static async Task<long> AppendResponseAsync(HttpResponseMessage response, string partialPath, long existingLength, long expectedLength, CancellationToken cancellationToken)
    {
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            partialPath,
            existingLength == 0 ? FileMode.Create : FileMode.Append,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
        var buffer = new byte[BufferSize];
        var received = 0L;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            received += read;
            if (existingLength + received > expectedLength)
            {
                throw new InvalidDataException("Artifact stream exceeded the signed length.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await output.FlushAsync(cancellationToken);
        return received;
    }

    private static void ValidateArtifactResponse(HttpResponseMessage response, PrivateRuntimeArtifact artifact, long existingLength, bool isRangeRequest)
    {
        if (response.Headers.Location is not null) throw new PrivateRuntimeDeliveryException("runtime_artifact_redirect_rejected", "The runtime artifact response was not a direct control-plane stream.");
        var cache = response.Headers.CacheControl;
        if (cache is null || !cache.Private || !cache.NoStore) throw new PrivateRuntimeDeliveryException("runtime_artifact_cache_policy_invalid", "The runtime artifact response was not private.");
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/zip", StringComparison.OrdinalIgnoreCase)) throw new PrivateRuntimeDeliveryException("runtime_artifact_content_type_invalid", "The runtime artifact response was not a ZIP stream.");
        if (response.Headers.ETag is not { IsWeak: false } etag || !IsExpectedEtag(etag.Tag, artifact.Sha256)) throw new PrivateRuntimeDeliveryException("runtime_artifact_etag_invalid", "The runtime artifact identity did not match the signed package.");

        var expectedBytes = artifact.SizeBytes - existingLength;
        if (response.Content.Headers.ContentLength != expectedBytes) throw new PrivateRuntimeDeliveryException("runtime_artifact_length_invalid", "The runtime artifact response length did not match the signed package.");
        if (!isRangeRequest && response.StatusCode != HttpStatusCode.OK) throw new PrivateRuntimeDeliveryException("runtime_artifact_status_invalid", "The runtime artifact response did not match the requested stream.");
        if (isRangeRequest)
        {
            var range = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent || range is null ||
                !range.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase) || range.From != existingLength ||
                range.To != artifact.SizeBytes - 1 || range.Length != artifact.SizeBytes)
            {
                throw new PrivateRuntimeDeliveryException("runtime_artifact_range_invalid", "The runtime artifact range did not match the signed package.");
            }
        }
    }

    private static bool IsExpectedEtag(string? tag, string sha256) => tag is not null &&
        (tag.Equals($"\"{sha256}\"", StringComparison.Ordinal) || tag.Equals($"\"sha256:{sha256}\"", StringComparison.Ordinal));

    private static PrivateRuntimeDeliverySession ParseDeliverySession(string body, PrivateRuntimeDeliveryPackage package, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8 });
            var root = document.RootElement;
            RequireExactProperties(root, ["contractVersion", "deliverySessionId", "expiresAt", "artifactReferences"]);
            RequireString(root, "contractVersion", PrivateRuntimeDeliveryPackage.AcquisitionContractVersionValue);
            var deliverySessionId = RequireString(root, "deliverySessionId");
            if (!Regex.IsMatch(deliverySessionId, "^rds_[A-Za-z0-9_-]{24,96}$", RegexOptions.CultureInvariant)) throw new InvalidDataException();
            var expiresAt = RequireDate(root, "expiresAt");
            if (expiresAt <= now || expiresAt > package.ExpiresAt) throw new InvalidDataException();
            var references = RequireObject(root, "artifactReferences");
            RequireExactProperties(references, ["api", "portal"]);
            if (!FixedTimeEquals(RequireString(references, "api"), package.ApiDeliveryReference) || !FixedTimeEquals(RequireString(references, "portal"), package.PortalDeliveryReference)) throw new InvalidDataException();
            return new PrivateRuntimeDeliverySession { DeliverySessionId = deliverySessionId, ExpiresAt = expiresAt };
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or FormatException)
        {
            throw new PrivateRuntimeDeliveryException("runtime_delivery_session_invalid", "The runtime delivery session response did not match the signed package.");
        }
    }

    private static bool IsAcceptedReceiptResponse(string body, PrivateRuntimeDeliveryReceipt receipt)
    {
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8 });
            var root = document.RootElement;
            RequireExactProperties(root, ["contractVersion", "status", "deliverySessionId", "idempotencyKey"]);
            return RequireString(root, "contractVersion", RuntimeDeliveryReceiptResponseContract) == RuntimeDeliveryReceiptResponseContract &&
                RequireString(root, "status") == "accepted" &&
                FixedTimeEquals(RequireString(root, "deliverySessionId"), receipt.DeliverySessionId) &&
                FixedTimeEquals(RequireString(root, "idempotencyKey"), receipt.IdempotencyKey);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return false;
        }
    }

    private static async Task<string> ReadBoundedJsonAsync(HttpResponseMessage response, int maximumBytes, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > int.MaxValue || response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new PrivateRuntimeDeliveryException("runtime_delivery_response_too_large", "The runtime delivery response was not accepted.");
        }
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (response.IsSuccessStatusCode && !string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            throw new PrivateRuntimeDeliveryException("runtime_delivery_response_invalid", "The runtime delivery response was not accepted.");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            if (memory.Length + read > maximumBytes) throw new PrivateRuntimeDeliveryException("runtime_delivery_response_too_large", "The runtime delivery response was not accepted.");
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return new UTF8Encoding(false, true).GetString(memory.ToArray());
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static long GetSafePartialLength(string partialPath, long maximumLength)
    {
        if (!File.Exists(partialPath)) return 0;
        var length = new FileInfo(partialPath).Length;
        if (length <= 0 || length >= maximumLength)
        {
            TryDelete(partialPath);
            return 0;
        }
        return length;
    }

    private static void RejectRedirectOrOriginChange(HttpResponseMessage response, Uri expectedEndpoint)
    {
        // Custom transports are permitted for local contract tests and may not
        // populate Response.RequestMessage. A populated response URI must
        // still be the exact control-plane endpoint, which catches any
        // automatic redirect before bytes or JSON are accepted.
        if ((int)response.StatusCode is >= 300 and <= 399 || response.Headers.Location is not null ||
            (response.RequestMessage?.RequestUri is Uri actualEndpoint && !SameEndpoint(actualEndpoint, expectedEndpoint)))
        {
            throw new PrivateRuntimeDeliveryException("runtime_delivery_redirect_rejected", "The runtime delivery request was not served by the control plane.");
        }
    }

    private static bool SameEndpoint(Uri left, Uri right) => left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase) && left.Port == right.Port && left.AbsolutePath.Equals(right.AbsolutePath, StringComparison.Ordinal) && string.IsNullOrEmpty(left.Query) && string.IsNullOrEmpty(left.Fragment);

    private Uri GetControlPlaneBaseUri(OnboardingBootstrapSession session)
    {
        var value = string.IsNullOrWhiteSpace(session.ApiBaseUrl) ? _options.ApiBaseUrl : session.ApiBaseUrl;
        var baseUri = TrustedPageMaker365EndpointPolicy.ValidateBaseUrl(value, "Private runtime delivery API base URL");
        if (!PrivateControlPlaneApiHosts.Contains(baseUri.Host) && !TrustedPageMaker365EndpointPolicy.IsLocalHost(baseUri.Host))
        {
            throw new InvalidDataException("Private runtime delivery must use a PageMaker365 control-plane API endpoint.");
        }
        return new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
    }

    private static Uri BuildRelativeEndpoint(Uri baseUri, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/", StringComparison.Ordinal) || path.Contains('?') || path.Contains('#') || path.Contains("//", StringComparison.Ordinal) || Uri.TryCreate(path, UriKind.Absolute, out _))
        {
            throw new InvalidDataException("Private runtime delivery endpoint must be a fixed relative path.");
        }
        return new Uri(baseUri, path.TrimStart('/'));
    }

    private void ApplyInstallerSessionAuthorization(HttpRequestMessage request, OnboardingBootstrapSession session)
    {
        var apiKey = Environment.GetEnvironmentVariable(_options.ApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("X-PM365-Onboarding-Session", session.SessionId);
        request.Headers.TryAddWithoutValidation("X-PM365-Onboarding-Code", session.OneTimeCode);
    }

    private static void ValidateSessionBinding(PrivateRuntimeDeliveryPackage package, OnboardingBootstrapSession onboardingSession)
    {
        if (string.IsNullOrWhiteSpace(onboardingSession.SessionId) || string.IsNullOrWhiteSpace(onboardingSession.OneTimeCode) ||
            !FixedTimeEquals(package.OnboardingSessionId, onboardingSession.SessionId))
        {
            throw new InvalidDataException("Private runtime delivery package does not match the active onboarding session.");
        }
        if (package.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidDataException("Private runtime delivery package has expired.");
    }

    private static void ValidateInstallerVersion(string installerVersion)
    {
        if (string.IsNullOrWhiteSpace(installerVersion) || !Regex.IsMatch(installerVersion, "^[A-Za-z0-9._+-]{1,64}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException("Installer version is invalid.");
        }
    }

    private static void ValidateReceipt(PrivateRuntimeDeliveryReceipt receipt)
    {
        if (receipt.ContractVersion != PrivateRuntimeDeliveryReceipt.ContractVersionValue ||
            !Regex.IsMatch(receipt.DeliverySessionId, "^rds_[A-Za-z0-9_-]{24,96}$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(receipt.PackageHash, "^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(receipt.ReleaseId, "^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$", RegexOptions.CultureInvariant) ||
            !Guid.TryParseExact(receipt.EventId, "D", out _) ||
            !Regex.IsMatch(receipt.IdempotencyKey, "^runtime-delivery:[0-9a-f]{64}$", RegexOptions.CultureInvariant) ||
            receipt.Outcome is not ("passed" or "failed") || receipt.OccurredAt.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(receipt.InstallerVersion) || receipt.Artifacts.Count != 2)
        {
            throw new InvalidDataException("Private runtime delivery receipt is invalid.");
        }
        foreach (var artifact in receipt.Artifacts)
        {
            if (artifact.ArtifactKind is not ("api" or "portal") || !Regex.IsMatch(artifact.Sha256, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant) || artifact.SizeBytes < 1 || artifact.BytesReceived < 0 || artifact.BytesReceived > artifact.SizeBytes || artifact.RangeRequestCount < 0 || artifact.VerificationStatus is not ("passed" or "not_verified"))
            {
                throw new InvalidDataException("Private runtime delivery receipt artifact is invalid.");
            }
        }
        if (receipt.SafeError is not null && (!Regex.IsMatch(receipt.SafeError.Code, "^[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant) || string.IsNullOrWhiteSpace(receipt.SafeError.Message) || receipt.SafeError.Message.Length > 240 || receipt.SafeError.Message.Contains("http", StringComparison.OrdinalIgnoreCase) || receipt.SafeError.Message.Contains("token", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Private runtime delivery receipt error is invalid.");
        }
        if (receipt.Outcome == "passed" && receipt.SafeError is not null || receipt.Outcome == "failed" && receipt.SafeError is null) throw new InvalidDataException("Private runtime delivery receipt outcome is invalid.");
    }

    private static async Task<string> StageReceiptAsync(string artifactRoot, PrivateRuntimeDeliveryReceipt receipt, CancellationToken cancellationToken)
    {
        ValidateReceipt(receipt);
        var outboxRoot = GetContainedPath(artifactRoot, "receipt-outbox");
        Directory.CreateDirectory(outboxRoot);
        var path = GetContainedPath(outboxRoot, $"{receipt.EventId}.json");
        var temporaryPath = path + ".tmp";
        var json = JsonSerializer.Serialize(receipt);
        await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
        return path;
    }

    private static async Task<string> TryStageReceiptAsync(string artifactRoot, PrivateRuntimeDeliveryReceipt receipt, CancellationToken cancellationToken)
    {
        try
        {
            return await StageReceiptAsync(artifactRoot, receipt, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Do not convert an already-sanitized delivery result into a raw
            // local-path or file-system error when an outbox cannot be staged.
            return "";
        }
    }

    private static string GetArtifactRoot(string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot)) throw new ArgumentException("A local output root is required.", nameof(outputRoot));
        var root = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(root);
        var artifactRoot = Path.Combine(root, "runtime-acquisition");
        Directory.CreateDirectory(artifactRoot);
        return artifactRoot;
    }

    private static string GetContainedPath(string root, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("Private runtime delivery local path is invalid.");
        var rootWithSeparator = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(rootWithSeparator, fileName));
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Private runtime delivery local path escaped its output root.");
        return path;
    }

    private static string MapPreHeaderFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "runtime_delivery_unauthorized",
        HttpStatusCode.NotFound => "runtime_delivery_unavailable",
        HttpStatusCode.Gone => "runtime_delivery_expired_or_revoked",
        HttpStatusCode.PreconditionFailed => "runtime_delivery_binding_failed",
        HttpStatusCode.RequestedRangeNotSatisfiable => "runtime_artifact_range_invalid",
        HttpStatusCode.TooManyRequests => "runtime_delivery_rate_limited",
        _ when (int)statusCode >= 500 => "runtime_delivery_service_unavailable",
        _ => "runtime_delivery_rejected"
    };

    private static void RequireExactProperties(JsonElement element, IReadOnlyCollection<string> fields)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(fields.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal)) throw new InvalidDataException();
    }

    private static JsonElement RequireObject(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        return value;
    }

    private static string RequireString(JsonElement parent, string property, string? expected = null)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw new InvalidDataException();
        var text = value.GetString()!;
        if (expected is not null && !text.Equals(expected, StringComparison.Ordinal)) throw new InvalidDataException();
        return text;
    }

    private static DateTimeOffset RequireDate(JsonElement parent, string property)
    {
        var text = RequireString(parent, property);
        if (!Regex.IsMatch(text, "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3}Z$", RegexOptions.CultureInvariant) || !DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var date)) throw new InvalidDataException();
        return date;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed record ReceiptSubmissionResult(bool Submitted, string OutboxPath);
}

internal sealed class PrivateRuntimeDeliveryException : InvalidOperationException
{
    public PrivateRuntimeDeliveryException(string code, string safeMessage) : base(safeMessage)
    {
        Code = code;
        SafeMessage = safeMessage;
    }

    public string Code { get; }
    public string SafeMessage { get; }
}

internal static class PrivateRuntimeArchiveVerifier
{
    private static readonly string[] ProvenanceFields = ["schemaVersion", "product", "artifactKind", "releaseId", "runtimeVersion", "sourceRepository", "sourceCommit", "dependencyLockSha256", "startupCommand"];

    public static void Verify(string archivePath, PrivateRuntimeArtifact artifact, PrivateRuntimeDeliveryPackage package)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count is < 1 or > 100_000) throw new InvalidDataException();
            long totalLength = 0;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                if (string.IsNullOrEmpty(name) || name.Contains('\\') || name.StartsWith("/", StringComparison.Ordinal) || name.Contains("//", StringComparison.Ordinal) || name.Split('/').Any(segment => segment is "." or "..") || Path.IsPathRooted(name) || !names.Add(name)) throw new InvalidDataException();
                var unixFileType = ((uint)entry.ExternalAttributes >> 16) & 0xF000;
                if (unixFileType == 0xA000 || ((FileAttributes)entry.ExternalAttributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
                totalLength += entry.Length;
                if (totalLength > 1_073_741_824) throw new InvalidDataException();
            }

            var required = artifact.ArtifactKind == "api"
                ? new[] { "dist/index.js", "package.json", ".pm365/provenance.json" }
                : new[] { "index.html", "auth-redirect.html", ".pm365/start-portal-runtime.mjs", ".pm365/generate-web-runtime-config.mjs", ".pm365/provenance.json" };
            if (required.Any(requiredEntry => !names.Contains(requiredEntry))) throw new InvalidDataException();
            var provenanceEntry = archive.GetEntry(".pm365/provenance.json") ?? throw new InvalidDataException();
            if (provenanceEntry.Length is < 1 or > 65_536) throw new InvalidDataException();
            using var reader = new StreamReader(provenanceEntry.Open(), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false, leaveOpen: false);
            var provenanceJson = reader.ReadToEnd();
            using var provenance = JsonDocument.Parse(provenanceJson, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8 });
            var root = provenance.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(ProvenanceFields.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal)) throw new InvalidDataException();
            RequireProvenance(root, "schemaVersion", "pagemaker365.runtime-provenance.v1");
            RequireProvenance(root, "product", "PageMaker365");
            RequireProvenance(root, "artifactKind", artifact.ArtifactKind);
            RequireProvenance(root, "releaseId", package.ReleaseId);
            RequireProvenance(root, "runtimeVersion", package.RuntimeVersion);
            RequireProvenance(root, "sourceRepository", "cloudbossdev/spo-ui");
            RequireProvenance(root, "sourceCommit", package.SourceCommit);
            RequireProvenance(root, "startupCommand", artifact.StartupCommand);
            if (!root.TryGetProperty("dependencyLockSha256", out var lockHash) || lockHash.ValueKind != JsonValueKind.String || !Regex.IsMatch(lockHash.GetString() ?? "", "^[0-9a-f]{64}$", RegexOptions.CultureInvariant)) throw new InvalidDataException();

            if (artifact.ArtifactKind == "portal")
            {
                if (names.Contains("staticwebapp.config.json")) throw new InvalidDataException();
                var index = archive.GetEntry("index.html") ?? throw new InvalidDataException();
                if (index.Length > 5_242_880) throw new InvalidDataException();
                using var indexReader = new StreamReader(index.Open(), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false, leaveOpen: false);
                var content = indexReader.ReadToEnd();
                if (!content.Contains("PageMaker365", StringComparison.OrdinalIgnoreCase) || !MatchesPortalReleaseMarker(content, package.ReleaseId)) throw new InvalidDataException();
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or JsonException)
        {
            throw new InvalidDataException("Runtime archive failed its immutable provenance checks.", exception);
        }
    }

    private static void RequireProvenance(JsonElement root, string property, string expected)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || !string.Equals(value.GetString(), expected, StringComparison.Ordinal)) throw new InvalidDataException();
    }

    private static bool MatchesPortalReleaseMarker(string content, string releaseId)
    {
        var options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;
        var first = Regex.Match(content, "<meta\\s+[^>]*\\bname\\s*=\\s*[\"']pm365-release-id[\"'][^>]*\\bcontent\\s*=\\s*[\"'](?<release>[^\"']*)[\"']", options);
        if (first.Success) return first.Groups["release"].Value.Equals(releaseId, StringComparison.Ordinal);
        var second = Regex.Match(content, "<meta\\s+[^>]*\\bcontent\\s*=\\s*[\"'](?<release>[^\"']*)[\"'][^>]*\\bname\\s*=\\s*[\"']pm365-release-id[\"']", options);
        return second.Success && second.Groups["release"].Value.Equals(releaseId, StringComparison.Ordinal);
    }
}
