using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class CustomerConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<CustomerInstallConfig> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<CustomerInstallConfig>(stream, JsonOptions, cancellationToken);
        return config ?? throw new InvalidOperationException("The customer install package could not be read.");
    }

    public ConfigValidationResult Validate(CustomerInstallConfig config)
    {
        var result = new ConfigValidationResult();

        Require(config.Customer.TenantName, "Customer tenant name is required.", result);
        Require(config.Customer.TenantId, "Customer tenant ID is required.", result);
        Require(config.Azure.SubscriptionId, "Azure subscription ID is required.", result);
        Require(config.Azure.Location, "Azure location is required.", result);
        Require(config.Azure.ResourceGroupName, "Azure resource group name is required.", result);
        Require(config.SharePoint.SiteUrl, "SharePoint site URL is required.", result);
        Require(config.App.AppName, "Application name is required.", result);

        if (string.IsNullOrWhiteSpace(config.ContractVersion))
        {
            result.Warnings.Add("Deployment contract version is not set.");
        }

        if (!string.IsNullOrWhiteSpace(config.SharePoint.SiteUrl) &&
            !Uri.TryCreate(config.SharePoint.SiteUrl, UriKind.Absolute, out _))
        {
            result.Errors.Add("SharePoint site URL must be an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(config.App.SupportEmail))
        {
            result.Warnings.Add("Support email is not set.");
        }

        return result;
    }

    public static string ToJson(CustomerInstallConfig config) => JsonSerializer.Serialize(config, JsonOptions);

    private static void Require(string value, string message, ConfigValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result.Errors.Add(message);
        }
    }
}
