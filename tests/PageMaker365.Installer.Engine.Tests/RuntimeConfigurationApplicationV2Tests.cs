using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        var first = fixture.Service.CreateDeploymentInput(fixture.PackageJson, enabled: true);
        var second = fixture.Service.CreateDeploymentInput(fixture.PackageJson, enabled: true);

        AssertEx.Equal(RuntimeConfigurationApplicationV2DeploymentInput.ContractVersionValue, first.ContractVersion);
        AssertEx.Equal(31, first.ApiPublicSettings.Count);
        AssertEx.Equal(11, first.PortalPublicSettings.Count);
        AssertEx.Equal(2, first.ApiVersionedProtectedSettingReferences.Count);
        AssertEx.Equal(44, first.Rollback.TargetQualifiedSettings.Count);
        AssertEx.False(first.Rollback.ContainsValues);
        AssertEx.True(first.Rollback.TargetQualifiedSettings.SequenceEqual(
            first.ApiPublicSettings.Select(item => $"api:{item.Name}")
                .Concat(first.PortalPublicSettings.Select(item => $"portal:{item.Name}"))
                .Concat(first.ApiVersionedProtectedSettingReferences.Select(item => $"api:{item.Name}")),
            StringComparer.Ordinal));
        AssertEx.Equal(first.CanonicalJson, second.CanonicalJson);
        AssertEx.Equal(first.InputSha256, second.InputSha256);
        AssertEx.Equal(Sha256(Encoding.UTF8.GetBytes(first.CanonicalJson)), first.InputSha256);
        AssertEx.True(first.CanonicalJson.EndsWith('\n'));
        AssertEx.False(first.CanonicalJson.Contains('\r'));

        AssertSetting(first.ApiPublicSettings, "API_CORS_ORIGIN", "string-list", JsonValueKind.Array);
        AssertSetting(first.ApiPublicSettings, "API_GRAPH_SCOPES", "string-list", JsonValueKind.Array);
        AssertSetting(first.ApiPublicSettings, "API_LICENSE_VALIDATION_GRACE_HOURS", "integer", JsonValueKind.Number);
        AssertSetting(first.ApiPublicSettings, "API_CONNECTOR_EGRESS_REQUIRE_ALLOWLIST", "boolean", JsonValueKind.True);
        AssertSetting(first.ApiPublicSettings, "API_RUNTIME_TRUST_FORWARDED_HOST", "boolean", JsonValueKind.False);
        AssertSetting(first.ApiPublicSettings, "API_FILE_PREVIEW_DOWNLOAD_POLICY", "string", JsonValueKind.String);
        AssertSetting(first.PortalPublicSettings, "WEB_RUNTIME_ENVIRONMENT", "string", JsonValueKind.String);

        var database = first.ApiVersionedProtectedSettingReferences[0];
        var entra = first.ApiVersionedProtectedSettingReferences[1];
        AssertEx.True(database.KeyVaultReference.EndsWith("/database-url/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa)", StringComparison.Ordinal));
        AssertEx.True(entra.KeyVaultReference.EndsWith("/entra-client-secret/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb)", StringComparison.Ordinal));
        AssertEx.False(first.ApiVersionedProtectedSettingReferences.Any(item => item.Name is "API_LICENSE_SIGNED_PAYLOAD" or "API_IMAGE_ASSET_CURSOR_SECRET"));
        foreach (var forbiddenPending in new[] { "psr_", "license-payload", "image-cursor-secret", "opaqueReference", "cursorGeneration", "generationAlgorithm" })
        {
            AssertEx.False(first.CanonicalJson.Contains(forbiddenPending, StringComparison.OrdinalIgnoreCase));
            AssertEx.False(JsonSerializer.Serialize(first).Contains(forbiddenPending, StringComparison.OrdinalIgnoreCase));
        }

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
        AssertError("runtime_configuration_application_v2_disabled", () => fixture.Service.CreateDeploymentInput("not-json"));
        AssertError("runtime_configuration_closed_shape", () => fixture.Service.CreateDeploymentInput(
            fixture.PackageJson.Replace("  \"customer\":", "  \"unexpected\": true,\n  \"customer\":", StringComparison.Ordinal),
            enabled: true));

        var wrongTrust = new PackageTrustOptions
        {
            TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["other"] = fixture.PublicKey
            }
        };
        var wrongTrustService = CreateService(fixture.Catalog, wrongTrust);
        AssertError("customer_install_v07_trust_key_id", () => wrongTrustService.CreateDeploymentInput(fixture.PackageJson, enabled: true));

        var v06Path = Path.Combine(FindRepositoryRoot(), "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-delivery-v2", "customer-install-0.6.json");
        AssertError("customer_install_v07_value", () => fixture.Service.CreateDeploymentInput(File.ReadAllText(v06Path), enabled: true));

        var wrongBooleanType = fixture.PackageJson.Replace("\"value\": true", "\"value\": \"true\"", StringComparison.Ordinal);
        AssertError("runtime_configuration_projection_v2_value_type", () => fixture.Service.CreateDeploymentInput(wrongBooleanType, enabled: true));
        var wrongIntegerType = fixture.PackageJson.Replace("\"value\": 24", "\"value\": \"24\"", StringComparison.Ordinal);
        AssertError("runtime_configuration_projection_v2_value_type", () => fixture.Service.CreateDeploymentInput(wrongIntegerType, enabled: true));

        ((Dictionary<string, string>)fixture.Trust.TrustedPublicKeysById).Clear();
        AssertEx.Equal(31, fixture.Service.CreateDeploymentInput(fixture.PackageJson, enabled: true).ApiPublicSettings.Count);
    }

    private static void GeneratesCursorMaterialOnlyThroughTheProtectedCallback()
    {
        var fixture = LoadFixture();
        var callback = new CapturingCallback();
        var deterministic = new CapturingGenerator(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        AssertError("runtime_configuration_cursor_generation_disabled", () => fixture.Service.GenerateCursorSecret(fixture.PackageJson, deterministic, callback));
        AssertEx.Equal(0, deterministic.CallCount);
        AssertEx.Equal(0, callback.CallCount);

        var deniedGenerator = new CapturingGenerator(new byte[32]);
        AssertError("customer_install_v07_value", () => fixture.Service.GenerateCursorSecret(
            fixture.PackageJson.Replace("\"product\": \"PageMaker365\"", "\"product\": \"Tampered\"", StringComparison.Ordinal),
            deniedGenerator,
            new CapturingCallback(),
            enabled: true));
        AssertEx.Equal(0, deniedGenerator.CallCount);

        fixture.Service.GenerateCursorSecret(fixture.PackageJson, deterministic, callback, enabled: true);
        AssertEx.Equal(1, deterministic.CallCount);
        AssertEx.Equal(1, callback.CallCount);
        AssertEx.Equal("image-cursor-secret", callback.SecretName);
        AssertEx.True(callback.SecretText!.Length >= 43);
        AssertEx.True(callback.SecretText.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
        AssertEx.True(deterministic.ReturnedBuffer!.All(value => value == 0), "Raw entropy must be zeroed after the callback returns.");
        AssertEx.True(callback.RetainedBuffer.ToArray().All(value => value == 0), "Encoded cursor bytes must be zeroed after callback success.");

        var shortGenerator = new CapturingGenerator(new byte[31]);
        AssertError("runtime_configuration_cursor_entropy_invalid", () => fixture.Service.GenerateCursorSecret(fixture.PackageJson, shortGenerator, new CapturingCallback(), enabled: true));
        AssertEx.True(shortGenerator.ReturnedBuffer!.All(value => value == 0), "Rejected entropy must be zeroed.");

        var throwingGenerator = new CapturingGenerator(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        var throwingCallback = new ThrowingCallback();
        AssertEx.Throws<InvalidOperationException>(() => fixture.Service.GenerateCursorSecret(fixture.PackageJson, throwingGenerator, throwingCallback, enabled: true));
        AssertEx.True(throwingGenerator.ReturnedBuffer!.All(value => value == 0), "Entropy must be zeroed when the protected callback fails.");
        AssertEx.True(throwingCallback.RetainedBuffer.ToArray().All(value => value == 0), "Encoded cursor bytes must be zeroed when callback fails.");

        using var cancelled = new CancellationTokenSource();
        var cancellingGenerator = new CancellingGenerator(cancelled);
        var cancelledCallback = new CapturingCallback();
        AssertEx.Throws<OperationCanceledException>(() => fixture.Service.GenerateCursorSecret(fixture.PackageJson, cancellingGenerator, cancelledCallback, true, cancelled.Token));
        AssertEx.True(cancellingGenerator.ReturnedBuffer!.All(value => value == 0), "Raw entropy must be zeroed on cancellation.");
        AssertEx.Equal(0, cancelledCallback.CallCount);

        var callbackCancellation = new CancellingCallback();
        var callbackCancellationGenerator = new CapturingGenerator(new byte[32]);
        AssertEx.Throws<OperationCanceledException>(() => fixture.Service.GenerateCursorSecret(fixture.PackageJson, callbackCancellationGenerator, callbackCancellation, true));
        AssertEx.True(callbackCancellationGenerator.ReturnedBuffer!.All(value => value == 0));
        AssertEx.True(callbackCancellation.RetainedBuffer.ToArray().All(value => value == 0));

        using var callbackCancels = new CancellationTokenSource();
        var cancelsAndReturns = new CancelsAndReturnsCallback(callbackCancels);
        var cancelOnReturnGenerator = new CapturingGenerator(new byte[32]);
        AssertEx.Throws<OperationCanceledException>(() => fixture.Service.GenerateCursorSecret(fixture.PackageJson, cancelOnReturnGenerator, cancelsAndReturns, true, callbackCancels.Token));
        AssertEx.True(cancelOnReturnGenerator.ReturnedBuffer!.All(value => value == 0));
        AssertEx.True(cancelsAndReturns.RetainedBuffer.ToArray().All(value => value == 0));

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
        var plan = fixture.Service.CreateDeploymentInput(fixture.PackageJson, enabled: true);
        foreach (var forbidden in new[] { "rawValue", "signedLicensePayload", "cursorSecret", "PRIVATE KEY" })
            AssertEx.False(plan.CanonicalJson.Contains(forbidden, StringComparison.OrdinalIgnoreCase));

        var publicMethods = typeof(RuntimeConfigurationApplicationV2Service).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
        AssertEx.False(publicMethods.Any(method => method.GetParameters().Any(parameter =>
            parameter.ParameterType == typeof(RuntimeConfigurationApplicationV2DeploymentInput) ||
            parameter.ParameterType == typeof(RuntimeConfigurationProjectionV2))), "No public behavior may accept a caller-constructed plan or projection.");
        AssertEx.True(publicMethods.Single(method => method.Name == nameof(RuntimeConfigurationApplicationV2Service.GenerateCursorSecret)).ReturnType == typeof(void));
        var serializedPropertyNames = typeof(RuntimeConfigurationApplicationV2DeploymentInput).GetProperties().Select(item => item.Name).ToArray();
        foreach (var forbiddenProperty in new[] { "License", "Cursor", "Pending", "Opaque", "Destination", "Directive" })
            AssertEx.False(serializedPropertyNames.Any(name => name.Contains(forbiddenProperty, StringComparison.OrdinalIgnoreCase)));
    }

    private static void AssertSetting(IReadOnlyList<RuntimeConfigurationApplicationTypedSettingV2> settings, string name, string valueType, JsonValueKind kind)
    {
        var setting = settings.Single(item => item.Name == name);
        AssertEx.Equal(valueType, setting.ValueType);
        AssertEx.Equal(kind, setting.Value.ValueKind);
    }

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
            catalog,
            CreateService(catalog, trust));
    }

    private static RuntimeConfigurationApplicationV2Service CreateService(RuntimeConfigurationCatalogV1Authority catalog, PackageTrustOptions trust) =>
        new(new PrivateRuntimeDeliveryV07PackageService(catalog), trust, ValidationTime);

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
        public string? VaultResourceId { get; private set; }
        public string? SecretName { get; private set; }
        public string? SecretText { get; private set; }
        public ReadOnlyMemory<byte> RetainedBuffer { get; private set; }
        public void Accept(string vaultResourceId, string secretName, ReadOnlyMemory<byte> base64UrlSecretUtf8, CancellationToken cancellationToken)
        {
            CallCount++;
            VaultResourceId = vaultResourceId;
            SecretName = secretName;
            SecretText = Encoding.ASCII.GetString(base64UrlSecretUtf8.Span);
            RetainedBuffer = base64UrlSecretUtf8;
        }
    }

    private sealed class ThrowingCallback : IRuntimeConfigurationProtectedSecretCallback
    {
        public ReadOnlyMemory<byte> RetainedBuffer { get; private set; }
        public void Accept(string vaultResourceId, string secretName, ReadOnlyMemory<byte> base64UrlSecretUtf8, CancellationToken cancellationToken)
        {
            RetainedBuffer = base64UrlSecretUtf8;
            throw new InvalidOperationException("Synthetic protected callback failure.");
        }
    }

    private sealed class CancellingCallback : IRuntimeConfigurationProtectedSecretCallback
    {
        public ReadOnlyMemory<byte> RetainedBuffer { get; private set; }
        public void Accept(string vaultResourceId, string secretName, ReadOnlyMemory<byte> base64UrlSecretUtf8, CancellationToken cancellationToken)
        {
            RetainedBuffer = base64UrlSecretUtf8;
            throw new OperationCanceledException("Synthetic cancellation.");
        }
    }

    private sealed class CancelsAndReturnsCallback(CancellationTokenSource cancellation) : IRuntimeConfigurationProtectedSecretCallback
    {
        public ReadOnlyMemory<byte> RetainedBuffer { get; private set; }
        public void Accept(string vaultResourceId, string secretName, ReadOnlyMemory<byte> base64UrlSecretUtf8, CancellationToken cancellationToken)
        {
            RetainedBuffer = base64UrlSecretUtf8;
            cancellation.Cancel();
        }
    }

    private sealed class CancellingGenerator(CancellationTokenSource cancellation) : IRuntimeConfigurationCursorSecretGenerator
    {
        public byte[]? ReturnedBuffer { get; private set; }
        public byte[] Generate(int entropyBytes)
        {
            ReturnedBuffer = new byte[entropyBytes];
            cancellation.Cancel();
            return ReturnedBuffer;
        }
    }

    private sealed record Fixture(
        string PackageJson,
        string PublicKey,
        PackageTrustOptions Trust,
        RuntimeConfigurationCatalogV1Authority Catalog,
        RuntimeConfigurationApplicationV2Service Service);
}
