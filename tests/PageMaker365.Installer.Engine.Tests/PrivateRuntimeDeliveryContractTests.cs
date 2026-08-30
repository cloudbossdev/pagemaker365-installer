using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

internal static class PrivateRuntimeDeliveryContractTests
{
    private const string ReleaseId = "pm365-runtime-0.1.0+fixture.abcdef1";
    private const string OnboardingSessionId = "onb_1234567890abcdef";
    private const string ApiReference = "ard_AAAAAAAAAAAAAAAAAAAAAAAA";
    private const string PortalReference = "ard_BBBBBBBBBBBBBBBBBBBBBBBB";

    public static Task AcquiresPrivateStreamsWithHeaderOnlyReferencesAndRangeResume() =>
        AcquiresPrivateStreamsWithHeaderOnlyReferencesAndRangeResume("0.5");

    public static Task AcquiresV06PrivateStreamsWithHeaderOnlyReferencesAndRangeResume() =>
        AcquiresPrivateStreamsWithHeaderOnlyReferencesAndRangeResume("0.6");

    private static async Task AcquiresPrivateStreamsWithHeaderOnlyReferencesAndRangeResume(string packageVersion)
    {
        var apiZip = CreateRuntimeArchive("api");
        var portalZip = CreateRuntimeArchive("portal");
        var fixture = CreatePackage(apiZip, portalZip, packageVersion);
        var package = packageVersion == "0.6"
            ? new PrivateRuntimeDeliveryV06PackageService().ValidateJson(fixture.Json, fixture.TrustOptions, new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero))
            : new PrivateRuntimeDeliveryPackageService().ValidateJson(fixture.Json, fixture.TrustOptions, new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero));
        var outputRoot = CreateTemporaryDirectory();
        var requestedPaths = new List<string>();
        try
        {
            var partialPath = Path.Combine(outputRoot, "runtime-acquisition", $"portal-{package.Portal.Sha256}.partial");
            Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
            var resumeAt = portalZip.Length / 2;
            await File.WriteAllBytesAsync(partialPath, portalZip[..resumeAt]);
            var requestCount = 0;
            using var delivery = new PrivateRuntimeDeliveryClient(new PrivateRuntimeDeliveryOptions { Timeout = TimeSpan.FromMinutes(1), EnablePackageV06 = packageVersion == "0.6" }, new ScriptedHandler(request =>
            {
                requestedPaths.Add(request.RequestUri!.PathAndQuery);
                requestCount++;
                if (request.RequestUri!.AbsolutePath == PrivateRuntimeDeliveryPackage.SessionPathValue)
                {
                    AssertEx.Equal(HttpMethod.Post, request.Method);
                    var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    using var sessionRequest = JsonDocument.Parse(body);
                    var sessionFields = sessionRequest.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();
                    AssertEx.True(sessionFields.SequenceEqual(["package"], StringComparer.Ordinal));
                    var canonicalPackage = packageVersion == "0.6"
                        ? PrivateRuntimeDeliveryV06PackageService.FormatCanonicalPackage(sessionRequest.RootElement.GetProperty("package"))
                        : PrivateRuntimeDeliveryPackageService.FormatCanonicalPackage(sessionRequest.RootElement.GetProperty("package"));
                    AssertEx.Equal(fixture.Json, canonicalPackage);
                    AssertEx.True(body.Contains(ApiReference, StringComparison.Ordinal), "The signed package is the only session authority.");
                    return JsonResponse(SessionResponse());
                }

                if (request.RequestUri!.AbsolutePath == PrivateRuntimeDeliveryPackage.ReceiptPathValue)
                {
                    AssertEx.Equal(HttpMethod.Post, request.Method);
                    var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    AssertEx.False(body.Contains(ApiReference, StringComparison.Ordinal));
                    AssertEx.False(body.Contains(PortalReference, StringComparison.Ordinal));
                    using var receipt = JsonDocument.Parse(body);
                    AssertEx.True(receipt.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name).SequenceEqual(
                        ["artifacts", "contractVersion", "deliverySessionId", "eventId", "idempotencyKey", "installerVersion", "manifestSha256", "occurredAt", "outcome", "packageHash", "releaseId", "safeResult"], StringComparer.Ordinal));
                    AssertEx.Equal("completed", receipt.RootElement.GetProperty("outcome").GetString());
                    AssertEx.Equal("runtime_artifacts_verified", receipt.RootElement.GetProperty("safeResult").GetProperty("code").GetString());
                    AssertEx.Equal("completed", receipt.RootElement.GetProperty("safeResult").GetProperty("state").GetString());
                    AssertEx.Equal("verified", receipt.RootElement.GetProperty("artifacts").GetProperty("api").GetProperty("verificationOutcome").GetString());
                    AssertEx.Equal("verified", receipt.RootElement.GetProperty("artifacts").GetProperty("portal").GetProperty("verificationOutcome").GetString());
                    return JsonResponse(ReceiptResponse(receipt.RootElement));
                }

                AssertEx.Equal(HttpMethod.Get, request.Method);
                AssertEx.False(request.RequestUri!.PathAndQuery.Contains("ard_", StringComparison.Ordinal));
                AssertEx.False(request.RequestUri!.Query.Contains("delivery", StringComparison.OrdinalIgnoreCase));
                AssertEx.Contains(request.Headers.GetValues(PrivateRuntimeDeliveryClient.DeliverySessionHeader), "rds_ABCDEFGHIJKLMNOPQRSTUVWXYZ");
                var kind = request.RequestUri.AbsolutePath.EndsWith("/api", StringComparison.Ordinal) ? "api" : "portal";
                var expectedReference = kind == "api" ? ApiReference : PortalReference;
                AssertEx.Contains(request.Headers.GetValues(PrivateRuntimeDeliveryClient.DeliveryReferenceHeader), expectedReference);
                AssertEx.False(request.Headers.TryGetValues("Location", out _));
                AssertEx.Contains(request.Headers.GetValues("If-Match"), $"\"sha256:{package.Artifact(kind).Sha256}\"");

                if (kind == "api")
                {
                    AssertEx.True(request.Headers.Range is null);
                    return ArtifactResponse(HttpStatusCode.OK, apiZip, package.Api, null);
                }

                AssertEx.Equal(resumeAt, request.Headers.Range!.Ranges.Single().From);
                AssertEx.Equal(portalZip.Length - 1, request.Headers.Range.Ranges.Single().To);
                return ArtifactResponse(HttpStatusCode.PartialContent, portalZip[resumeAt..], package.Portal, new ContentRangeHeaderValue(resumeAt, portalZip.Length - 1, portalZip.Length));
            }));
            var result = packageVersion == "0.6"
                ? await delivery.AcquireV06Async(fixture.Json, fixture.TrustOptions, CreateOnboardingSession(), outputRoot, "0.1.0", CancellationToken.None)
                : await delivery.AcquireAsync(fixture.Json, fixture.TrustOptions, CreateOnboardingSession(), outputRoot, "0.1.0", CancellationToken.None);

            AssertEx.Equal("passed", result.Outcome, $"{result.SafeErrorCode} {result.SafeErrorMessage} {result.ReceiptStatus}");
            AssertEx.Equal("submitted", result.ReceiptStatus);
            AssertEx.Equal(2, result.Artifacts.Count);
            AssertEx.Equal(1, result.Artifacts.Single(artifact => artifact.ArtifactKind == "portal").RangeRequestCount);
            AssertEx.Equal(apiZip.Length, (await File.ReadAllBytesAsync(result.Artifacts.Single(artifact => artifact.ArtifactKind == "api").VerifiedPath)).Length);
            AssertEx.Equal(portalZip.Length, (await File.ReadAllBytesAsync(result.Artifacts.Single(artifact => artifact.ArtifactKind == "portal").VerifiedPath)).Length);
            AssertEx.Equal(4, requestCount);
            AssertEx.False(requestedPaths.Any(path => path.Contains("ard_", StringComparison.Ordinal) || path.Contains("?", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    public static async Task V06IsDefaultDeniedBeforeValidationOrTransport()
    {
        var fixture = CreatePackage(CreateRuntimeArchive("api"), CreateRuntimeArchive("portal"), "0.6");
        var requests = 0;
        using var delivery = new PrivateRuntimeDeliveryClient(
            new PrivateRuntimeDeliveryOptions { Timeout = TimeSpan.FromMinutes(1) },
            new ScriptedHandler(_ =>
            {
                requests++;
                return JsonResponse("{}");
            }));
        var outputRoot = Path.Combine(Path.GetTempPath(), "pm365-v06-disabled-" + Guid.NewGuid().ToString("N"));
        await AssertEx.ThrowsAsync<InvalidOperationException>(() => delivery.AcquireV06Async(
            fixture.Json,
            fixture.TrustOptions,
            CreateOnboardingSession(),
            outputRoot,
            "0.1.0"));
        AssertEx.Equal(0, requests);
        AssertEx.False(Directory.Exists(outputRoot));

        using var defaultTransport = new PrivateRuntimeDeliveryClient(new PrivateRuntimeDeliveryOptions
        {
            Timeout = TimeSpan.FromMinutes(1),
            EnablePackageV06 = true
        });
        await AssertEx.ThrowsAsync<InvalidOperationException>(() => defaultTransport.AcquireV06Async(
            fixture.Json,
            fixture.TrustOptions,
            CreateOnboardingSession(),
            outputRoot,
            "0.1.0"));
        AssertEx.False(Directory.Exists(outputRoot));
    }

    public static Task V06BindsCanonicalManifestAndUsesExactV3ValueRules()
    {
        var apiZip = CreateRuntimeArchive("api");
        var portalZip = CreateRuntimeArchive("portal");
        var service = new PrivateRuntimeDeliveryV06PackageService();

        var safeNamesAndEqualHashes = CreatePackage(
            apiZip,
            portalZip,
            "0.6",
            apiFileName: "api-runtime.zip",
            portalFileName: "portal-runtime.zip",
            portalHashOverride: Hash(apiZip),
            runtimeVersion: "2147483647.2147483647.2147483647");
        var accepted = service.ValidateJson(safeNamesAndEqualHashes.Json, safeNamesAndEqualHashes.TrustOptions);
        AssertEx.Equal("api-runtime.zip", accepted.Api.FileName);
        AssertEx.Equal("portal-runtime.zip", accepted.Portal.FileName);
        AssertEx.Equal(accepted.Api.Sha256, accepted.Portal.Sha256);
        AssertEx.Equal("2147483647.2147483647.2147483647", accepted.RuntimeVersion);

        var signedWrongDigest = CreatePackage(apiZip, portalZip, "0.6", manifestHashOverride: new string('e', 64));
        AssertEx.Throws<InvalidDataException>(() => service.ValidateJson(signedWrongDigest.Json, signedWrongDigest.TrustOptions));

        var signedIdentityDigestMismatch = CreatePackage(
            apiZip,
            portalZip,
            "0.6",
            packageSourceCommit: new string('c', 40),
            manifestSourceCommit: new string('a', 40));
        AssertEx.Throws<InvalidDataException>(() => service.ValidateJson(signedIdentityDigestMismatch.Json, signedIdentityDigestMismatch.TrustOptions));

        var duplicateName = CreatePackage(apiZip, portalZip, "0.6", apiFileName: "same.zip", portalFileName: "same.zip");
        AssertEx.Throws<InvalidDataException>(() => service.ValidateJson(duplicateName.Json, duplicateName.TrustOptions));

        var unsafeName = CreatePackage(apiZip, portalZip, "0.6", apiFileName: "../unsafe.zip");
        AssertEx.Throws<InvalidDataException>(() => service.ValidateJson(unsafeName.Json, unsafeName.TrustOptions));

        var oversizedSemver = CreatePackage(apiZip, portalZip, "0.6", runtimeVersion: "2147483648.0.0");
        AssertEx.Throws<InvalidDataException>(() => service.ValidateJson(oversizedSemver.Json, oversizedSemver.TrustOptions));

        var invalidUuid = CreatePackage(apiZip, portalZip, "0.6");
        var invalidUuidJson = invalidUuid.Json.Replace(
            "11111111-1111-4111-8111-111111111111",
            "11111111-1111-0111-0111-111111111111",
            StringComparison.Ordinal);
        AssertEx.Throws<InvalidDataException>(() => service.ValidateJson(invalidUuidJson, invalidUuid.TrustOptions));
        return Task.CompletedTask;
    }

    public static async Task V06RejectsUnsafeResponseMetadataBeforeVerifiedOutput()
    {
        foreach (var scenario in new[] { "cache", "range" })
        {
            var apiZip = CreateRuntimeArchive("api");
            var portalZip = CreateRuntimeArchive("portal");
            var fixture = CreatePackage(apiZip, portalZip, "0.6");
            var package = new PrivateRuntimeDeliveryV06PackageService().ValidateJson(fixture.Json, fixture.TrustOptions);
            var outputRoot = CreateTemporaryDirectory();
            try
            {
                var resumeAt = 0;
                if (scenario == "range")
                {
                    resumeAt = apiZip.Length / 2;
                    var partial = Path.Combine(outputRoot, "runtime-acquisition", $"api-{package.Api.Sha256}.partial");
                    Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
                    await File.WriteAllBytesAsync(partial, apiZip[..resumeAt]);
                }

                using var delivery = new PrivateRuntimeDeliveryClient(
                    new PrivateRuntimeDeliveryOptions { Timeout = TimeSpan.FromMinutes(1), EnablePackageV06 = true },
                    new ScriptedHandler(request =>
                    {
                        if (request.RequestUri!.AbsolutePath == PrivateRuntimeDeliveryPackage.SessionPathValue) return JsonResponse(SessionResponse());
                        if (request.RequestUri.AbsolutePath == PrivateRuntimeDeliveryPackage.ReceiptPathValue)
                        {
                            using var receipt = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                            return JsonResponse(ReceiptResponse(receipt.RootElement));
                        }

                        if (scenario == "cache")
                        {
                            var response = ArtifactResponse(HttpStatusCode.OK, apiZip, package.Api, null);
                            response.Headers.CacheControl = new CacheControlHeaderValue { Public = true };
                            return response;
                        }

                        return ArtifactResponse(
                            HttpStatusCode.PartialContent,
                            apiZip[resumeAt..],
                            package.Api,
                            new ContentRangeHeaderValue(resumeAt + 1, apiZip.Length - 1, apiZip.Length));
                    }));

                var result = await delivery.AcquireV06Async(fixture.Json, fixture.TrustOptions, CreateOnboardingSession(), outputRoot, "0.1.0");
                AssertEx.Equal("failed", result.Outcome);
                AssertEx.Equal(scenario == "cache" ? "runtime_artifact_cache_policy_invalid" : "runtime_artifact_range_invalid", result.SafeErrorCode);
                AssertEx.Equal(0, Directory.GetFiles(Path.Combine(outputRoot, "runtime-acquisition"), "*.zip").Length);
            }
            finally
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    public static async Task V06RejectsExpirySessionMismatchAndInvalidArchiveProvenance()
    {
        var apiZip = CreateRuntimeArchive("api");
        var portalZip = CreateRuntimeArchive("portal");
        var fixture = CreatePackage(apiZip, portalZip, "0.6");
        var service = new PrivateRuntimeDeliveryV06PackageService();
        AssertEx.Throws<InvalidDataException>(() => service.ValidateJson(
            fixture.Json,
            fixture.TrustOptions,
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var requests = 0;
        using var delivery = new PrivateRuntimeDeliveryClient(
            new PrivateRuntimeDeliveryOptions { Timeout = TimeSpan.FromMinutes(1), EnablePackageV06 = true },
            new ScriptedHandler(_ =>
            {
                requests++;
                return JsonResponse("{}");
            }));
        var outputRoot = Path.Combine(Path.GetTempPath(), "pm365-v06-session-denial-" + Guid.NewGuid().ToString("N"));
        var mismatchedSession = CreateOnboardingSession();
        mismatchedSession.SessionId = "onb_0000000000000000";
        await AssertEx.ThrowsAsync<InvalidDataException>(() => delivery.AcquireV06Async(
            fixture.Json,
            fixture.TrustOptions,
            mismatchedSession,
            outputRoot,
            "0.1.0"));
        AssertEx.Equal(0, requests);
        AssertEx.False(Directory.Exists(outputRoot));

        foreach (var invalidApiZip in new[]
        {
            CreateRuntimeArchive("api", provenanceSourceCommit: new string('c', 40)),
            CreateRuntimeArchive("api", unsafeEntry: "../escape.txt")
        })
        {
            var invalidFixture = CreatePackage(invalidApiZip, portalZip, "0.6");
            var package = service.ValidateJson(invalidFixture.Json, invalidFixture.TrustOptions);
            var archivePath = Path.Combine(Path.GetTempPath(), "pm365-v06-invalid-archive-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                await File.WriteAllBytesAsync(archivePath, invalidApiZip);
                AssertEx.Throws<InvalidDataException>(() => PrivateRuntimeArchiveVerifier.Verify(archivePath, package.Api, package));
            }
            finally
            {
                if (File.Exists(archivePath)) File.Delete(archivePath);
            }
        }
    }

    public static Task RejectsLocationBearingOrTamperedV05PackageBeforeTransport()
    {
        var fixture = CreatePackage(CreateRuntimeArchive("api"), CreateRuntimeArchive("portal"));
        var service = new PrivateRuntimeDeliveryPackageService();
        var publicUrl = fixture.Json.Replace("\"runtimeArtifacts\": {", "\"downloadUrl\": \"https://downloads.pagemaker365.com/evil.zip\",\n  \"runtimeArtifacts\": {", StringComparison.Ordinal);
        AssertEx.Throws<InvalidDataException>(() => service.ValidateJson(publicUrl, fixture.TrustOptions));
        var tampered = fixture.Json.Replace("\"releaseId\": \"" + ReleaseId + "\"", "\"releaseId\": \"pm365-runtime-0.1.0+tampered\"", StringComparison.Ordinal);
        AssertEx.Throws<InvalidDataException>(() => service.ValidateJson(tampered, fixture.TrustOptions));
        var nonCanonical = fixture.Json.Replace("\n  \"customer\"", "\n\t\"customer\"", StringComparison.Ordinal);
        AssertEx.Throws<InvalidDataException>(() => service.ValidateJson(nonCanonical, fixture.TrustOptions));
        return Task.CompletedTask;
    }

    public static async Task RejectsMismatchedReceiptAcknowledgementAndStagesOutbox()
    {
        var apiZip = CreateRuntimeArchive("api");
        var portalZip = CreateRuntimeArchive("portal");
        var fixture = CreatePackage(apiZip, portalZip);
        var outputRoot = CreateTemporaryDirectory();
        try
        {
            using var delivery = new PrivateRuntimeDeliveryClient(new PrivateRuntimeDeliveryOptions { Timeout = TimeSpan.FromMinutes(1) }, new ScriptedHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath == PrivateRuntimeDeliveryPackage.SessionPathValue) return JsonResponse(SessionResponse());
                if (request.RequestUri!.AbsolutePath == PrivateRuntimeDeliveryPackage.ReceiptPathValue)
                {
                    using var receipt = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    return JsonResponse(MismatchedReceiptResponse(receipt.RootElement));
                }

                var kind = request.RequestUri!.AbsolutePath.EndsWith("/api", StringComparison.Ordinal) ? "api" : "portal";
                return kind == "api"
                    ? ArtifactResponse(HttpStatusCode.OK, apiZip, new PrivateRuntimeDeliveryPackageService().ValidateJson(fixture.Json, fixture.TrustOptions).Api, null)
                    : ArtifactResponse(HttpStatusCode.OK, portalZip, new PrivateRuntimeDeliveryPackageService().ValidateJson(fixture.Json, fixture.TrustOptions).Portal, null);
            }));
            var result = await delivery.AcquireAsync(fixture.Json, fixture.TrustOptions, CreateOnboardingSession(), outputRoot, "0.1.0");

            AssertEx.Equal("verified_receipt_pending", result.Outcome);
            AssertEx.Equal("outbox_pending", result.ReceiptStatus);
            AssertEx.True(File.Exists(result.ReceiptOutboxPath));
            var staged = await File.ReadAllTextAsync(result.ReceiptOutboxPath);
            AssertEx.True(staged.Contains("\"runtime_artifacts_verified\"", StringComparison.Ordinal));
            AssertEx.False(staged.Contains("runtime_artifacts_failed", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    public static async Task RejectsRedirectAndStagesOnlySanitizedFailureReceipt()
    {
        var fixture = CreatePackage(CreateRuntimeArchive("api"), CreateRuntimeArchive("portal"));
        var package = new PrivateRuntimeDeliveryPackageService().ValidateJson(fixture.Json, fixture.TrustOptions);
        var outputRoot = CreateTemporaryDirectory();
        try
        {
            var redirectedEndpointRequests = 0;
            var redirectedEndpointReceivedDeliveryHeader = false;
            using var delivery = new PrivateRuntimeDeliveryClient(new PrivateRuntimeDeliveryOptions { Timeout = TimeSpan.FromMinutes(1) }, new ScriptedHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/redirected-artifact")
                {
                    redirectedEndpointRequests++;
                    redirectedEndpointReceivedDeliveryHeader = request.Headers.Contains(PrivateRuntimeDeliveryClient.DeliveryReferenceHeader);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                return request.RequestUri.AbsolutePath == PrivateRuntimeDeliveryPackage.SessionPathValue
                ? JsonResponse(SessionResponse())
                : new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://localhost:5443/redirected-artifact") } };
            }));
            var result = await delivery.AcquireAsync(fixture.Json, fixture.TrustOptions, CreateOnboardingSession(), outputRoot, "0.1.0");

            AssertEx.Equal("failed", result.Outcome);
            AssertEx.Equal("runtime_delivery_redirect_rejected", result.SafeErrorCode);
            AssertEx.Equal("outbox_pending", result.ReceiptStatus, $"{result.SafeErrorCode} {result.SafeErrorMessage}");
            AssertEx.True(File.Exists(result.ReceiptOutboxPath));
            var receipt = await File.ReadAllTextAsync(result.ReceiptOutboxPath);
            AssertEx.False(receipt.Contains("downloads.pagemaker365.com", StringComparison.OrdinalIgnoreCase));
            AssertEx.False(receipt.Contains(ApiReference, StringComparison.Ordinal));
            AssertEx.False(receipt.Contains("OneTime", StringComparison.OrdinalIgnoreCase));
            using var stagedReceipt = JsonDocument.Parse(receipt);
            AssertEx.True(stagedReceipt.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name).SequenceEqual(
                ["artifacts", "contractVersion", "deliverySessionId", "eventId", "idempotencyKey", "installerVersion", "manifestSha256", "occurredAt", "outcome", "packageHash", "releaseId", "safeResult"], StringComparer.Ordinal));
            AssertEx.Equal("failed", stagedReceipt.RootElement.GetProperty("outcome").GetString());
            AssertEx.Equal("runtime_artifacts_failed", stagedReceipt.RootElement.GetProperty("safeResult").GetProperty("code").GetString());
            AssertEx.Equal("failed", stagedReceipt.RootElement.GetProperty("safeResult").GetProperty("state").GetString());
            AssertEx.Equal("not_attempted", stagedReceipt.RootElement.GetProperty("artifacts").GetProperty("api").GetProperty("verificationOutcome").GetString());
            AssertEx.Equal(0, redirectedEndpointRequests);
            AssertEx.False(redirectedEndpointReceivedDeliveryHeader);
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    public static async Task RejectsNonApiControlPlaneOriginBeforeTransport()
    {
        var fixture = CreatePackage(CreateRuntimeArchive("api"), CreateRuntimeArchive("portal"));
        var requests = 0;
        using var delivery = new PrivateRuntimeDeliveryClient(new PrivateRuntimeDeliveryOptions { Timeout = TimeSpan.FromMinutes(1) }, new ScriptedHandler(_ =>
        {
            requests++;
            return JsonResponse("{}");
        }));
        var session = CreateOnboardingSession();
        session.ApiBaseUrl = "https://pagemaker365.com";
        var outputRoot = CreateTemporaryDirectory();
        try
        {
            await AssertEx.ThrowsAsync<InvalidDataException>(() => delivery.AcquireAsync(fixture.Json, fixture.TrustOptions, session, outputRoot, "0.1.0"));
            AssertEx.Equal(0, requests);
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    public static Task InstallerEngineExposesNonDeployingPrivateAcquisitionGate()
    {
        var method = typeof(InstallerEngine).GetMethod(
            "AcquirePrivateRuntimeAsync",
            [typeof(string), typeof(PackageTrustOptions), typeof(OnboardingBootstrapSession), typeof(string), typeof(string), typeof(CancellationToken)]);
        AssertEx.True(method is not null, "InstallerEngine must expose the explicit v0.5 acquisition gate.");
        AssertEx.Equal(typeof(Task<PrivateRuntimeDeliveryResult>), method!.ReturnType);
        AssertEx.False(method.Name.Contains("Deploy", StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    private static PackageFixture CreatePackage(
        byte[] apiZip,
        byte[] portalZip,
        string packageVersion = "0.5",
        string? manifestHashOverride = null,
        string? packageSourceCommit = null,
        string? manifestSourceCommit = null,
        string? apiFileName = null,
        string? portalFileName = null,
        string? portalHashOverride = null,
        string runtimeVersion = "0.1.0")
    {
        if (packageVersion is not ("0.5" or "0.6")) throw new ArgumentOutOfRangeException(nameof(packageVersion));
        var isV06 = packageVersion == "0.6";
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        var privateKey = (Ed25519PrivateKeyParameters)pair.Private;
        var publicKey = (Ed25519PublicKeyParameters)pair.Public;
        var publicKeyPem = ToPem("PUBLIC KEY", SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey).GetDerEncoded());
        var keyId = "private-runtime-test-key";
        var apiHash = Hash(apiZip);
        var portalHash = portalHashOverride ?? Hash(portalZip);
        packageSourceCommit ??= new string('a', 40);
        manifestSourceCommit ??= packageSourceCommit;
        apiFileName ??= $"pagemaker365-api-{ReleaseId}.zip";
        portalFileName ??= $"pagemaker365-portal-{ReleaseId}.zip";
        var manifestHash = isV06
            ? manifestHashOverride ?? Hash(Encoding.UTF8.GetBytes(FormatManifestV3(
                manifestSourceCommit,
                runtimeVersion,
                apiFileName,
                apiZip.Length,
                apiHash,
                portalFileName,
                portalZip.Length,
                portalHash)))
            : new string('b', 64);
        const string projectionSchema = "pagemaker365.runtime-configuration-projection.v1";
        var projection = "{\"publicSettings\":[{\"name\":\"API_LOG_LEVEL\",\"targetApp\":\"api\",\"value\":\"info\"},{\"name\":\"WEB_PRODUCT_NAME\",\"targetApp\":\"portal\",\"value\":\"PageMaker365\"}],\"schemaVersion\":\"" + projectionSchema + "\"}";
        var projectionHash = Hash(Encoding.UTF8.GetBytes(projection));
        var unsigned = """
            {
              "contractVersion":"__PACKAGE_VERSION__",
              "customer":{"customerId":"11111111-1111-4111-8111-111111111111"},
              "installation":{"installationId":"22222222-2222-4222-8222-222222222222","environmentId":"33333333-3333-4333-8333-333333333333","tenantId":"44444444-4444-4444-8444-444444444444"},
              "deployment":{"deploymentExportId":"55555555-5555-4555-8555-555555555555"},
              "controlPlane":{"onboardingSessionId":"__ONBOARDING_SESSION__","expiresAt":"2030-01-01T00:00:00.000Z","acceptedInstallerCapability":"__CAPABILITY__","packageHash":"","packageHashAlgorithm":"SHA-256","canonicalization":"json-c14n-v1","signatureAlgorithm":"Ed25519","signingKeyId":"__SIGNING_KEY__","signature":""},
              "runtimeArtifacts":{"manifestContractVersion":"__MANIFEST_VERSION__","manifestSha256":"__MANIFEST_HASH__",__PRODUCT__"releaseId":"__RELEASE_ID__","runtimeVersion":"__RUNTIME_VERSION__","sourceRepository":"cloudbossdev/spo-ui","sourceCommit":"__SOURCE_COMMIT__","provenanceSchemaVersion":"pagemaker365.runtime-provenance.v1","api":{"artifactKind":"api","fileName":"__API_FILE_NAME__","sizeBytes":__API_SIZE__,"sha256":"__API_HASH__","startupCommand":"node dist/index.js"},"portal":{"artifactKind":"portal","fileName":"__PORTAL_FILE_NAME__","sizeBytes":__PORTAL_SIZE__,"sha256":"__PORTAL_HASH__","startupCommand":"node .pm365/start-portal-runtime.mjs"}},
              "protectedAcquisition":{"contractVersion":"pagemaker365.protected-acquisition.v1","sessionPath":"/api/onboarding/installer/runtime-delivery-sessions","artifactPath":"/api/onboarding/installer/runtime-artifacts/{artifactKind}","receiptPath":"/api/onboarding/installer/runtime-delivery-receipts","authorizationMode":"installer-session-v1","expiresAt":"2030-01-01T00:00:00.000Z","artifactReferences":{"api":"{{ApiReference}}","portal":"{{PortalReference}}"}},
              "runtimeConfiguration":{"schemaVersion":"__PROJECTION_SCHEMA__","projectionSha256":"__PROJECTION_HASH__","publicSettings":[{"targetApp":"api","name":"API_LOG_LEVEL","value":"info"},{"targetApp":"portal","name":"WEB_PRODUCT_NAME","value":"PageMaker365"}]}
            }
            """
            .Replace("__PACKAGE_VERSION__", packageVersion, StringComparison.Ordinal)
            .Replace("__CAPABILITY__", isV06 ? PrivateRuntimeDeliveryPackageV06.CapabilityValue : PrivateRuntimeDeliveryPackage.CapabilityValue, StringComparison.Ordinal)
            .Replace("__MANIFEST_VERSION__", isV06 ? "3.0" : "2.0", StringComparison.Ordinal)
            .Replace("__PRODUCT__", isV06 ? "\"product\":\"PageMaker365\"," : "", StringComparison.Ordinal)
            .Replace("__ONBOARDING_SESSION__", OnboardingSessionId, StringComparison.Ordinal)
            .Replace("__SIGNING_KEY__", keyId, StringComparison.Ordinal)
            .Replace("__MANIFEST_HASH__", manifestHash, StringComparison.Ordinal)
            .Replace("__RELEASE_ID__", ReleaseId, StringComparison.Ordinal)
            .Replace("__RUNTIME_VERSION__", runtimeVersion, StringComparison.Ordinal)
            .Replace("__SOURCE_COMMIT__", packageSourceCommit, StringComparison.Ordinal)
            .Replace("__API_FILE_NAME__", apiFileName, StringComparison.Ordinal)
            .Replace("__PORTAL_FILE_NAME__", portalFileName, StringComparison.Ordinal)
            .Replace("__API_SIZE__", apiZip.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__PORTAL_SIZE__", portalZip.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__API_HASH__", apiHash, StringComparison.Ordinal)
            .Replace("__PORTAL_HASH__", portalHash, StringComparison.Ordinal)
            .Replace("__PROJECTION_SCHEMA__", projectionSchema, StringComparison.Ordinal)
            .Replace("__PROJECTION_HASH__", projectionHash, StringComparison.Ordinal)
            .Replace("{{ApiReference}}", ApiReference, StringComparison.Ordinal)
            .Replace("{{PortalReference}}", PortalReference, StringComparison.Ordinal);
        using var unsignedDocument = JsonDocument.Parse(unsigned);
        var payload = PrivateRuntimeDeliveryPackageService.CanonicalizeSigningPayload(unsignedDocument.RootElement);
        var packageHash = "sha256:" + Hash(payload);
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(payload, 0, payload.Length);
        var signature = Base64Url(signer.GenerateSignature());
        var signedJson = unsigned.Replace("\"packageHash\":\"\"", "\"packageHash\":\"" + packageHash + "\"", StringComparison.Ordinal)
            .Replace("\"signature\":\"\"", "\"signature\":\"" + signature + "\"", StringComparison.Ordinal);
        using var signedDocument = JsonDocument.Parse(signedJson);
        var json = isV06
            ? PrivateRuntimeDeliveryV06PackageService.FormatCanonicalPackage(signedDocument.RootElement)
            : PrivateRuntimeDeliveryPackageService.FormatCanonicalPackage(signedDocument.RootElement);
        return new PackageFixture(json, new PackageTrustOptions
        {
            TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [keyId] = publicKeyPem }
        });
    }

    private static string FormatManifestV3(
        string sourceCommit,
        string runtimeVersion,
        string apiFileName,
        int apiSize,
        string apiHash,
        string portalFileName,
        int portalSize,
        string portalHash) => $$"""
        {
          "contractVersion": "3.0",
          "product": "PageMaker365",
          "releaseId": "{{ReleaseId}}",
          "runtimeVersion": "{{runtimeVersion}}",
          "sourceRepository": "cloudbossdev/spo-ui",
          "sourceCommit": "{{sourceCommit}}",
          "provenanceSchemaVersion": "pagemaker365.runtime-provenance.v1",
          "api": {
            "fileName": "{{apiFileName}}",
            "sizeBytes": {{apiSize}},
            "sha256": "{{apiHash}}",
            "startupCommand": "node dist/index.js",
            "artifactKind": "api"
          },
          "portal": {
            "fileName": "{{portalFileName}}",
            "sizeBytes": {{portalSize}},
            "sha256": "{{portalHash}}",
            "startupCommand": "node .pm365/start-portal-runtime.mjs",
            "artifactKind": "portal"
          }
        }
        """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

    private static OnboardingBootstrapSession CreateOnboardingSession() => new()
    {
        SessionId = OnboardingSessionId,
        OneTimeCode = "test-one-time-code",
        ApiBaseUrl = "https://localhost:5443"
    };

    private static byte[] CreateRuntimeArchive(string kind, string? provenanceSourceCommit = null, string? unsafeEntry = null)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (kind == "api")
            {
                WriteEntry(archive, "dist/index.js", "console.log('api');");
                WriteEntry(archive, "package.json", "{\"name\":\"fixture-api\"}");
            }
            else
            {
                WriteEntry(archive, "index.html", $"<html><head><meta name=\"pm365-release-id\" content=\"{ReleaseId}\"></head><body>PageMaker365</body></html>");
                WriteEntry(archive, "auth-redirect.html", "<html>PageMaker365</html>");
                WriteEntry(archive, ".pm365/start-portal-runtime.mjs", "console.log('start');");
                WriteEntry(archive, ".pm365/generate-web-runtime-config.mjs", "console.log('config');");
            }
            var startup = kind == "api" ? "node dist/index.js" : "node .pm365/start-portal-runtime.mjs";
            WriteEntry(archive, ".pm365/provenance.json", $$"""{"schemaVersion":"pagemaker365.runtime-provenance.v1","product":"PageMaker365","artifactKind":"{{kind}}","releaseId":"{{ReleaseId}}","runtimeVersion":"0.1.0","sourceRepository":"cloudbossdev/spo-ui","sourceCommit":"{{provenanceSourceCommit ?? new string('a', 40)}}","dependencyLockSha256":"{{new string('d', 64)}}","startupCommand":"{{startup}}"}""");
            if (unsafeEntry is not null) WriteEntry(archive, unsafeEntry, "unsafe");
        }
        return output.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static HttpResponseMessage ArtifactResponse(HttpStatusCode status, byte[] bytes, PrivateRuntimeArtifact artifact, ContentRangeHeaderValue? range)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Headers.CacheControl = new CacheControlHeaderValue { Private = true, NoStore = true };
        response.Headers.ETag = new EntityTagHeaderValue($"\"sha256:{artifact.Sha256}\"");
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        response.Content.Headers.ContentLength = bytes.Length;
        response.Content.Headers.ContentRange = range;
        return response;
    }

    private static string SessionResponse() =>
        "{\"ok\":true,\"created\":true,\"deliverySession\":{\"contractVersion\":\"pagemaker365.runtime-delivery-session.v1\",\"deliverySessionId\":\"rds_ABCDEFGHIJKLMNOPQRSTUVWXYZ\",\"expiresAt\":\"2029-12-31T23:59:59.000Z\",\"artifactKinds\":[\"api\",\"portal\"],\"status\":\"active\"}}";

    private static string ReceiptResponse(JsonElement requestReceipt)
    {
        var accepted = new
        {
            deliverySessionId = requestReceipt.GetProperty("deliverySessionId").GetString(),
            packageHash = requestReceipt.GetProperty("packageHash").GetString(),
            releaseId = requestReceipt.GetProperty("releaseId").GetString(),
            eventId = requestReceipt.GetProperty("eventId").GetString(),
            occurredAt = requestReceipt.GetProperty("occurredAt").GetString(),
            installerVersion = requestReceipt.GetProperty("installerVersion").GetString(),
            outcome = requestReceipt.GetProperty("outcome").GetString(),
            artifacts = requestReceipt.GetProperty("artifacts"),
            safeResult = requestReceipt.GetProperty("safeResult"),
            createdAt = "2026-08-17T00:00:00.000Z"
        };
        return JsonSerializer.Serialize(new { ok = true, created = true, receipt = accepted });
    }

    private static string MismatchedReceiptResponse(JsonElement requestReceipt)
    {
        var accepted = new
        {
            deliverySessionId = requestReceipt.GetProperty("deliverySessionId").GetString(),
            packageHash = requestReceipt.GetProperty("packageHash").GetString(),
            releaseId = requestReceipt.GetProperty("releaseId").GetString(),
            eventId = requestReceipt.GetProperty("eventId").GetString(),
            occurredAt = requestReceipt.GetProperty("occurredAt").GetString(),
            installerVersion = requestReceipt.GetProperty("installerVersion").GetString(),
            outcome = requestReceipt.GetProperty("outcome").GetString(),
            artifacts = requestReceipt.GetProperty("artifacts"),
            safeResult = new { code = "runtime_artifacts_failed", state = "failed" },
            createdAt = "2026-08-17T00:00:00.000Z"
        };
        return JsonSerializer.Serialize(new { ok = true, created = true, receipt = accepted });
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pm365-private-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string ToPem(string label, byte[] der)
    {
        var body = Convert.ToBase64String(der);
        var lines = Enumerable.Range(0, (body.Length + 63) / 64).Select(index => body.Substring(index * 64, Math.Min(64, body.Length - index * 64)));
        return $"-----BEGIN {label}-----\n{string.Join("\n", lines)}\n-----END {label}-----\n";
    }

    private sealed record PackageFixture(string Json, PackageTrustOptions TrustOptions);

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
