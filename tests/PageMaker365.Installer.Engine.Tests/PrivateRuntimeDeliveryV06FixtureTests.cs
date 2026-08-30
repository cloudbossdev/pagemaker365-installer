using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

internal static class PrivateRuntimeDeliveryV06FixtureTests
{
    private const string ProducerRepository = "cloudbossdev/pagemaker365";
    private const string ProducerCommit = "d4edd4b16e417ff3d4f519f9d622ac8bb0090712";
    private const string ProducerFixturePath = "apps/api/test/fixtures/private-runtime-delivery-v2";
    private const string ProducerFixtureManifestSha256 = "bd8b2af6373a16bfb654dc0501815d4ace9d31c2050ab6408f1cb8e5917d6175";

    public static Task LocksAcceptedProducerBytesAndValidatesV06V3Pair()
    {
        var fixture = LoadFixture();
        using var hashManifest = ReadJson(fixture.Directory, "sha256-manifest.json");
        AssertExactProperties(hashManifest.RootElement, "fixture hash manifest", "schemaVersion", "files");
        AssertEx.Equal("pagemaker365.private-runtime-delivery-fixtures.v2", hashManifest.RootElement.GetProperty("schemaVersion").GetString());

        var expectedFiles = hashManifest.RootElement.GetProperty("files").EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
        var expectedNames = new[]
        {
            "customer-install-0.6.json",
            "receipt-request.json",
            "receipt-response.json",
            "runtime-delivery-http-vectors.json",
            "runtime-release-manifest-v3.schema.json",
            "session-response.json",
            "signing-public-key.pem",
            "signing-trust.json",
            "spo-runtime-manifest-v3.json"
        };
        AssertEx.True(expectedFiles.Select(property => property.Name).SequenceEqual(expectedNames, StringComparer.Ordinal));
        foreach (var expected in expectedFiles)
        {
            var bytes = File.ReadAllBytes(Path.Combine(fixture.Directory, expected.Name));
            AssertEx.Equal(expected.Value.GetString(), Sha256(bytes), $"Producer fixture byte lock failed for {expected.Name}.");
            AssertEx.False(Encoding.UTF8.GetString(bytes).Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase), $"Producer fixture contains private signing material: {expected.Name}.");
        }

        var actualNames = Directory.GetFiles(fixture.Directory).Select(Path.GetFileName).Where(name => name is not null).Select(name => name!).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        AssertEx.True(actualNames.SequenceEqual(expectedNames.Append("producer.json").Append("sha256-manifest.json").OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal));
        AssertEx.Equal(ProducerFixtureManifestSha256, Sha256(File.ReadAllBytes(Path.Combine(fixture.Directory, "sha256-manifest.json"))));

        using (var producer = ReadJson(fixture.Directory, "producer.json"))
        {
            AssertExactProperties(producer.RootElement, "fixture producer", "sourceRepository", "sourceCommit", "sourceFixturePath", "sourceFixtureManifestSha256");
            AssertEx.Equal(ProducerRepository, producer.RootElement.GetProperty("sourceRepository").GetString());
            AssertEx.Equal(ProducerCommit, producer.RootElement.GetProperty("sourceCommit").GetString());
            AssertEx.Equal(ProducerFixturePath, producer.RootElement.GetProperty("sourceFixturePath").GetString());
            AssertEx.Equal(ProducerFixtureManifestSha256, producer.RootElement.GetProperty("sourceFixtureManifestSha256").GetString());
        }

        var package = new PrivateRuntimeDeliveryV06PackageService().ValidateJson(
            fixture.PackageJson,
            fixture.TrustOptions,
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        AssertEx.Equal("0.6", package.ContractVersion);
        AssertEx.Equal("3.0", package.ManifestContractVersion);
        AssertEx.Equal("PageMaker365", package.Product);
        AssertEx.Equal(fixture.PackageJson, CanonicalV06(fixture.PackageJson));

        using var manifest = ReadJson(fixture.Directory, "spo-runtime-manifest-v3.json");
        AssertExactProperties(manifest.RootElement, "SPO manifest v3", "contractVersion", "product", "releaseId", "runtimeVersion", "sourceRepository", "sourceCommit", "provenanceSchemaVersion", "api", "portal");
        AssertEx.Equal("3.0", manifest.RootElement.GetProperty("contractVersion").GetString());
        AssertEx.Equal("PageMaker365", manifest.RootElement.GetProperty("product").GetString());
        AssertEx.Equal(Sha256(File.ReadAllBytes(Path.Combine(fixture.Directory, "spo-runtime-manifest-v3.json"))), package.ManifestSha256);
        AssertManifestIdentity(package, manifest.RootElement);

        using var vectors = ReadJson(fixture.Directory, "runtime-delivery-http-vectors.json");
        AssertExactProperties(vectors.RootElement, "v0.6 HTTP vectors", "contractVersion", "session", "api", "portal", "receipt");
        AssertEx.Equal("pagemaker365.private-runtime-delivery-http-fixture.v2", vectors.RootElement.GetProperty("contractVersion").GetString());
        AssertEx.Equal(PrivateRuntimeDeliveryPackage.SessionPathValue, vectors.RootElement.GetProperty("session").GetProperty("path").GetString());
        AssertEx.Equal(package.ApiDeliveryReference, vectors.RootElement.GetProperty("api").GetProperty("request").GetProperty("deliveryRef").GetString());
        AssertEx.Equal(package.PortalDeliveryReference, vectors.RootElement.GetProperty("portal").GetProperty("request").GetProperty("deliveryRef").GetString());
        return Task.CompletedTask;
    }

