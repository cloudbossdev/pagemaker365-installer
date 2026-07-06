namespace PageMaker365.Installer.Engine.Models;

public sealed class PersistedInstallerState
{
    public string StateVersion { get; set; } = "0.1";
    public string StateId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public bool IsCompleted { get; set; }
    public string WorkflowMode { get; set; } = "Setup";
    public int CurrentStepNumber { get; set; } = 1;
    public int MaxAccessibleStepNumber { get; set; } = 2;
    public List<PersistedInstallerStepState> Steps { get; set; } = [];
    public string PackagePath { get; set; } = "";
    public CustomerInstallConfig? Config { get; set; }
    public InstallerSession? InstallerSession { get; set; }
    public string BootstrapSourcePath { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string AzureSubscription { get; set; } = "";
    public string SharePointSite { get; set; } = "";
    public string OnboardingSessionId { get; set; } = "";
    public string OnboardingStatus { get; set; } = "";
    public string OnboardingApiBaseUrl { get; set; } = "";
    public TenantDiscoveryResult? TenantDiscovery { get; set; }
    public OnboardingPortalStatus? OnboardingPortalStatus { get; set; }
    public OnboardingPackageReadiness? PackageReadiness { get; set; }
    public string DiscoverySummary { get; set; } = "";
    public string DiscoveryOutputPath { get; set; } = "";
    public string PortalSyncStatus { get; set; } = "";
    public string PortalMissingFieldsSummary { get; set; } = "";
    public string PackageReadinessStatus { get; set; } = "";
    public string PackageReadinessVersion { get; set; } = "";
    public string PackageDownloadPath { get; set; } = "";
    public string PortalStatusOutputPath { get; set; } = "";
    public string DiscoveryReviewSummary { get; set; } = "";
    public string DiscoveredLibrariesSummary { get; set; } = "";
    public string DiscoverySyncReadinessSummary { get; set; } = "";
    public string DiscoverySyncStatusBrush { get; set; } = "";
    public string PackageReadinessStatusBrush { get; set; } = "";
    public string PackageReadinessSummary { get; set; } = "";
    public PersistedPortalSyncReceipt PortalSyncReceipt { get; set; } = new();
    public List<InstallerStepResult> CheckResults { get; set; } = [];
    public List<InstallerStepResult> PreviewResults { get; set; } = [];
    public List<InstallerStepResult> DeploymentResults { get; set; } = [];
    public List<InstallerStepResult> ValidationResults { get; set; } = [];
    public InstallStatus LastPreviewStatus { get; set; } = InstallStatus.NotStarted;
    public InstallStatus LastDeploymentStatus { get; set; } = InstallStatus.NotStarted;
    public InstallStatus LastValidationStatus { get; set; } = InstallStatus.NotStarted;
    public string PreviewStatus { get; set; } = "";
    public string PreviewStatusBrush { get; set; } = "";
    public string PreviewSummary { get; set; } = "";
    public string PreviewOutputPath { get; set; } = "";
    public string DeploymentStatus { get; set; } = "";
    public string DeploymentStatusBrush { get; set; } = "";
    public string DeploymentSummary { get; set; } = "";
    public string DeploymentOutputPath { get; set; } = "";
    public string ValidationStatus { get; set; } = "";
    public string ValidationStatusBrush { get; set; } = "";
    public string ValidationSummary { get; set; } = "";
    public string ValidationOutputPath { get; set; } = "";
    public string FinishStatus { get; set; } = "";
    public string FinishStatusBrush { get; set; } = "";
    public string FinishSummary { get; set; } = "";
    public string FinalReportPath { get; set; } = "";
    public string FinalManifestPath { get; set; } = "";
    public string FinalBundlePath { get; set; } = "";
    public string FinalEvidenceDirectory { get; set; } = "";
    public string AiTitle { get; set; } = "";
    public string AiSummary { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string SessionStatus { get; set; } = "";
    public string FooterStatus { get; set; } = "";
}

public sealed class PersistedInstallerStepState
{
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string StatusLabel { get; set; } = "Pending";
    public string StatusBrush { get; set; } = "#2A355E";
}

public sealed class PersistedPortalSyncReceipt
{
    public string SessionId { get; set; } = "";
    public string DiscoveryId { get; set; } = "";
    public string SyncStatus { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string PackageReadinessStatus { get; set; } = "";
    public string PortalRecordUrl { get; set; } = "";
    public string ReceiptOutputPath { get; set; } = "";
    public string SyncedAt { get; set; } = "";
}
