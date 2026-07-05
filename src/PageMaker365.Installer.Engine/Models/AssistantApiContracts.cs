namespace PageMaker365.Installer.Engine.Models;

public sealed class AssistantMessageRequest
{
    public string ContractVersion { get; set; } = "2026-07-05";
    public string ConversationId { get; set; } = "";
    public bool IncludeDiagnostics { get; set; }
    public AssistantDiagnosticContext DiagnosticContext { get; set; } = new();
    public AssistantMessage UserMessage { get; set; } = new();
    public List<AssistantMessage> ConversationHistory { get; set; } = [];
    public string LocalTranscriptPath { get; set; } = "";
}

public sealed class AssistantMessageResponse
{
    public string ContractVersion { get; set; } = "2026-07-05";
    public string ConversationId { get; set; } = "";
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public string Source { get; set; } = "LocalMock";
    public bool UsedFallback { get; set; }
    public DateTimeOffset RespondedAt { get; set; } = DateTimeOffset.UtcNow;
    public AssistantMessage Message { get; set; } = new();
    public List<AssistantRecommendedAction> RecommendedActions { get; set; } = [];
}

public sealed class AssistantRecommendedAction
{
    public string ActionId { get; set; } = "";
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public bool RequiresApproval { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class AssistantAttachmentUploadRequest
{
    public string ContractVersion { get; set; } = "2026-07-05";
    public string ConversationId { get; set; } = "";
    public string AttachmentId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public AssistantDiagnosticContext DiagnosticContext { get; set; } = new();
}

public sealed class AssistantAttachmentUploadResponse
{
    public string ContractVersion { get; set; } = "2026-07-05";
    public string ConversationId { get; set; } = "";
    public string AttachmentId { get; set; } = "";
    public string UploadedAttachmentId { get; set; } = "";
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public string Source { get; set; } = "LocalMock";
    public bool UsedFallback { get; set; }
    public string Status { get; set; } = "Uploaded";
    public string Message { get; set; } = "";
}

public sealed class AssistantSupportTicketRequest
{
    public string ContractVersion { get; set; } = "2026-07-05";
    public string ConversationId { get; set; } = "";
    public bool IncludeDiagnostics { get; set; }
    public AssistantDiagnosticContext DiagnosticContext { get; set; } = new();
    public string Subject { get; set; } = "";
    public string Description { get; set; } = "";
    public List<AssistantMessage> ConversationHistory { get; set; } = [];
    public List<AssistantUploadedAttachmentReference> UploadedAttachments { get; set; } = [];
    public string LocalTranscriptPath { get; set; } = "";
}

public sealed class AssistantSupportTicketResponse
{
    public string ContractVersion { get; set; } = "2026-07-05";
    public string ConversationId { get; set; } = "";
    public string TicketDraftId { get; set; } = "";
    public string PortalRecordUrl { get; set; } = "";
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public string Source { get; set; } = "LocalMock";
    public bool UsedFallback { get; set; }
    public string Status { get; set; } = "Drafted";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<AssistantUploadedAttachmentReference> UploadedAttachments { get; set; } = [];
}

public sealed class AssistantUploadedAttachmentReference
{
    public string AttachmentId { get; set; } = "";
    public string UploadedAttachmentId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string Source { get; set; } = "";
    public string Status { get; set; } = "";
    public string CorrelationId { get; set; } = "";
}
