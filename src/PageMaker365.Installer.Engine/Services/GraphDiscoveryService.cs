using System.Text.Json;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.PowerShell;

namespace PageMaker365.Installer.Engine.Services;

public sealed class GraphDiscoveryService
{
    private readonly PowerShellProcessRunner _powerShellRunner;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public GraphDiscoveryService(PowerShellProcessRunner? powerShellRunner = null)
    {
        _powerShellRunner = powerShellRunner ?? new PowerShellProcessRunner();
    }

    public async Task<GraphDiscoveryResult> DiscoverAsync(
        string workspaceRoot,
        CustomerInstallConfig? installConfig,
        bool allowSharePointDiscovery,
        CancellationToken cancellationToken = default)
    {
        var modulePath = Path.Combine(workspaceRoot, "modules", "PageMaker365.Install", "PageMaker365.Install.psd1");
        if (!File.Exists(modulePath))
        {
            return CreateFallbackResult(
                installConfig,
                "GraphDiscoveryModuleMissing",
                "The PageMaker365.Install module was not found.",
                modulePath);
        }

        var configPath = await WriteDiscoveryConfigAsync(workspaceRoot, installConfig, cancellationToken);
        var command = BuildModuleCommand(modulePath, configPath, allowSharePointDiscovery);
        var execution = await _powerShellRunner.RunAsync(command, workspaceRoot, cancellationToken);
        if (!execution.Succeeded)
        {
            return CreateFallbackResult(
                installConfig,
                "GraphDiscoveryCommandFailed",
                "Graph discovery command failed.",
                string.IsNullOrWhiteSpace(execution.StandardError) ? execution.StandardOutput : execution.StandardError);
        }

        return ParseDiscoveryResult(execution.StandardOutput, installConfig);
    }

    public static void ApplyToDiscovery(TenantDiscoveryResult discovery, GraphDiscoveryResult graph)
    {
        if (!string.IsNullOrWhiteSpace(graph.TenantId))
        {
            if (string.IsNullOrWhiteSpace(discovery.Customer.TenantId))
            {
                discovery.Customer.TenantId = graph.TenantId;
            }

            if (string.IsNullOrWhiteSpace(discovery.Azure.TenantId))
            {
                discovery.Azure.TenantId = graph.TenantId;
            }
        }

        discovery.Customer.DefaultDomain = Coalesce(graph.DefaultDomain, discovery.Customer.DefaultDomain);
        AddVerifiedDomain(discovery, graph.DefaultDomain);
        foreach (var domain in graph.VerifiedDomains)
        {
            AddVerifiedDomain(discovery, domain);
        }

        discovery.SharePoint.TenantHostname = Coalesce(graph.TenantHostname, discovery.SharePoint.TenantHostname);
        discovery.SharePoint.SiteUrl = Coalesce(graph.SiteUrl, discovery.SharePoint.SiteUrl);
        discovery.SharePoint.SiteId = Coalesce(graph.SiteId, discovery.SharePoint.SiteId);
        discovery.SharePoint.SiteDisplayName = Coalesce(graph.SiteDisplayName, discovery.SharePoint.SiteDisplayName);
        discovery.SharePoint.DefaultDocumentLibrary = Coalesce(graph.DefaultDocumentLibrary, discovery.SharePoint.DefaultDocumentLibrary);
        discovery.SharePoint.DefaultDocumentLibraryId = Coalesce(graph.DefaultDocumentLibraryId, discovery.SharePoint.DefaultDocumentLibraryId);
        discovery.SharePoint.PermissionMode = Coalesce(graph.PermissionMode, discovery.SharePoint.PermissionMode);
        discovery.SharePoint.SiteResolved = graph.SiteResolved;
        discovery.SharePoint.AvailableDocumentLibraries.Clear();
        foreach (var library in graph.AvailableDocumentLibraries)
        {
            if (!string.IsNullOrWhiteSpace(library.Name) || !string.IsNullOrWhiteSpace(library.DriveId))
            {
                discovery.SharePoint.AvailableDocumentLibraries.Add(library);
            }
        }

        discovery.Entra.AccountId = Coalesce(graph.AccountId, discovery.Entra.AccountId);
        discovery.Entra.Scopes = graph.Scopes.Count > 0
            ? graph.Scopes.ToList()
            : discovery.Entra.Scopes;
        discovery.Entra.AppRegistrationMode = Coalesce(graph.AppRegistrationMode, discovery.Entra.AppRegistrationMode);
        discovery.Entra.ConsentStatus = Coalesce(graph.ConsentStatus, discovery.Entra.ConsentStatus);
        discovery.Entra.PermissionMode = Coalesce(graph.EntraPermissionMode, discovery.Entra.PermissionMode);
        discovery.Entra.RequiredApplicationPermissions = graph.RequiredApplicationPermissions.Count > 0
            ? graph.RequiredApplicationPermissions.ToList()
            : discovery.Entra.RequiredApplicationPermissions;
        discovery.Entra.RequiredDelegatedScopes = graph.RequiredDelegatedScopes.Count > 0
            ? graph.RequiredDelegatedScopes.ToList()
            : discovery.Entra.RequiredDelegatedScopes;

        foreach (var finding in graph.Findings)
        {
            if (!string.IsNullOrWhiteSpace(finding.Code))
            {
                discovery.Findings.Add(finding);
            }
        }

        var source = string.IsNullOrWhiteSpace(graph.Source) ? "GraphPowerShell" : graph.Source;
        if (!discovery.Source.Contains(source, StringComparison.OrdinalIgnoreCase))
        {
            discovery.Source = $"{discovery.Source}+{source}";
        }
    }

