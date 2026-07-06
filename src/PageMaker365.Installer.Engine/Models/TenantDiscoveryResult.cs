namespace PageMaker365.Installer.Engine.Models;

public sealed class TenantDiscoveryResult
{
    public string ContractVersion { get; set; } = "0.1";
    public string DiscoveryId { get; set; } = "";
    public string OnboardingSessionId { get; set; } = "";
    public string Source { get; set; } = "";
    public string DataPolicy { get; set; } = "InstallReadinessOnly";
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;
    public TenantDiscoveryCustomer Customer { get; set; } = new();
    public TenantDiscoveryAzure Azure { get; set; } = new();
    public TenantDiscoverySharePoint SharePoint { get; set; } = new();
    public TenantDiscoveryEntra Entra { get; set; } = new();
    public List<DiscoveryFinding> Findings { get; set; } = [];
}

public sealed class TenantDiscoveryCustomer
{
    public string TenantName { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string PrimaryContact { get; set; } = "";
    public List<string> VerifiedDomains { get; set; } = [];
}

public sealed class TenantDiscoveryAzure
{
    public string TenantId { get; set; } = "";
    public string SelectedSubscriptionId { get; set; } = "";
    public string SelectedSubscriptionName { get; set; } = "";
    public string RecommendedLocation { get; set; } = "";
    public string TargetResourceGroupName { get; set; } = "";
    public List<AzureSubscriptionDiscovery> AccessibleSubscriptions { get; set; } = [];
}

public sealed class AzureSubscriptionDiscovery
{
    public string SubscriptionId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string State { get; set; } = "";
}

public sealed class TenantDiscoverySharePoint
{
    public string TenantHostname { get; set; } = "";
    public string SiteUrl { get; set; } = "";
    public string SiteId { get; set; } = "";
    public string SiteDisplayName { get; set; } = "";
    public string DefaultDocumentLibrary { get; set; } = "";
    public string DefaultDocumentLibraryId { get; set; } = "";
    public string PermissionMode { get; set; } = "";
    public bool SiteResolved { get; set; }
    public List<SharePointDocumentLibraryDiscovery> AvailableDocumentLibraries { get; set; } = [];
}

public sealed class TenantDiscoveryEntra
{
    public string AppRegistrationMode { get; set; } = "";
    public string ConsentStatus { get; set; } = "Unknown";
    public string PermissionMode { get; set; } = "";
    public List<string> RequiredApplicationPermissions { get; set; } = [];
    public List<string> RequiredDelegatedScopes { get; set; } = [];
}

public sealed class DiscoveryFinding
{
    public string Severity { get; set; } = "";
    public string Code { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Details { get; set; } = "";
}
