using System.Globalization;
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
/// Produces the closed, redacted deployment input for a signed package 0.7.
/// It has no process, network, persistence, Key Vault, or deployment behavior.
/// </summary>
public sealed class RuntimeConfigurationApplicationV2Service(PrivateRuntimeDeliveryV07PackageService packageService)
{
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

    public RuntimeConfigurationApplicationV2Plan CreatePlan(
        string canonicalPackageJson,
        PackageTrustOptions trust,
        bool enabled = false,
        DateTimeOffset? now = null)
    {
        if (!enabled) Fail("runtime_configuration_application_v2_disabled");
        ArgumentNullException.ThrowIfNull(trust);

        // Re-enter through the accepted parser so this layer cannot consume a
        // caller-constructed approximation of a validated package.
        var package = packageService.ValidateJson(canonicalPackageJson, trust, now);
        var projection = package.RuntimeConfiguration;
        AssertTrustedProjection(package, projection);

        var publicSettings = projection.PublicSettings;
        var api = ConvertPublicSettings(publicSettings.Take(ApiPublicNames.Length).ToArray(), "api", ApiPublicNames);
        var portal = ConvertPublicSettings(publicSettings.Skip(ApiPublicNames.Length).ToArray(), "portal", PortalPublicNames);
        var protectedReferences = ConvertVersionedProtectedSettings(projection.ProtectedSettings, publicSettings);
        var license = projection.ProtectedSettings.Single(item => item.Name == "API_LICENSE_SIGNED_PAYLOAD");
        var cursor = projection.ProtectedSettings.Single(item => item.Name == "API_IMAGE_ASSET_CURSOR_SECRET");
        var rollbackTargets = api.Select(item => $"api:{item.Name}")
            .Concat(portal.Select(item => $"portal:{item.Name}"))
            .Concat(protectedReferences.Select(item => $"api:{item.Name}"))
            .ToArray();

        var plan = new RuntimeConfigurationApplicationV2Plan
        {
            PackageHash = package.PackageHash,
            ProjectionSha256 = projection.ProjectionSha256,
            Binding = new RuntimeConfigurationApplicationBindingV2
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
            },
            ApiPublicSettings = api,
            PortalPublicSettings = portal,
            ApiProtectedSettingReferences = protectedReferences,
            LicenseAcquisition = new RuntimeConfigurationApplicationLicenseDescriptorV2
            {
                ContractVersion = license.Reference.ContractVersion!,
                OpaqueReference = license.Reference.OpaqueReference!,
                VaultResourceId = license.Reference.VaultResourceId,
                SecretName = license.Reference.SecretName
            },
            CursorGeneration = new RuntimeConfigurationApplicationCursorDescriptorV2
            {
                GenerationAlgorithm = cursor.Reference.GenerationAlgorithm!,
                MinimumEntropyBytes = cursor.Reference.MinimumEntropyBytes!.Value,
                VaultResourceId = cursor.Reference.VaultResourceId,
                SecretName = cursor.Reference.SecretName
            },
            Rollback = new RuntimeConfigurationRollbackPlanV2
            {
                TargetQualifiedSettings = rollbackTargets,
                ContainsValues = false
            }
        };
        var canonical = FormatCanonicalPlan(plan);
        return CopyWithCanonicalIdentity(plan, canonical, Sha256(Encoding.UTF8.GetBytes(canonical)));
    }

    public void GenerateCursorSecret(
        RuntimeConfigurationApplicationV2Plan plan,
        IRuntimeConfigurationCursorSecretGenerator generator,
        IRuntimeConfigurationProtectedSecretCallback callback,
        bool enabled = false)
    {
        if (!enabled) Fail("runtime_configuration_cursor_generation_disabled");
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(callback);
        AssertPlanIdentity(plan);

        var entropy = generator.Generate(plan.CursorGeneration.MinimumEntropyBytes);
        if (entropy is null || entropy.Length < plan.CursorGeneration.MinimumEntropyBytes)
        {
            if (entropy is not null) CryptographicOperations.ZeroMemory(entropy);
            Fail("runtime_configuration_cursor_entropy_invalid");
        }

        byte[]? encoded = null;
        try
        {
            encoded = Encoding.ASCII.GetBytes(Convert.ToBase64String(entropy!).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
            callback.Accept(plan.CursorGeneration, encoded);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
            if (encoded is not null) CryptographicOperations.ZeroMemory(encoded);
        }
    }

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

    private static IReadOnlyList<RuntimeConfigurationApplicationSettingV2> ConvertPublicSettings(
        IReadOnlyList<RuntimeConfigurationPublicSettingV2> settings,
        string target,
        IReadOnlyList<string> expectedNames)
    {
        if (settings.Count != expectedNames.Count) Fail("runtime_configuration_application_v2_public_shape");
        var result = new List<RuntimeConfigurationApplicationSettingV2>(settings.Count);
        for (var index = 0; index < settings.Count; index++)
        {
            var setting = settings[index];
            if (setting.TargetApp != target || setting.Name != expectedNames[index]) Fail("runtime_configuration_application_v2_public_shape");
            result.Add(new RuntimeConfigurationApplicationSettingV2 { Name = setting.Name, Value = ToEnvironmentValue(setting) });
        }
        return result;
    }

    private static string ToEnvironmentValue(RuntimeConfigurationPublicSettingV2 setting) => setting.ValueType switch
    {
        "string" when setting.Value.ValueKind == JsonValueKind.String => setting.Value.GetString()!,
        "string-list" when setting.Value.ValueKind == JsonValueKind.Array => string.Join(",", setting.Value.EnumerateArray().Select(item => item.GetString()!)),
        "integer" when setting.Value.ValueKind == JsonValueKind.Number && setting.Value.TryGetInt32(out var number) => number.ToString(CultureInfo.InvariantCulture),
        "boolean" when setting.Value.ValueKind is JsonValueKind.True or JsonValueKind.False => setting.Value.GetBoolean() ? "true" : "false",
        _ => throw new InvalidDataException("runtime_configuration_application_v2_public_type")
    };

    private static IReadOnlyList<RuntimeConfigurationApplicationProtectedReferenceV2> ConvertVersionedProtectedSettings(
        IReadOnlyList<RuntimeConfigurationProtectedSettingV2> settings,
        IReadOnlyList<RuntimeConfigurationPublicSettingV2> publicSettings)
    {
        if (settings.Count != ProtectedSettings.Length) Fail("runtime_configuration_application_v2_protected_shape");
        var vaultOrigin = publicSettings.Single(item => item.Name == "API_AZURE_KEY_VAULT_URL").Value.GetString()!;
        var vaultName = new Uri(vaultOrigin).Host.Split('.')[0];
        var result = new List<RuntimeConfigurationApplicationProtectedReferenceV2>(settings.Count);
        for (var index = 0; index < settings.Count; index++)
        {
            var item = settings[index];
            var expected = ProtectedSettings[index];
            if (item.TargetApp != "api" || item.Name != expected.Name || item.Mode != expected.Mode ||
                !item.Reference.VaultResourceId.EndsWith($"/vaults/{vaultName}", StringComparison.OrdinalIgnoreCase))
                Fail("runtime_configuration_application_v2_protected_shape");
            if (item.Mode != "customer-azure-key-vault-reference") continue;
            var secretUri = $"{vaultOrigin}/secrets/{item.Reference.SecretName}/{item.Reference.SecretVersion}";
            result.Add(new RuntimeConfigurationApplicationProtectedReferenceV2
            {
                Name = item.Name,
                Mode = item.Mode,
                KeyVaultReference = $"@Microsoft.KeyVault(SecretUri={secretUri})"
            });
        }
        return result;
    }

    private static void AssertPlanIdentity(RuntimeConfigurationApplicationV2Plan plan)
    {
        if (plan.ContractVersion != RuntimeConfigurationApplicationV2Plan.ContractVersionValue ||
            plan.ApiPublicSettings.Count != 31 || plan.PortalPublicSettings.Count != 11 ||
            plan.ApiProtectedSettingReferences.Count != 2 || plan.CursorGeneration.GenerationAlgorithm != "random-base64url" ||
            plan.CursorGeneration.MinimumEntropyBytes != 32 || plan.Rollback.ContainsValues ||
            plan.CanonicalJson != FormatCanonicalPlan(plan) ||
            !FixedEquals(plan.PlanSha256, Sha256(Encoding.UTF8.GetBytes(plan.CanonicalJson))))
            Fail("runtime_configuration_application_v2_plan_identity");
    }

    private static RuntimeConfigurationApplicationV2Plan CopyWithCanonicalIdentity(RuntimeConfigurationApplicationV2Plan plan, string json, string digest) => new()
    {
        PackageHash = plan.PackageHash,
        ProjectionSha256 = plan.ProjectionSha256,
        Binding = plan.Binding,
        ApiPublicSettings = plan.ApiPublicSettings,
        PortalPublicSettings = plan.PortalPublicSettings,
        ApiProtectedSettingReferences = plan.ApiProtectedSettingReferences,
        LicenseAcquisition = plan.LicenseAcquisition,
        CursorGeneration = plan.CursorGeneration,
        Rollback = plan.Rollback,
        CanonicalJson = json,
        PlanSha256 = digest
    };

    private static string FormatCanonicalPlan(RuntimeConfigurationApplicationV2Plan plan)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("contractVersion", plan.ContractVersion);
            writer.WriteString("packageHash", plan.PackageHash);
            writer.WriteString("projectionSha256", plan.ProjectionSha256);
            writer.WritePropertyName("binding");
            writer.WriteStartObject();
            writer.WriteString("customerId", plan.Binding.CustomerId); writer.WriteString("installationId", plan.Binding.InstallationId);
            writer.WriteString("environmentId", plan.Binding.EnvironmentId); writer.WriteString("tenantId", plan.Binding.TenantId);
            writer.WriteString("azureSubscriptionId", plan.Binding.AzureSubscriptionId); writer.WriteString("deploymentExportId", plan.Binding.DeploymentExportId);
            writer.WriteString("runtimeReleaseId", plan.Binding.RuntimeReleaseId); writer.WriteString("runtimeVersion", plan.Binding.RuntimeVersion);
            writer.WriteString("manifestSha256", plan.Binding.ManifestSha256); writer.WriteEndObject();
            WriteSettings(writer, "apiPublicSettings", plan.ApiPublicSettings);
            WriteSettings(writer, "portalPublicSettings", plan.PortalPublicSettings);
            writer.WritePropertyName("apiProtectedSettingReferences"); writer.WriteStartArray();
            foreach (var item in plan.ApiProtectedSettingReferences) { writer.WriteStartObject(); writer.WriteString("name", item.Name); writer.WriteString("mode", item.Mode); writer.WriteString("keyVaultReference", item.KeyVaultReference); writer.WriteEndObject(); }
            writer.WriteEndArray();
            writer.WritePropertyName("licenseAcquisition"); writer.WriteStartObject(); writer.WriteString("contractVersion", plan.LicenseAcquisition.ContractVersion); writer.WriteString("opaqueReference", plan.LicenseAcquisition.OpaqueReference); writer.WriteString("vaultResourceId", plan.LicenseAcquisition.VaultResourceId); writer.WriteString("secretName", plan.LicenseAcquisition.SecretName); writer.WriteEndObject();
            writer.WritePropertyName("cursorGeneration"); writer.WriteStartObject(); writer.WriteString("generationAlgorithm", plan.CursorGeneration.GenerationAlgorithm); writer.WriteNumber("minimumEntropyBytes", plan.CursorGeneration.MinimumEntropyBytes); writer.WriteString("vaultResourceId", plan.CursorGeneration.VaultResourceId); writer.WriteString("secretName", plan.CursorGeneration.SecretName); writer.WriteEndObject();
            writer.WritePropertyName("rollback"); writer.WriteStartObject(); writer.WriteString("strategy", plan.Rollback.Strategy); writer.WritePropertyName("targetQualifiedSettings"); writer.WriteStartArray(); foreach (var item in plan.Rollback.TargetQualifiedSettings) writer.WriteStringValue(item); writer.WriteEndArray(); writer.WriteBoolean("containsValues", plan.Rollback.ContainsValues); writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void WriteSettings(Utf8JsonWriter writer, string name, IReadOnlyList<RuntimeConfigurationApplicationSettingV2> settings)
    {
        writer.WritePropertyName(name); writer.WriteStartArray();
        foreach (var item in settings) { writer.WriteStartObject(); writer.WriteString("name", item.Name); writer.WriteString("value", item.Value); writer.WriteEndObject(); }
        writer.WriteEndArray();
    }

    private static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    private static void Fail(string code) => throw new InvalidDataException(code);
}
