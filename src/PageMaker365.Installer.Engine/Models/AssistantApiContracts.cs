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
