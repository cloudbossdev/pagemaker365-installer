namespace PageMaker365.Installer.Engine.Models;

public sealed class GraphDiscoveryResult
{
    public string ContractVersion { get; set; } = "0.1";
    public string Source { get; set; } = "GraphPowerShell";
    public string DataPolicy { get; set; } = "InstallReadinessOnly";
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;
    public string AccountId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public List<string> Scopes { get; set; } = [];
    public List<string> VerifiedDomains { get; set; } = [];
    public string DefaultDomain { get; set; } = "";
    public string TenantHostname { get; set; } = "";
    public string SiteUrl { get; set; } = "";
    public string SiteId { get; set; } = "";
    public string SiteDisplayName { get; set; } = "";
    public bool SiteResolved { get; set; }
    public string DefaultDocumentLibrary { get; set; } = "";
    public string DefaultDocumentLibraryId { get; set; } = "";
    public string PermissionMode { get; set; } = "";
    public List<SharePointDocumentLibraryDiscovery> AvailableDocumentLibraries { get; set; } = [];
    public string AppRegistrationMode { get; set; } = "";
    public string ConsentStatus { get; set; } = "Unknown";
    public string EntraPermissionMode { get; set; } = "";
    public List<string> RequiredApplicationPermissions { get; set; } = [];
    public List<string> RequiredDelegatedScopes { get; set; } = [];
    public List<DiscoveryFinding> Findings { get; set; } = [];
}

public sealed class SharePointDocumentLibraryDiscovery
{
    public string DriveId { get; set; } = "";
    public string Name { get; set; } = "";
    public string WebUrl { get; set; } = "";
    public string DriveType { get; set; } = "";
}
