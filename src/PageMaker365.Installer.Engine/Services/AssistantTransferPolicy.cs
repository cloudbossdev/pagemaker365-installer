using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public static partial class AssistantTransferPolicy
{
    public const string ContractVersion = "2026-07-05";

    private static readonly RedactionService Redaction = new();
    private static readonly IReadOnlyDictionary<string, string> PortalAttachmentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = "text/plain",
            [".log"] = "text/plain",
            [".json"] = "application/json",
            [".md"] = "text/markdown"
        };

    public static string SanitizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value ?? "";
        }

        var sanitized = Redaction.Redact(value);
        sanitized = WindowsPathRegex().Replace(sanitized, "[local path omitted]");
        sanitized = UncPathRegex().Replace(sanitized, "[local path omitted]");
        sanitized = UnixHomePathRegex().Replace(sanitized, "[local path omitted]");
        return sanitized;
    }

    public static bool IsPortalAttachmentAllowed(string fileName)
    {
        return PortalAttachmentTypes.ContainsKey(Path.GetExtension(fileName));
    }

    public static string ContentTreatment(string fileName)
    {
        return IsPortalAttachmentAllowed(fileName) ? "RedactedText" : "LocalOnlyBinary";
    }

    public static string CreateTransferFileName(string attachmentId, string originalFileName)
    {
        RequireIdentifier(attachmentId, "attachmentId");
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (!PortalAttachmentTypes.ContainsKey(extension))
        {
            throw new InvalidDataException("The attachment type is not approved for portal transfer.");
        }

        var shortId = attachmentId[..Math.Min(12, attachmentId.Length)];
        return $"attachment-{shortId}{extension}";
    }

    public static void ValidateMessageRequest(AssistantMessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireContract(request.ContractVersion);
        RequireIdentifier(request.ConversationId, "conversationId");
        RequireNoLocalTranscriptPath(request.LocalTranscriptPath);
        ValidateContext(request.DiagnosticContext);
        ValidateMessage(request.UserMessage);
        foreach (var message in request.ConversationHistory)
        {
            ValidateMessage(message);
        }
    }

    public static void ValidateSupportTicketRequest(AssistantSupportTicketRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireContract(request.ContractVersion);
        RequireIdentifier(request.ConversationId, "conversationId");
        RequireNoLocalTranscriptPath(request.LocalTranscriptPath);
        RequireSanitized(request.Subject, "subject");
        RequireSanitized(request.Description, "description");
        ValidateContext(request.DiagnosticContext);
        foreach (var message in request.ConversationHistory)
        {
            ValidateMessage(message);
        }

        foreach (var attachment in request.UploadedAttachments)
        {
            ValidateUploadedReference(attachment);
        }
    }

    public static void ValidateAttachmentUpload(
        AssistantAttachmentUploadRequest request,
        string storedPath,
        long maxAttachmentBytes)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireContract(request.ContractVersion);
        RequireIdentifier(request.ConversationId, "conversationId");
        RequireIdentifier(request.AttachmentId, "attachmentId");
        ValidateContext(request.DiagnosticContext);

        if (!File.Exists(storedPath))
        {
            throw new FileNotFoundException("Assistant attachment file does not exist.", storedPath);
        }

        var expectedName = CreateTransferFileName(request.AttachmentId, request.FileName);
        if (!request.FileName.Equals(expectedName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Assistant attachment filenames must use the generated transfer identity.");
        }

        var extension = Path.GetExtension(request.FileName);
        if (!PortalAttachmentTypes.TryGetValue(extension, out var expectedContentType) ||
            !request.ContentType.Equals(expectedContentType, StringComparison.OrdinalIgnoreCase) ||
            !request.ContentTreatment.Equals("RedactedText", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Assistant attachment type or content treatment is not approved for portal transfer.");
        }

        var file = new FileInfo(storedPath);
        if (request.SizeBytes <= 0 || request.SizeBytes != file.Length)
        {
            throw new InvalidDataException("Assistant attachment size does not match the prepared file.");
        }

        if (request.SizeBytes > maxAttachmentBytes)
        {
            throw new InvalidOperationException($"Attachment exceeds the configured upload limit of {maxAttachmentBytes} bytes.");
        }

        using var stream = File.OpenRead(storedPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream));
        if (!actualHash.Equals(request.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Assistant attachment hash does not match the prepared file.");
        }
    }

    public static AssistantMessage SanitizeResponseMessage(AssistantMessage message)
    {
        return new AssistantMessage
        {
            Role = message.Role,
            Content = SanitizeText(message.Content),
            CreatedAt = message.CreatedAt,
            Attachments = []
        };
    }

    private static void ValidateContext(AssistantDiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.IsNullOrWhiteSpace(context.PackagePath) ||
            !string.IsNullOrWhiteSpace(context.DiscoveryOutputPath))
        {
            throw new InvalidDataException("Assistant diagnostic payloads must not contain local artifact paths.");
        }

        foreach (var value in new[]
        {
            context.WorkflowMode,
            context.WorkflowTitle,
            context.CurrentStep,
            context.CustomerName,
            context.AzureSubscription,
            context.SharePointSite,
            context.OnboardingSessionId,
            context.OnboardingStatus,
            context.OnboardingApiBaseUrl,
            context.PortalSyncStatus,
            context.DiscoverySummary,
            context.InstallerSessionId,
            context.InstallerSessionStatus,
            context.FooterStatus
        })
        {
            RequireSanitized(value, "diagnostic context");
        }

        foreach (var check in context.Checks)
        {
            RequireSanitized(check.Name, "check name");
            RequireSanitized(check.Code, "check code");
            RequireSanitized(check.Status, "check status");
            RequireSanitized(check.Summary, "check summary");
        }
    }

    private static void ValidateMessage(AssistantMessage message)
    {
        if (message.Role is not ("User" or "Assistant" or "System"))
        {
            throw new InvalidDataException("Assistant message role is invalid.");
        }

        RequireSanitized(message.Content, "message content");
        foreach (var attachment in message.Attachments)
        {
            RequireIdentifier(attachment.AttachmentId, "attachmentId");
            if (!string.IsNullOrWhiteSpace(attachment.OriginalPath) ||
                !string.IsNullOrWhiteSpace(attachment.StoredPath) ||
                !attachment.FileName.Equals(
                    CreateTransferFileName(attachment.AttachmentId, attachment.FileName),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Assistant attachment metadata contains a local path or original filename.");
            }
        }
    }

    private static void ValidateUploadedReference(AssistantUploadedAttachmentReference attachment)
    {
        RequireIdentifier(attachment.AttachmentId, "attachmentId");
        RequireIdentifier(attachment.UploadedAttachmentId, "uploadedAttachmentId");
        if (!attachment.Status.Equals("Uploaded", StringComparison.Ordinal) ||
            !attachment.ContentTreatment.Equals("RedactedText", StringComparison.Ordinal) ||
            !attachment.FileName.Equals(
                CreateTransferFileName(attachment.AttachmentId, attachment.FileName),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Support ticket attachment references must identify a successfully uploaded redacted artifact.");
        }
    }

    private static void RequireContract(string value)
    {
        if (!value.Equals(ContractVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Assistant API contract must be {ContractVersion}.");
        }
    }

    private static void RequireIdentifier(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || !IdentifierRegex().IsMatch(value))
        {
            throw new InvalidDataException($"Assistant {field} is missing or invalid.");
        }
    }

    private static void RequireNoLocalTranscriptPath(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("Assistant payloads must not include a local transcript path.");
        }
    }

    private static void RequireSanitized(string value, string field)
    {
        if (!string.Equals(value, SanitizeText(value), StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Assistant {field} contains prohibited secret-like or local-path content.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9])(?:[A-Z]:\\)(?:[^\s<>\""""|?*]+\\)*[^\s<>\""""|?*]*")]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"(?<!\\)\\\\[^\s\\]+\\[^\s]+")]
    private static partial Regex UncPathRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])/(?:home|Users)/[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UnixHomePathRegex();
}
