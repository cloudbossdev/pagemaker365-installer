using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

/// <summary>
/// Installer-owned catalog authority. Callers may supply bytes only to this
/// constructor; both files must match the independently pinned SPO hashes
/// before any catalog entry becomes trusted.
/// </summary>
public sealed class RuntimeConfigurationCatalogV1Authority
{
    public const string SchemaVersion = "pagemaker365.runtime-configuration.v1";
    public const string SourceRepository = "cloudbossdev/spo-ui";
    public const string SourceCommit = "c31427d0027adb4fd03de142fde18c4209ca44ce";
    public const string CatalogSha256 = "441a7083b27c6a76a0910b68b0aab3bd47efaf977cf48d594b5a3e48374e9cc6";
    public const string CatalogSchemaSha256 = "fb0df2a4b19c0dc8b4a951e4aeb4cd9cb21217ee4e91d86399e64809ce8b8f7e";

    private static readonly string[] ConditionalKeys =
    [
        "api:API_CONNECTOR_ENTITLEMENTS_SYNC_URL",
        "api:API_CONNECTOR_ENTITLEMENTS_PUBLIC_KEY_PEM",
        "api:API_WEB_PART_ENTITLEMENTS_SYNC_URL",
        "api:API_WEB_PART_ENTITLEMENT_PUBLIC_KEYS",
        "api:API_WEB_PART_ENTITLEMENTS_SYNC_INTERVAL_HOURS",
        "api:API_WEB_PART_ENTITLEMENTS_TIMEOUT_SECONDS",
        "api:API_CONNECTOR_EGRESS_ALLOWED_HOSTS"
    ];

    private static readonly string[] OptionalKeys =
    [
        "api:API_PORT", "api:API_LOG_LEVEL", "api:API_LICENSE_VALIDATION_URL",
        "api:API_WEB_PART_CATALOG_MODE", "api:API_WEB_PART_REGISTRY_MODE",
        "portal:WEB_ENABLE_WEB_PART_WORKBENCH"
    ];

    private static readonly string[] PlatformKeys = ["api:PORT", "portal:PORT"];

    private static readonly string[] ForbiddenKeys =
    [
        "api:API_WEBPART_TEST_ARTIFACTS_ENABLED",
        "api:API_WEBPART_TEST_ARTIFACTS_ALLOWED_USER_IDS",
        "api:API_WEBPART_TEST_ARTIFACTS_ALLOWED_WORKSPACE_SLUGS",
        "api:API_WEBPART_TEST_ARTIFACTS_ALLOWED_SITE_CONNECTION_IDS",
        "api:API_WEBPART_TEST_ARTIFACTS_ALLOWED_PAGE_IDS",
        "api:API_WEBPART_TEST_ARTIFACTS_FILE_PREVIEW_PAGE_ID",
        "api:API_WEBPART_TEST_ARTIFACTS_FILE_PREVIEW_SOURCE_KEY",
        "api:API_WEBPART_TEST_ARTIFACTS_FILE_PREVIEW_DRIVE_ITEM_ID",
        "api:API_WEBPART_TEST_ARTIFACTS_RETENTION_HOURS"
    ];

    private RuntimeConfigurationCatalogV1Authority(IReadOnlyList<CatalogEntry> entries, string orderSha256)
    {
        Entries = entries;
        TargetQualifiedOrderSha256 = orderSha256;
        RequiredPublic = entries.Where(item => item.RequiredWhen == "customer-production" && item.Classification == "public").ToArray();
        RequiredProtected = entries.Where(item => item.RequiredWhen == "customer-production" && item.Classification == "protected-reference").ToArray();
    }

    internal IReadOnlyList<CatalogEntry> Entries { get; }
    internal IReadOnlyList<CatalogEntry> RequiredPublic { get; }
    internal IReadOnlyList<CatalogEntry> RequiredProtected { get; }
    public string TargetQualifiedOrderSha256 { get; }

