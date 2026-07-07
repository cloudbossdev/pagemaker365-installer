namespace PageMaker365.Installer.Engine.Models;

public sealed class OnboardingSessionConnectRequest
{
    public string ContractVersion { get; set; } = "0.1";
    public string SessionId { get; set; } = "";
    public string OneTimeCode { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public string CustomerName { get; set; } = "";
}

public sealed class OnboardingDiscoverySubmitRequest
{
    public string ContractVersion { get; set; } = "0.1";
    public string SessionId { get; set; } = "";
    public string OneTimeCode { get; set; } = "";
    public TenantDiscoveryResult Discovery { get; set; } = new();
}

public sealed class OnboardingStatusRequest
{
    public string ContractVersion { get; set; } = "0.1";
    public string SessionId { get; set; } = "";
    public string OneTimeCode { get; set; } = "";
    public TenantDiscoveryResult? Discovery { get; set; }
    public OnboardingPackageContext? LoadedPackage { get; set; }
}

public sealed class OnboardingPackageContext
{
    public string TenantId { get; set; } = "";
    public string TenantName { get; set; } = "";
    public string AzureSubscriptionId { get; set; } = "";
    public string AzureLocation { get; set; } = "";
    public string ResourceGroupName { get; set; } = "";
    public string SharePointSiteUrl { get; set; } = "";
    public string SharePointTenantHostname { get; set; } = "";
    public string PrimaryContact { get; set; } = "";
    public string EnvironmentId { get; set; } = "";
    public string DeploymentExportId { get; set; } = "";
    public string PackageHashAlgorithm { get; set; } = "";
    public string PackageHash { get; set; } = "";
    public string TrustMode { get; set; } = "";
}
