namespace PageMaker365.Installer.Engine.Models;

public sealed class InstallerEvidenceEvent
{
    public string Lifecycle { get; set; } = InstallerEvidenceLifecycle.Install;
    public string AttemptId { get; set; } = "";
    public string EventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string InstallAttemptId { get; set; } = "";
    public string RemovalAttemptId { get; set; } = "";
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
    public List<InstallerEvidenceSmokeTest> SmokeTests { get; set; } = [];
    public RemovalEvidenceOutcomeSummary? RemovalOutcomes { get; set; }
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
    public string Lifecycle { get; set; } = "";
    public string AttemptId { get; set; } = "";
    public string InstallAttemptId { get; set; } = "";
    public string RemovalAttemptId { get; set; } = "";
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

public sealed class RemovalEvidenceOutboxState
{
    public string RemovalAttemptId { get; set; } = "";
    public int NextSequence { get; set; } = 1;
    public bool RemovalStarted { get; set; }
    public bool InventoryCompleted { get; set; }
    public bool ExecutionCompleted { get; set; }
    public bool ValidationCompleted { get; set; }
    public bool IsTerminal { get; set; }
    public string LastEventType { get; set; } = "";
    public List<PendingInstallerEvidenceEvent> PendingEvents { get; set; } = [];
}

public sealed class RemovalEvidenceOutcomeSummary
{
    public int Removed { get; set; }
    public int Retained { get; set; }
    public int Skipped { get; set; }
    public int Blocked { get; set; }
    public int Failed { get; set; }
    public List<string> RetainedCategories { get; set; } = [];
    public List<string> SkippedCategories { get; set; } = [];
}

public static class InstallerEvidenceLifecycle
{
    public const string Install = "install";
    public const string Removal = "removal";
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
    public const string RemovalStarted = "removal_started";
    public const string RemovalInventoryCompleted = "removal_inventory_completed";
    public const string RemovalExecutionCompleted = "removal_execution_completed";
    public const string RemovalValidationCompleted = "removal_validation_completed";
    public const string RemovalCompleted = "removal_completed";
    public const string RemovalBlocked = "removal_blocked";
    public const string RemovalFailed = "removal_failed";
}
