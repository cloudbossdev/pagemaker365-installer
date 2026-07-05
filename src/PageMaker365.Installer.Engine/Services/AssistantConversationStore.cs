using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class AssistantConversationStore
{
    private readonly RedactionService _redactionService;

    public AssistantConversationStore(RedactionService redactionService)
    {
        _redactionService = redactionService;
    }

    public async Task<string> SaveAsync(
        AssistantConversation conversation,
        string conversationRoot,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(conversationRoot);
        conversation.UpdatedAt = DateTimeOffset.UtcNow;

        var redactedConversation = RedactConversation(conversation);
        var json = JsonSerializer.Serialize(redactedConversation, new JsonSerializerOptions { WriteIndented = true });
        var path = Path.Combine(conversationRoot, "assistant-conversation.redacted.json");
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    private AssistantConversation RedactConversation(AssistantConversation conversation)
    {
        return new AssistantConversation
        {
            ConversationId = conversation.ConversationId,
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt,
            DiagnosticContext = RedactContext(conversation.DiagnosticContext),
            Messages = conversation.Messages.Select(RedactMessage).ToList()
        };
    }

    private AssistantMessage RedactMessage(AssistantMessage message)
    {
        return new AssistantMessage
        {
            Role = message.Role,
            Content = _redactionService.Redact(message.Content),
            CreatedAt = message.CreatedAt,
            Attachments = message.Attachments.Select(RedactAttachment).ToList()
        };
    }

    private static AssistantAttachment RedactAttachment(AssistantAttachment attachment)
    {
        return new AssistantAttachment
        {
            AttachmentId = attachment.AttachmentId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            OriginalPath = string.IsNullOrWhiteSpace(attachment.OriginalPath) ? "" : "[local path omitted]",
            StoredPath = string.IsNullOrWhiteSpace(attachment.StoredPath) ? "" : Path.GetFileName(attachment.StoredPath),
            SizeBytes = attachment.SizeBytes,
            Sha256 = attachment.Sha256,
            IsImage = attachment.IsImage,
            UploadStatus = attachment.UploadStatus,
            UploadedAttachmentId = attachment.UploadedAttachmentId,
            UploadCorrelationId = attachment.UploadCorrelationId,
            UploadError = string.IsNullOrWhiteSpace(attachment.UploadError) ? "" : "[upload error omitted]",
            CreatedAt = attachment.CreatedAt
        };
    }

    private AssistantDiagnosticContext RedactContext(AssistantDiagnosticContext context)
    {
        return new AssistantDiagnosticContext
        {
            WorkflowMode = context.WorkflowMode,
            WorkflowTitle = context.WorkflowTitle,
            CurrentStep = context.CurrentStep,
            CustomerName = _redactionService.Redact(context.CustomerName),
            PackagePath = ShortPath(context.PackagePath),
            AzureSubscription = _redactionService.Redact(context.AzureSubscription),
            SharePointSite = _redactionService.Redact(context.SharePointSite),
            OnboardingSessionId = _redactionService.Redact(context.OnboardingSessionId),
            OnboardingStatus = _redactionService.Redact(context.OnboardingStatus),
            OnboardingApiBaseUrl = _redactionService.Redact(context.OnboardingApiBaseUrl),
            PortalSyncStatus = _redactionService.Redact(context.PortalSyncStatus),
            DiscoverySummary = _redactionService.Redact(context.DiscoverySummary),
            DiscoveryOutputPath = ShortPath(context.DiscoveryOutputPath),
            InstallerSessionId = context.InstallerSessionId,
            InstallerSessionStatus = context.InstallerSessionStatus,
            FooterStatus = _redactionService.Redact(context.FooterStatus),
            Checks = context.Checks
                .Select(check => new AssistantCheckSummary
                {
                    Name = check.Name,
                    Code = check.Code,
                    Status = check.Status,
                    Summary = _redactionService.Redact(check.Summary),
                    RetrySafe = check.RetrySafe,
                    RequiresApproval = check.RequiresApproval
                })
                .ToList()
        };
    }

    private static string ShortPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return path;
        }

        return Path.GetFileName(path);
    }
}
