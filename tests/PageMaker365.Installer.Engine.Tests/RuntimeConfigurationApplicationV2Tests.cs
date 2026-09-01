using System.Security.Cryptography;
using System.Text;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

internal static class RuntimeConfigurationApplicationV2Tests
{
    private static readonly DateTimeOffset ValidationTime = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    public static void RunAll()
    {
        MapsTheExactClosedProfileDeterministically();
        RemainsDefaultDisabledAndRejectsUntrustedInput();
        GeneratesCursorMaterialOnlyThroughTheProtectedCallback();
        KeepsThePrimitiveOfflineAndRedacted();
    }

    private static void MapsTheExactClosedProfileDeterministically()
    {
        var fixture = LoadFixture();
        var first = fixture.Service.CreatePlan(fixture.PackageJson, fixture.Trust, enabled: true, ValidationTime);
        var second = fixture.Service.CreatePlan(fixture.PackageJson, fixture.Trust, enabled: true, ValidationTime);

        AssertEx.Equal(RuntimeConfigurationApplicationV2Plan.ContractVersionValue, first.ContractVersion);
        AssertEx.Equal(31, first.ApiPublicSettings.Count);
        AssertEx.Equal(11, first.PortalPublicSettings.Count);
        AssertEx.Equal(2, first.ApiProtectedSettingReferences.Count);
        AssertEx.Equal(44, first.Rollback.TargetQualifiedSettings.Count);
        AssertEx.False(first.Rollback.ContainsValues);
        AssertEx.True(first.Rollback.TargetQualifiedSettings.SequenceEqual(
            first.ApiPublicSettings.Select(item => $"api:{item.Name}")
                .Concat(first.PortalPublicSettings.Select(item => $"portal:{item.Name}"))
                .Concat(first.ApiProtectedSettingReferences.Select(item => $"api:{item.Name}")),
            StringComparer.Ordinal));
        AssertEx.Equal(first.CanonicalJson, second.CanonicalJson);
        AssertEx.Equal(first.PlanSha256, second.PlanSha256);
        AssertEx.Equal(Sha256(Encoding.UTF8.GetBytes(first.CanonicalJson)), first.PlanSha256);
        AssertEx.True(first.CanonicalJson.EndsWith('\n'));
        AssertEx.False(first.CanonicalJson.Contains('\r'));

        AssertSetting(first.ApiPublicSettings, "API_CORS_ORIGIN", "https://portal.customer.example");
        AssertSetting(first.ApiPublicSettings, "API_GRAPH_SCOPES", "https://graph.microsoft.com/.default");
        AssertSetting(first.ApiPublicSettings, "API_LICENSE_VALIDATION_GRACE_HOURS", "24");
        AssertSetting(first.ApiPublicSettings, "API_CONNECTOR_EGRESS_REQUIRE_ALLOWLIST", "true");
        AssertSetting(first.ApiPublicSettings, "API_RUNTIME_TRUST_FORWARDED_HOST", "false");
        AssertSetting(first.ApiPublicSettings, "API_FILE_PREVIEW_DOWNLOAD_POLICY", "disabled");
        AssertSetting(first.ApiPublicSettings, "API_FILE_PREVIEW_SOURCE_FALLBACK_POLICY", "disabled");
        AssertSetting(first.PortalPublicSettings, "WEB_RUNTIME_ENVIRONMENT", "production");

        var database = first.ApiProtectedSettingReferences[0];
        var entra = first.ApiProtectedSettingReferences[1];
        AssertEx.True(database.KeyVaultReference.EndsWith("/database-url/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa)", StringComparison.Ordinal));
        AssertEx.True(entra.KeyVaultReference.EndsWith("/entra-client-secret/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb)", StringComparison.Ordinal));
        AssertEx.False(first.ApiProtectedSettingReferences.Any(item => item.Name is "API_LICENSE_SIGNED_PAYLOAD" or "API_IMAGE_ASSET_CURSOR_SECRET"));
        AssertEx.False(first.CanonicalJson.Contains("@Microsoft.KeyVault(SecretUri=https://pm365fixture.vault.azure.net/secrets/license-payload)", StringComparison.Ordinal));
        AssertEx.False(first.CanonicalJson.Contains("@Microsoft.KeyVault(SecretUri=https://pm365fixture.vault.azure.net/secrets/image-cursor-secret)", StringComparison.Ordinal));
        AssertEx.Equal(RuntimeConfigurationProjectionV2Validator.ProtectedSettingAcquisitionVersion, first.LicenseAcquisition.ContractVersion);
        AssertEx.True(first.LicenseAcquisition.OpaqueReference.StartsWith("psr_", StringComparison.Ordinal));
        AssertEx.Equal("random-base64url", first.CursorGeneration.GenerationAlgorithm);
        AssertEx.Equal(32, first.CursorGeneration.MinimumEntropyBytes);

        foreach (var forbidden in new[]
        {
            "API_CONNECTOR_ENTITLEMENTS_SYNC_URL", "API_WEB_PART_ENTITLEMENTS_SYNC_URL", "API_PORT", "API_LOG_LEVEL",
            "API_LICENSE_VALIDATION_URL", "API_WEB_PART_CATALOG_MODE", "API_WEB_PART_REGISTRY_MODE",
            "WEB_ENABLE_WEB_PART_WORKBENCH", "API_WEBPART_TEST_ARTIFACTS_ENABLED", "api:PORT", "portal:PORT"
        }) AssertEx.False(first.CanonicalJson.Contains($"\"{forbidden}\"", StringComparison.Ordinal), $"Application plan represented omitted setting {forbidden}.");
    }

