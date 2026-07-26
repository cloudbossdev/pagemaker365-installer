namespace PageMaker365.Installer.Engine.Models;

public sealed class InstallerEvidenceEvent
{
    public string Lifecycle { get; set; } = "install";
    public string AttemptId { get; set; } = "";
    public string EventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string InstallAttemptId { get; set; } = "";
    public string UpgradeAttemptId { get; set; } = "";
    public int Sequence { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string OnboardingSessionId { get; set; } = "";
    public string DeploymentExportId { get; set; } = "";
    public string LifecycleStatus { get; set; } = "";
    public string Outcome { get; set; } = "";
    public InstallerEvidenceError? Error { get; set; }
    public string InstallerVersion { get; set; } = "";
    public string PackageHash { get; set; } = "";
    public string RuntimeUrl { get; set; } = "";
    public string ApiUrl { get; set; } = "";
    public string AzureResourceGroup { get; set; } = "";
    public string Operation { get; set; } = "";
    public string SourceRuntimeVersion { get; set; } = "";
    public string TargetRuntimeVersion { get; set; } = "";
    public List<InstallerEvidenceSmokeTest> SmokeTests { get; set; } = [];
    public string Message { get; set; } = "";
}

public sealed class InstallerEvidenceError
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string Category { get; set; } = "";
    public bool Retryable { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class InstallerEvidenceSmokeTest
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class InstallerEvidenceReceipt
{
    public string ContractVersion { get; set; } = "";
    public string Status { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string EventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string InstallAttemptId { get; set; } = "";
    public int Sequence { get; set; }
    public string LifecycleStatus { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string InstallStatus { get; set; } = "";
    public bool Deduped { get; set; }
    public string CorrelationId { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class PendingInstallerEvidenceEvent
{
    public string IdempotencyKey { get; set; } = "";
    public InstallerEvidenceEvent Payload { get; set; } = new();
    public int DeliveryAttempts { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string LastDeliveryStatus { get; set; } = "Pending";
}

public sealed class InstallerEvidenceOutboxState
{
    public string InstallAttemptId { get; set; } = "";
    public int NextSequence { get; set; } = 1;
    public bool InstallStarted { get; set; }
    public bool IsTerminal { get; set; }
    public List<PendingInstallerEvidenceEvent> PendingEvents { get; set; } = [];
}

public static class InstallerEvidenceEventType
{
    public const string PackageValidated = "package_validated";
    public const string PackageValidationFailed = "package_validation_failed";
    public const string InstallStarted = "install_started";
    public const string AzureDeploymentCompleted = "azure_deployment_completed";
    public const string RuntimeConfigured = "runtime_configured";
    public const string SmokeTestsCompleted = "smoke_tests_completed";
    public const string InstallCompleted = "install_completed";
    public const string InstallFailed = "install_failed";
    public const string UpgradePackageValidated = "upgrade_package_validated";
    public const string UpgradePackageValidationFailed = "upgrade_package_validation_failed";
    public const string UpgradeStarted = "upgrade_started";
    public const string UpgradeDeploymentCompleted = "upgrade_deployment_completed";
    public const string UpgradeRuntimeConfigured = "upgrade_runtime_configured";
    public const string UpgradeValidationCompleted = "upgrade_validation_completed";
    public const string UpgradeCompleted = "upgrade_completed";
    public const string UpgradeFailed = "upgrade_failed";
}
