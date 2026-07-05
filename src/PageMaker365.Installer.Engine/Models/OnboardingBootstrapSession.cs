namespace PageMaker365.Installer.Engine.Models;

public sealed class OnboardingBootstrapSession
{
    public string ContractVersion { get; set; } = "0.1";
    public string SessionId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string ExpectedTenantId { get; set; } = "";
    public string PortalBaseUrl { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "";
    public string OneTimeCode { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(8);
    public List<string> AllowedOperations { get; set; } = [];
    public DiscoveryPolicy DiscoveryPolicy { get; set; } = new();
}

public sealed class DiscoveryPolicy
{
    public bool AllowAzureDiscovery { get; set; } = true;
    public bool AllowGraphDiscovery { get; set; } = true;
    public bool AllowSharePointDiscovery { get; set; } = true;
    public bool AllowPortalSync { get; set; } = true;
    public List<string> RequiredFields { get; set; } = [];
    public List<string> ExcludedDataTypes { get; set; } = [];
}

public sealed class OnboardingSessionConnection
{
    public string Status { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OnboardingDiscoverySubmission
{
    public string Status { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string DiscoveryId { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string PortalRecordUrl { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
}
