using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class CryptographicRuntimeConfigurationCursorSecretGenerator : IRuntimeConfigurationCursorSecretGenerator
{
    public byte[] Generate(int entropyBytes)
    {
        if (entropyBytes < 32) throw new InvalidDataException("runtime_configuration_cursor_entropy_invalid");
        return RandomNumberGenerator.GetBytes(entropyBytes);
    }
}

/// <summary>
/// Produces a closed, redacted deployment input from signed package 0.7 bytes.
/// It has no process, network, persistence, Key Vault, or deployment behavior.
/// </summary>
public sealed class RuntimeConfigurationApplicationV2Service
{
    private readonly PrivateRuntimeDeliveryV07PackageService packageService;
    private readonly PackageTrustOptions trust;
    private readonly DateTimeOffset validationTime;

    private static readonly string[] ApiPublicNames =
    [
        "API_APP_VERSION", "API_ENV", "API_HOST", "API_CORS_ORIGIN", "API_ENTRA_TENANT_ID",
        "API_ENTRA_AUDIENCE", "API_ENTRA_CLIENT_ID", "API_GRAPH_SCOPES", "API_REQUIRED_SCOPES",
        "API_SHAREPOINT_SITE_URL", "API_SHAREPOINT_UPLOADS_LIBRARY_NAME", "API_SHAREPOINT_ISSUES_LIST_NAME",
        "API_TENANT_CONNECTION_ID", "API_TENANT_DISPLAY_NAME", "PAGEMAKER365_PORTAL_URL",
        "API_LICENSE_PUBLIC_KEY_PEM", "API_LICENSE_ENVIRONMENT_KEY", "API_LICENSE_RUNTIME_HOSTNAME",
        "API_LICENSE_VALIDATION_GRACE_HOURS", "API_LICENSE_VALIDATION_INTERVAL_HOURS", "API_AZURE_KEY_VAULT_URL",
        "API_CONNECTOR_EGRESS_REQUIRE_ALLOWLIST", "API_RUNTIME_TRUST_FORWARDED_HOST", "NODE_ENV", "PM365_PRODUCT",
        "PM365_DEPLOYMENT_EXPORT_ID", "PM365_RUNTIME_RELEASE_ID", "PM365_RUNTIME_VERSION",
        "API_FILE_PREVIEW_ALLOWED_FRAME_ORIGINS", "API_FILE_PREVIEW_DOWNLOAD_POLICY",
        "API_FILE_PREVIEW_SOURCE_FALLBACK_POLICY"
    ];

    private static readonly string[] PortalPublicNames =
    [
        "WEB_API_BASE_URL", "WEB_ENTRA_CLIENT_ID", "WEB_ENTRA_TENANT_ID", "WEB_ENTRA_AUTHORITY", "WEB_API_SCOPE",
        "WEB_RUNTIME_ENVIRONMENT", "WEB_PRODUCT_NAME", "WEB_PRODUCT_LOGO_URL", "WEB_CUSTOMER_DISPLAY_NAME",
        "WEB_CUSTOMER_SHORT_NAME", "WEB_FILE_PREVIEW_ALLOWED_FRAME_ORIGINS"
    ];

    private static readonly (string Name, string Mode)[] ProtectedSettings =
    [
        ("DATABASE_URL", "customer-azure-key-vault-reference"),
        ("API_ENTRA_CLIENT_SECRET", "customer-azure-key-vault-reference"),
        ("API_LICENSE_SIGNED_PAYLOAD", "control-plane-protected-setting-delivery"),
        ("API_IMAGE_ASSET_CURSOR_SECRET", "installer-generated-key-vault-secret")
    ];

    public RuntimeConfigurationApplicationV2Service(
        PrivateRuntimeDeliveryV07PackageService packageService,
        PackageTrustOptions trust,
        DateTimeOffset validationTime)
    {
        ArgumentNullException.ThrowIfNull(packageService);
        ArgumentNullException.ThrowIfNull(trust);
        this.packageService = packageService;
        this.trust = new PackageTrustOptions
        {
            TrustedPublicKeysById = new Dictionary<string, string>(trust.TrustedPublicKeysById, StringComparer.OrdinalIgnoreCase)
        };
        this.validationTime = validationTime;
    }