    public static RuntimeConfigurationCatalogV1Authority Create(ReadOnlySpan<byte> catalogBytes, ReadOnlySpan<byte> schemaBytes)
    {
        if (!PrivateRuntimeCanonicalJson.FixedEquals(PrivateRuntimeCanonicalJson.Sha256(catalogBytes), CatalogSha256) ||
            !PrivateRuntimeCanonicalJson.FixedEquals(PrivateRuntimeCanonicalJson.Sha256(schemaBytes), CatalogSchemaSha256))
        {
            Fail("runtime_configuration_catalog_bytes_mismatch");
        }

        using var document = JsonDocument.Parse(catalogBytes.ToArray(), StrictDocumentOptions);
        var root = document.RootElement;
        RequireExactProperties(root, "schemaVersion", "settings");
        if (RequireString(root, "schemaVersion") != SchemaVersion || root.GetProperty("settings").ValueKind != JsonValueKind.Array)
        {
            Fail("runtime_configuration_catalog_invalid");
        }

        var entries = new List<CatalogEntry>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in root.GetProperty("settings").EnumerateArray())
        {
            RequireExactProperties(item, "targetApp", "name", "classification", "requiredWhen", "valueType", "validationRule", "valueOwner", "installerSource");
            var entry = new CatalogEntry(
                RequireString(item, "targetApp"), RequireString(item, "name"), RequireString(item, "classification"),
                RequireString(item, "requiredWhen"), RequireString(item, "valueType"), RequireString(item, "validationRule"),
                RequireString(item, "valueOwner"), RequireString(item, "installerSource"));
            if (entry.TargetApp is not ("api" or "portal") || !Regex.IsMatch(entry.Name, "^[A-Z][A-Z0-9_]{0,127}$", RegexOptions.CultureInvariant) || !keys.Add(entry.Key))
            {
                Fail("runtime_configuration_catalog_invalid");
            }
            entries.Add(entry);
        }

        var requiredPublic = entries.Where(item => item.RequiredWhen == "customer-production" && item.Classification == "public").ToArray();
        var requiredProtected = entries.Where(item => item.RequiredWhen == "customer-production" && item.Classification == "protected-reference").ToArray();
        var conditional = entries.Where(item => item.RequiredWhen is "connector-sync-enabled" or "web-part-sync-enabled").Select(item => item.Key).ToArray();
        var optional = entries.Where(item => item.RequiredWhen == "optional").Select(item => item.Key).ToArray();
        var platform = entries.Where(item => item.RequiredWhen == "platform-supplied").Select(item => item.Key).ToArray();
        var forbidden = entries.Where(item => item.Classification == "production-forbidden").Select(item => item.Key).ToArray();
        if (entries.Count != 70 || requiredPublic.Length != 42 || requiredProtected.Length != 4 ||
            !conditional.SequenceEqual(ConditionalKeys, StringComparer.Ordinal) ||
            !optional.SequenceEqual(OptionalKeys, StringComparer.Ordinal) ||
            !platform.SequenceEqual(PlatformKeys, StringComparer.Ordinal) ||
            !forbidden.SequenceEqual(ForbiddenKeys, StringComparer.Ordinal))
        {
            Fail("runtime_configuration_catalog_partition_mismatch");
        }

        var keyJson = JsonSerializer.SerializeToUtf8Bytes(entries.Select(item => item.Key).ToArray(), CompactJsonOptions);
        return new RuntimeConfigurationCatalogV1Authority(entries, PrivateRuntimeCanonicalJson.Sha256(keyJson));
    }

    internal static JsonDocumentOptions StrictDocumentOptions => new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };

    internal static JsonSerializerOptions CompactJsonOptions => new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    internal sealed record CatalogEntry(
        string TargetApp,
        string Name,
        string Classification,
        string RequiredWhen,
        string ValueType,
        string ValidationRule,
        string ValueOwner,
        string InstallerSource)
    {
        public string Key => $"{TargetApp}:{Name}";
    }

    internal static void RequireExactProperties(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.EnumerateObject().Select(item => item.Name).SequenceEqual(expected, StringComparer.Ordinal))
        {
            Fail("runtime_configuration_closed_shape");
        }
    }

    internal static string RequireString(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var candidate) || candidate.ValueKind != JsonValueKind.String)
        {
            Fail("runtime_configuration_value_type");
        }
        return candidate.GetString() ?? "";
    }

    internal static void Fail(string code) => throw new InvalidDataException(code);
}

public sealed class RuntimeConfigurationProjectionV2Validator(RuntimeConfigurationCatalogV1Authority catalog)
{
    public const string ProjectionVersion = "pagemaker365.runtime-configuration-projection.v2";
    public const string ProtectedSettingAcquisitionVersion = "pagemaker365.protected-setting-acquisition.v1";

