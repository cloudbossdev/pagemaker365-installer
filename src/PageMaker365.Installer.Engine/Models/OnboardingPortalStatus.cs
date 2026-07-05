namespace PageMaker365.Installer.Engine.Models;

public sealed class OnboardingPortalStatus
{
    public string ContractVersion { get; set; } = "0.1";
    public string SessionId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string Status { get; set; } = "Unknown";
    public string PortalRecordUrl { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset LastSyncAt { get; set; } = DateTimeOffset.UtcNow;
    public List<OnboardingMissingField> MissingFields { get; set; } = [];
    public OnboardingPackageReadiness PackageReadiness { get; set; } = new();
}

public sealed class OnboardingMissingField
{
    public string FieldKey { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Required { get; set; } = true;
    public string Source { get; set; } = "Portal";
    public string Notes { get; set; } = "";
}

public sealed class OnboardingPackageReadiness
{
    public string Status { get; set; } = "NotChecked";
    public string PackageVersion { get; set; } = "";
    public string PackageDownloadUrl { get; set; } = "";
    public string LocalPackagePath { get; set; } = "";
    public DateTimeOffset? ReadyAt { get; set; }
    public string Message { get; set; } = "";
}

public sealed class OnboardingPackageDownloadResult
{
    public string Status { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string PackagePath { get; set; } = "";
    public string PackageVersion { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset DownloadedAt { get; set; } = DateTimeOffset.UtcNow;
}
