using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class CustomerConfigService
{
    private static readonly HashSet<string> BlockedSecretProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "values",
        "connectionStrings",
        "passwords",
        "tokens",
        "clientSecrets",
        "apiKeys"
    };

    private static readonly HashSet<string> HashExcludedControlPlaneProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "packageHash",
        "signature"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<CustomerInstallConfig> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var packageJson = await File.ReadAllTextAsync(path, cancellationToken);
        var contractValidation = RuntimeContractValidator.ValidateCustomerInstallPackageJson(packageJson);
        if (!contractValidation.IsValid)
        {
            throw new InvalidDataException("The customer install package failed contract validation: " +
                string.Join(" ", contractValidation.Errors));
        }

        var config = JsonSerializer.Deserialize<CustomerInstallConfig>(packageJson, JsonOptions);
        return config ?? throw new InvalidOperationException("The customer install package could not be read.");
    }

    public ConfigValidationResult Validate(
        CustomerInstallConfig config,
        string packageJson = "",
        PackageProvenanceContext? provenanceContext = null)
    {
        var result = new ConfigValidationResult();

        Require(config.Customer.TenantName, "Customer tenant name is required.", result);
        Require(config.Customer.TenantId, "Customer tenant ID is required.", result);
        Require(config.Customer.PrimaryContact, "Customer primary contact is required.", result);
        Require(config.Azure.SubscriptionId, "Azure subscription ID is required.", result);
        Require(config.Azure.Location, "Azure location is required.", result);
        Require(config.Azure.ResourceGroupName, "Azure resource group name is required.", result);
        Require(config.Azure.Environment, "Azure environment is required.", result);
        Require(config.SharePoint.SiteUrl, "SharePoint site URL is required.", result);
        Require(config.SharePoint.DefaultDocumentLibrary, "SharePoint default document library is required.", result);
        Require(config.App.AppName, "Application name is required.", result);
        Require(config.App.SupportEmail, "Support email is required.", result);

        if (string.IsNullOrWhiteSpace(config.ContractVersion))
        {
            result.Warnings.Add("Deployment contract version is not set.");
        }

        if (!string.IsNullOrWhiteSpace(config.SharePoint.SiteUrl) &&
            !Uri.TryCreate(config.SharePoint.SiteUrl, UriKind.Absolute, out _))
        {
            result.Errors.Add("SharePoint site URL must be an absolute URL.");
        }

        if (!string.IsNullOrWhiteSpace(packageJson))
        {
            var contractValidation = RuntimeContractValidator.ValidateCustomerInstallPackageJson(packageJson);
            result.Errors.AddRange(contractValidation.Errors);
            result.Warnings.AddRange(contractValidation.Warnings);
            ValidateSecretContainers(packageJson, result);
            if (!contractValidation.IsValid)
            {
                return result;
            }
        }

        ValidateRequiredPackageProperties(packageJson, result);
        ValidatePackageTrust(config, packageJson, result);

        if (provenanceContext is not null)
        {
            ValidatePackageProvenance(config, provenanceContext, result);
        }

        return result;
    }

    public static string ToJson(CustomerInstallConfig config) => JsonSerializer.Serialize(config, JsonOptions);

    public static string ComputePackageHash(string packageJson)
    {
        using var document = JsonDocument.Parse(packageJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonicalJson(writer, document.RootElement, path: "");
        }

        var hash = SHA256.HashData(stream.ToArray());
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void Require(string value, string message, ConfigValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result.Errors.Add(message);
        }
    }

    private static void ValidateSecretContainers(string packageJson, ConfigValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(packageJson))
        {
            return;
        }

        using var document = JsonDocument.Parse(packageJson);
        if (!TryGetProperty(document.RootElement, "secrets", out var secrets) ||
            secrets.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var blocked = secrets.EnumerateObject()
            .Where(property => BlockedSecretProperties.Contains(property.Name))
            .Select(property => $"secrets.{property.Name}")
            .ToArray();
        if (blocked.Length > 0)
        {
            result.Errors.Add("Customer install package must not contain raw secret containers: " + string.Join(", ", blocked) + ".");
        }
    }

    private static void ValidateRequiredPackageProperties(string packageJson, ConfigValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(packageJson))
        {
            return;
        }

        using var document = JsonDocument.Parse(packageJson);
        var root = document.RootElement;
        RequireJsonPath(root, "features", result);
        RequireJsonPath(root, "features.knowledgeBase", result);
        RequireJsonPath(root, "features.customerPortal", result);
        RequireJsonPath(root, "features.billingIntegration", result);
    }

    private static void RequireJsonPath(JsonElement root, string path, ConfigValidationResult result)
    {
        if (!TryGetJsonPath(root, path, out var value) ||
            value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            result.Errors.Add($"{path} is required.");
        }
    }

    private static void ValidatePackageTrust(
        CustomerInstallConfig config,
        string packageJson,
        ConfigValidationResult result)
    {
        var controlPlane = config.ControlPlane;
        result.DeploymentExportId = controlPlane.DeploymentExportId;
        result.DeclaredPackageHash = controlPlane.PackageHash;
        result.SigningKeyId = First(controlPlane.PublicKeyId, controlPlane.JwksUrl);
        result.TrustMode = string.IsNullOrWhiteSpace(controlPlane.TrustMode) ? "UnsignedAllowed" : controlPlane.TrustMode;

        var signedRequired = result.TrustMode.Equals("SignedRequired", StringComparison.OrdinalIgnoreCase);
        RequireOrWarn(controlPlane.DeploymentExportId, "controlPlane.deploymentExportId", signedRequired, result);
        RequireOrWarn(controlPlane.ExportedAt, "controlPlane.exportedAt", signedRequired, result);
        RequireOrWarn(controlPlane.Issuer, "controlPlane.issuer", signedRequired, result);
        RequireOrWarn(controlPlane.SchemaId, "controlPlane.schemaId", signedRequired, result);
        RequireOrWarn(controlPlane.PublicKeyId, "controlPlane.publicKeyId", signedRequired, result);
        RequireOrWarn(controlPlane.PackageHash, "controlPlane.packageHash", signedRequired, result);
        RequireOrWarn(controlPlane.Signature, "controlPlane.signature", signedRequired, result);
        RequireOrWarn(controlPlane.SignatureAlgorithm, "controlPlane.signatureAlgorithm", signedRequired, result);

        if (string.IsNullOrWhiteSpace(controlPlane.PackageHash))
        {
            result.PackageTrustStatus = signedRequired ? "Missing signature" : "Legacy package";
            result.PackageTrustSummary = signedRequired
                ? "Signed package mode is required, but the package hash is missing."
                : "Package export metadata is incomplete. The installer can continue for alpha compatibility, but production exports should include a hash and signature metadata.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(controlPlane.PackageHashAlgorithm) &&
            !controlPlane.PackageHashAlgorithm.Equals("SHA-256", StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add($"Unsupported package hash algorithm '{controlPlane.PackageHashAlgorithm}'. Only SHA-256 is currently supported.");
        }

        var jsonForHash = string.IsNullOrWhiteSpace(packageJson) ? ToJson(config) : packageJson;
        result.ComputedPackageHash = ComputePackageHash(jsonForHash);
        var normalizedDeclaredHash = NormalizePackageHash(controlPlane.PackageHash);
        if (!normalizedDeclaredHash.Equals(result.ComputedPackageHash, StringComparison.OrdinalIgnoreCase))
        {
            result.PackageTrustStatus = "Hash mismatch";
            result.PackageTrustSummary = "The customer package hash does not match the package contents. Download or export a fresh package from the PageMaker365 portal.";
            result.Errors.Add($"Customer package hash mismatch. Declared {controlPlane.PackageHash}; computed {result.ComputedPackageHash}.");
            return;
        }

        if (string.IsNullOrWhiteSpace(controlPlane.Signature))
        {
            result.PackageTrustStatus = "Hash verified";
            result.PackageTrustSummary = "Package hash matches the package contents. Signature metadata is not present yet, so this is not a fully signed production package.";
            return;
        }

        result.PackageTrustStatus = "Verified";
        result.PackageTrustSummary = "Package hash matches the package contents and signature metadata is present. Cryptographic signature verification will be wired in a later signing slice.";
    }

    private static void ValidatePackageProvenance(
        CustomerInstallConfig config,
        PackageProvenanceContext context,
        ConfigValidationResult result)
    {
        var originalErrorCount = result.Errors.Count;
        var controlPlane = config.ControlPlane;

        if (context.RequireDeploymentExportId &&
            string.IsNullOrWhiteSpace(controlPlane.DeploymentExportId))
        {
            result.Errors.Add("controlPlane.deploymentExportId is required for generated packages.");
        }

        ValidateExpectedValue(
            "onboarding session",
            "controlPlane.onboardingSessionId",
            controlPlane.OnboardingSessionId,
            context.ExpectedOnboardingSessionId,
            context.RequireOnboardingSessionId,
            result);

        var expectedTenantId = FirstUsableTenantId(context.ExpectedTenantId, context.ExpectedDiscoveryTenantId);
        if (!string.IsNullOrWhiteSpace(expectedTenantId))
        {
            ValidateExpectedValue(
                "tenant",
                "customer.tenantId",
                config.Customer.TenantId,
                expectedTenantId,
                required: true,
                result);

            if (!string.IsNullOrWhiteSpace(config.Azure.TenantId))
            {
                ValidateExpectedValue(
                    "tenant",
                    "azure.tenantId",
                    config.Azure.TenantId,
                    expectedTenantId,
                    required: false,
                    result);
            }
        }

        ValidateExpectedValue(
            "discovery",
            "controlPlane.discoveryId",
            controlPlane.DiscoveryId,
            context.ExpectedDiscoveryId,
            context.RequireDiscoveryId,
            result);

        if (!string.IsNullOrWhiteSpace(context.ExpectedSharePointSiteUrl) &&
            !string.IsNullOrWhiteSpace(config.SharePoint.SiteUrl))
        {
            var expectedSite = NormalizeUrlForComparison(context.ExpectedSharePointSiteUrl);
            var packageSite = NormalizeUrlForComparison(config.SharePoint.SiteUrl);
            if (!expectedSite.Equals(packageSite, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add(
                    $"Generated package SharePoint site does not match active discovery. Expected '{context.ExpectedSharePointSiteUrl}', package declares '{config.SharePoint.SiteUrl}'.");
            }
        }

        if (result.Errors.Count > originalErrorCount &&
            result.PackageTrustStatus is "Verified" or "Hash verified" or "Legacy package" or "Missing signature" or "Not checked")
        {
            result.PackageTrustStatus = "Provenance mismatch";
            result.PackageTrustSummary = "The generated package metadata does not match the active onboarding session, tenant, or discovery context.";
        }
    }

    private static void ValidateExpectedValue(
        string label,
        string field,
        string actual,
        string expected,
        bool required,
        ConfigValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            if (required && string.IsNullOrWhiteSpace(actual))
            {
                result.Errors.Add($"{field} is required for generated packages.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(actual))
        {
            result.Errors.Add($"{field} is required for generated packages.");
            return;
        }

        if (!actual.Trim().Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add(
                $"Generated package {label} does not match active context. Expected '{expected}', package declares '{actual}'.");
        }
    }

    private static void RequireOrWarn(
        string value,
        string field,
        bool required,
        ConfigValidationResult result)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (required)
        {
            result.Errors.Add($"{field} is required when controlPlane.trustMode is SignedRequired.");
            return;
        }

        result.Warnings.Add($"{field} is not set.");
    }

    private static string NormalizePackageHash(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            return "sha256:" + trimmed["sha256:".Length..].ToLowerInvariant();
        }

        return trimmed.Length == 64 && trimmed.All(IsHex)
            ? "sha256:" + trimmed.ToLowerInvariant()
            : trimmed;
    }

    private static bool IsHex(char value)
    {
        return value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    private static string First(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static string FirstUsableTenantId(params string[] values)
    {
        return values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value) &&
            !IsEmptyGuid(value)) ?? "";
    }

    private static bool IsEmptyGuid(string value)
    {
        return Guid.TryParse(value, out var guid) && guid == Guid.Empty;
    }

    private static string NormalizeUrlForComparison(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return value.Trim().TrimEnd('/');
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}{path}";
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetJsonPath(JsonElement element, string path, out JsonElement value)
    {
        value = element;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(value, segment, out value))
            {
                value = default;
                return false;
            }
        }

        return true;
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    if (ShouldExcludeFromPackageHash(path, property.Name))
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    var childPath = string.IsNullOrWhiteSpace(path) ? property.Name : $"{path}.{property.Name}";
                    WriteCanonicalJson(writer, property.Value, childPath);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(writer, item, path);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
        }
    }

    private static bool ShouldExcludeFromPackageHash(string path, string propertyName)
    {
        return path.Equals("controlPlane", StringComparison.OrdinalIgnoreCase) &&
            HashExcludedControlPlaneProperties.Contains(propertyName);
    }
}
