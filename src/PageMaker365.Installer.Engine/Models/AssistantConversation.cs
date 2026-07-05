namespace PageMaker365.Installer.Engine.Models;

public sealed class AssistantConversation
{
    public string ConversationId { get; set; } = $"assistant-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public AssistantDiagnosticContext DiagnosticContext { get; set; } = new();
    public List<AssistantMessage> Messages { get; set; } = [];
}

public sealed class AssistantMessage
{
    public string Role { get; set; } = "User";
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<AssistantAttachment> Attachments { get; set; } = [];
}

public sealed class AssistantAttachment
{
    public string AttachmentId { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public string OriginalPath { get; set; } = "";
    public string StoredPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public bool IsImage { get; set; }
    public string UploadStatus { get; set; } = "Local";
    public string UploadedAttachmentId { get; set; } = "";
    public string UploadCorrelationId { get; set; } = "";
    public string UploadError { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AssistantDiagnosticContext
{
    public string WorkflowMode { get; set; } = "";
    public string WorkflowTitle { get; set; } = "";
    public string CurrentStep { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string PackagePath { get; set; } = "";
    public string AzureSubscription { get; set; } = "";
    public string SharePointSite { get; set; } = "";
    public string OnboardingSessionId { get; set; } = "";
    public string OnboardingStatus { get; set; } = "";
    public string OnboardingApiBaseUrl { get; set; } = "";
    public string PortalSyncStatus { get; set; } = "";
    public string DiscoverySummary { get; set; } = "";
    public string DiscoveryOutputPath { get; set; } = "";
    public string InstallerSessionId { get; set; } = "";
    public string InstallerSessionStatus { get; set; } = "";
    public string FooterStatus { get; set; } = "";
    public List<AssistantCheckSummary> Checks { get; set; } = [];
}

public sealed class AssistantCheckSummary
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Status { get; set; } = "";
    public string Summary { get; set; } = "";
    public bool RetrySafe { get; set; }
    public bool RequiresApproval { get; set; }
}
