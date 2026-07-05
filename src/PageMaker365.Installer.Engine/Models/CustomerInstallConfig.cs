using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PageMaker365.Installer.Engine.Models;

public sealed class CustomerInstallConfig
{
    public string ContractVersion { get; set; } = "";
    public CustomerInfo Customer { get; set; } = new();
    public AzureInfo Azure { get; set; } = new();
    public SharePointInfo SharePoint { get; set; } = new();
    public AppInfo App { get; set; } = new();
    public EntraInfo Entra { get; set; } = new();
    public ControlPlaneInfo ControlPlane { get; set; } = new();
    public SecretContractInfo Secrets { get; set; } = new();
    public FeatureFlags Features { get; set; } = new();
    public SmokeTestInfo SmokeTests { get; set; } = new();

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Customer.TenantName)
        ? App.AppName
        : Customer.TenantName;
}

public sealed class CustomerInfo
{
    public string CustomerId { get; set; } = "";
    public string AccountKey { get; set; } = "";
    public string InstallationId { get; set; } = "";
    public string TenantName { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string PrimaryContact { get; set; } = "";
}

public sealed class AzureInfo
{
    public string TenantId { get; set; } = "";
    public string SubscriptionId { get; set; } = "";
    public string Location { get; set; } = "";
    public string ResourceGroupName { get; set; } = "";
    public string Environment { get; set; } = "";
    public AzureResourceNames ResourceNames { get; set; } = new();
}

public sealed class SharePointInfo
{
    public string SiteUrl { get; set; } = "";
    public string SiteId { get; set; } = "";
    public string DefaultDocumentLibrary { get; set; } = "";
    public string PermissionMode { get; set; } = "";
}

public sealed class AppInfo
{
    public string AppName { get; set; } = "";
    public string RuntimeBaseUrl { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "";
    public string CustomDomain { get; set; } = "";
    public string SupportEmail { get; set; } = "";
}

public sealed class FeatureFlags
{
    public bool KnowledgeBase { get; set; }
    public bool CustomerPortal { get; set; }
    public bool BillingIntegration { get; set; }
    public bool Connectors { get; set; }
    public bool Orchestrator { get; set; }
}

public sealed class AzureResourceNames
{
    public string KeyVaultName { get; set; } = "";
    public string StorageAccountName { get; set; } = "";
    public string LogAnalyticsName { get; set; } = "";
    public string ApplicationInsightsName { get; set; } = "";
    public string AppServicePlanName { get; set; } = "";
    public string ApiAppName { get; set; } = "";
    public string PortalAppName { get; set; } = "";
    public string ManagedIdentityName { get; set; } = "";
}

public sealed class EntraInfo
{
    public string AppRegistrationMode { get; set; } = "";
    public string PortalClientId { get; set; } = "";
    public string ApiClientId { get; set; } = "";
    public string PermissionMode { get; set; } = "";
    public List<string> RequiredApplicationPermissions { get; set; } = [];
    public List<string> RequiredDelegatedScopes { get; set; } = [];
}

public sealed class ControlPlaneInfo
{
    public string BaseUrl { get; set; } = "";
    public string DeploymentExportId { get; set; } = "";
    public string EnvironmentId { get; set; } = "";
    public string LicenseActivationId { get; set; } = "";
    public string EntitlementSyncUrl { get; set; } = "";
    public string PublicKeyId { get; set; } = "";
    public string PackageHash { get; set; } = "";
}

public sealed class SecretContractInfo
{
    public string KeyVaultName { get; set; } = "";
    public List<string> RequiredSecretNames { get; set; } = [];
    public List<SecretPromptInfo> PromptForSecrets { get; set; } = [];
}

public sealed class SecretPromptInfo
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Required { get; set; }
    public bool GeneratedByInstaller { get; set; }
}

public sealed class SmokeTestInfo
{
    public string ApiHealthPath { get; set; } = "";
    public string PortalPath { get; set; } = "";
    public string LicenseValidationPath { get; set; } = "";
    public string EntitlementSyncPath { get; set; } = "";
}
