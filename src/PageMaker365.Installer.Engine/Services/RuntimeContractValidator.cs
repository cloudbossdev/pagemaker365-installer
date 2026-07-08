using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public static class RuntimeContractValidator
{
    private static readonly HashSet<string> AllowedBootstrapOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        OnboardingOperation.TenantDiscovery,
        OnboardingOperation.InstallPackageGeneration,
        OnboardingOperation.InstallStatusSync,
        OnboardingOperation.RemovalDiscovery,
        OnboardingOperation.RemovalStatusSync
    };

    private static readonly HashSet<string> AllowedExcludedDataTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "documentContent",
        "mailboxContent",
        "userFiles",
        "rawSecrets",
        "broadUserProfileExport",
        "marketingTrackingData"
    };

    private static readonly string[] RequiredCustomerInstallSections =
    [
        "customer",
        "azure",
        "sharePoint",
        "app",
        "features"
    ];

    public static ConfigValidationResult ValidateBootstrapJson(string bootstrapJson)
    {
        var result = new ConfigValidationResult();
        if (!TryParseObject(bootstrapJson, "onboarding bootstrap session", result, out var document))
        {
            return result;
        }

        using (document)
        {
            var root = document.RootElement;
            RequireString(root, "contractVersion", result);
            RequireString(root, "sessionId", result);
            RequireString(root, "customerName", result);
            RequireAbsoluteUri(root, "portalBaseUrl", result);
            RequireAbsoluteUri(root, "apiBaseUrl", result);
            RequireString(root, "oneTimeCode", result);
            RequireDateTime(root, "expiresAt", result);
            RequireStringArray(root, "allowedOperations", result, AllowedBootstrapOperations);
            RequireObject(root, "discoveryPolicy", result);

            if (TryGetPath(root, "discoveryPolicy", out var policy) &&
                policy.ValueKind == JsonValueKind.Object)
            {
                RequireBoolean(policy, "allowAzureDiscovery", "discoveryPolicy.allowAzureDiscovery", result);
                RequireBoolean(policy, "allowGraphDiscovery", "discoveryPolicy.allowGraphDiscovery", result);
                RequireBoolean(policy, "allowSharePointDiscovery", "discoveryPolicy.allowSharePointDiscovery", result);
                RequireBoolean(policy, "allowPortalSync", "discoveryPolicy.allowPortalSync", result);
                RequireStringArray(policy, "requiredFields", result, fullPath: "discoveryPolicy.requiredFields");
                RequireStringArray(
                    policy,
                    "excludedDataTypes",
                    result,
                    AllowedExcludedDataTypes,
                    "discoveryPolicy.excludedDataTypes");
            }
        }

        return result;
    }

    public static ConfigValidationResult ValidateCustomerInstallPackageJson(string packageJson)
    {
        var result = new ConfigValidationResult();
        if (!TryParseObject(packageJson, "customer install package", result, out var document))
        {
            return result;
        }

        using (document)
        {
            var root = document.RootElement;
            foreach (var section in RequiredCustomerInstallSections)
            {
                RequireObject(root, section, result);
            }

            RequireString(root, "customer.tenantName", result);
            RequireString(root, "customer.tenantId", result);
            RequireString(root, "customer.primaryContact", result);
            RequireString(root, "azure.subscriptionId", result);
            RequireString(root, "azure.location", result);
            RequireString(root, "azure.resourceGroupName", result);
            RequireString(root, "azure.environment", result);
            RequireAbsoluteUri(root, "sharePoint.siteUrl", result);
            RequireString(root, "sharePoint.defaultDocumentLibrary", result);
            RequireString(root, "app.appName", result);
            RequireString(root, "app.supportEmail", result);
            RequireBoolean(root, "features.knowledgeBase", "features.knowledgeBase", result);
            RequireBoolean(root, "features.customerPortal", "features.customerPortal", result);
            RequireBoolean(root, "features.billingIntegration", "features.billingIntegration", result);
        }

        return result;
    }

    public static ConfigValidationResult ValidateOnboardingStatusJson(string statusJson)
    {
        var result = new ConfigValidationResult();
        if (!TryParseObject(statusJson, "onboarding status response", result, out var document))
        {
            return result;
        }

        using (document)
        {
            ValidateOnboardingStatusJson(document.RootElement, result);
        }

        return result;
    }

    public static ConfigValidationResult ValidateOnboardingStatusJson(JsonElement root)
    {
        var result = new ConfigValidationResult();
        ValidateOnboardingStatusJson(root, result);
        return result;
    }

    private static void ValidateOnboardingStatusJson(JsonElement root, ConfigValidationResult result)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            result.Errors.Add("onboarding status response must be a JSON object.");
            return;
        }

        RequireString(root, "status", result);
        RequireString(root, "sessionId", result);
        RequireString(root, "correlationId", result);
        RequireObject(root, "packageReadiness", result);
        RequireString(root, "packageReadiness.status", result);

        if (TryGetPath(root, "missingFields", out var missingFields) &&
            missingFields.ValueKind != JsonValueKind.Null &&
            missingFields.ValueKind != JsonValueKind.Array)
        {
            result.Errors.Add("missingFields must be an array when provided.");
        }

        if (TryGetPath(root, "packageReadiness.status", out var status) &&
            status.ValueKind == JsonValueKind.String &&
            status.GetString()?.Equals("Ready", StringComparison.OrdinalIgnoreCase) == true)
        {
            RequireString(root, "packageReadiness.packageDownloadUrl", result);
        }
    }

    private static bool TryParseObject(
        string json,
        string contractName,
        ConfigValidationResult result,
        out JsonDocument document)
    {
        document = null!;

        if (string.IsNullOrWhiteSpace(json))
        {
            result.Errors.Add($"{contractName} is empty.");
            return false;
        }

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            result.Errors.Add($"{contractName} is not valid JSON. {exception.Message}");
            return false;
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        result.Errors.Add($"{contractName} must be a JSON object.");
        document.Dispose();
        document = null!;
        return false;
    }

    private static void RequireObject(JsonElement root, string path, ConfigValidationResult result)
    {
        if (!TryGetPath(root, path, out var value))
        {
            result.Errors.Add($"{path} is required.");
            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            result.Errors.Add($"{path} must be an object.");
        }
    }

    private static void RequireString(JsonElement root, string path, ConfigValidationResult result)
    {
        if (!TryGetPath(root, path, out var value))
        {
            result.Errors.Add($"{path} is required.");
            return;
        }

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            result.Errors.Add($"{path} must be a non-empty string.");
        }
    }

    private static void RequireAbsoluteUri(JsonElement root, string path, ConfigValidationResult result)
    {
        RequireString(root, path, result);
        if (TryGetPath(root, path, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString()) &&
            !Uri.TryCreate(value.GetString(), UriKind.Absolute, out _))
        {
            result.Errors.Add($"{path} must be an absolute URL.");
        }
    }

    private static void RequireDateTime(JsonElement root, string path, ConfigValidationResult result)
    {
        RequireString(root, path, result);
        if (TryGetPath(root, path, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString()) &&
            !DateTimeOffset.TryParse(value.GetString(), out _))
        {
            result.Errors.Add($"{path} must be an ISO 8601 date-time value.");
        }
    }

    private static void RequireBoolean(
        JsonElement root,
        string path,
        string fullPath,
        ConfigValidationResult result)
    {
        if (!TryGetPath(root, path, out var value))
        {
            result.Errors.Add($"{fullPath} is required.");
            return;
        }

        if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            result.Errors.Add($"{fullPath} must be a boolean.");
        }
    }

    private static void RequireStringArray(
        JsonElement root,
        string path,
        ConfigValidationResult result,
        HashSet<string>? allowedValues = null,
        string? fullPath = null)
    {
        fullPath ??= path;
        if (!TryGetPath(root, path, out var value))
        {
            result.Errors.Add($"{fullPath} is required.");
            return;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            result.Errors.Add($"{fullPath} must be an array.");
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var itemPath = $"{fullPath}[{index}]";
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                result.Errors.Add($"{itemPath} must be a non-empty string.");
            }
            else if (allowedValues is not null && !allowedValues.Contains(item.GetString() ?? ""))
            {
                result.Errors.Add($"{itemPath} has unsupported value '{item.GetString()}'.");
            }

            index++;
        }
    }

    private static bool TryGetPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                value = default;
                return false;
            }

            var found = false;
            foreach (var property in value.EnumerateObject())
            {
                if (!property.Name.Equals(part, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = property.Value;
                found = true;
                break;
            }

            if (!found)
            {
                value = default;
                return false;
            }
        }

        return true;
    }
}