    private static readonly Regex Sha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex SafeToken = new("^[A-Za-z0-9][A-Za-z0-9._:+-]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex RealUuid = new("^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UnsafeText = new("[\\u0000-\\u001f\\u007f-\\u009f\\u2028\\u2029\\u202a-\\u202e\\u2066-\\u2069]", RegexOptions.CultureInvariant);
    private static readonly string[] SharePointSuffixes = [".sharepoint.com", ".sharepoint.cn", ".sharepoint.de", ".sharepoint.us"];
    private static readonly IReadOnlyDictionary<string, string> ProtectedModes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DATABASE_URL"] = "customer-azure-key-vault-reference",
        ["API_ENTRA_CLIENT_SECRET"] = "customer-azure-key-vault-reference",
        ["API_LICENSE_SIGNED_PAYLOAD"] = "control-plane-protected-setting-delivery",
        ["API_IMAGE_ASSET_CURSOR_SECRET"] = "installer-generated-key-vault-secret"
    };

    public RuntimeConfigurationProjectionV2 Validate(JsonElement value, RuntimeConfigurationPackageBindingV2 expectedBinding)
    {
        RuntimeConfigurationCatalogV1Authority.RequireExactProperties(value,
            "schemaVersion", "catalog", "binding", "featureProfile", "publicSettings", "protectedSettings", "projectionSha256");
        if (RuntimeConfigurationCatalogV1Authority.RequireString(value, "schemaVersion") != ProjectionVersion)
        {
            Fail("runtime_configuration_projection_v2_identity");
        }

        var catalogBinding = ValidateCatalog(value.GetProperty("catalog"));
        var binding = ValidateBinding(value.GetProperty("binding"));
        AssertBinding(binding, expectedBinding);
        var feature = value.GetProperty("featureProfile");
        RuntimeConfigurationCatalogV1Authority.RequireExactProperties(feature, "connectorSynchronization", "webPartSynchronization");
        if (feature.GetProperty("connectorSynchronization").ValueKind != JsonValueKind.False ||
            feature.GetProperty("webPartSynchronization").ValueKind != JsonValueKind.False)
        {
            Fail("runtime_configuration_projection_v2_feature_profile");
        }

        var publicSettings = ValidatePublicSettings(value.GetProperty("publicSettings"), binding);
        var protectedSettings = ValidateProtectedSettings(value.GetProperty("protectedSettings"), binding.AzureSubscriptionId);
        var digest = RuntimeConfigurationCatalogV1Authority.RequireString(value, "projectionSha256");
        if (!Sha256.IsMatch(digest)) Fail("runtime_configuration_projection_v2_identity");
        var computed = PrivateRuntimeCanonicalJson.Sha256(PrivateRuntimeCanonicalJson.CanonicalizeObjectWithoutProperty(value, "projectionSha256"));
        if (!PrivateRuntimeCanonicalJson.FixedEquals(digest, computed)) Fail("runtime_configuration_projection_v2_digest");

        return new RuntimeConfigurationProjectionV2
        {
            Catalog = catalogBinding,
            Binding = binding,
            ConnectorSynchronization = false,
            WebPartSynchronization = false,
            PublicSettings = publicSettings,
            ProtectedSettings = protectedSettings,
            ProjectionSha256 = digest
        };
    }