    private static async Task<string> WriteDiscoveryConfigAsync(
        string workspaceRoot,
        CustomerInstallConfig? installConfig,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.Combine(workspaceRoot, "support-bundle", "discovery-input");
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "graph-discovery.customer.install.json");
        var json = JsonSerializer.Serialize(
            new
            {
                customer = installConfig?.Customer ?? new CustomerInfo(),
                azure = installConfig?.Azure ?? new AzureInfo(),
                sharePoint = installConfig?.SharePoint ?? new SharePointInfo(),
                entra = installConfig?.Entra ?? new EntraInfo()
            },
            JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    private static string BuildModuleCommand(string modulePath, string configPath, bool allowSharePointDiscovery)
    {
        var escapedPath = modulePath.Replace("'", "''");
        var escapedConfigPath = configPath.Replace("'", "''");
        var skipSharePoint = allowSharePointDiscovery ? "" : " -SkipSharePoint";
        var script = "$ErrorActionPreference = 'Stop'; " +
                     $"Import-Module '{escapedPath}' -Force; " +
                     $"Get-PM365GraphDiscovery -ConfigPath '{escapedConfigPath}'{skipSharePoint} | ConvertTo-Json -Depth 16";
        return $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"";
    }

    private static GraphDiscoveryResult ParseDiscoveryResult(string json, CustomerInstallConfig? installConfig)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateFallbackResult(
                installConfig,
                "GraphDiscoveryNoOutput",
                "Graph discovery command returned no output.",
                "No JSON was returned from Get-PM365GraphDiscovery.");
        }

        try
        {
            var result = JsonSerializer.Deserialize<GraphDiscoveryResult>(json.Trim(), JsonOptions);
            if (result is null)
            {
                return CreateFallbackResult(
                    installConfig,
                    "GraphDiscoveryEmptyOutput",
                    "Graph discovery command returned an empty result.",
                    "The command output could not be deserialized.");
            }

            return Normalize(result, installConfig);
        }
        catch (JsonException exception)
        {
            return CreateFallbackResult(
                installConfig,
                "GraphDiscoveryInvalidJson",
                "Graph discovery command returned invalid JSON.",
                exception.Message);
        }
    }

    private static GraphDiscoveryResult Normalize(GraphDiscoveryResult result, CustomerInstallConfig? installConfig)
    {
        result.DataPolicy = string.IsNullOrWhiteSpace(result.DataPolicy)
            ? "InstallReadinessOnly"
            : result.DataPolicy;
        result.TenantId = Coalesce(result.TenantId, installConfig?.Customer.TenantId, installConfig?.Azure.TenantId);
        result.SiteUrl = Coalesce(result.SiteUrl, installConfig?.SharePoint.SiteUrl);
        result.TenantHostname = Coalesce(result.TenantHostname, GetSharePointHostname(result.SiteUrl));
        result.SiteId = Coalesce(result.SiteId, installConfig?.SharePoint.SiteId);
        result.DefaultDocumentLibrary = Coalesce(result.DefaultDocumentLibrary, installConfig?.SharePoint.DefaultDocumentLibrary);
        result.PermissionMode = Coalesce(result.PermissionMode, installConfig?.SharePoint.PermissionMode);
        result.AppRegistrationMode = Coalesce(result.AppRegistrationMode, installConfig?.Entra.AppRegistrationMode);
        result.ConsentStatus = string.IsNullOrWhiteSpace(result.ConsentStatus) ? "Unknown" : result.ConsentStatus;
        result.EntraPermissionMode = Coalesce(result.EntraPermissionMode, installConfig?.Entra.PermissionMode);

        if (result.RequiredApplicationPermissions.Count == 0 && installConfig?.Entra.RequiredApplicationPermissions.Count > 0)
        {
            result.RequiredApplicationPermissions = installConfig.Entra.RequiredApplicationPermissions.ToList();
        }

        if (result.RequiredDelegatedScopes.Count == 0 && installConfig?.Entra.RequiredDelegatedScopes.Count > 0)
        {
            result.RequiredDelegatedScopes = installConfig.Entra.RequiredDelegatedScopes.ToList();
        }

        if (!string.IsNullOrWhiteSpace(result.TenantHostname) &&
            !result.VerifiedDomains.Contains(result.TenantHostname, StringComparer.OrdinalIgnoreCase))
        {
            result.VerifiedDomains.Add(result.TenantHostname);
        }

        result.VerifiedDomains = result.VerifiedDomains
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return result;
    }

    private static GraphDiscoveryResult CreateFallbackResult(
        CustomerInstallConfig? installConfig,
        string code,
        string summary,
        string details)
    {
        var result = Normalize(
            new GraphDiscoveryResult
            {
                Source = "GraphDiscoveryFallback",
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

    private static void AddVerifiedDomain(TenantDiscoveryResult discovery, string domain)
    {
        if (string.IsNullOrWhiteSpace(domain) ||
            discovery.Customer.VerifiedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        discovery.Customer.VerifiedDomains.Add(domain);
    }

    private static string Coalesce(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static string GetSharePointHostname(string? siteUrl)
    {
        if (string.IsNullOrWhiteSpace(siteUrl) ||
            !Uri.TryCreate(siteUrl, UriKind.Absolute, out var uri))
        {
            return "";
        }

        return uri.Host;
    }
}
