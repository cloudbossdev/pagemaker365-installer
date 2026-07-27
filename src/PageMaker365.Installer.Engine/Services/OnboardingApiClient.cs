using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class OnboardingApiClient : IOnboardingApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions PrettyJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly CustomerConfigService ConfigService = new();
    private static readonly RedactionService EvidenceRedactionService = new();

    private readonly OnboardingApiOptions _options;
    private readonly HttpClient _httpClient;
    private readonly PackageTrustKeyResolver _packageTrustKeyResolver;
    private readonly MockOnboardingApiClient _mockClient = new();

    public OnboardingApiClient(OnboardingApiOptions options, HttpClient? httpClient = null)
    {
        _options = options;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _packageTrustKeyResolver = new PackageTrustKeyResolver(_httpClient);
    }

    public string ConnectionLabel => _options.UseMock
        ? _mockClient.ConnectionLabel
        : $"Portal onboarding API: {_options.ApiBaseUrl}";

    public async Task<OnboardingSessionConnection> ConnectAsync(
        OnboardingBootstrapSession session,
        CancellationToken cancellationToken = default)
    {
        if (_options.UseMock)
        {
            return await _mockClient.ConnectAsync(session, cancellationToken);
        }

        try
        {
            var request = new OnboardingSessionConnectRequest
            {
                SessionId = session.SessionId,
                OneTimeCode = session.OneTimeCode,
                RequestedBy = session.RequestedBy,
                CustomerName = session.CustomerName
            };
            var response = await PostJsonAsync<OnboardingSessionConnectRequest, OnboardingSessionConnection>(
                "connect",
                _options.ConnectEndpoint(session),
                request,
                session,
                cancellationToken,
                root => EnsureRequiredJsonFields(
                    "connect",
                    _options.ConnectEndpoint(session),
                    root,
                    "status",
                    "sessionId",
                    "correlationId"));
            EnsureSessionMatches("connect", _options.ConnectEndpoint(session), session, response.SessionId);
            return response;
        }
        catch (Exception exception) when (ShouldFallback(exception))
        {
            var response = await _mockClient.ConnectAsync(session, cancellationToken);
            response.Message = $"Portal onboarding API connect failed; using local mock fallback. {response.Message}";
            return response;
        }
    }

    public async Task<OnboardingDiscoverySubmission> SubmitDiscoveryAsync(
        OnboardingBootstrapSession session,
        TenantDiscoveryResult discovery,
        CancellationToken cancellationToken = default)
    {
        if (_options.UseMock)
        {
            return await _mockClient.SubmitDiscoveryAsync(session, discovery, cancellationToken);
        }

        try
        {
            var request = new OnboardingDiscoverySubmitRequest
            {
                SessionId = session.SessionId,
                OneTimeCode = session.OneTimeCode,
                Discovery = discovery
            };
            var response = await PostJsonAsync<OnboardingDiscoverySubmitRequest, OnboardingDiscoverySubmission>(
                "discovery sync",
                _options.DiscoveryEndpoint(session),
                request,
                session,
                cancellationToken,
                root => EnsureRequiredJsonFields(
                    "discovery sync",
                    _options.DiscoveryEndpoint(session),
                    root,
                    "status",
                    "sessionId",
                    "discoveryId",
                    "correlationId"));
            EnsureSessionMatches("discovery sync", _options.DiscoveryEndpoint(session), session, response.SessionId);
            return response;
        }
        catch (Exception exception) when (ShouldFallback(exception))
        {
            var response = await _mockClient.SubmitDiscoveryAsync(session, discovery, cancellationToken);
            response.Message = $"Portal onboarding API discovery sync failed; using local mock fallback. {response.Message}";
            return response;
        }
    }

    public async Task<OnboardingPortalStatus> GetOnboardingStatusAsync(
        OnboardingBootstrapSession session,
        TenantDiscoveryResult? discovery,
        CustomerInstallConfig? config,
        CancellationToken cancellationToken = default)
    {
        if (_options.UseMock)
        {
            return await _mockClient.GetOnboardingStatusAsync(session, discovery, config, cancellationToken);
        }

        try
        {
            var request = new OnboardingStatusRequest
            {
                SessionId = session.SessionId,
                OneTimeCode = session.OneTimeCode,
                Discovery = discovery,
                LoadedPackage = CreatePackageContext(config)
            };
            var response = await PostJsonAsync<OnboardingStatusRequest, OnboardingPortalStatus>(
                "status check",
                _options.StatusEndpoint(session),
                request,
                session,
                cancellationToken,
                root => ValidateStatusJson(_options.StatusEndpoint(session), root));
            EnsureSessionMatches("status check", _options.StatusEndpoint(session), session, response.SessionId);
            return response;
        }
        catch (Exception exception) when (ShouldFallback(exception))
        {
            var response = await _mockClient.GetOnboardingStatusAsync(session, discovery, config, cancellationToken);
            response.Message = $"Portal onboarding API status check failed; using local mock fallback. {response.Message}";
            return response;
        }
    }

    public async Task<string> SaveStatusAsync(
        OnboardingPortalStatus status,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        if (_options.UseMock)
        {
            return await _mockClient.SaveStatusAsync(status, outputRoot, cancellationToken);
        }

        var directory = Path.Combine(outputRoot, "onboarding", status.SessionId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "portal-status.json");
        var json = JsonSerializer.Serialize(status, PrettyJsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    public async Task<InstallerEvidenceReceipt> SubmitEvidenceAsync(
        OnboardingBootstrapSession session,
        InstallerEvidenceEvent evidence,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (_options.UseMock)
        {
            return await _mockClient.SubmitEvidenceAsync(session, evidence, idempotencyKey, cancellationToken);
        }

        ValidateEvidenceRequest(session, evidence, idempotencyKey);
        var endpoint = _options.EvidenceEndpoint(session);
        const int maxAttempts = 4;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var receipt = await PostJsonAsync<InstallerEvidenceEvent, InstallerEvidenceReceipt>(
                    "installer evidence",
                    endpoint,
                    evidence,
                    session,
                    cancellationToken,
                    root => EnsureRequiredJsonFields(
                        "installer evidence",
                        endpoint,
                        root,
                        "status",
                        "sessionId",
                        "eventId",
                        "eventType",
                        "sequence",
                        "correlationId"),
                    request => request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey));
                EnsureSessionMatches("installer evidence", endpoint, session, receipt.SessionId);
                var submittedAttemptId = EvidenceAttemptId(evidence);
                var receivedAttemptId = First(receipt.AttemptId, receipt.RemovalAttemptId, receipt.InstallAttemptId);
                var lifecycle = EvidenceLifecycle(evidence);
                if (!receipt.Status.Equals("Accepted", StringComparison.Ordinal) ||
                    !receipt.EventId.Equals(evidence.EventId, StringComparison.Ordinal) ||
                    !receipt.EventType.Equals(evidence.EventType, StringComparison.Ordinal) ||
                    receipt.Sequence != evidence.Sequence ||
                    string.IsNullOrWhiteSpace(receivedAttemptId) ||
                    !receivedAttemptId.Equals(submittedAttemptId, StringComparison.Ordinal) ||
                    (!string.IsNullOrWhiteSpace(receipt.Lifecycle) &&
                        !receipt.Lifecycle.Equals(lifecycle, StringComparison.Ordinal)) ||
                    (lifecycle.Equals(InstallerEvidenceLifecycle.Removal, StringComparison.Ordinal) &&
                        !IsMatchingRemovalReceipt(receipt, evidence, submittedAttemptId)))
                {
                    throw new OnboardingApiException(
                        "Portal onboarding API installer evidence response did not match the submitted event.",
                        endpoint);
                }

                return receipt;
            }
            catch (Exception exception) when (
                attempt < maxAttempts && IsEvidenceRetryable(exception, cancellationToken))
            {
                var backoffMs = Math.Min(2000, 200 * (1 << (attempt - 1))) + Random.Shared.Next(25, 126);
                await Task.Delay(backoffMs, cancellationToken);
            }
        }
    }

    public async Task<OnboardingPackageDownloadResult> DownloadPackageAsync(
        OnboardingBootstrapSession session,
        OnboardingPackageReadiness readiness,
        string workspaceRoot,
        TenantDiscoveryResult? discovery = null,
        CancellationToken cancellationToken = default)
    {
        if (_options.UseMock)
        {
            return await _mockClient.DownloadPackageAsync(session, readiness, workspaceRoot, discovery, cancellationToken);
        }

        try
        {
            return await DownloadPackageFromPortalAsync(session, readiness, workspaceRoot, discovery, cancellationToken);
        }
        catch (Exception exception) when (ShouldFallback(exception))
        {
            var response = await _mockClient.DownloadPackageAsync(session, readiness, workspaceRoot, discovery, cancellationToken);
            response.Message = $"Portal onboarding API package download failed; using local mock fallback. {response.Message}";
            return response;
        }
    }

    private async Task<OnboardingPackageDownloadResult> DownloadPackageFromPortalAsync(
        OnboardingBootstrapSession session,
        OnboardingPackageReadiness readiness,
        string workspaceRoot,
        TenantDiscoveryResult? discovery,
        CancellationToken cancellationToken)
    {
        if (!IsDownloadablePackageReadiness(readiness.Status))
        {
            return new OnboardingPackageDownloadResult
            {
                Status = "NotReady",
                SessionId = session.SessionId,
                PackageVersion = readiness.PackageVersion,
                Message = "Package is not ready for download."
            };
        }

        var endpoint = _options.PackageEndpoint(session, readiness.PackageDownloadUrl);
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
            ApplyAuthorization(httpRequest, session);

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var correlationId = GetCorrelationId(response, body);
            if (!response.IsSuccessStatusCode)
            {
                var exception = CreateApiException("package download", endpoint, response.StatusCode, body, correlationId);
                if (attempt < maxAttempts && IsTransientStatusCode(response.StatusCode))
                {
                    await Task.Delay(GetPackageRetryDelay(response, attempt), cancellationToken);
                    continue;
                }

                throw exception;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!IsJsonMediaType(mediaType))
            {
                throw new OnboardingApiException(
                    $"Portal package download returned unsupported content type '{mediaType ?? "none"}'. Expected application/json.",
                    endpoint,
                    response.StatusCode,
                    correlationId);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                throw new OnboardingApiException(
                    "Portal package download returned an empty package.",
                    endpoint,
                    response.StatusCode,
                    correlationId);
            }

            await ValidateDownloadedPackageAsync(body, session, discovery, endpoint, response.StatusCode, correlationId, cancellationToken);

            var outputDirectory = Path.Combine(workspaceRoot, "support-bundle", "onboarding", session.SessionId, "generated-package");
            Directory.CreateDirectory(outputDirectory);
            var packagePath = Path.Combine(outputDirectory, SafeFileName(GetDownloadFileName(response, session)));

            await File.WriteAllTextAsync(packagePath, body, Encoding.UTF8, cancellationToken);

            return new OnboardingPackageDownloadResult
            {
                Status = "Downloaded",
                SessionId = session.SessionId,
                PackagePath = packagePath,
                PackageVersion = readiness.PackageVersion,
                CorrelationId = correlationId,
                Message = attempt == 1
                    ? "Generated install package downloaded from the PageMaker365 portal."
                    : $"Generated install package downloaded from the PageMaker365 portal after {attempt} attempts."
            };
        }
    }

    private static bool IsDownloadablePackageReadiness(string status)
    {
        return status.Equals("Ready", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Downloaded", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        string operation,
        Uri endpoint,
        TRequest request,
        OnboardingBootstrapSession session,
        CancellationToken cancellationToken,
        Action<JsonElement>? validateJson = null,
        Action<HttpRequestMessage>? configureRequest = null)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };
        ApplyAuthorization(httpRequest, session);
        configureRequest?.Invoke(httpRequest);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var correlationId = GetCorrelationId(response, body);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(operation, endpoint, response.StatusCode, body, correlationId);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new OnboardingApiException(
                $"Portal onboarding API {operation} returned an empty response.",
                endpoint,
                response.StatusCode,
                correlationId);
        }

        using var document = ParseJsonDocument(operation, endpoint, response.StatusCode, body, correlationId);
        validateJson?.Invoke(document.RootElement);

        try
        {
            var result = document.RootElement.Deserialize<TResponse>(JsonOptions);
            return result ?? throw new OnboardingApiException(
                $"Portal onboarding API {operation} returned an empty response.",
                endpoint,
                response.StatusCode,
                correlationId);
        }
        catch (JsonException exception)
        {
            throw new OnboardingApiException(
                $"Portal onboarding API {operation} returned invalid JSON. {exception.Message}",
                endpoint,
                response.StatusCode,
                correlationId,
                exception);
        }
    }

    private void ApplyAuthorization(HttpRequestMessage httpRequest, OnboardingBootstrapSession session)
    {
        var apiKey = Environment.GetEnvironmentVariable(_options.ApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        if (!string.IsNullOrWhiteSpace(session.SessionId))
        {
            httpRequest.Headers.TryAddWithoutValidation("X-PM365-Onboarding-Session", session.SessionId);
        }

        if (!string.IsNullOrWhiteSpace(session.OneTimeCode))
        {
            httpRequest.Headers.TryAddWithoutValidation("X-PM365-Onboarding-Code", session.OneTimeCode);
        }
    }

    private bool ShouldFallback(Exception exception)
    {
        if (!_options.FallbackToMockOnFailure || exception is OperationCanceledException)
        {
            return false;
        }

        if (exception is OnboardingApiException apiException)
        {
            return apiException.StatusCode is not null && IsTransientStatusCode(apiException.StatusCode.Value);
        }

        return exception is HttpRequestException or TaskCanceledException or IOException;
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
            code >= 500;
    }

    private static TimeSpan GetPackageRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is not null)
        {
            retryAfter = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
        }

        if (retryAfter is not null)
        {
            return TimeSpan.FromSeconds(Math.Clamp(retryAfter.Value.TotalSeconds, 0, 30));
        }

        return TimeSpan.FromMilliseconds(Math.Min(4000, 250 * (1 << (attempt - 1))));
    }

    private static bool IsEvidenceRetryable(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (exception is OnboardingApiException apiException && apiException.StatusCode is not null)
        {
            var code = (int)apiException.StatusCode.Value;
            return apiException.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests ||
                code >= 500;
        }

        return exception is HttpRequestException or TaskCanceledException or IOException;
    }

    private static void ValidateEvidenceRequest(
        OnboardingBootstrapSession session,
        InstallerEvidenceEvent evidence,
        string idempotencyKey)
    {
        var lifecycle = EvidenceLifecycle(evidence);
        var attemptId = EvidenceAttemptId(evidence);
        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            string.IsNullOrWhiteSpace(evidence.EventId) ||
            string.IsNullOrWhiteSpace(evidence.EventType) ||
            string.IsNullOrWhiteSpace(attemptId) ||
            evidence.Sequence <= 0 ||
            string.IsNullOrWhiteSpace(evidence.OnboardingSessionId) ||
            string.IsNullOrWhiteSpace(evidence.DeploymentExportId) ||
            string.IsNullOrWhiteSpace(evidence.LifecycleStatus) ||
            string.IsNullOrWhiteSpace(evidence.Outcome))
        {
            throw new InvalidDataException("Installer evidence is missing required hardened contract fields.");
        }

        if (lifecycle is not (InstallerEvidenceLifecycle.Install or InstallerEvidenceLifecycle.Removal) ||
            (lifecycle.Equals(InstallerEvidenceLifecycle.Install, StringComparison.Ordinal) &&
                !attemptId.Equals(evidence.InstallAttemptId, StringComparison.Ordinal)) ||
            (lifecycle.Equals(InstallerEvidenceLifecycle.Removal, StringComparison.Ordinal) &&
                (!attemptId.Equals(evidence.AttemptId, StringComparison.Ordinal) ||
                    !attemptId.Equals(evidence.RemovalAttemptId, StringComparison.Ordinal) ||
                    !attemptId.Equals(evidence.InstallAttemptId, StringComparison.Ordinal))))
        {
            throw new InvalidDataException("Installer evidence lifecycle and attempt identity do not match.");
        }

        if (lifecycle.Equals(InstallerEvidenceLifecycle.Removal, StringComparison.Ordinal))
        {
            var expectedIdempotencyKey = $"{attemptId}:{evidence.Sequence}:{evidence.EventId}";
            if (!idempotencyKey.Equals(expectedIdempotencyKey, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Removal evidence idempotency identity does not match its persisted event identity.");
            }

            RemovalEvidenceLifecycleService.ValidatePayload(evidence);
        }

        if (!evidence.OnboardingSessionId.Equals(session.SessionId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Installer evidence does not match the active onboarding session.");
        }

        ValidateSanitizedEvidenceText(evidence.Message, "message");
        ValidateSanitizedEvidenceText(evidence.RuntimeUrl, "runtimeUrl");
        ValidateSanitizedEvidenceText(evidence.ApiUrl, "apiUrl");
        ValidateSanitizedEvidenceText(evidence.AzureResourceGroup, "azureResourceGroup");
        if (evidence.Error is not null)
        {
            ValidateSanitizedEvidenceText(evidence.Error.Code, "error.code");
            ValidateSanitizedEvidenceText(evidence.Error.Message, "error.message");
            ValidateSanitizedEvidenceText(evidence.Error.Category, "error.category");
            ValidateSanitizedEvidenceText(evidence.Error.Detail, "error.detail");
        }

        foreach (var smokeTest in evidence.SmokeTests)
        {
            ValidateSanitizedEvidenceText(smokeTest.Name, "smokeTests.name");
            ValidateSanitizedEvidenceText(smokeTest.Status, "smokeTests.status");
        }
    }

    private static bool IsMatchingRemovalReceipt(
        InstallerEvidenceReceipt receipt,
        InstallerEvidenceEvent evidence,
        string submittedAttemptId)
    {
        return receipt.ContractVersion.Equals("0.3", StringComparison.Ordinal) &&
            receipt.Lifecycle.Equals(InstallerEvidenceLifecycle.Removal, StringComparison.Ordinal) &&
            receipt.AttemptId.Equals(submittedAttemptId, StringComparison.Ordinal) &&
            receipt.RemovalAttemptId.Equals(submittedAttemptId, StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(receipt.InstallAttemptId) ||
                receipt.InstallAttemptId.Equals(submittedAttemptId, StringComparison.Ordinal)) &&
            receipt.LifecycleStatus.Equals(evidence.LifecycleStatus, StringComparison.Ordinal) &&
            receipt.Outcome.Equals(evidence.Outcome, StringComparison.Ordinal);
    }

    private static string EvidenceLifecycle(InstallerEvidenceEvent evidence)
    {
        return string.IsNullOrWhiteSpace(evidence.Lifecycle)
            ? evidence.EventType.StartsWith("removal_", StringComparison.Ordinal)
                ? InstallerEvidenceLifecycle.Removal
                : InstallerEvidenceLifecycle.Install
            : evidence.Lifecycle;
    }

    private static string EvidenceAttemptId(InstallerEvidenceEvent evidence)
    {
        return First(evidence.AttemptId, evidence.RemovalAttemptId, evidence.InstallAttemptId);
    }

    private static void ValidateSanitizedEvidenceText(string value, string field)
    {
        if (!string.Equals(value, EvidenceRedactionService.Redact(value), StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Installer evidence {field} contains prohibited secret-like content.");
        }
    }

    private static OnboardingPackageContext? CreatePackageContext(CustomerInstallConfig? config)
    {
        if (config is null)
        {
            return null;
        }

        return new OnboardingPackageContext
        {
            TenantId = First(config.Customer.TenantId, config.Azure.TenantId),
            TenantName = config.Customer.TenantName,
            AzureSubscriptionId = config.Azure.SubscriptionId,
            AzureLocation = config.Azure.Location,
            ResourceGroupName = config.Azure.ResourceGroupName,
            SharePointSiteUrl = config.SharePoint.SiteUrl,
            SharePointTenantHostname = HostFromSharePointUrl(config.SharePoint.SiteUrl),
            PrimaryContact = config.Customer.PrimaryContact,
            EnvironmentId = config.ControlPlane.EnvironmentId,
            DeploymentExportId = config.ControlPlane.DeploymentExportId,
            PackageHashAlgorithm = config.ControlPlane.PackageHashAlgorithm,
            PackageHash = config.ControlPlane.PackageHash,
            TrustMode = config.ControlPlane.TrustMode
        };
    }

    private static void ValidateStatusJson(Uri endpoint, JsonElement root)
    {
        var validation = RuntimeContractValidator.ValidateOnboardingStatusJson(root);
        if (!validation.IsValid)
        {
            throw new OnboardingApiException(
                $"Portal onboarding API status check response failed validation: missing required field(s) or invalid field(s): {string.Join(" ", validation.Errors)}",
                endpoint);
        }
    }

    private static void EnsureSessionMatches(
        string operation,
        Uri endpoint,
        OnboardingBootstrapSession session,
        string responseSessionId)
    {
        if (string.IsNullOrWhiteSpace(session.SessionId) ||
            responseSessionId.Equals(session.SessionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new OnboardingApiException(
            $"Portal onboarding API {operation} returned session '{responseSessionId}' for expected session '{session.SessionId}'.",
            endpoint);
    }

    private static void EnsureRequiredJsonFields(
        string operation,
        Uri endpoint,
        JsonElement root,
        params string[] propertyPaths)
    {
        var missing = propertyPaths.Where(path => IsJsonPathMissingOrBlank(root, path)).ToArray();
        if (missing.Length > 0)
        {
            throw new OnboardingApiException(
                $"Portal onboarding API {operation} response is missing required field(s): {string.Join(", ", missing)}.",
                endpoint);
        }
    }

    private static bool IsJsonPathMissingOrBlank(JsonElement root, string propertyPath)
    {
        if (!TryGetJsonPath(root, propertyPath, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        return value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString());
    }

    private static bool TryGetJsonPath(JsonElement root, string propertyPath, out JsonElement value)
    {
        value = root;
        foreach (var propertyName in propertyPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                value = default;
                return false;
            }

            var found = false;
            foreach (var property in value.EnumerateObject())
            {
                if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = property.Value;
                found = true;
                break;
            }

            if (!found)
            {
                value = default;
                return false;
            }
        }

        return true;
    }

    private static JsonDocument ParseJsonDocument(
        string operation,
        Uri endpoint,
        HttpStatusCode? statusCode,
        string body,
        string correlationId)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new OnboardingApiException(
                $"Portal onboarding API {operation} returned invalid JSON. {exception.Message}",
                endpoint,
                statusCode,
                correlationId,
                exception);
        }
    }

    private async Task ValidateDownloadedPackageAsync(
        string body,
        OnboardingBootstrapSession session,
        TenantDiscoveryResult? discovery,
        Uri endpoint,
        HttpStatusCode statusCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        CustomerInstallConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<CustomerInstallConfig>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new OnboardingApiException(
                $"Generated package returned by the portal is not valid JSON. {exception.Message}",
                endpoint,
                statusCode,
                correlationId,
                exception);
        }

        if (config is null)
        {
            throw new OnboardingApiException(
                "Generated package returned by the portal is empty.",
                endpoint,
                statusCode,
                correlationId);
        }

        PackageTrustOptions trustOptions;
        try
        {
            trustOptions = await _packageTrustKeyResolver.ResolveAsync(
                config,
                PackageTrustOptions.FromEnvironment(),
                cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or HttpRequestException or JsonException ||
            exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new OnboardingApiException(
                $"Generated package trust could not be resolved: {exception.Message}",
                endpoint,
                statusCode,
                correlationId,
                exception);
        }

        var validation = ConfigService.Validate(
            config,
            body,
            PackageProvenanceContext.ForPortalDownload(session, discovery),
            trustOptions);
        if (!validation.IsValid)
        {
            throw new OnboardingApiException(
                $"Generated package returned by the portal failed validation: {string.Join(" ", validation.Errors)}",
                endpoint,
                statusCode,
                correlationId);
        }
    }

    private static bool IsJsonMediaType(string? mediaType)
    {
        return !string.IsNullOrWhiteSpace(mediaType) &&
            (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
                mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }

    private static OnboardingApiException CreateApiException(
        string operation,
        Uri endpoint,
        HttpStatusCode statusCode,
        string body,
        string correlationId)
    {
        var detail = ExtractApiErrorDetail(body, ref correlationId);
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"Portal onboarding API {operation} returned {(int)statusCode} {statusCode}."
            : $"Portal onboarding API {operation} returned {(int)statusCode} {statusCode}: {detail}";

        return new OnboardingApiException(message, endpoint, statusCode, correlationId);
    }

    private static string ExtractApiErrorDetail(string body, ref string correlationId)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (string.IsNullOrWhiteSpace(correlationId) &&
                TryGetJsonPath(root, "correlationId", out var correlationProperty) &&
                correlationProperty.ValueKind == JsonValueKind.String)
            {
                correlationId = correlationProperty.GetString() ?? "";
            }

            foreach (var path in new[] { "message", "error.message", "error.code", "details.message", "details", "code" })
            {
                if (TryGetJsonPath(root, path, out var property) &&
                    property.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(property.GetString()))
                {
                    return SanitizeApiErrorDetail(property.GetString() ?? "");
                }
            }
        }
        catch (JsonException)
        {
            return "";
        }

        return "";
    }

    private static string SanitizeApiErrorDetail(string value)
    {
        var sanitized = AssistantTransferPolicy.SanitizeText(value);
        var oneLine = string.Join(
            " ",
            sanitized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine[..Math.Min(512, oneLine.Length)];
    }

    private static string GetDownloadFileName(HttpResponseMessage response, OnboardingBootstrapSession session)
    {
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName;
        fileName = fileName?.Trim('"');
        return string.IsNullOrWhiteSpace(fileName)
            ? $"{session.SessionId}.customer.install.json"
            : fileName;
    }

    private static string GetCorrelationId(HttpResponseMessage response, string body = "")
    {
        if (response.Headers.TryGetValues("X-Correlation-ID", out var values))
        {
            return values.FirstOrDefault() ?? "";
        }

        var correlationId = "";
        ExtractApiErrorDetail(body, ref correlationId);
        return correlationId;
    }

    private static string First(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static string HostFromSharePointUrl(string? siteUrl)
    {
        return Uri.TryCreate(siteUrl, UriKind.Absolute, out var uri) ? uri.Host : "";
    }

    private static string SafeFileName(string fileName)
    {
        return string.Concat(fileName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
    }
}