    private RuntimeConfigurationCatalogBindingV2 ValidateCatalog(JsonElement value)
    {
        RuntimeConfigurationCatalogV1Authority.RequireExactProperties(value,
            "schemaVersion", "sourceRepository", "sourceCommit", "catalogSha256", "catalogSchemaSha256", "targetQualifiedOrderSha256", "settingCount");
        if (RuntimeConfigurationCatalogV1Authority.RequireString(value, "schemaVersion") != RuntimeConfigurationCatalogV1Authority.SchemaVersion ||
            RuntimeConfigurationCatalogV1Authority.RequireString(value, "sourceRepository") != RuntimeConfigurationCatalogV1Authority.SourceRepository ||
            RuntimeConfigurationCatalogV1Authority.RequireString(value, "sourceCommit") != RuntimeConfigurationCatalogV1Authority.SourceCommit ||
            RuntimeConfigurationCatalogV1Authority.RequireString(value, "catalogSha256") != RuntimeConfigurationCatalogV1Authority.CatalogSha256 ||
            RuntimeConfigurationCatalogV1Authority.RequireString(value, "catalogSchemaSha256") != RuntimeConfigurationCatalogV1Authority.CatalogSchemaSha256 ||
            RuntimeConfigurationCatalogV1Authority.RequireString(value, "targetQualifiedOrderSha256") != catalog.TargetQualifiedOrderSha256 ||
            !value.GetProperty("settingCount").TryGetInt32(out var count) || count != 70 ||
            !string.Equals(value.GetProperty("settingCount").GetRawText(), "70", StringComparison.Ordinal))
        {
            Fail("runtime_configuration_projection_v2_catalog");
        }
        return new RuntimeConfigurationCatalogBindingV2
        {
            SchemaVersion = RuntimeConfigurationCatalogV1Authority.SchemaVersion,
            SourceRepository = RuntimeConfigurationCatalogV1Authority.SourceRepository,
            SourceCommit = RuntimeConfigurationCatalogV1Authority.SourceCommit,
            CatalogSha256 = RuntimeConfigurationCatalogV1Authority.CatalogSha256,
            CatalogSchemaSha256 = RuntimeConfigurationCatalogV1Authority.CatalogSchemaSha256,
            TargetQualifiedOrderSha256 = catalog.TargetQualifiedOrderSha256,
            SettingCount = 70
        };
    }

    private static RuntimeConfigurationPackageBindingV2 ValidateBinding(JsonElement value)
    {
        RuntimeConfigurationCatalogV1Authority.RequireExactProperties(value,
            "packageContractVersion", "customerId", "installationId", "environmentId", "tenantId", "azureSubscriptionId",
            "deploymentExportId", "runtimeReleaseId", "runtimeVersion", "manifestSha256");
        var result = new RuntimeConfigurationPackageBindingV2
        {
            PackageContractVersion = Require(value, "packageContractVersion"),
            CustomerId = Require(value, "customerId"),
            InstallationId = Require(value, "installationId"),
            EnvironmentId = Require(value, "environmentId"),
            TenantId = Require(value, "tenantId"),
            AzureSubscriptionId = Require(value, "azureSubscriptionId"),
            DeploymentExportId = Require(value, "deploymentExportId"),
            RuntimeReleaseId = Require(value, "runtimeReleaseId"),
            RuntimeVersion = Require(value, "runtimeVersion"),
            ManifestSha256 = Require(value, "manifestSha256")
        };
        if (result.PackageContractVersion != PrivateRuntimeDeliveryPackageV07.ContractVersionValue ||
            new[] { result.CustomerId, result.InstallationId, result.EnvironmentId, result.TenantId, result.AzureSubscriptionId, result.DeploymentExportId }.Any(item => !IsRealUuid(item)) ||
            !SafeToken.IsMatch(result.RuntimeReleaseId) || !IsInt32Semver(result.RuntimeVersion) || !Sha256.IsMatch(result.ManifestSha256))
        {
            Fail("runtime_configuration_projection_v2_binding");
        }
        return result;
    }

    private static void AssertBinding(RuntimeConfigurationPackageBindingV2 actual, RuntimeConfigurationPackageBindingV2 expected)
    {
        var fields = new[]
        {
            (actual.PackageContractVersion, expected.PackageContractVersion), (actual.CustomerId, expected.CustomerId),
            (actual.InstallationId, expected.InstallationId), (actual.EnvironmentId, expected.EnvironmentId),
            (actual.TenantId, expected.TenantId), (actual.AzureSubscriptionId, expected.AzureSubscriptionId),
            (actual.DeploymentExportId, expected.DeploymentExportId), (actual.RuntimeReleaseId, expected.RuntimeReleaseId),
            (actual.RuntimeVersion, expected.RuntimeVersion), (actual.ManifestSha256, expected.ManifestSha256)
        };
        if (fields.Any(pair => !string.Equals(pair.Item1, pair.Item2, StringComparison.Ordinal))) Fail("runtime_configuration_projection_v2_binding");
    }

