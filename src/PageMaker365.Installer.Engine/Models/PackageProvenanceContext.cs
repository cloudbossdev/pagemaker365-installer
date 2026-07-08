namespace PageMaker365.Installer.Engine.Models;

public sealed class PackageProvenanceContext
{
    public string Source { get; set; } = "";
    public string ExpectedOnboardingSessionId { get; set; } = "";
    public string ExpectedTenantId { get; set; } = "";
    public string ExpectedDiscoveryId { get; set; } = "";
    public string ExpectedDiscoveryTenantId { get; set; } = "";
    public string ExpectedSharePointSiteUrl { get; set; } = "";
    public bool RequireOnboardingSessionId { get; set; }
    public bool RequireDeploymentExportId { get; set; }
    public bool RequireDiscoveryId { get; set; }

    public static PackageProvenanceContext ForPortalDownload(
        OnboardingBootstrapSession session,
        TenantDiscoveryResult? discovery = null)
    {
        return new PackageProvenanceContext
        {
            Source = "PortalDownload",
            ExpectedOnboardingSessionId = session.SessionId,
            ExpectedTenantId = session.ExpectedTenantId,
            ExpectedDiscoveryId = discovery?.DiscoveryId ?? "",
            ExpectedDiscoveryTenantId = First(discovery?.Customer.TenantId, discovery?.Azure.TenantId),
            ExpectedSharePointSiteUrl = discovery?.SharePoint.SiteUrl ?? "",
            RequireOnboardingSessionId = true,
            RequireDeploymentExportId = true,
            RequireDiscoveryId = !string.IsNullOrWhiteSpace(discovery?.DiscoveryId)
        };
    }

    private static string First(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }
}
