namespace PageMaker365.Installer.Engine.Models;

public sealed class AzureDiscoveryResult
{
    public string ContractVersion { get; set; } = "0.1";
    public string Source { get; set; } = "AzurePowerShell";
    public string DataPolicy { get; set; } = "InstallReadinessOnly";
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;
    public string AccountId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string SelectedSubscriptionId { get; set; } = "";
    public string SelectedSubscriptionName { get; set; } = "";
    public string SelectedSubscriptionState { get; set; } = "";
    public string RecommendedLocation { get; set; } = "";
    public string TargetResourceGroupName { get; set; } = "";
    public bool ResourceGroupExists { get; set; }
    public List<AzureSubscriptionDiscovery> AccessibleSubscriptions { get; set; } = [];
    public List<DiscoveryFinding> Findings { get; set; } = [];
}
