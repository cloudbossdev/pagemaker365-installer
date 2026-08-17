using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

/// <summary>
/// Consumer-side lock for the PageMaker365-owned private-delivery fixture.
/// The asset files are copied raw, while producer provenance remains explicit
/// so installer changes cannot silently create a competing wire contract.
/// </summary>
internal static class PrivateRuntimeDeliveryFixtureTests
{
    private const string ProducerRepository = "cloudbossdev/pagemaker365";
    private const string ProducerCommit = "c203283887f6be78c3224ea0c863bc42da1a4f0b";
    private const string ProducerFixturePath = "apps/api/test/fixtures/private-runtime-delivery-v1";
    private const string ProducerManifestSha256 = "85bb5ce8af86c6fc01dd5dfa08c62c95fdadee118aa776e6596c38359da07f1c";
    private const string FixtureApiKeyEnvironmentVariable = "PM365_PRIVATE_DELIVERY_FIXTURE_API_KEY";
    private const string FixtureApiKey = "fixture-installer-bearer-token";
    private const string FixtureOnboardingCode = "fixture-one-time-code";

    public static Task LocksP365OwnedBytesAndAcceptsTheStrictSignedPackage()
    {
        var fixture = LoadFixture();
        using var fixtureManifest = ReadJson(fixture, "sha256-manifest.json");
        var root = fixtureManifest.RootElement;
        AssertExactProperties(root, "fixture SHA manifest", "schemaVersion", "files");
        AssertEx.Equal("pagemaker365.private-runtime-delivery-fixtures.v1", root.GetProperty("schemaVersion").GetString());

        var expectedFiles = root.GetProperty("files").EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
        AssertEx.True(expectedFiles.Select(property => property.Name).SequenceEqual(new[]
        {
            "customer-install-0.5.json",
            "receipt-request.json",
            "receipt-response.json",
            "runtime-delivery-http-vectors.json",
            "session-response.json",
            "signing-public-key.pem",
            "signing-trust.json",
            "spo-runtime-manifest-v2.json"
        }, StringComparer.Ordinal));

        var actualFiles = Directory.GetFiles(fixture.Directory)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        AssertEx.True(actualFiles.SequenceEqual(expectedFiles.Select(property => property.Name).Append("producer.json").Append("sha256-manifest.json").OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal));

        foreach (var expected in expectedFiles)
        {
            var bytes = File.ReadAllBytes(Path.Combine(fixture.Directory, expected.Name));
            AssertEx.Equal(expected.Value.GetString(), Sha256(bytes), $"P365 fixture byte lock failed for {expected.Name}.");
            AssertEx.False(Regex.IsMatch(Encoding.UTF8.GetString(bytes), "-----BEGIN (?:[A-Z0-9 ]+ )?PRIVATE KEY-----|privateKeyPem|private_key", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), $"Fixture contains private key material: {expected.Name}.");
        }

        using (var producer = ReadJson(fixture, "producer.json"))
        {
            AssertExactProperties(producer.RootElement, "fixture producer provenance", "sourceRepository", "sourceCommit", "sourceFixturePath", "sourceFixtureManifestSha256");
            AssertEx.Equal(ProducerRepository, producer.RootElement.GetProperty("sourceRepository").GetString());
            AssertEx.Equal(ProducerCommit, producer.RootElement.GetProperty("sourceCommit").GetString());
            AssertEx.Equal(ProducerFixturePath, producer.RootElement.GetProperty("sourceFixturePath").GetString());
            AssertEx.Equal(ProducerManifestSha256, producer.RootElement.GetProperty("sourceFixtureManifestSha256").GetString());
        }
        AssertEx.Equal(ProducerManifestSha256, Sha256(File.ReadAllBytes(Path.Combine(fixture.Directory, "sha256-manifest.json"))));

        var package = new PrivateRuntimeDeliveryPackageService().ValidateJson(fixture.PackageJson, fixture.TrustOptions);
        using var manifest = ReadJson(fixture, "spo-runtime-manifest-v2.json");
        AssertExactProperties(manifest.RootElement, "SPO runtime manifest 2.0", "contractVersion", "releaseId", "runtimeVersion", "sourceCommit", "api", "portal");
        AssertEx.Equal("2.0", manifest.RootElement.GetProperty("contractVersion").GetString());
        AssertEx.Equal(Sha256(File.ReadAllBytes(Path.Combine(fixture.Directory, "spo-runtime-manifest-v2.json"))), package.ManifestSha256);
        AssertEx.Equal(manifest.RootElement.GetProperty("releaseId").GetString(), package.ReleaseId);
        AssertEx.Equal(manifest.RootElement.GetProperty("runtimeVersion").GetString(), package.RuntimeVersion);
        AssertEx.Equal(manifest.RootElement.GetProperty("sourceCommit").GetString(), package.SourceCommit);
        AssertManifestArtifact(package.Api, manifest.RootElement.GetProperty("api"), "api");
        AssertManifestArtifact(package.Portal, manifest.RootElement.GetProperty("portal"), "portal");

        using var vectors = ReadJson(fixture, "runtime-delivery-http-vectors.json");
        ValidatesP365HttpVectors(package, vectors.RootElement, fixture);
        return Task.CompletedTask;
    }

