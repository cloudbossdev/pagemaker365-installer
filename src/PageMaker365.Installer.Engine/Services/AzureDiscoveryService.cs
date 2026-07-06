using System.Text.Json;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.PowerShell;

namespace PageMaker365.Installer.Engine.Services;

public sealed class AzureDiscoveryService
{
    private readonly PowerShellProcessRunner _powerShellRunner;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public AzureDiscoveryService(PowerShellProcessRunner? powerShellRunner = null)
    {
        _powerShellRunner = powerShellRunner ?? new PowerShellProcessRunner();
    }

    public async Task<AzureDiscoveryResult> DiscoverAsync(
        string workspaceRoot,
        CustomerInstallConfig? installConfig,
        CancellationToken cancellationToken = default)
    {
        var modulePath = Path.Combine(workspaceRoot, "modules", "PageMaker365.Install", "PageMaker365.Install.psd1");
        if (!File.Exists(modulePath))
        {
            return CreateFallbackResult(
                installConfig,
                "AzureDiscoveryModuleMissing",
                "The PageMaker365.Install module was not found.",
                modulePath);
        }

        var configPath = await WriteDiscoveryConfigAsync(workspaceRoot, installConfig, cancellationToken);
        var command = BuildModuleCommand(modulePath, configPath);
        var execution = await _powerShellRunner.RunAsync(command, workspaceRoot, cancellationToken);
        if (!execution.Succeeded)
        {
            return CreateFallbackResult(
                installConfig,
                "AzureDiscoveryCommandFailed",
                "Azure discovery command failed.",
                string.IsNullOrWhiteSpace(execution.StandardError) ? execution.StandardOutput : execution.StandardError);
        }

        return ParseDiscoveryResult(execution.StandardOutput, installConfig);
    }

    public static void ApplyToDiscovery(TenantDiscoveryResult discovery, AzureDiscoveryResult azure)
    {
        if (!string.IsNullOrWhiteSpace(azure.TenantId))
        {
            discovery.Azure.TenantId = azure.TenantId;
            if (string.IsNullOrWhiteSpace(discovery.Customer.TenantId))
            {
                discovery.Customer.TenantId = azure.TenantId;
            }
        }

        discovery.Azure.SelectedSubscriptionId = Coalesce(azure.SelectedSubscriptionId, discovery.Azure.SelectedSubscriptionId);
        discovery.Azure.SelectedSubscriptionName = Coalesce(azure.SelectedSubscriptionName, discovery.Azure.SelectedSubscriptionName);
        discovery.Azure.RecommendedLocation = Coalesce(azure.RecommendedLocation, discovery.Azure.RecommendedLocation);
        discovery.Azure.TargetResourceGroupName = Coalesce(azure.TargetResourceGroupName, discovery.Azure.TargetResourceGroupName);

        discovery.Azure.AccessibleSubscriptions.Clear();
        foreach (var subscription in azure.AccessibleSubscriptions)
        {
            if (!string.IsNullOrWhiteSpace(subscription.SubscriptionId))
            {
                discovery.Azure.AccessibleSubscriptions.Add(subscription);
            }
        }

        foreach (var finding in azure.Findings)
        {
            if (!string.IsNullOrWhiteSpace(finding.Code))
            {
                discovery.Findings.Add(finding);
            }
        }

        discovery.Source = discovery.Source.Contains("AzurePowerShell", StringComparison.OrdinalIgnoreCase)
            ? discovery.Source
            : $"{discovery.Source}+AzurePowerShell";
    }

    private static async Task<string> WriteDiscoveryConfigAsync(
        string workspaceRoot,
        CustomerInstallConfig? installConfig,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.Combine(workspaceRoot, "support-bundle", "discovery-input");
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "azure-discovery.customer.install.json");
        var json = JsonSerializer.Serialize(
            new
            {
                customer = installConfig?.Customer ?? new CustomerInfo(),
                azure = installConfig?.Azure ?? new AzureInfo()
            },
            JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    private static string BuildModuleCommand(string modulePath, string configPath)
    {
        var escapedPath = modulePath.Replace("'", "''");
        var escapedConfigPath = configPath.Replace("'", "''");
        var script = "$ErrorActionPreference = 'Stop'; " +
                     $"Import-Module '{escapedPath}' -Force; " +
                     $"Get-PM365AzureDiscovery -ConfigPath '{escapedConfigPath}' | ConvertTo-Json -Depth 12";
        return $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"";
    }

    private static AzureDiscoveryResult ParseDiscoveryResult(string json, CustomerInstallConfig? installConfig)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateFallbackResult(
                installConfig,
                "AzureDiscoveryNoOutput",
                "Azure discovery command returned no output.",
                "No JSON was returned from Get-PM365AzureDiscovery.");
        }

        try
        {
            var result = JsonSerializer.Deserialize<AzureDiscoveryResult>(json.Trim(), JsonOptions);
            if (result is null)
            {
                return CreateFallbackResult(
                    installConfig,
                    "AzureDiscoveryEmptyOutput",
                    "Azure discovery command returned an empty result.",
                    "The command output could not be deserialized.");
            }

            return Normalize(result, installConfig);
        }
        catch (JsonException exception)
        {
            return CreateFallbackResult(
                installConfig,
                "AzureDiscoveryInvalidJson",
                "Azure discovery command returned invalid JSON.",
                exception.Message);
        }
    }

    private static AzureDiscoveryResult Normalize(AzureDiscoveryResult result, CustomerInstallConfig? installConfig)
    {
        result.DataPolicy = string.IsNullOrWhiteSpace(result.DataPolicy)
            ? "InstallReadinessOnly"
            : result.DataPolicy;
        result.TenantId = Coalesce(result.TenantId, installConfig?.Azure.TenantId, installConfig?.Customer.TenantId);
        result.SelectedSubscriptionId = Coalesce(result.SelectedSubscriptionId, installConfig?.Azure.SubscriptionId);
        result.SelectedSubscriptionName = Coalesce(
            result.SelectedSubscriptionName,
            string.IsNullOrWhiteSpace(result.SelectedSubscriptionId)
                ? ""
                : $"Subscription {result.SelectedSubscriptionId[..Math.Min(8, result.SelectedSubscriptionId.Length)]}");
        result.RecommendedLocation = Coalesce(result.RecommendedLocation, installConfig?.Azure.Location);
        result.TargetResourceGroupName = Coalesce(result.TargetResourceGroupName, installConfig?.Azure.ResourceGroupName);

        if (result.AccessibleSubscriptions.Count == 0 && !string.IsNullOrWhiteSpace(result.SelectedSubscriptionId))
        {
            result.AccessibleSubscriptions.Add(new AzureSubscriptionDiscovery
            {
                SubscriptionId = result.SelectedSubscriptionId,
                DisplayName = result.SelectedSubscriptionName,
                State = string.IsNullOrWhiteSpace(result.SelectedSubscriptionState) ? "Unknown" : result.SelectedSubscriptionState
            });
        }

        return result;
    }

    private static AzureDiscoveryResult CreateFallbackResult(
        CustomerInstallConfig? installConfig,
        string code,
        string summary,
        string details)
    {
        var result = Normalize(
            new AzureDiscoveryResult
            {
                Source = "AzureDiscoveryFallback",
                Findings =
                {
                    new DiscoveryFinding
                    {
                        Severity = "Warning",
                        Code = code,
                        Summary = summary,
                        Details = details
                    }
                }
            },
            installConfig);
        return result;
    }

    private static string Coalesce(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }
}