    private IReadOnlyList<RuntimeConfigurationPublicSettingV2> ValidatePublicSettings(JsonElement values, RuntimeConfigurationPackageBindingV2 binding)
    {
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != catalog.RequiredPublic.Count)
        {
            Fail("runtime_configuration_projection_v2_public_count");
        }
        var result = new List<RuntimeConfigurationPublicSettingV2>();
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var index = 0;
        foreach (var value in values.EnumerateArray())
        {
            var expected = catalog.RequiredPublic[index++];
            RuntimeConfigurationCatalogV1Authority.RequireExactProperties(value, "targetApp", "name", "valueType", "value");
            var target = Require(value, "targetApp");
            var name = Require(value, "name");
            var type = Require(value, "valueType");
            if (target != expected.TargetApp || name != expected.Name || type != expected.ValueType) Fail("runtime_configuration_projection_v2_public_order");
            var settingValue = value.GetProperty("value");
            ValidateCatalogValue(settingValue, expected);
            ValidatePolicy(name, settingValue);
            map.Add(name, settingValue.Clone());
            result.Add(new RuntimeConfigurationPublicSettingV2 { TargetApp = target, Name = name, ValueType = type, Value = settingValue.Clone() });
        }
        ValidateCrossBindings(map, binding);
        return result;
    }

    private static void ValidateCatalogValue(JsonElement value, RuntimeConfigurationCatalogV1Authority.CatalogEntry item)
    {
        switch (item.ValueType)
        {
            case "string":
                if (value.ValueKind != JsonValueKind.String) Fail("runtime_configuration_projection_v2_value_type");
                var text = value.GetString() ?? "";
                if (text.Length == 0 || Encoding.UTF8.GetByteCount(text) > 16_384 || text.Contains('\0') ||
                    (item.ValidationRule != "public-key-pem.max-16384" && text != text.Trim())) Fail("runtime_configuration_projection_v2_value_type");
                break;
            case "string-list":
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < 1 or > 32) Fail("runtime_configuration_projection_v2_value_type");
                var entries = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "").ToArray();
                if (entries.Any(item => item.Length == 0 || item != item.Trim()) || entries.Distinct(StringComparer.Ordinal).Count() != entries.Length) Fail("runtime_configuration_projection_v2_value_type");
                break;
            case "integer":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 0 || value.GetRawText() != number.ToString(CultureInfo.InvariantCulture)) Fail("runtime_configuration_projection_v2_value_type");
                break;
            case "boolean":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Fail("runtime_configuration_projection_v2_value_type");
                break;
            default:
                Fail("runtime_configuration_projection_v2_value_type");
                break;
        }
        ValidateRule(value, item.ValidationRule);
    }

    private static void ValidateRule(JsonElement value, string rule)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        var list = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(item => item.GetString() ?? "").ToArray() : [];
        var integer = value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) ? parsed : 0;
        var valid = rule switch
        {
            "stable-semver.equals-runtime-version" or "stable-semver" => IsInt32Semver(text),
            "constant.production" => text == "production",
            "constant.ipv4-any" => text == "0.0.0.0",
            "constant.pagemaker365" => text == "PageMaker365",
            "constant.product-logo-path" => text == "/branding/pagemaker365-logo.png",
            "guid.non-placeholder" or "guid.non-placeholder.equals-api-tenant" or "deployment-export-id" => IsRealUuid(text),
            "safe-lowercase-slug" => Regex.IsMatch(text, "^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant),
            "safe-text.max-128" or "sharepoint-container-name" => IsSafeText(text, 128),
            "safe-text.max-64" => IsSafeText(text, 64),
            "https-url.no-credentials" => IsSafeHttpsUrl(text),
            "https-origin.api" => IsCanonicalHttpsOrigin(text),
            "azure-key-vault-origin" => IsAzureKeyVaultOrigin(text),
            "sharepoint-site-url" => IsSharePointSiteUrl(text),
            "dns-hostname" => Regex.IsMatch(text, "^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "license-environment-key" => Regex.IsMatch(text, "^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant),
            "runtime-release-id" => SafeToken.IsMatch(text),
            "integer.nonnegative" => integer >= 0,
            "integer.positive" => integer >= 1,
            "boolean.true-production-default" => value.ValueKind == JsonValueKind.True,
            "boolean.proxy-topology" => value.ValueKind == JsonValueKind.False,
            "enum.enabled-disabled" => text is "enabled" or "disabled",
            "enum.preview-unavailable-disabled" => text is "preview-unavailable" or "disabled",
            "public-key-pem.max-16384" => IsCanonicalPublicKeyPem(text),
            "entra-api-audience" => IsUniqueList(list, item => Regex.IsMatch(item, "^api://[0-9a-f-]{36}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            "graph-scope-list" => list.SequenceEqual(["https://graph.microsoft.com/.default"], StringComparer.Ordinal),
            "delegated-api-scope-list" => list.SequenceEqual(["access_as_user"], StringComparer.Ordinal),
            "delegated-api-scope.access-as-user" => Regex.IsMatch(text, "^api://[0-9a-f-]{36}/access_as_user$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "entra-authority.exact-tenant" => Regex.IsMatch(text, "^https://login\\.microsoftonline\\.com/[0-9a-f-]{36}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "https-origin-list.portal" => IsUniqueList(list, IsCanonicalHttpsOrigin),
            "sharepoint-origin-list.max-8" or "sharepoint-origin-list.max-8.equals-api" => list.Length <= 8 && IsUniqueList(list, IsSharePointOrigin),
            _ => false
        };
        if (!valid) Fail("runtime_configuration_projection_v2_value");
    }

    private static void ValidatePolicy(string name, JsonElement value)
    {
        var valid = name switch
        {
            "API_CONNECTOR_EGRESS_REQUIRE_ALLOWLIST" => value.ValueKind == JsonValueKind.True,
            "API_RUNTIME_TRUST_FORWARDED_HOST" => value.ValueKind == JsonValueKind.False,
            "API_FILE_PREVIEW_DOWNLOAD_POLICY" or "API_FILE_PREVIEW_SOURCE_FALLBACK_POLICY" => value.ValueKind == JsonValueKind.String && value.GetString() == "disabled",
            _ => true
        };
        if (!valid) Fail("runtime_configuration_projection_v2_policy");
    }

    private static void ValidateCrossBindings(IReadOnlyDictionary<string, JsonElement> values, RuntimeConfigurationPackageBindingV2 binding)
    {
        string Text(string key) => values[key].GetString() ?? "";
        string[] List(string key) => values[key].EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
        if (Text("API_APP_VERSION") != binding.RuntimeVersion || Text("PM365_RUNTIME_VERSION") != binding.RuntimeVersion || Text("PM365_RUNTIME_RELEASE_ID") != binding.RuntimeReleaseId)
            Fail("runtime_configuration_projection_v2_release_binding");
        if (Text("API_ENTRA_TENANT_ID") != binding.TenantId || Text("WEB_ENTRA_TENANT_ID") != binding.TenantId || Text("WEB_ENTRA_AUTHORITY") != $"https://login.microsoftonline.com/{binding.TenantId}")
            Fail("runtime_configuration_projection_v2_tenant_binding");
        if (Text("PM365_DEPLOYMENT_EXPORT_ID") != binding.DeploymentExportId) Fail("runtime_configuration_projection_v2_export_binding");
        if (!List("API_ENTRA_AUDIENCE").SequenceEqual([$"api://{Text("API_ENTRA_CLIENT_ID")}"], StringComparer.Ordinal)) Fail("runtime_configuration_projection_v2_audience_binding");
        if (Text("WEB_API_SCOPE") != $"api://{Text("API_ENTRA_CLIENT_ID")}/access_as_user" || Text("WEB_ENTRA_CLIENT_ID") == Text("API_ENTRA_CLIENT_ID")) Fail("runtime_configuration_projection_v2_consent_binding");
        if (!IsCanonicalHttpsOrigin(Text("PAGEMAKER365_PORTAL_URL")) || !List("API_CORS_ORIGIN").SequenceEqual([Text("PAGEMAKER365_PORTAL_URL")], StringComparer.Ordinal)) Fail("runtime_configuration_projection_v2_portal_origin_binding");
        if (!List("API_FILE_PREVIEW_ALLOWED_FRAME_ORIGINS").SequenceEqual(List("WEB_FILE_PREVIEW_ALLOWED_FRAME_ORIGINS"), StringComparer.Ordinal)) Fail("runtime_configuration_projection_v2_preview_binding");
    }

    private IReadOnlyList<RuntimeConfigurationProtectedSettingV2> ValidateProtectedSettings(JsonElement values, string subscriptionId)
    {
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != catalog.RequiredProtected.Count) Fail("runtime_configuration_projection_v2_protected_count");
        var result = new List<RuntimeConfigurationProtectedSettingV2>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var value in values.EnumerateArray())
        {
            var expected = catalog.RequiredProtected[index++];
            RuntimeConfigurationCatalogV1Authority.RequireExactProperties(value, "targetApp", "name", "mode", "reference");
            var target = Require(value, "targetApp");
            var name = Require(value, "name");
            var mode = Require(value, "mode");
            if (target != expected.TargetApp || name != expected.Name || !ProtectedModes.TryGetValue(name, out var expectedMode) || mode != expectedMode)
                Fail("runtime_configuration_projection_v2_protected_mode");
            var reference = ValidateProtectedReference(mode, value.GetProperty("reference"), subscriptionId);
            if (!targets.Add($"{reference.VaultResourceId}:{reference.SecretName}")) Fail("runtime_configuration_projection_v2_protected_reference_reuse");
            result.Add(new RuntimeConfigurationProtectedSettingV2 { TargetApp = target, Name = name, Mode = mode, Reference = reference });
        }
        return result;
    }

    private static RuntimeConfigurationProtectedReferenceV2 ValidateProtectedReference(string mode, JsonElement value, string subscriptionId)
    {
        RuntimeConfigurationProtectedReferenceV2 result;
        if (mode == "customer-azure-key-vault-reference")
        {
            RuntimeConfigurationCatalogV1Authority.RequireExactProperties(value, "vaultResourceId", "secretName", "secretVersion");
            result = new RuntimeConfigurationProtectedReferenceV2 { VaultResourceId = Require(value, "vaultResourceId"), SecretName = Require(value, "secretName"), SecretVersion = Require(value, "secretVersion") };
            if (!Regex.IsMatch(result.SecretVersion, "^[0-9a-f]{32}$", RegexOptions.CultureInvariant)) Fail("runtime_configuration_projection_v2_protected_reference");
        }
        else if (mode == "control-plane-protected-setting-delivery")
        {
            RuntimeConfigurationCatalogV1Authority.RequireExactProperties(value, "contractVersion", "opaqueReference", "vaultResourceId", "secretName");
            result = new RuntimeConfigurationProtectedReferenceV2 { ContractVersion = Require(value, "contractVersion"), OpaqueReference = Require(value, "opaqueReference"), VaultResourceId = Require(value, "vaultResourceId"), SecretName = Require(value, "secretName") };
            if (result.ContractVersion != ProtectedSettingAcquisitionVersion || !Regex.IsMatch(result.OpaqueReference, "^psr_[A-Za-z0-9_-]{24,64}$", RegexOptions.CultureInvariant)) Fail("runtime_configuration_projection_v2_protected_reference");
        }
        else
        {
            RuntimeConfigurationCatalogV1Authority.RequireExactProperties(value, "vaultResourceId", "secretName", "generationAlgorithm", "minimumEntropyBytes");
            if (!value.GetProperty("minimumEntropyBytes").TryGetInt32(out var entropy) ||
                !string.Equals(value.GetProperty("minimumEntropyBytes").GetRawText(), "32", StringComparison.Ordinal))
                Fail("runtime_configuration_projection_v2_protected_reference");
            result = new RuntimeConfigurationProtectedReferenceV2 { VaultResourceId = Require(value, "vaultResourceId"), SecretName = Require(value, "secretName"), GenerationAlgorithm = Require(value, "generationAlgorithm"), MinimumEntropyBytes = entropy };
            if (result.GenerationAlgorithm != "random-base64url" || entropy != 32) Fail("runtime_configuration_projection_v2_protected_reference");
        }
        ValidateVaultTarget(result.VaultResourceId, result.SecretName, subscriptionId);
        return result;
    }

    private static void ValidateVaultTarget(string resourceId, string secretName, string subscriptionId)
    {
        var match = Regex.Match(resourceId, "^/subscriptions/([0-9a-f-]{36})/resourceGroups/([A-Za-z0-9._()-]{1,90})/providers/Microsoft\\.KeyVault/vaults/([A-Za-z0-9-]{3,24})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !string.Equals(match.Groups[1].Value, subscriptionId, StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(secretName, "^[A-Za-z0-9-]{1,127}$", RegexOptions.CultureInvariant))
            Fail("runtime_configuration_projection_v2_protected_reference");
    }

    private static bool IsInt32Semver(string value)
    {
        var match = Regex.Match(value, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);
        return match.Success && match.Groups.Cast<Group>().Skip(1).All(group => int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    private static bool IsRealUuid(string value)
    {
        if (!RealUuid.IsMatch(value) || !Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty) return false;
        var nibbles = value.ToLowerInvariant().Replace("-", "", StringComparison.Ordinal).ToCharArray();
        return nibbles.Where((_value, index) => index is not (12 or 16)).Distinct().Count() > 1;
    }
    private static bool IsSafeText(string value, int maximum) => value.Length <= maximum && value == value.Trim() && !UnsafeText.IsMatch(value);
    private static bool IsUniqueList(IReadOnlyList<string> values, Func<string, bool> predicate) => values.Count > 0 && values.All(predicate) && values.Distinct(StringComparer.Ordinal).Count() == values.Count;

    private static bool IsSafeHttpsUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool IsCanonicalHttpsOrigin(string value)
    {
        if (!Regex.IsMatch(value, "^https://[a-z0-9.-]+$", RegexOptions.CultureInvariant) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !uri.IsDefaultPort || uri.AbsolutePath != "/" || uri.IdnHost != uri.IdnHost.ToLowerInvariant() ||
            !Regex.IsMatch(uri.IdnHost, "^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$", RegexOptions.CultureInvariant)) return false;
        return string.Equals(value, $"https://{uri.IdnHost}", StringComparison.Ordinal);
    }

    private static bool IsSharePointOrigin(string value) => IsCanonicalHttpsOrigin(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri) && SharePointSuffixes.Any(suffix => uri.IdnHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    private static bool IsSharePointSiteUrl(string value) => IsSafeHttpsUrl(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri) && string.IsNullOrEmpty(uri.Query) && SharePointSuffixes.Any(suffix => uri.IdnHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    private static bool IsAzureKeyVaultOrigin(string value) => IsCanonicalHttpsOrigin(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri) && Regex.IsMatch(uri.IdnHost, "^[a-z0-9-]{3,24}\\.vault\\.azure\\.net$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsCanonicalPublicKeyPem(string value)
    {
        if (Encoding.UTF8.GetByteCount(value) > 16_384 || !value.EndsWith('\n') || value.Contains('\r')) return false;
        var match = Regex.Match(value, "^-----BEGIN PUBLIC KEY-----\\n((?:[A-Za-z0-9+/]{1,64}={0,2}\\n)+)-----END PUBLIC KEY-----\\n$", RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        try
        {
            var bytes = Convert.FromBase64String(match.Groups[1].Value.Replace("\n", "", StringComparison.Ordinal));
            var key = PublicKeyFactory.CreateKey(bytes);
            var canonicalBytes = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(key).GetDerEncoded();
            if (!bytes.SequenceEqual(canonicalBytes)) return false;
            var base64 = Convert.ToBase64String(bytes);
            var lines = Enumerable.Range(0, (base64.Length + 63) / 64).Select(index => base64.Substring(index * 64, Math.Min(64, base64.Length - index * 64)));
            var canonicalPem = "-----BEGIN PUBLIC KEY-----\n" + string.Join("\n", lines) + "\n-----END PUBLIC KEY-----\n";
            return value == canonicalPem;
        }
        catch (FormatException) { return false; }
        catch (Exception error) when (error is ArgumentException or IOException) { return false; }
    }

    private static string Require(JsonElement value, string property) => RuntimeConfigurationCatalogV1Authority.RequireString(value, property);
    private static void Fail(string code) => RuntimeConfigurationCatalogV1Authority.Fail(code);
}

internal static class PrivateRuntimeCanonicalJson
{
    public static byte[] Canonicalize(JsonElement value, bool excludePackageIntegrity = false)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            Write(writer, value, excludePackageIntegrity);
        }
        return stream.ToArray();
    }

    public static byte[] CanonicalizeObjectWithoutProperty(JsonElement value, string excludedProperty)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().Where(item => item.Name != excludedProperty).OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                Write(writer, property.Value, false);
            }
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value, bool excludePackageIntegrity)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject()
                    .Where(item => !excludePackageIntegrity || item.Name is not ("packageHash" or "signature"))
                    .OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value, excludePackageIntegrity);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) Write(writer, item, excludePackageIntegrity);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    public static string Sha256(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    public static bool FixedEquals(string left, string right)
    {
        var a = Encoding.ASCII.GetBytes(left);
        var b = Encoding.ASCII.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
