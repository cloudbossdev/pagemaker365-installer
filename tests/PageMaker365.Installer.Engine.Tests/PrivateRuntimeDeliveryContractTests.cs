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

    public static async Task AcquiresPrivateStreamsWithHeaderOnlyReferencesAndRangeResume()
    {
        var apiZip = CreateRuntimeArchive("api");
        var portalZip = CreateRuntimeArchive("portal");
        var fixture = CreatePackage(apiZip, portalZip);
        var package = new PrivateRuntimeDeliveryPackageService().ValidateJson(fixture.Json, fixture.TrustOptions, new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero));
        var outputRoot = CreateTemporaryDirectory();
        var requestedPaths = new List<string>();
        try
        {
            var partialPath = Path.Combine(outputRoot, "runtime-acquisition", $"portal-{package.Portal.Sha256}.partial");
            Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
            var resumeAt = portalZip.Length / 2;
            await File.WriteAllBytesAsync(partialPath, portalZip[..resumeAt]);
            var requestCount = 0;
            using var delivery = new PrivateRuntimeDeliveryClient(new PrivateRuntimeDeliveryOptions { Timeout = TimeSpan.FromMinutes(1) }, new ScriptedHandler(request =>
            {
                requestedPaths.Add(request.RequestUri!.PathAndQuery);
                requestCount++;
                if (request.RequestUri!.AbsolutePath == PrivateRuntimeDeliveryPackage.SessionPathValue)
                {
                    AssertEx.Equal(HttpMethod.Post, request.Method);
                    var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    AssertEx.False(body.Contains("ard_", StringComparison.Ordinal), "Delivery references must not be sent in session JSON.");
                    AssertEx.False(body.Contains("download", StringComparison.OrdinalIgnoreCase));
                    return JsonResponse(SessionResponse());
                }

                if (request.RequestUri!.AbsolutePath == PrivateRuntimeDeliveryPackage.ReceiptPathValue)
                {
                    AssertEx.Equal(HttpMethod.Post, request.Method);
                    var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    AssertEx.False(body.Contains(ApiReference, StringComparison.Ordinal));
                    AssertEx.False(body.Contains(PortalReference, StringComparison.Ordinal));
                    using var receipt = JsonDocument.Parse(body);
                    var idempotencyKey = receipt.RootElement.GetProperty("idempotencyKey").GetString();
                    return JsonResponse(ReceiptResponse(idempotencyKey!));
                }

                AssertEx.Equal(HttpMethod.Get, request.Method);
                AssertEx.False(request.RequestUri!.PathAndQuery.Contains("ard_", StringComparison.Ordinal));
                AssertEx.False(request.RequestUri!.Query.Contains("delivery", StringComparison.OrdinalIgnoreCase));
                AssertEx.Contains(request.Headers.GetValues(PrivateRuntimeDeliveryClient.DeliverySessionHeader), "rds_ABCDEFGHIJKLMNOPQRSTUVWXYZ");
                AssertEx.Contains(request.Headers.GetValues(PrivateRuntimeDeliveryClient.PackageHashHeader), package.PackageHash);
                var kind = request.RequestUri.AbsolutePath.EndsWith("/api", StringComparison.Ordinal) ? "api" : "portal";
                var expectedReference = kind == "api" ? ApiReference : PortalReference;
                AssertEx.Contains(request.Headers.GetValues(PrivateRuntimeDeliveryClient.DeliveryReferenceHeader), expectedReference);
                AssertEx.False(request.Headers.TryGetValues("Location", out _));

                if (kind == "api")
                {
                    AssertEx.True(request.Headers.Range is null);
                    return ArtifactResponse(HttpStatusCode.OK, apiZip, package.Api, null);
                }

                AssertEx.Equal(resumeAt, request.Headers.Range!.Ranges.Single().From);
                AssertEx.Equal(portalZip.Length - 1, request.Headers.Range.Ranges.Single().To);
                return ArtifactResponse(HttpStatusCode.PartialContent, portalZip[resumeAt..], package.Portal, new ContentRangeHeaderValue(resumeAt, portalZip.Length - 1, portalZip.Length));
            }));
            var result = await delivery.AcquireAsync(fixture.Json, fixture.TrustOptions, CreateOnboardingSession(), outputRoot, "0.1.0", CancellationToken.None);

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

    private static PackageFixture CreatePackage(byte[] apiZip, byte[] portalZip)
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        var privateKey = (Ed25519PrivateKeyParameters)pair.Private;
        var publicKey = (Ed25519PublicKeyParameters)pair.Public;
        var publicKeyPem = ToPem("PUBLIC KEY", SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey).GetDerEncoded());
        var keyId = "private-runtime-test-key";
        var apiHash = Hash(apiZip);
        var portalHash = Hash(portalZip);
        const string projectionSchema = "pagemaker365.runtime-configuration-projection.v1";
        var projection = "{\"publicSettings\":[{\"name\":\"API_LOG_LEVEL\",\"targetApp\":\"api\",\"value\":\"info\"},{\"name\":\"WEB_PRODUCT_NAME\",\"targetApp\":\"portal\",\"value\":\"PageMaker365\"}],\"schemaVersion\":\"" + projectionSchema + "\"}";
        var projectionHash = Hash(Encoding.UTF8.GetBytes(projection));
        var unsigned = """
            {
              "contractVersion":"0.5",
              "customer":{"customerId":"11111111-1111-4111-8111-111111111111"},
              "installation":{"installationId":"22222222-2222-4222-8222-222222222222","environmentId":"33333333-3333-4333-8333-333333333333","tenantId":"44444444-4444-8444-8444-444444444444"},
              "deployment":{"deploymentExportId":"55555555-5555-4555-8555-555555555555"},
              "controlPlane":{"onboardingSessionId":"__ONBOARDING_SESSION__","expiresAt":"2030-01-01T00:00:00.000Z","acceptedInstallerCapability":"pagemaker365.customer-install.0.5.protected-acquisition.v1","packageHash":"","packageHashAlgorithm":"SHA-256","canonicalization":"json-c14n-v1","signatureAlgorithm":"Ed25519","signingKeyId":"__SIGNING_KEY__","signature":""},
              "runtimeArtifacts":{"manifestContractVersion":"2.0","manifestSha256":"__MANIFEST_HASH__","releaseId":"__RELEASE_ID__","runtimeVersion":"0.1.0","sourceRepository":"cloudbossdev/spo-ui","sourceCommit":"__SOURCE_COMMIT__","provenanceSchemaVersion":"pagemaker365.runtime-provenance.v1","api":{"artifactKind":"api","fileName":"pagemaker365-api-__RELEASE_ID__.zip","sizeBytes":__API_SIZE__,"sha256":"__API_HASH__","startupCommand":"node dist/index.js"},"portal":{"artifactKind":"portal","fileName":"pagemaker365-portal-__RELEASE_ID__.zip","sizeBytes":__PORTAL_SIZE__,"sha256":"__PORTAL_HASH__","startupCommand":"node .pm365/start-portal-runtime.mjs"}},
              "protectedAcquisition":{"contractVersion":"pagemaker365.protected-acquisition.v1","sessionPath":"/api/onboarding/installer/runtime-delivery-sessions","artifactPath":"/api/onboarding/installer/runtime-artifacts/{artifactKind}","receiptPath":"/api/onboarding/installer/runtime-delivery-receipts","authorizationMode":"installer-session-v1","expiresAt":"2030-01-01T00:00:00.000Z","artifactReferences":{"api":"{{ApiReference}}","portal":"{{PortalReference}}"}},
              "runtimeConfiguration":{"schemaVersion":"__PROJECTION_SCHEMA__","projectionSha256":"__PROJECTION_HASH__","publicSettings":[{"targetApp":"api","name":"API_LOG_LEVEL","value":"info"},{"targetApp":"portal","name":"WEB_PRODUCT_NAME","value":"PageMaker365"}]}
            }
            """
            .Replace("__ONBOARDING_SESSION__", OnboardingSessionId, StringComparison.Ordinal)
            .Replace("__SIGNING_KEY__", keyId, StringComparison.Ordinal)
            .Replace("__MANIFEST_HASH__", new string('b', 64), StringComparison.Ordinal)
            .Replace("__RELEASE_ID__", ReleaseId, StringComparison.Ordinal)
            .Replace("__SOURCE_COMMIT__", new string('a', 40), StringComparison.Ordinal)
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
        var json = PrivateRuntimeDeliveryPackageService.FormatCanonicalPackage(signedDocument.RootElement);
        return new PackageFixture(json, new PackageTrustOptions
        {
            TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [keyId] = publicKeyPem }
        });
    }

    private static OnboardingBootstrapSession CreateOnboardingSession() => new()
    {
        SessionId = OnboardingSessionId,
        OneTimeCode = "test-one-time-code",
        ApiBaseUrl = "https://localhost:5443"
    };

    private static byte[] CreateRuntimeArchive(string kind)
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
            WriteEntry(archive, ".pm365/provenance.json", $$"""{"schemaVersion":"pagemaker365.runtime-provenance.v1","product":"PageMaker365","artifactKind":"{{kind}}","releaseId":"{{ReleaseId}}","runtimeVersion":"0.1.0","sourceRepository":"cloudbossdev/spo-ui","sourceCommit":"{{new string('a', 40)}}","dependencyLockSha256":"{{new string('d', 64)}}","startupCommand":"{{startup}}"}""");
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
        response.Headers.ETag = new EntityTagHeaderValue($"\"{artifact.Sha256}\"");
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        response.Content.Headers.ContentLength = bytes.Length;
        response.Content.Headers.ContentRange = range;
        return response;
    }

    private static string SessionResponse() =>
        "{\"contractVersion\":\"pagemaker365.protected-acquisition.v1\",\"deliverySessionId\":\"rds_ABCDEFGHIJKLMNOPQRSTUVWXYZ\",\"expiresAt\":\"2029-12-31T23:59:59.000Z\",\"artifactReferences\":{\"api\":\"" + ApiReference + "\",\"portal\":\"" + PortalReference + "\"}}";

    private static string ReceiptResponse(string idempotencyKey) =>
        "{\"contractVersion\":\"pagemaker365.runtime-delivery-receipt.v1\",\"status\":\"accepted\",\"deliverySessionId\":\"rds_ABCDEFGHIJKLMNOPQRSTUVWXYZ\",\"idempotencyKey\":\"" + idempotencyKey + "\"}";

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