    public static async Task MockTransportAcceptsP365SessionArtifactAndReceiptVectorsWithoutLeakingReferences()
    {
        var fixture = LoadFixture();
        var package = new PrivateRuntimeDeliveryPackageService().ValidateJson(fixture.PackageJson, fixture.TrustOptions);
        using var vectorsDocument = ReadJson(fixture, "runtime-delivery-http-vectors.json");
        var vectors = vectorsDocument.RootElement;
        var sessionVector = vectors.GetProperty("sessionCreate").Clone();
        var apiVector = vectors.GetProperty("api").Clone();
        var receiptVector = vectors.GetProperty("receipt").Clone();
        var requestedUrls = new List<string>();
        var outputRoot = Path.Combine(Path.GetTempPath(), "pm365-private-delivery-fixture-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var apiKey = new EnvironmentVariableScope(FixtureApiKeyEnvironmentVariable, FixtureApiKey);
            using var client = new PrivateRuntimeDeliveryClient(
                new PrivateRuntimeDeliveryOptions { ApiKeyEnvironmentVariable = FixtureApiKeyEnvironmentVariable, Timeout = TimeSpan.FromMinutes(1) },
                new FixtureHandler(request =>
                {
                    requestedUrls.Add(request.RequestUri!.PathAndQuery);
                    AssertNoReferenceInUrl(request.RequestUri, package);
                    if (request.RequestUri.AbsolutePath == PrivateRuntimeDeliveryPackage.SessionPathValue)
                    {
                        AssertFixtureRequest(request, sessionVector.GetProperty("request"));
                        using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                        AssertExactProperties(body.RootElement, "session request", "package");
                        AssertEx.Equal(fixture.PackageJson, PrivateRuntimeDeliveryPackageService.FormatCanonicalPackage(body.RootElement.GetProperty("package")));
                        return CreateFixtureResponse(sessionVector.GetProperty("response"), Encoding.UTF8.GetBytes(fixture.SessionResponseJson));
                    }

                    if (request.RequestUri.AbsolutePath == "/api/onboarding/installer/runtime-artifacts/api")
                    {
                        AssertFixtureRequest(request, apiVector.GetProperty("request"));
                        return CreateFixtureResponse(apiVector.GetProperty("fullResponse"), new byte[checked((int)package.Api.SizeBytes)]);
                    }

                    if (request.RequestUri.AbsolutePath == PrivateRuntimeDeliveryPackage.ReceiptPathValue)
                    {
                        AssertEx.False(request.Headers.Contains("X-PM365-Runtime-Delivery-Ref"));
                        AssertEx.False(request.Headers.Contains("X-PM365-Package-Hash"));
                        var receiptBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                        AssertNoReferenceInText(receiptBody, package);
                        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                        {
                            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("{}"))
                        };
                    }

                    throw new InvalidOperationException($"Unexpected private delivery request: {request.RequestUri.AbsolutePath}");
                }));

            var result = await client.AcquireAsync(fixture.PackageJson, fixture.TrustOptions, CreateFixtureOnboardingSession(package), outputRoot, "0.1.0-fixture");
            AssertEx.Equal("failed", result.Outcome);
            AssertEx.Equal("runtime_artifact_integrity_failed", result.SafeErrorCode);
            AssertEx.Equal("outbox_pending", result.ReceiptStatus);
            AssertEx.True(File.Exists(result.ReceiptOutboxPath));
            AssertNoReferenceInText(await File.ReadAllTextAsync(result.ReceiptOutboxPath), package);
            AssertEx.False(requestedUrls.Any(url => url.Contains(package.ApiDeliveryReference, StringComparison.Ordinal) || url.Contains(package.PortalDeliveryReference, StringComparison.Ordinal) || url.Contains('?', StringComparison.Ordinal)));
        }
        finally
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }

        await AssertReceiptAcknowledgementTransportAsync(fixture, package, receiptVector);
    }

    private static async Task AssertReceiptAcknowledgementTransportAsync(Fixture fixture, PrivateRuntimeDeliveryPackage package, JsonElement receiptVector)
    {
        var receiptRequestJson = File.ReadAllText(Path.Combine(fixture.Directory, "receipt-request.json"), new UTF8Encoding(false, true));
        var receipt = JsonSerializer.Deserialize<PrivateRuntimeDeliveryReceipt>(receiptRequestJson) ?? throw new InvalidDataException("Fixture receipt could not be parsed.");
        using var apiKey = new EnvironmentVariableScope(FixtureApiKeyEnvironmentVariable, FixtureApiKey);
        using var client = new PrivateRuntimeDeliveryClient(
            new PrivateRuntimeDeliveryOptions { ApiKeyEnvironmentVariable = FixtureApiKeyEnvironmentVariable, Timeout = TimeSpan.FromMinutes(1) },
            new FixtureHandler(request =>
            {
                AssertNoReferenceInUrl(request.RequestUri!, package);
                AssertFixtureRequest(request, receiptVector.GetProperty("request"));
                var requestJson = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                AssertNoReferenceInText(requestJson, package);
                AssertJsonEquivalent(receiptRequestJson, requestJson, "receipt request body");
                return CreateFixtureResponse(receiptVector.GetProperty("response"), Encoding.UTF8.GetBytes(fixture.ReceiptResponseJson));
            }));

        var submitReceipt = typeof(PrivateRuntimeDeliveryClient).GetMethod("SubmitReceiptAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("Private runtime receipt transport seam is unavailable.");
        var invocation = submitReceipt.Invoke(client, [package, CreateFixtureOnboardingSession(package), new Uri("https://localhost:5443/"), receipt, CancellationToken.None]);
        AssertEx.True(invocation is Task, "Receipt transport seam must return a task.");
        await (Task)invocation!;
    }

    private static void ValidatesP365HttpVectors(PrivateRuntimeDeliveryPackage package, JsonElement vectors, Fixture fixture)
    {
        AssertExactProperties(vectors, "private delivery HTTP vectors", "contractVersion", "corsOrigin", "nonProductionResponseHeaders", "productionResponseHeaderAdditions", "sessionCreate", "api", "portal", "receipt");
        AssertEx.Equal("pagemaker365.private-runtime-delivery-http-fixture.v1", vectors.GetProperty("contractVersion").GetString());
        AssertEx.Equal("https://installer.fixture.test", vectors.GetProperty("corsOrigin").GetString());
        AssertEx.False(File.ReadAllText(Path.Combine(fixture.Directory, "runtime-delivery-http-vectors.json")).Contains("X-PM365-Package-Hash", StringComparison.Ordinal));

        var common = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Authorization"] = "Bearer " + FixtureApiKey,
            ["X-PM365-Onboarding-Session"] = package.OnboardingSessionId,
            ["X-PM365-Onboarding-Code"] = FixtureOnboardingCode
        };
        var sessionHeaders = new Dictionary<string, string>(common, StringComparer.Ordinal)
        {
            ["Content-Type"] = "application/json"
        };
        AssertVectorRequest(vectors.GetProperty("sessionCreate").GetProperty("request"), HttpMethod.Post, PrivateRuntimeDeliveryPackage.SessionPathValue, sessionHeaders, "session create");
        AssertJsonResponseVector(vectors.GetProperty("sessionCreate").GetProperty("response"), "session create");

        foreach (var artifactKind in new[] { "api", "portal" })
        {
            var artifact = package.Artifact(artifactKind);
            var reference = package.DeliveryReference(artifactKind);
            var vector = vectors.GetProperty(artifactKind);
            var requestHeaders = new Dictionary<string, string>(common, StringComparer.Ordinal)
            {
                [PrivateRuntimeDeliveryClient.DeliverySessionHeader] = "rds_ABCDEFGHIJKLMNOPQRSTUVWXYZ",
                [PrivateRuntimeDeliveryClient.DeliveryReferenceHeader] = reference,
                ["If-Match"] = $"\"sha256:{artifact.Sha256}\""
            };
            AssertVectorRequest(vector.GetProperty("request"), HttpMethod.Get, PrivateRuntimeDeliveryPackage.ArtifactPathValue.Replace("{artifactKind}", artifactKind, StringComparison.Ordinal), requestHeaders, $"{artifactKind} full artifact");
            AssertArtifactResponseVector(vector.GetProperty("fullResponse"), artifact, null, $"{artifactKind} full artifact");

            var range = vector.GetProperty("rangeResponse");
            var rangeHeaders = new Dictionary<string, string>(requestHeaders, StringComparer.Ordinal)
            {
                ["Range"] = range.GetProperty("requestHeaders").GetProperty("Range").GetString()!
            };
            AssertHeaderObject(range.GetProperty("requestHeaders"), rangeHeaders, $"{artifactKind} range request");
            var (start, end) = ParseRange(rangeHeaders["Range"], artifact.SizeBytes);
            AssertArtifactResponseVector(range.GetProperty("headers"), artifact, (start, end), $"{artifactKind} range artifact");
            AssertEx.Equal(206, range.GetProperty("statusCode").GetInt32());
        }

        var receiptHeaders = new Dictionary<string, string>(common, StringComparer.Ordinal)
        {
            [PrivateRuntimeDeliveryClient.DeliverySessionHeader] = "rds_ABCDEFGHIJKLMNOPQRSTUVWXYZ",
            ["Idempotency-Key"] = "fixture-runtime-delivery-receipt-001",
            ["Content-Type"] = "application/json"
        };
        AssertVectorRequest(vectors.GetProperty("receipt").GetProperty("request"), HttpMethod.Post, PrivateRuntimeDeliveryPackage.ReceiptPathValue, receiptHeaders, "receipt");
        AssertJsonResponseVector(vectors.GetProperty("receipt").GetProperty("response"), "receipt");
        AssertNoReferenceInText(File.ReadAllText(Path.Combine(fixture.Directory, "receipt-request.json")), package);
        AssertNoReferenceInText(fixture.ReceiptResponseJson, package);
    }

    private static void AssertManifestArtifact(PrivateRuntimeArtifact artifact, JsonElement manifestArtifact, string expectedKind)
    {
        AssertExactProperties(manifestArtifact, $"{expectedKind} manifest artifact", "fileName", "sizeBytes", "sha256", "startupCommand");
        AssertEx.Equal(manifestArtifact.GetProperty("fileName").GetString(), artifact.FileName);
        AssertEx.Equal(manifestArtifact.GetProperty("sizeBytes").GetInt64(), artifact.SizeBytes);
        AssertEx.Equal(manifestArtifact.GetProperty("sha256").GetString(), artifact.Sha256);
        AssertEx.Equal(manifestArtifact.GetProperty("startupCommand").GetString(), artifact.StartupCommand);
        AssertEx.Equal(expectedKind, artifact.ArtifactKind);
    }

    private static void AssertVectorRequest(JsonElement request, HttpMethod method, string path, IReadOnlyDictionary<string, string> expectedHeaders, string description)
    {
        AssertExactProperties(request, description + " request", "method", "path", "headers");
        AssertEx.Equal(method.Method, request.GetProperty("method").GetString());
        AssertEx.Equal(path, request.GetProperty("path").GetString());
        AssertHeaderObject(request.GetProperty("headers"), expectedHeaders, description + " headers");
    }

    private static void AssertJsonResponseVector(JsonElement response, string description)
    {
        AssertExactProperties(response, description + " response", "statusCode", "headers");
        AssertEx.Equal(201, response.GetProperty("statusCode").GetInt32());
        var headers = response.GetProperty("headers");
        AssertResponseSecurityHeaders(headers, description);
        AssertEx.Equal("application/json; charset=utf-8", headers.GetProperty("Content-Type").GetString());
        AssertEx.Equal("no-store", headers.GetProperty("Cache-Control").GetString());
    }

    private static void AssertArtifactResponseVector(JsonElement response, PrivateRuntimeArtifact artifact, (long Start, long End)? range, string description)
    {
        var headers = response.TryGetProperty("headers", out var nestedHeaders) ? nestedHeaders : response;
        var expectedBytes = range is null ? artifact.SizeBytes : range.Value.End - range.Value.Start + 1;
        AssertResponseSecurityHeaders(headers, description);
        AssertEx.Equal("application/zip", headers.GetProperty("Content-Type").GetString());
        AssertEx.Equal(expectedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), headers.GetProperty("Content-Length").GetString());
        AssertEx.Equal($"attachment; filename=\"{artifact.FileName}\"", headers.GetProperty("Content-Disposition").GetString());
        AssertEx.Equal("bytes", headers.GetProperty("Accept-Ranges").GetString());
        AssertEx.Equal("private, no-store", headers.GetProperty("Cache-Control").GetString());
        AssertEx.Equal($"\"sha256:{artifact.Sha256}\"", headers.GetProperty("ETag").GetString());
        AssertEx.False(headers.TryGetProperty("Location", out _), $"{description} must not redirect.");
        if (range is not null)
        {
            AssertEx.Equal($"bytes {range.Value.Start}-{range.Value.End}/{artifact.SizeBytes}", headers.GetProperty("Content-Range").GetString());
        }
    }

    private static void AssertResponseSecurityHeaders(JsonElement headers, string description)
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
            ["Pragma"] = "no-cache",
            ["X-Content-Type-Options"] = "nosniff",
            ["X-Frame-Options"] = "DENY",
            ["X-Permitted-Cross-Domain-Policies"] = "none",
            ["Referrer-Policy"] = "no-referrer",
            ["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()",
            ["Cross-Origin-Resource-Policy"] = "same-site",
            ["Access-Control-Allow-Origin"] = "https://installer.fixture.test",
            ["Access-Control-Allow-Credentials"] = "true",
            ["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS",
            ["Access-Control-Allow-Headers"] = "Authorization,Content-Type,Idempotency-Key,X-Idempotency-Key,X-Admin-API-Key,X-Customer-Portal-Email,X-Customer-Portal-Code,X-Scheduling-Token,X-PM365-Onboarding-Session,X-PM365-Onboarding-Code,X-PM365-Initial-Install-Code,X-PM365-Initial-Install-Receipt",
            ["Access-Control-Max-Age"] = "86400"
        };
        foreach (var header in expected)
        {
            AssertEx.Equal(header.Value, headers.GetProperty(header.Key).GetString(), $"{description} response security header differs: {header.Key}.");
        }
        AssertEx.False(headers.TryGetProperty("Strict-Transport-Security", out _), $"{description} fixture must keep the production-only HSTS addition separate.");
    }

    private static void AssertFixtureRequest(HttpRequestMessage request, JsonElement expectedRequest)
    {
        AssertEx.Equal(expectedRequest.GetProperty("method").GetString(), request.Method.Method);
        AssertEx.Equal(expectedRequest.GetProperty("path").GetString(), request.RequestUri!.AbsolutePath);
        AssertHeaderObjectFromRequest(request, expectedRequest.GetProperty("headers"));
    }

    private static void AssertHeaderObjectFromRequest(HttpRequestMessage request, JsonElement expectedHeaders)
    {
        var expected = ToHeaderMap(expectedHeaders);
        var actual = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .Where(header => IsWireHeader(header.Key))
            .ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
        AssertEx.True(actual.Count == expected.Count, "Request contained unexpected or missing private-delivery headers.");
        foreach (var header in expected)
        {
            AssertEx.True(actual.TryGetValue(header.Key, out var actualValue), $"Missing request header: {header.Key}.");
            AssertEx.Equal(header.Value, actualValue, $"Unexpected request header: {header.Key}.");
        }
        AssertEx.False(actual.ContainsKey("X-PM365-Package-Hash"), "The locked P365 contract has no package-hash request header.");
    }

    private static bool IsWireHeader(string name) => name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Idempotency-Key", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("If-Match", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Range", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("X-PM365-", StringComparison.OrdinalIgnoreCase);

    private static void AssertHeaderObject(JsonElement headerObject, IReadOnlyDictionary<string, string> expected, string description)
    {
        var actual = ToHeaderMap(headerObject);
        AssertEx.True(actual.Count == expected.Count, $"{description} header count differs.");
        foreach (var header in expected)
        {
            AssertEx.True(actual.TryGetValue(header.Key, out var actualValue), $"{description} is missing {header.Key}.");
            AssertEx.Equal(header.Value, actualValue, $"{description} differs at {header.Key}.");
        }
        AssertEx.False(actual.ContainsKey("X-PM365-Package-Hash"), $"{description} must not add a package-hash header.");
    }

    private static Dictionary<string, string> ToHeaderMap(JsonElement headerObject)
    {
        if (headerObject.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Fixture headers must be an object.");
        return headerObject.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString() ?? throw new InvalidDataException("Fixture header value is invalid."), StringComparer.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage CreateFixtureResponse(JsonElement responseVector, byte[] content)
    {
        var response = new HttpResponseMessage((HttpStatusCode)responseVector.GetProperty("statusCode").GetInt32())
        {
            Content = new ByteArrayContent(content)
        };
        foreach (var header in responseVector.GetProperty("headers").EnumerateObject())
        {
            if (header.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                response.Content.Headers.ContentLength = long.Parse(header.Value.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (!response.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString()))
            {
                response.Content.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString());
            }
        }
        return response;
    }

    private static (long Start, long End) ParseRange(string value, long sizeBytes)
    {
        var match = Regex.Match(value, "^bytes=([0-9]+)-([0-9]+)$", RegexOptions.CultureInvariant);
        AssertEx.True(match.Success, "Fixture range header is invalid.");
        var start = long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var end = long.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        AssertEx.True(start >= 0 && end >= start && end < sizeBytes, "Fixture range bounds are invalid.");
        return (start, end);
    }

    private static void AssertNoReferenceInUrl(Uri uri, PrivateRuntimeDeliveryPackage package)
    {
        AssertNoReferenceInText(uri.PathAndQuery, package);
        AssertEx.False(uri.PathAndQuery.Contains('?', StringComparison.Ordinal), "Private delivery URL must not contain a query string.");
    }

    private static void AssertNoReferenceInText(string text, PrivateRuntimeDeliveryPackage package)
    {
        AssertEx.False(text.Contains(package.ApiDeliveryReference, StringComparison.Ordinal));
        AssertEx.False(text.Contains(package.PortalDeliveryReference, StringComparison.Ordinal));
    }

    private static void AssertJsonEquivalent(string expectedJson, string actualJson, string description)
    {
        using var expected = JsonDocument.Parse(expectedJson);
        using var actual = JsonDocument.Parse(actualJson);
        AssertJsonEquivalent(expected.RootElement, actual.RootElement, description);
    }

    private static void AssertJsonEquivalent(JsonElement expected, JsonElement actual, string description)
    {
        AssertEx.Equal(expected.ValueKind, actual.ValueKind, description + " value kind differs.");
        if (expected.ValueKind == JsonValueKind.Object)
        {
            var expectedProperties = expected.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
            var actualProperties = actual.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
            AssertEx.True(expectedProperties.Select(property => property.Name).SequenceEqual(actualProperties.Select(property => property.Name), StringComparer.Ordinal), description + " properties differ.");
            foreach (var property in expectedProperties)
            {
                AssertJsonEquivalent(property.Value, actual.GetProperty(property.Name), description + "." + property.Name);
            }
            return;
        }
        if (expected.ValueKind == JsonValueKind.Array)
        {
            var expectedItems = expected.EnumerateArray().ToArray();
            var actualItems = actual.EnumerateArray().ToArray();
            AssertEx.True(expectedItems.Length == actualItems.Length, description + " item count differs.");
            for (var index = 0; index < expectedItems.Length; index++) AssertJsonEquivalent(expectedItems[index], actualItems[index], description + "[" + index + "]");
            return;
        }
        if (expected.ValueKind == JsonValueKind.String)
        {
            AssertEx.Equal(expected.GetString(), actual.GetString(), description + " string value differs.");
            return;
        }
        AssertEx.Equal(expected.GetRawText(), actual.GetRawText(), description + $" value differs: expected {expected.GetRawText()}, actual {actual.GetRawText()}.");
    }

    private static void AssertExactProperties(JsonElement element, string description, params string[] expectedProperties)
    {
        AssertEx.True(element.ValueKind == JsonValueKind.Object, description + " must be an object.");
        AssertEx.True(element.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(expectedProperties.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal), description + " properties differ.");
    }

    private static OnboardingBootstrapSession CreateFixtureOnboardingSession(PrivateRuntimeDeliveryPackage package) => new()
    {
        SessionId = package.OnboardingSessionId,
        OneTimeCode = FixtureOnboardingCode,
        ApiBaseUrl = "https://localhost:5443"
    };

    private static Fixture LoadFixture()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-delivery-v1");
        using var trust = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "signing-trust.json")));
        AssertExactProperties(trust.RootElement, "fixture signing trust", "contractVersion", "keys");
        AssertEx.Equal("pagemaker365.customer-install-package-trust.v1", trust.RootElement.GetProperty("contractVersion").GetString());
        var keys = trust.RootElement.GetProperty("keys");
        AssertEx.True(keys.ValueKind == JsonValueKind.Array && keys.GetArrayLength() == 1, "Fixture signing trust must contain one key.");
        var key = keys[0];
        AssertExactProperties(key, "fixture signing key", "keyId", "algorithm", "publicKeyPemFile", "publicKeySha256", "purpose");
        AssertEx.Equal("Ed25519", key.GetProperty("algorithm").GetString());
        AssertEx.Equal("signing-public-key.pem", key.GetProperty("publicKeyPemFile").GetString());
        AssertEx.Equal("test-fixture-only", key.GetProperty("purpose").GetString());
        var publicKeyPem = File.ReadAllText(Path.Combine(directory, "signing-public-key.pem"), new UTF8Encoding(false, true));
        AssertEx.Equal(key.GetProperty("publicKeySha256").GetString(), Sha256(Encoding.UTF8.GetBytes(publicKeyPem)));
        return new Fixture(
            directory,
            File.ReadAllText(Path.Combine(directory, "customer-install-0.5.json"), new UTF8Encoding(false, true)),
            File.ReadAllText(Path.Combine(directory, "session-response.json"), new UTF8Encoding(false, true)),
            File.ReadAllText(Path.Combine(directory, "receipt-response.json"), new UTF8Encoding(false, true)),
            new PackageTrustOptions
            {
                TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [key.GetProperty("keyId").GetString()!] = publicKeyPem
                }
            });
    }

    private static JsonDocument ReadJson(Fixture fixture, string fileName) => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture.Directory, fileName)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PageMaker365.Installer.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root from the test output directory.");
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record Fixture(string Directory, string PackageJson, string SessionResponseJson, string ReceiptResponseJson, PackageTrustOptions TrustOptions);

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
