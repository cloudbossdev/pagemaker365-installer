using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class TenantDiscoveryService
{
    private readonly RedactionService _redactionService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public TenantDiscoveryService(RedactionService redactionService)
    {
        _redactionService = redactionService;
    }

    public TenantDiscoveryResult CreateDiscovery(
        OnboardingBootstrapSession session,
        CustomerInstallConfig? installConfig = null)
    {
        var discovery = new TenantDiscoveryResult
        {
            DiscoveryId = $"disc_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..38],
            OnboardingSessionId = session.SessionId,
            Source = installConfig is null ? "MockDiscoveryFromBootstrap" : "MockDiscoveryFromInstallPackage",
            Customer =
            {
                TenantName = Coalesce(installConfig?.Customer.TenantName, session.CustomerName),
                TenantId = Coalesce(installConfig?.Customer.TenantId, session.ExpectedTenantId),
                PrimaryContact = Coalesce(installConfig?.Customer.PrimaryContact, session.RequestedBy)
            },
            Azure =
            {
                TenantId = Coalesce(installConfig?.Azure.TenantId, installConfig?.Customer.TenantId, session.ExpectedTenantId),
                SelectedSubscriptionId = installConfig?.Azure.SubscriptionId ?? "",
                SelectedSubscriptionName = string.IsNullOrWhiteSpace(installConfig?.Azure.SubscriptionId)
                    ? ""
                    : $"Subscription {installConfig.Azure.SubscriptionId[..Math.Min(8, installConfig.Azure.SubscriptionId.Length)]}",
                RecommendedLocation = installConfig?.Azure.Location ?? "",
                TargetResourceGroupName = installConfig?.Azure.ResourceGroupName ?? ""
            },
            SharePoint =
            {
                TenantHostname = GetSharePointHostname(installConfig?.SharePoint.SiteUrl),
                SiteUrl = installConfig?.SharePoint.SiteUrl ?? "",
                SiteId = installConfig?.SharePoint.SiteId ?? "",
                DefaultDocumentLibrary = installConfig?.SharePoint.DefaultDocumentLibrary ?? "",
                PermissionMode = installConfig?.SharePoint.PermissionMode ?? "",
                SiteResolved = !string.IsNullOrWhiteSpace(installConfig?.SharePoint.SiteUrl)
            },
            Entra =
            {
                AppRegistrationMode = installConfig?.Entra.AppRegistrationMode ?? "",
                ConsentStatus = "Unknown",
                PermissionMode = installConfig?.Entra.PermissionMode ?? "",
                RequiredApplicationPermissions = installConfig?.Entra.RequiredApplicationPermissions.ToList() ?? [],
                RequiredDelegatedScopes = installConfig?.Entra.RequiredDelegatedScopes.ToList() ?? []
            }
        };

        if (!string.IsNullOrWhiteSpace(discovery.Azure.SelectedSubscriptionId))
        {
            discovery.Azure.AccessibleSubscriptions.Add(new AzureSubscriptionDiscovery
            {
                SubscriptionId = discovery.Azure.SelectedSubscriptionId,
                DisplayName = discovery.Azure.SelectedSubscriptionName,
                State = "Unknown"
            });
        }

        if (!string.IsNullOrWhiteSpace(discovery.SharePoint.TenantHostname))
        {
            discovery.Customer.VerifiedDomains.Add(discovery.SharePoint.TenantHostname);
        }

        discovery.Findings.Add(new DiscoveryFinding
        {
            Severity = "Info",
            Code = "DiscoveryMockOnly",
            Summary = "Discovery is currently mocked from bootstrap and install package values.",
            Details = "The next implementation step is to replace these values with read-only Azure, Graph, and SharePoint queries."
        });

        discovery.Findings.Add(new DiscoveryFinding
        {
            Severity = "Info",
            Code = "PortalReadyPayload",
            Summary = "The discovery payload is shaped for PageMaker365 portal onboarding.",
            Details = "The production API can use this payload to pre-fill tenant onboarding fields and generate the final install package after review."
        });

        return discovery;
    }

    public async Task<string> SaveRedactedAsync(
        TenantDiscoveryResult discovery,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputRoot);
        var redacted = _redactionService.RedactDiscovery(discovery);
        var safeId = string.IsNullOrWhiteSpace(redacted.OnboardingSessionId)
            ? "unknown-session"
            : redacted.OnboardingSessionId.Replace(Path.DirectorySeparatorChar, '-').Replace(Path.AltDirectorySeparatorChar, '-');
        var path = Path.Combine(outputRoot, $"tenant-discovery-{safeId}.json");
        var json = JsonSerializer.Serialize(redacted, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    public static string ToJson(TenantDiscoveryResult discovery) => JsonSerializer.Serialize(discovery, JsonOptions);

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
