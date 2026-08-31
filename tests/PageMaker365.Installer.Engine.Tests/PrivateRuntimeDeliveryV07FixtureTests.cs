using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

internal static class PrivateRuntimeDeliveryV07FixtureTests
{
    private const string ProducerCommit = "aff1c693580f0a4f15d223b32b03b77175b73200";
    private const string ProducerMain = "52b6be17f9f98124cb815f972a8d34437a4c5a45";
    private const string FixtureManifestSha256 = "9ba86d6b38de6fc7eca54f7df9994a2fe92f4c68cbcf7edc489c751873e1b17d";
    private static readonly DateTimeOffset ValidationTime = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    public static Task LocksAcceptedProducerBytesAndValidatesClosedPackage()
    {
        var fixture = LoadFixture();
        var expectedNames = new[]
        {
            "customer-install-0.7.json", "customer-install-package-v0.7.schema.json",
            "runtime-configuration-projection-v2.json", "runtime-configuration-projection-v2.schema.json",
            "runtime-configuration.catalog.json", "runtime-configuration.schema.json", "sha256-manifest.json",
            "signature-vector.json", "signing-public-key.pem", "signing-trust.json"
        }.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        AssertEx.True(Directory.GetFiles(fixture.Directory).Select(Path.GetFileName).OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(expectedNames, StringComparer.Ordinal));
        AssertEx.Equal(FixtureManifestSha256, Sha256(File.ReadAllBytes(Path.Combine(fixture.Directory, "sha256-manifest.json"))));

        using var manifest = ReadJson(fixture.Directory, "sha256-manifest.json");
        AssertExactProperties(manifest.RootElement, "schemaVersion", "sourceRepository", "sourceCommit", "files", "canonicalArtifacts");
        AssertEx.Equal("pagemaker365.private-runtime-delivery-fixtures.v3", manifest.RootElement.GetProperty("schemaVersion").GetString());
        AssertEx.Equal("cloudbossdev/spo-ui", manifest.RootElement.GetProperty("sourceRepository").GetString());
        AssertEx.Equal(RuntimeConfigurationCatalogV1Authority.SourceCommit, manifest.RootElement.GetProperty("sourceCommit").GetString());
        foreach (var item in manifest.RootElement.GetProperty("files").EnumerateObject())
            AssertEx.Equal(item.Value.GetString(), Sha256(File.ReadAllBytes(Path.Combine(fixture.Directory, item.Name))));
        foreach (var item in manifest.RootElement.GetProperty("canonicalArtifacts").EnumerateArray())
        {
            var path = item.GetProperty("path").GetString()!;
            var bytes = File.ReadAllBytes(Path.Combine(fixture.Directory, path));
            AssertEx.Equal(item.GetProperty("sizeBytes").GetInt32(), bytes.Length);
            AssertEx.Equal(item.GetProperty("sha256").GetString(), Sha256(bytes));
        }

        AssertEx.True(File.ReadAllBytes(Path.Combine(fixture.Directory, "customer-install-package-v0.7.schema.json"))
            .SequenceEqual(File.ReadAllBytes(Path.Combine(FindRepositoryRoot(), "schemas", "customer-install-v0.7.schema.json"))));
        AssertEx.True(File.ReadAllBytes(Path.Combine(fixture.Directory, "runtime-configuration-projection-v2.schema.json"))
            .SequenceEqual(File.ReadAllBytes(Path.Combine(FindRepositoryRoot(), "schemas", "runtime-configuration-projection-v2.schema.json"))));
        foreach (var file in expectedNames)
            AssertEx.False(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(fixture.Directory, file))).Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase));

        var package = fixture.Service.ValidateJson(fixture.PackageJson, fixture.Trust, ValidationTime);
        AssertEx.Equal("0.7", package.ContractVersion);
        AssertEx.Equal(RuntimeConfigurationCatalogV1Authority.SourceCommit, package.SourceCommit);
        AssertEx.Equal(42, package.RuntimeConfiguration.PublicSettings.Count);
        AssertEx.Equal(4, package.RuntimeConfiguration.ProtectedSettings.Count);
        AssertEx.False(package.RuntimeConfiguration.ConnectorSynchronization);
        AssertEx.False(package.RuntimeConfiguration.WebPartSynchronization);
        AssertEx.Equal(RuntimeConfigurationCatalogV1Authority.CatalogSha256, package.RuntimeConfiguration.Catalog.CatalogSha256);
        AssertEx.Equal(70, package.RuntimeConfiguration.Catalog.SettingCount);
        var publicNames = package.RuntimeConfiguration.PublicSettings.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var omitted in new[]
        {
            "API_CONNECTOR_ENTITLEMENTS_SYNC_URL", "API_WEB_PART_ENTITLEMENTS_SYNC_URL", "API_PORT", "API_LOG_LEVEL",
            "API_LICENSE_VALIDATION_URL", "API_WEB_PART_CATALOG_MODE", "API_WEB_PART_REGISTRY_MODE", "WEB_ENABLE_WEB_PART_WORKBENCH",
            "PORT", "API_WEBPART_TEST_ARTIFACTS_ENABLED"
        }) AssertEx.False(publicNames.Contains(omitted), $"Closed projection represented omitted setting {omitted}.");
        var license = package.RuntimeConfiguration.ProtectedSettings.Single(item => item.Name == "API_LICENSE_SIGNED_PAYLOAD");
        AssertEx.Equal("control-plane-protected-setting-delivery", license.Mode);
        AssertEx.Equal(RuntimeConfigurationProjectionV2Validator.ProtectedSettingAcquisitionVersion, license.Reference.ContractVersion);
        AssertEx.True(license.Reference.OpaqueReference!.StartsWith("psr_", StringComparison.Ordinal));
        AssertEx.False(typeof(RuntimeConfigurationProtectedReferenceV2).GetProperties().Any(property => property.Name is "Value" or "RawValue" or "SignedLicensePayload"));
        AssertEx.Equal(fixture.PackageJson, PrivateRuntimeDeliveryV07PackageService.FormatCanonicalPackage(JsonDocument.Parse(fixture.PackageJson).RootElement));

        using var vector = ReadJson(fixture.Directory, "signature-vector.json");
        AssertEx.Equal(vector.RootElement.GetProperty("packageHash").GetString(), package.PackageHash);
        AssertEx.Equal(vector.RootElement.GetProperty("canonicalPayloadSha256").GetString(), Sha256(package.CanonicalSigningPayloadUtf8));
        AssertEx.Equal(ProducerCommit, ProducerCommit);
        AssertEx.Equal(40, ProducerMain.Length);
        return Task.CompletedTask;
    }

    public static Task RejectsCrossPairsCanonicalAndCatalogDrift()
    {
        var fixture = LoadFixture();
        var v06Directory = Path.Combine(FindRepositoryRoot(), "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-delivery-v2");
        var v06Json = File.ReadAllText(Path.Combine(v06Directory, "customer-install-0.6.json"), new UTF8Encoding(false, true));
        var v06Trust = LoadHistoricalTrust(v06Directory);
        AssertEx.Throws<InvalidDataException>(() => fixture.Service.ValidateJson(v06Json, v06Trust, ValidationTime));
        AssertEx.Throws<InvalidDataException>(() => new PrivateRuntimeDeliveryV06PackageService().ValidateJson(fixture.PackageJson, fixture.Trust, ValidationTime));
        AssertEx.Throws<InvalidDataException>(() => new PrivateRuntimeDeliveryPackageService().ValidateJson(fixture.PackageJson, fixture.Trust, ValidationTime));

        var catalogBytes = File.ReadAllBytes(Path.Combine(fixture.Directory, "runtime-configuration.catalog.json"));
        var schemaBytes = File.ReadAllBytes(Path.Combine(fixture.Directory, "runtime-configuration.schema.json"));
        catalogBytes[0] ^= 1;
        AssertError("runtime_configuration_catalog_bytes_mismatch", () => RuntimeConfigurationCatalogV1Authority.Create(catalogBytes, schemaBytes));
        catalogBytes[0] ^= 1;
        schemaBytes[0] ^= 1;
        AssertError("runtime_configuration_catalog_bytes_mismatch", () => RuntimeConfigurationCatalogV1Authority.Create(catalogBytes, schemaBytes));

        Deny(fixture, root => root["contractVersion"] = "0.6", "customer_install_v07_value");
        Deny(fixture, root => root["controlPlane"]!["acceptedInstallerCapability"] = "pagemaker365.customer-install.0.6.protected-acquisition.v1", "customer_install_v07_value");
        Deny(fixture, root => root["runtimeArtifacts"]!["manifestContractVersion"] = "2.0", "customer_install_v07_value");
        Deny(fixture, root => root["runtimeArtifacts"]!["sourceCommit"] = new string('a', 40), "customer_install_v07_value");
        Deny(fixture, root => root["runtimeArtifacts"]!["manifestSha256"] = new string('a', 64), "customer_install_v07_manifest_binding");
        Deny(fixture, root => root["runtimeArtifacts"]!["api"]!["artifactKind"] = "portal", "customer_install_v07_value");
        Deny(fixture, root => root["protectedAcquisition"]!["contractVersion"] = "pagemaker365.protected-setting-acquisition.v1", "customer_install_v07_value");
        Deny(fixture, root => root["runtimeConfiguration"]!["schemaVersion"] = "pagemaker365.runtime-configuration-projection.v1", "runtime_configuration_projection_v2_identity");
        Deny(fixture, root => root["runtimeConfiguration"]!["catalog"]!["sourceCommit"] = new string('a', 40), "runtime_configuration_projection_v2_catalog");
        Deny(fixture, root => root["runtimeConfiguration"]!["catalog"]!["catalogSha256"] = new string('a', 64), "runtime_configuration_projection_v2_catalog");
        Deny(fixture, root => root["runtimeConfiguration"]!["catalog"]!["settingCount"] = 69, "runtime_configuration_projection_v2_catalog");
        Deny(fixture, root => root["runtimeConfiguration"]!["projectionSha256"] = new string('a', 64), "runtime_configuration_projection_v2_digest");
        Deny(fixture, root => root.Add("unknownField", true), "runtime_configuration_closed_shape");

        var noncanonical = fixture.PackageJson.Replace("  \"customer\"", "   \"customer\"", StringComparison.Ordinal);
        AssertError("customer_install_v07_noncanonical", () => fixture.Service.ValidateJson(noncanonical, fixture.Trust, ValidationTime));
        var duplicate = fixture.PackageJson.Replace("  \"contractVersion\": \"0.7\",", "  \"contractVersion\": \"0.7\",\n  \"contractVersion\": \"0.7\",", StringComparison.Ordinal);
        AssertError("customer_install_v07_json_duplicate", () => fixture.Service.ValidateJson(duplicate, fixture.Trust, ValidationTime));

        var wrongTrust = new PackageTrustOptions { TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal) { ["other"] = fixture.PublicKey } };
        AssertError("customer_install_v07_trust_key_id", () => fixture.Service.ValidateJson(fixture.PackageJson, wrongTrust, ValidationTime));
        Deny(fixture, root => root["controlPlane"]!["packageHash"] = "sha256:" + new string('a', 64), "customer_install_v07_hash_invalid");
        Deny(fixture, root => root["controlPlane"]!["signature"] = new string('A', 86), "customer_install_v07_signature_invalid");
        return Task.CompletedTask;
    }

    public static Task RejectsIncompleteUnsafeOrRawConfiguration()
    {
        var fixture = LoadFixture();
        Deny(fixture, root => root["runtimeConfiguration"]!["featureProfile"]!["connectorSynchronization"] = true, "runtime_configuration_projection_v2_feature_profile");
        Deny(fixture, root => ((JsonArray)root["runtimeConfiguration"]!["publicSettings"]!).RemoveAt(0), "runtime_configuration_projection_v2_public_count");
        Deny(fixture, root =>
        {
            var values = (JsonArray)root["runtimeConfiguration"]!["publicSettings"]!;
            var first = values[0]!.DeepClone();
            var second = values[1]!.DeepClone();
            values[0] = second;
            values[1] = first;
        }, "runtime_configuration_projection_v2_public_order");
        Deny(fixture, root => root["runtimeConfiguration"]!["publicSettings"]![0]!["valueType"] = "integer", "runtime_configuration_projection_v2_public_order");
        Deny(fixture, root => FindPublic(root, "API_CONNECTOR_EGRESS_REQUIRE_ALLOWLIST")["value"] = false, "runtime_configuration_projection_v2_value");
        Deny(fixture, root => FindPublic(root, "API_APP_VERSION")["value"] = "2147483648.0.0", "runtime_configuration_projection_v2_value");
        Deny(fixture, root => FindPublic(root, "API_ENTRA_TENANT_ID")["value"] = "00000000-0000-0000-0000-000000000000", "runtime_configuration_projection_v2_value");
        Deny(fixture, root => FindPublic(root, "API_ENTRA_TENANT_ID")["value"] = "11111111-1111-4111-8111-111111111111", "runtime_configuration_projection_v2_value");
        Deny(fixture, root => FindPublic(root, "API_LICENSE_PUBLIC_KEY_PEM")["value"] = "-----BEGIN PUBLIC KEY-----\nQUFBQQ==\n-----END PUBLIC KEY-----\n", "runtime_configuration_projection_v2_value");
        Deny(fixture, root => FindPublic(root, "PAGEMAKER365_PORTAL_URL")["value"] = "https://portal.example.test/other", "runtime_configuration_projection_v2_portal_origin_binding");
        Deny(fixture, root => FindPublic(root, "PAGEMAKER365_PORTAL_URL")["value"] = "https://portal.example.test:444", "runtime_configuration_projection_v2_portal_origin_binding");
        Deny(fixture, root => ((JsonArray)root["runtimeConfiguration"]!["publicSettings"]!).Add(new JsonObject
        {
            ["targetApp"] = "api", ["name"] = "API_WEBPART_TEST_ARTIFACTS_ENABLED", ["valueType"] = "boolean", ["value"] = false
        }), "runtime_configuration_projection_v2_public_count");

        Deny(fixture, root => root["runtimeConfiguration"]!["protectedSettings"]![0]!["mode"] = "control-plane-protected-setting-delivery", "runtime_configuration_projection_v2_protected_mode");
        Deny(fixture, root => root["runtimeConfiguration"]!["protectedSettings"]![0]!["reference"]!.AsObject().Add("value", "raw-secret"), "runtime_configuration_closed_shape");
        Deny(fixture, root => root["runtimeConfiguration"]!["protectedSettings"]![2]!["reference"]!["opaqueReference"] = "psr_short", "runtime_configuration_projection_v2_protected_reference");
        Deny(fixture, root => root["runtimeConfiguration"]!["protectedSettings"]![3]!["reference"]!["minimumEntropyBytes"] = 16, "runtime_configuration_projection_v2_protected_reference");
        Deny(fixture, root => root["runtimeConfiguration"]!["protectedSettings"]![1]!["reference"]!["secretName"] = "database-url", "runtime_configuration_projection_v2_protected_reference_reuse");
        Deny(fixture, root => root["runtimeConfiguration"]!["protectedSettings"]![0]!["reference"]!["vaultResourceId"] = "/subscriptions/11111111-1111-4111-8111-111111111111/resourceGroups/pm365/providers/Microsoft.KeyVault/vaults/pm365fixture", "runtime_configuration_projection_v2_protected_reference");
        Deny(fixture, root => root["runtimeConfiguration"]!["binding"]!["tenantId"] = "11111111-1111-4111-8111-111111111111", "runtime_configuration_projection_v2_binding");
        Deny(fixture, root => root["customer"]!["customerId"] = "11111111-1111-4111-8111-111111111111", "customer_install_v07_binding_invalid");
        return Task.CompletedTask;
    }

    public static Task RemainsParserOnlyAndPreservesHistoricalConsumers()
    {
        AssertEx.Equal(0, typeof(InstallerEngine).GetMethods().Count(method => method.Name.Contains("V07", StringComparison.OrdinalIgnoreCase)));
        AssertEx.Equal(0, typeof(PrivateRuntimeDeliveryClient).GetMethods().Count(method => method.Name.Contains("V07", StringComparison.OrdinalIgnoreCase)));
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PageMaker365.Installer.Engine", "Services", "PrivateRuntimeDeliveryV07PackageService.cs"));
        foreach (var forbidden in new[] { "HttpClient", "Acquire", "Invoke-PM365", "Publish-AzWebApp", "PowerShell", "Set-PM365RuntimeConfiguration" })
            AssertEx.False(source.Contains(forbidden, StringComparison.Ordinal), $"Parser-only source contains forbidden bridge: {forbidden}");

        var fixture = LoadFixture();
        var v05Directory = Path.Combine(FindRepositoryRoot(), "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-delivery-v1");
        var v06Directory = Path.Combine(FindRepositoryRoot(), "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-delivery-v2");
        AssertEx.Equal("0.5", new PrivateRuntimeDeliveryPackageService().ValidateJson(File.ReadAllText(Path.Combine(v05Directory, "customer-install-0.5.json")), LoadHistoricalTrust(v05Directory), ValidationTime).ContractVersion);
        AssertEx.Equal("0.6", new PrivateRuntimeDeliveryV06PackageService().ValidateJson(File.ReadAllText(Path.Combine(v06Directory, "customer-install-0.6.json")), LoadHistoricalTrust(v06Directory), ValidationTime).ContractVersion);
        AssertEx.Equal("0.7", fixture.Service.ValidateJson(fixture.PackageJson, fixture.Trust, ValidationTime).ContractVersion);
        return Task.CompletedTask;
    }

    private static void Deny(Fixture fixture, Action<JsonObject> mutate, string expectedCode)
    {
        var root = JsonNode.Parse(fixture.PackageJson)!.AsObject();
        mutate(root);
        var candidate = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        AssertError(expectedCode, () => fixture.Service.ValidateJson(candidate, fixture.Trust, ValidationTime));
    }

    private static JsonObject FindPublic(JsonObject root, string name) =>
        ((JsonArray)root["runtimeConfiguration"]!["publicSettings"]!).Select(item => item!.AsObject()).Single(item => item["name"]!.GetValue<string>() == name);

    private static void AssertError(string expectedCode, Action action)
    {
        var error = AssertEx.Throws<InvalidDataException>(action);
        AssertEx.Equal(expectedCode, error.Message);
    }

    private static Fixture LoadFixture()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-delivery-v3");
        var catalog = RuntimeConfigurationCatalogV1Authority.Create(
            File.ReadAllBytes(Path.Combine(directory, "runtime-configuration.catalog.json")),
            File.ReadAllBytes(Path.Combine(directory, "runtime-configuration.schema.json")));
        var publicKey = File.ReadAllText(Path.Combine(directory, "signing-public-key.pem"), new UTF8Encoding(false, true));
        using var trustDocument = ReadJson(directory, "signing-trust.json");
        var keyId = trustDocument.RootElement.GetProperty("keyId").GetString()!;
        AssertEx.Equal(trustDocument.RootElement.GetProperty("publicKeySha256").GetString(), Sha256(Encoding.UTF8.GetBytes(publicKey)));
        return new Fixture(
            directory,
            File.ReadAllText(Path.Combine(directory, "customer-install-0.7.json"), new UTF8Encoding(false, true)),
            publicKey,
            new PackageTrustOptions { TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal) { [keyId] = publicKey } },
            new PrivateRuntimeDeliveryV07PackageService(catalog));
    }

    private static PackageTrustOptions LoadHistoricalTrust(string directory)
    {
        using var trust = ReadJson(directory, "signing-trust.json");
        var key = trust.RootElement.GetProperty("keys")[0];
        var publicKey = File.ReadAllText(Path.Combine(directory, key.GetProperty("publicKeyPemFile").GetString()!), new UTF8Encoding(false, true));
        return new PackageTrustOptions { TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal) { [key.GetProperty("keyId").GetString()!] = publicKey } };
    }

    private static JsonDocument ReadJson(string directory, string name) => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, name)));
    private static void AssertExactProperties(JsonElement value, params string[] names) => AssertEx.True(value.EnumerateObject().Select(item => item.Name).SequenceEqual(names, StringComparer.Ordinal));
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

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

    private sealed record Fixture(string Directory, string PackageJson, string PublicKey, PackageTrustOptions Trust, PrivateRuntimeDeliveryV07PackageService Service);
}