    public RuntimeConfigurationApplicationV2DeploymentInput CreateDeploymentInput(
        string canonicalPackageJson,
        bool enabled = false)
    {
        if (!enabled) Fail("runtime_configuration_application_v2_disabled");
        var authorized = Authorize(canonicalPackageJson);
        var projection = authorized.Package.RuntimeConfiguration;
        var api = CopyPublicSettings(projection.PublicSettings.Take(ApiPublicNames.Length).ToArray(), "api", ApiPublicNames);
        var portal = CopyPublicSettings(projection.PublicSettings.Skip(ApiPublicNames.Length).ToArray(), "portal", PortalPublicNames);
        var protectedReferences = ConvertVersionedProtectedSettings(projection.ProtectedSettings, projection.PublicSettings);
        var rollbackTargets = api.Select(item => $"api:{item.Name}")
            .Concat(portal.Select(item => $"portal:{item.Name}"))
            .Concat(protectedReferences.Select(item => $"api:{item.Name}"))
            .ToArray();

        var input = new RuntimeConfigurationApplicationV2DeploymentInput
        {
            PackageHash = authorized.Package.PackageHash,
            ProjectionSha256 = projection.ProjectionSha256,
            Binding = CreateBinding(authorized.Package),
            ApiPublicSettings = api,
            PortalPublicSettings = portal,
            ApiVersionedProtectedSettingReferences = protectedReferences,
            Rollback = new RuntimeConfigurationRollbackPlanV2
            {
                TargetQualifiedSettings = rollbackTargets,
                ContainsValues = false
            }
        };
        var canonical = FormatCanonicalInput(input);
        return CopyWithCanonicalIdentity(input, canonical, Sha256(Encoding.UTF8.GetBytes(canonical)));
    }