    public static Task RejectsEveryMixedOrUnknownPackageManifestPair()
    {
        var v06 = LoadFixture();
        var v05Directory = Path.Combine(FindRepositoryRoot(), "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-delivery-v1");
        var v05Json = File.ReadAllText(Path.Combine(v05Directory, "customer-install-0.5.json"), new UTF8Encoding(false, true));
        var v05Trust = LoadTrust(v05Directory);
        var historical = new PrivateRuntimeDeliveryPackageService().ValidateJson(v05Json, v05Trust, new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        AssertEx.Equal("0.5", historical.ContractVersion);
        AssertEx.Equal("2.0", historical.ManifestContractVersion);

        AssertEx.Throws<InvalidDataException>(() => new PrivateRuntimeDeliveryV06PackageService().ValidateJson(v05Json, v05Trust));
        AssertEx.Throws<InvalidDataException>(() => new PrivateRuntimeDeliveryPackageService().ValidateJson(v06.PackageJson, v06.TrustOptions));
        AssertEx.Throws<InvalidDataException>(() => new PrivateRuntimeDeliveryV06PackageService().ValidateJson(
            v06.PackageJson.Replace("\"manifestContractVersion\": \"3.0\"", "\"manifestContractVersion\": \"2.0\"", StringComparison.Ordinal),
            v06.TrustOptions));
        AssertEx.Throws<InvalidDataException>(() => new PrivateRuntimeDeliveryV06PackageService().ValidateJson(
            v06.PackageJson.Replace("\"contractVersion\": \"0.6\"", "\"contractVersion\": \"0.7\"", StringComparison.Ordinal),
            v06.TrustOptions));
        AssertEx.Throws<InvalidDataException>(() => new PrivateRuntimeDeliveryV06PackageService().ValidateJson(
            v06.PackageJson.Replace("\"product\": \"PageMaker365\"", "\"product\": \"Other\"", StringComparison.Ordinal),
            v06.TrustOptions));
        AssertEx.Throws<InvalidDataException>(() => new PrivateRuntimeDeliveryV06PackageService().ValidateJson(
            v06.PackageJson.Replace("\"product\": \"PageMaker365\",\n", "", StringComparison.Ordinal),
            v06.TrustOptions));
        AssertEx.Throws<InvalidDataException>(() => new PrivateRuntimeDeliveryV06PackageService().ValidateJson(
            v06.PackageJson.Replace("\"runtimeArtifacts\": {", "\"unknownField\": true,\n  \"runtimeArtifacts\": {", StringComparison.Ordinal),
            v06.TrustOptions));
        AssertEx.Throws<InvalidDataException>(() => new PrivateRuntimeDeliveryV06PackageService().ValidateJson(
            v06.PackageJson.Replace("\"artifactKind\": \"api\"", "\"artifactKind\": \"portal\"", StringComparison.Ordinal),
            v06.TrustOptions));
        AssertEx.Throws<InvalidDataException>(() => new PrivateRuntimeDeliveryV06PackageService().ValidateJson(
            v06.PackageJson.Replace("/api/onboarding/installer/runtime-artifacts/{artifactKind}", "https://api.pagemaker365.com/runtime/{artifactKind}", StringComparison.Ordinal),
            v06.TrustOptions));

        var wrongTrust = new PackageTrustOptions
        {
            TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal) { ["unrelated"] = v06.PublicKeyPem }
        };
        AssertEx.Throws<InvalidDataException>(() => new PrivateRuntimeDeliveryV06PackageService().ValidateJson(v06.PackageJson, wrongTrust));

        var legacyV04 = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "samples", "contoso.customer.install.json"), new UTF8Encoding(false, true));
        AssertEx.True(RuntimeContractValidator.ValidateCustomerInstallPackageJson(legacyV04).IsValid, "Historical v0.4 validation contract drifted.");
        AssertEx.Throws<InvalidDataException>(() => new PrivateRuntimeDeliveryV06PackageService().ValidateJson(legacyV04, v06.TrustOptions));
        return Task.CompletedTask;
    }

    public static Task ProvesV06HasNoDeploymentEngineBridgeOrConfigurationClaim()
    {
        var v06EngineMethods = typeof(InstallerEngine).GetMethods().Where(method => method.Name.Contains("V06", StringComparison.OrdinalIgnoreCase)).ToArray();
        AssertEx.Equal(0, v06EngineMethods.Length, "Package 0.6 must not be wired into the deployment engine in this assignment.");

        var clientSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PageMaker365.Installer.Engine", "Services", "PrivateRuntimeDeliveryClient.cs"));
        foreach (var forbidden in new[] { "Publish-AzWebApp", "Publish-PM365RuntimeArtifacts", "Invoke-PM365Deployment" })
        {
            AssertEx.False(clientSource.Contains(forbidden, StringComparison.Ordinal), $"Acquisition source unexpectedly invokes {forbidden}.");
        }

        var modelSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PageMaker365.Installer.Engine", "Models", "PrivateRuntimeDeliveryContracts.cs"));
        AssertEx.True(modelSource.Contains("EnablePackageV06", StringComparison.Ordinal));
        AssertEx.True(modelSource.Contains("public bool EnablePackageV06 { get; init; }", StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    private static void AssertManifestIdentity(PrivateRuntimeDeliveryPackage package, JsonElement manifest)
    {
        AssertEx.Equal(manifest.GetProperty("releaseId").GetString(), package.ReleaseId);
        AssertEx.Equal(manifest.GetProperty("runtimeVersion").GetString(), package.RuntimeVersion);
        AssertEx.Equal(manifest.GetProperty("sourceCommit").GetString(), package.SourceCommit);
        foreach (var kind in new[] { "api", "portal" })
        {
            var expected = manifest.GetProperty(kind);
            var actual = package.Artifact(kind);
            AssertExactProperties(expected, $"{kind} manifest artifact", "fileName", "sizeBytes", "sha256", "startupCommand", "artifactKind");
            AssertEx.Equal(kind, expected.GetProperty("artifactKind").GetString());
            AssertEx.Equal(expected.GetProperty("fileName").GetString(), actual.FileName);
            AssertEx.Equal(expected.GetProperty("sizeBytes").GetInt64(), actual.SizeBytes);
            AssertEx.Equal(expected.GetProperty("sha256").GetString(), actual.Sha256);
            AssertEx.Equal(expected.GetProperty("startupCommand").GetString(), actual.StartupCommand);
            AssertEx.Equal(kind, actual.ArtifactKind);
        }
    }

    private static Fixture LoadFixture()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-delivery-v2");
        var trust = LoadTrust(directory);
        var publicKey = trust.TrustedPublicKeysById.Values.Single();
        return new Fixture(
            directory,
            File.ReadAllText(Path.Combine(directory, "customer-install-0.6.json"), new UTF8Encoding(false, true)),
            publicKey,
            trust);
    }

    private static PackageTrustOptions LoadTrust(string directory)
    {
        using var trust = ReadJson(directory, "signing-trust.json");
        AssertExactProperties(trust.RootElement, "fixture trust", "contractVersion", "keys");
        AssertEx.Equal("pagemaker365.customer-install-package-trust.v1", trust.RootElement.GetProperty("contractVersion").GetString());
        var key = trust.RootElement.GetProperty("keys")[0];
        var publicKey = File.ReadAllText(Path.Combine(directory, key.GetProperty("publicKeyPemFile").GetString()!), new UTF8Encoding(false, true));
        AssertEx.Equal(key.GetProperty("publicKeySha256").GetString(), Sha256(Encoding.UTF8.GetBytes(publicKey)));
        return new PackageTrustOptions
        {
            TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [key.GetProperty("keyId").GetString()!] = publicKey
            }
        };
    }

    private static string CanonicalV06(string json)
    {
        using var document = JsonDocument.Parse(json);
        return PrivateRuntimeDeliveryV06PackageService.FormatCanonicalPackage(document.RootElement);
    }

    private static void AssertExactProperties(JsonElement element, string description, params string[] expected)
    {
        AssertEx.True(element.ValueKind == JsonValueKind.Object, description + " must be an object.");
        AssertEx.True(element.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal), description + " properties differ.");
    }

    private static JsonDocument ReadJson(string directory, string fileName) =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, fileName)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PageMaker365.Installer.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate installer repository root.");
    }

    private static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private sealed record Fixture(string Directory, string PackageJson, string PublicKeyPem, PackageTrustOptions TrustOptions);
}