    private static void RemainsDefaultDisabledAndRejectsUntrustedInput()
    {
        var fixture = LoadFixture();
        AssertError("runtime_configuration_application_v2_disabled", () => fixture.Service.CreatePlan("not-json", fixture.Trust));
        AssertError("runtime_configuration_closed_shape", () => fixture.Service.CreatePlan(
            fixture.PackageJson.Replace("  \"customer\":", "  \"unexpected\": true,\n  \"customer\":", StringComparison.Ordinal),
            fixture.Trust,
            enabled: true,
            ValidationTime));

        var wrongTrust = new PackageTrustOptions
        {
            TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["other"] = fixture.PublicKey
            }
        };
        AssertError("customer_install_v07_trust_key_id", () => fixture.Service.CreatePlan(fixture.PackageJson, wrongTrust, enabled: true, ValidationTime));

        var v06Path = Path.Combine(FindRepositoryRoot(), "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-delivery-v2", "customer-install-0.6.json");
        AssertError("customer_install_v07_value", () => fixture.Service.CreatePlan(File.ReadAllText(v06Path), fixture.Trust, enabled: true, ValidationTime));
    }

    private static void GeneratesCursorMaterialOnlyThroughTheProtectedCallback()
    {
        var fixture = LoadFixture();
        var plan = fixture.Service.CreatePlan(fixture.PackageJson, fixture.Trust, enabled: true, ValidationTime);
        var callback = new CapturingCallback();
        var deterministic = new CapturingGenerator(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        AssertError("runtime_configuration_cursor_generation_disabled", () => fixture.Service.GenerateCursorSecret(plan, deterministic, callback));
        AssertEx.Equal(0, deterministic.CallCount);
        AssertEx.Equal(0, callback.CallCount);

        fixture.Service.GenerateCursorSecret(plan, deterministic, callback, enabled: true);
        AssertEx.Equal(1, deterministic.CallCount);
        AssertEx.Equal(1, callback.CallCount);
        AssertEx.Equal("image-cursor-secret", callback.Destination!.SecretName);
        AssertEx.True(callback.SecretText!.Length >= 43);
        AssertEx.True(callback.SecretText.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
        AssertEx.True(deterministic.ReturnedBuffer!.All(value => value == 0), "Raw entropy must be zeroed after the callback returns.");

        var shortGenerator = new CapturingGenerator(new byte[31]);
        AssertError("runtime_configuration_cursor_entropy_invalid", () => fixture.Service.GenerateCursorSecret(plan, shortGenerator, new CapturingCallback(), enabled: true));
        AssertEx.True(shortGenerator.ReturnedBuffer!.All(value => value == 0), "Rejected entropy must be zeroed.");

        var throwingGenerator = new CapturingGenerator(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        AssertEx.Throws<InvalidOperationException>(() => fixture.Service.GenerateCursorSecret(plan, throwingGenerator, new ThrowingCallback(), enabled: true));
        AssertEx.True(throwingGenerator.ReturnedBuffer!.All(value => value == 0), "Entropy must be zeroed when the protected callback fails.");

        var cryptographic = new CryptographicRuntimeConfigurationCursorSecretGenerator();
        var one = cryptographic.Generate(32);
        var two = cryptographic.Generate(32);
        try
        {
            AssertEx.Equal(32, one.Length);
            AssertEx.Equal(32, two.Length);
            AssertEx.False(one.SequenceEqual(two), "Independent cryptographic cursor secrets must differ.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(one);
            CryptographicOperations.ZeroMemory(two);
        }
    }

    private static void KeepsThePrimitiveOfflineAndRedacted()
    {
        var root = FindRepositoryRoot();
        var serviceSource = File.ReadAllText(Path.Combine(root, "src", "PageMaker365.Installer.Engine", "Services", "RuntimeConfigurationApplicationV2Service.cs"));
        foreach (var forbidden in new[]
        {
            "HttpClient", "Process.Start", "PowerShell", "Invoke-PM365", "New-Az", "Set-Az", "Get-Az",
            "InstallerStateStore", "PrivateRuntimeDeliveryClient", "API_LICENSE_SIGNED_PAYLOAD="
        }) AssertEx.False(serviceSource.Contains(forbidden, StringComparison.Ordinal), $"Offline application primitive contains forbidden bridge: {forbidden}");

        var fixture = LoadFixture();
        var plan = fixture.Service.CreatePlan(fixture.PackageJson, fixture.Trust, enabled: true, ValidationTime);
        foreach (var forbidden in new[] { "rawValue", "signedLicensePayload", "cursorSecret", "PRIVATE KEY" })
            AssertEx.False(plan.CanonicalJson.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertSetting(IReadOnlyList<RuntimeConfigurationApplicationSettingV2> settings, string name, string expected) =>
        AssertEx.Equal(expected, settings.Single(item => item.Name == name).Value);

    private static void AssertError(string expected, Action action) => AssertEx.Equal(expected, AssertEx.Throws<InvalidDataException>(action).Message);

    private static Fixture LoadFixture()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-delivery-v3");
        var catalog = RuntimeConfigurationCatalogV1Authority.Create(
            File.ReadAllBytes(Path.Combine(directory, "runtime-configuration.catalog.json")),
            File.ReadAllBytes(Path.Combine(directory, "runtime-configuration.schema.json")));
        var publicKey = File.ReadAllText(Path.Combine(directory, "signing-public-key.pem"), new UTF8Encoding(false, true));
        using var trustDocument = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "signing-trust.json")));
        var keyId = trustDocument.RootElement.GetProperty("keyId").GetString()!;
        var trust = new PackageTrustOptions { TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal) { [keyId] = publicKey } };
        return new Fixture(
            File.ReadAllText(Path.Combine(directory, "customer-install-0.7.json"), new UTF8Encoding(false, true)),
            publicKey,
            trust,
            new RuntimeConfigurationApplicationV2Service(new PrivateRuntimeDeliveryV07PackageService(catalog)));
    }

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

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class CapturingGenerator(byte[] bytes) : IRuntimeConfigurationCursorSecretGenerator
    {
        public int CallCount { get; private set; }
        public byte[]? ReturnedBuffer { get; private set; }
        public byte[] Generate(int entropyBytes)
        {
            CallCount++;
            ReturnedBuffer = bytes.ToArray();
            return ReturnedBuffer;
        }
    }

    private sealed class CapturingCallback : IRuntimeConfigurationProtectedSecretCallback
    {
        public int CallCount { get; private set; }
        public RuntimeConfigurationApplicationCursorDescriptorV2? Destination { get; private set; }
        public string? SecretText { get; private set; }
        public void Accept(RuntimeConfigurationApplicationCursorDescriptorV2 destination, ReadOnlyMemory<byte> base64UrlSecretUtf8)
        {
            CallCount++;
            Destination = destination;
            SecretText = Encoding.ASCII.GetString(base64UrlSecretUtf8.Span);
        }
    }

    private sealed class ThrowingCallback : IRuntimeConfigurationProtectedSecretCallback
    {
        public void Accept(RuntimeConfigurationApplicationCursorDescriptorV2 destination, ReadOnlyMemory<byte> base64UrlSecretUtf8) =>
            throw new InvalidOperationException("Synthetic protected callback failure.");
    }

    private sealed record Fixture(
        string PackageJson,
        string PublicKey,
        PackageTrustOptions Trust,
        RuntimeConfigurationApplicationV2Service Service);
}