    public void GenerateCursorSecret(
        string canonicalPackageJson,
        IRuntimeConfigurationCursorSecretGenerator generator,
        IRuntimeConfigurationProtectedSecretCallback callback,
        bool enabled = false,
        CancellationToken cancellationToken = default)
    {
        if (!enabled) Fail("runtime_configuration_cursor_generation_disabled");
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();
        var cursor = Authorize(canonicalPackageJson).Cursor;
        cancellationToken.ThrowIfCancellationRequested();

        byte[]? entropy = null;
        byte[]? encoded = null;
        try
        {
            entropy = generator.Generate(cursor.MinimumEntropyBytes);
            if (entropy is null) throw new InvalidDataException("runtime_configuration_cursor_entropy_invalid");
            if (entropy.Length < cursor.MinimumEntropyBytes)
                Fail("runtime_configuration_cursor_entropy_invalid");
            cancellationToken.ThrowIfCancellationRequested();

            encoded = new byte[Base64.GetMaxEncodedToUtf8Length(entropy.Length)];
            var status = Base64.EncodeToUtf8(entropy, encoded, out var consumed, out var written);
            if (status != OperationStatus.Done || consumed != entropy.Length)
                Fail("runtime_configuration_cursor_encoding_invalid");
            while (written > 0 && encoded[written - 1] == (byte)'=') written--;
            for (var index = 0; index < written; index++)
            {
                if (encoded[index] == (byte)'+') encoded[index] = (byte)'-';
                else if (encoded[index] == (byte)'/') encoded[index] = (byte)'_';
            }
            cancellationToken.ThrowIfCancellationRequested();
            callback.Accept(cursor.VaultResourceId, cursor.SecretName, encoded.AsMemory(0, written), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            if (entropy is not null) CryptographicOperations.ZeroMemory(entropy);
            if (encoded is not null) CryptographicOperations.ZeroMemory(encoded);
        }
    }

    private AuthorizedProjection Authorize(string canonicalPackageJson)
    {
        var package = packageService.ValidateJson(canonicalPackageJson, trust, validationTime);
        var projection = package.RuntimeConfiguration;
        AssertTrustedProjection(package, projection);
        AssertProtectedSettings(projection.ProtectedSettings, projection.PublicSettings);
        var license = projection.ProtectedSettings[2];
        var cursor = projection.ProtectedSettings[3];
        return new AuthorizedProjection(
            package,
            new PendingLicenseDescriptor(license.Reference.ContractVersion!, license.Reference.OpaqueReference!, license.Reference.VaultResourceId, license.Reference.SecretName),
            new PendingCursorDescriptor(cursor.Reference.MinimumEntropyBytes!.Value, cursor.Reference.VaultResourceId, cursor.Reference.SecretName));
    }

    private static RuntimeConfigurationApplicationBindingV2 CreateBinding(PrivateRuntimeDeliveryPackageV07 package) => new()
    {
        CustomerId = package.CustomerId,
        InstallationId = package.InstallationId,
        EnvironmentId = package.EnvironmentId,
        TenantId = package.TenantId,
        AzureSubscriptionId = package.AzureSubscriptionId,
        DeploymentExportId = package.DeploymentExportId,
        RuntimeReleaseId = package.ReleaseId,
        RuntimeVersion = package.RuntimeVersion,
        ManifestSha256 = package.ManifestSha256
    };

    private static void AssertTrustedProjection(PrivateRuntimeDeliveryPackageV07 package, RuntimeConfigurationProjectionV2 projection)
    {
        if (package.ContractVersion != PrivateRuntimeDeliveryPackageV07.ContractVersionValue ||
            package.SourceCommit != RuntimeConfigurationCatalogV1Authority.SourceCommit ||
            projection.Catalog.SchemaVersion != RuntimeConfigurationCatalogV1Authority.SchemaVersion ||
            projection.Catalog.SourceRepository != RuntimeConfigurationCatalogV1Authority.SourceRepository ||
            projection.Catalog.SourceCommit != RuntimeConfigurationCatalogV1Authority.SourceCommit ||
            projection.Catalog.CatalogSha256 != RuntimeConfigurationCatalogV1Authority.CatalogSha256 ||
            projection.Catalog.CatalogSchemaSha256 != RuntimeConfigurationCatalogV1Authority.CatalogSchemaSha256 ||
            projection.Catalog.SettingCount != 70 || projection.PublicSettings.Count != 42 ||
            projection.ProtectedSettings.Count != 4 || projection.ConnectorSynchronization || projection.WebPartSynchronization ||
            !Regex.IsMatch(projection.ProjectionSha256, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
            Fail("runtime_configuration_application_v2_binding");

        var binding = projection.Binding;
        if (binding.PackageContractVersion != package.ContractVersion || binding.CustomerId != package.CustomerId ||
            binding.InstallationId != package.InstallationId || binding.EnvironmentId != package.EnvironmentId ||
            binding.TenantId != package.TenantId || binding.AzureSubscriptionId != package.AzureSubscriptionId ||
            binding.DeploymentExportId != package.DeploymentExportId || binding.RuntimeReleaseId != package.ReleaseId ||
            binding.RuntimeVersion != package.RuntimeVersion || binding.ManifestSha256 != package.ManifestSha256)
            Fail("runtime_configuration_application_v2_binding");

        if (projection.PublicSettings.Single(item => item.Name == "API_CONNECTOR_EGRESS_REQUIRE_ALLOWLIST").Value.ValueKind != JsonValueKind.True ||
            projection.PublicSettings.Single(item => item.Name == "API_RUNTIME_TRUST_FORWARDED_HOST").Value.ValueKind != JsonValueKind.False ||
            projection.PublicSettings.Single(item => item.Name == "API_FILE_PREVIEW_DOWNLOAD_POLICY").Value.GetString() != "disabled" ||
            projection.PublicSettings.Single(item => item.Name == "API_FILE_PREVIEW_SOURCE_FALLBACK_POLICY").Value.GetString() != "disabled")
            Fail("runtime_configuration_application_v2_policy");
    }

    private static IReadOnlyList<RuntimeConfigurationApplicationTypedSettingV2> CopyPublicSettings(
        IReadOnlyList<RuntimeConfigurationPublicSettingV2> settings,
        string target,
        IReadOnlyList<string> expectedNames)
    {
        if (settings.Count != expectedNames.Count) Fail("runtime_configuration_application_v2_public_shape");
        var result = new List<RuntimeConfigurationApplicationTypedSettingV2>(settings.Count);
        for (var index = 0; index < settings.Count; index++)
        {
            var setting = settings[index];
            if (setting.TargetApp != target || setting.Name != expectedNames[index]) Fail("runtime_configuration_application_v2_public_shape");
            ValidateTypedValue(setting.ValueType, setting.Value);
            result.Add(new RuntimeConfigurationApplicationTypedSettingV2
            {
                TargetApp = setting.TargetApp,
                Name = setting.Name,
                ValueType = setting.ValueType,
                Value = setting.Value.Clone()
            });
        }
        return result;
    }

    private static void ValidateTypedValue(string valueType, JsonElement value)
    {
        var valid = valueType switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "string-list" => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String),
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => false
        };
        if (!valid) Fail("runtime_configuration_application_v2_public_type");
    }

    private static void AssertProtectedSettings(
        IReadOnlyList<RuntimeConfigurationProtectedSettingV2> settings,
        IReadOnlyList<RuntimeConfigurationPublicSettingV2> publicSettings)
    {
        if (settings.Count != ProtectedSettings.Length) Fail("runtime_configuration_application_v2_protected_shape");
        var vaultOrigin = publicSettings.Single(item => item.Name == "API_AZURE_KEY_VAULT_URL").Value.GetString()!;
        var vaultName = new Uri(vaultOrigin).Host.Split('.')[0];
        for (var index = 0; index < settings.Count; index++)
        {
            var item = settings[index];
            var expected = ProtectedSettings[index];
            if (item.TargetApp != "api" || item.Name != expected.Name || item.Mode != expected.Mode ||
                !item.Reference.VaultResourceId.EndsWith($"/vaults/{vaultName}", StringComparison.OrdinalIgnoreCase))
                Fail("runtime_configuration_application_v2_protected_shape");
        }
    }

    private static IReadOnlyList<RuntimeConfigurationApplicationProtectedReferenceV2> ConvertVersionedProtectedSettings(
        IReadOnlyList<RuntimeConfigurationProtectedSettingV2> settings,
        IReadOnlyList<RuntimeConfigurationPublicSettingV2> publicSettings)
    {
        AssertProtectedSettings(settings, publicSettings);
        var vaultOrigin = publicSettings.Single(item => item.Name == "API_AZURE_KEY_VAULT_URL").Value.GetString()!;
        return settings.Take(2).Select(item => new RuntimeConfigurationApplicationProtectedReferenceV2
        {
            Name = item.Name,
            Mode = item.Mode,
            KeyVaultReference = $"@Microsoft.KeyVault(SecretUri={vaultOrigin}/secrets/{item.Reference.SecretName}/{item.Reference.SecretVersion})"
        }).ToArray();
    }

    private static RuntimeConfigurationApplicationV2DeploymentInput CopyWithCanonicalIdentity(
        RuntimeConfigurationApplicationV2DeploymentInput input,
        string json,
        string digest) => new()
    {
        PackageHash = input.PackageHash,
        ProjectionSha256 = input.ProjectionSha256,
        Binding = input.Binding,
        ApiPublicSettings = input.ApiPublicSettings,
        PortalPublicSettings = input.PortalPublicSettings,
        ApiVersionedProtectedSettingReferences = input.ApiVersionedProtectedSettingReferences,
        Rollback = input.Rollback,
        CanonicalJson = json,
        InputSha256 = digest
    };

    private static string FormatCanonicalInput(RuntimeConfigurationApplicationV2DeploymentInput input)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("contractVersion", input.ContractVersion);
            writer.WriteString("packageHash", input.PackageHash);
            writer.WriteString("projectionSha256", input.ProjectionSha256);
            writer.WritePropertyName("binding");
            writer.WriteStartObject();
            writer.WriteString("customerId", input.Binding.CustomerId); writer.WriteString("installationId", input.Binding.InstallationId);
            writer.WriteString("environmentId", input.Binding.EnvironmentId); writer.WriteString("tenantId", input.Binding.TenantId);
            writer.WriteString("azureSubscriptionId", input.Binding.AzureSubscriptionId); writer.WriteString("deploymentExportId", input.Binding.DeploymentExportId);
            writer.WriteString("runtimeReleaseId", input.Binding.RuntimeReleaseId); writer.WriteString("runtimeVersion", input.Binding.RuntimeVersion);
            writer.WriteString("manifestSha256", input.Binding.ManifestSha256); writer.WriteEndObject();
            WriteSettings(writer, "apiPublicSettings", input.ApiPublicSettings);
            WriteSettings(writer, "portalPublicSettings", input.PortalPublicSettings);
            writer.WritePropertyName("apiVersionedProtectedSettingReferences"); writer.WriteStartArray();
            foreach (var item in input.ApiVersionedProtectedSettingReferences) { writer.WriteStartObject(); writer.WriteString("name", item.Name); writer.WriteString("mode", item.Mode); writer.WriteString("keyVaultReference", item.KeyVaultReference); writer.WriteEndObject(); }
            writer.WriteEndArray();
            writer.WritePropertyName("rollback"); writer.WriteStartObject(); writer.WriteString("strategy", input.Rollback.Strategy); writer.WritePropertyName("targetQualifiedSettings"); writer.WriteStartArray(); foreach (var item in input.Rollback.TargetQualifiedSettings) writer.WriteStringValue(item); writer.WriteEndArray(); writer.WriteBoolean("containsValues", input.Rollback.ContainsValues); writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void WriteSettings(Utf8JsonWriter writer, string name, IReadOnlyList<RuntimeConfigurationApplicationTypedSettingV2> settings)
    {
        writer.WritePropertyName(name); writer.WriteStartArray();
        foreach (var item in settings)
        {
            writer.WriteStartObject(); writer.WriteString("targetApp", item.TargetApp); writer.WriteString("name", item.Name);
            writer.WriteString("valueType", item.ValueType); writer.WritePropertyName("value"); item.Value.WriteTo(writer); writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static void Fail(string code) => throw new InvalidDataException(code);

    private sealed record AuthorizedProjection(
        PrivateRuntimeDeliveryPackageV07 Package,
        PendingLicenseDescriptor License,
        PendingCursorDescriptor Cursor);
    private sealed record PendingLicenseDescriptor(string ContractVersion, string OpaqueReference, string VaultResourceId, string SecretName);
    private sealed record PendingCursorDescriptor(int MinimumEntropyBytes, string VaultResourceId, string SecretName);
}
