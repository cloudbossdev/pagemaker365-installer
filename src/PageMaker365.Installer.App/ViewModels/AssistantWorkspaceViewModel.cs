using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.App.ViewModels;

public sealed class AssistantWorkspaceViewModel : ViewModelBase
{
    private readonly AssistantAttachmentService _attachmentService = new();
    private readonly AssistantConversationStore _conversationStore;
    private readonly AssistantApiClient _assistantApiClient;
    private readonly RedactionService _redactionService;
    private readonly AssistantConversation _conversation;
    private readonly string _conversationRoot;
    private readonly string _attachmentRoot;
    private readonly string _outboxRoot;
    private string _draftMessage = "";
    private string _statusText;
    private string _supportTicketStatus = "No support ticket draft created.";
    private bool _includeDiagnostics = true;
    private bool _uploadAttachmentsWithHandoff = true;

    public AssistantWorkspaceViewModel(
        AssistantDiagnosticContext diagnosticContext,
        string workspaceRoot,
        RedactionService redactionService)
    {
        _redactionService = redactionService;
        _conversationStore = new AssistantConversationStore(redactionService);
        var assistantOptions = new AssistantApiOptionsService().Load(workspaceRoot);
        _assistantApiClient = new AssistantApiClient(assistantOptions);
        _conversation = new AssistantConversation
        {
            DiagnosticContext = diagnosticContext
        };
        _conversationRoot = Path.Combine(workspaceRoot, "support-bundle", "assistant", _conversation.ConversationId);
        _attachmentRoot = Path.Combine(_conversationRoot, "attachments");
        _outboxRoot = Path.Combine(_conversationRoot, "portal-outbox");
        _statusText = $"{_assistantApiClient.ConnectionLabel}. Transcript will be saved to {_conversationRoot}";

        AttachFilesCommand = new RelayCommand(AttachFilesAsync);
        PasteClipboardImageCommand = new RelayCommand(PasteClipboardImageAsync);
        SendMessageCommand = new RelayCommand(SendMessageAsync, CanSendMessage);
        RemovePendingAttachmentCommand = new RelayCommand(RemovePendingAttachmentAsync);
        CreateSupportTicketDraftCommand = new RelayCommand(CreateSupportTicketDraftAsync, CanCreateSupportTicketDraft);

        AddAssistantGreeting();
    }

    public ObservableCollection<AssistantMessageViewModel> Messages { get; } = [];
    public ObservableCollection<AssistantAttachmentViewModel> PendingAttachments { get; } = [];
    public RelayCommand AttachFilesCommand { get; }
    public RelayCommand PasteClipboardImageCommand { get; }
    public RelayCommand SendMessageCommand { get; }
    public RelayCommand RemovePendingAttachmentCommand { get; }
    public RelayCommand CreateSupportTicketDraftCommand { get; }

    public string DraftMessage
    {
        get => _draftMessage;
        set
        {
            if (SetProperty(ref _draftMessage, value))
            {
                SendMessageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IncludeDiagnostics
    {
        get => _includeDiagnostics;
        set => SetProperty(ref _includeDiagnostics, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string SupportTicketStatus
    {
        get => _supportTicketStatus;
        set => SetProperty(ref _supportTicketStatus, value);
    }

    public bool UploadAttachmentsWithHandoff
    {
        get => _uploadAttachmentsWithHandoff;
        set => SetProperty(ref _uploadAttachmentsWithHandoff, value);
    }

    public string ContextTitle => $"{_conversation.DiagnosticContext.WorkflowMode} Assistant";
    public string ContextStep => _conversation.DiagnosticContext.CurrentStep;
    public string ContextCustomer => _conversation.DiagnosticContext.CustomerName;
    public string ContextSession => _conversation.DiagnosticContext.InstallerSessionId;
    public string ContextStatus => _conversation.DiagnosticContext.InstallerSessionStatus;
    public string ContextSite => _conversation.DiagnosticContext.SharePointSite;
    public string ConversationRoot => _conversationRoot;
    public string AssistantConnectionLabel => _assistantApiClient.ConnectionLabel;

    public async Task AddDroppedFilesAsync(IEnumerable<string> paths)
    {
        await AddFilesAsync(paths);
    }

    private async Task AttachFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Attach assistant context",
            Multiselect = true,
            Filter = "Supported files (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.txt;*.log;*.json;*.md)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.txt;*.log;*.json;*.md|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            await AddFilesAsync(dialog.FileNames);
        }
    }

    private async Task PasteClipboardImageAsync()
    {
        if (!Clipboard.ContainsImage())
        {
            StatusText = "Clipboard does not contain an image.";
            return;
        }

        var image = Clipboard.GetImage();
        if (image is null)
        {
            StatusText = "Clipboard image could not be read.";
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"pm365-clipboard-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.png");
        await using (var stream = File.Create(tempPath))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(stream);
        }

        try
        {
            await AddFilesAsync([tempPath]);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task AddFilesAsync(IEnumerable<string> paths)
    {
        var imported = 0;
        foreach (var path in paths.Where(File.Exists))
        {
            try
            {
                var attachment = await _attachmentService.ImportAsync(path, _attachmentRoot);
                PendingAttachments.Add(new AssistantAttachmentViewModel(attachment));
                imported++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                StatusText = exception.Message;
            }
        }

        if (imported > 0)
        {
            StatusText = $"Added {imported} attachment(s). Send a message to save them with the transcript.";
            SendMessageCommand.RaiseCanExecuteChanged();
        }
    }

    private Task RemovePendingAttachmentAsync(object? parameter)
    {
        if (parameter is AssistantAttachmentViewModel attachment)
        {
            PendingAttachments.Remove(attachment);
            StatusText = $"Removed {attachment.FileName}.";
            SendMessageCommand.RaiseCanExecuteChanged();
        }

        return Task.CompletedTask;
    }

    private bool CanSendMessage()
    {
        return !string.IsNullOrWhiteSpace(DraftMessage) || PendingAttachments.Count > 0;
    }

    private async Task SendMessageAsync()
    {
        if (!CanSendMessage())
        {
            return;
        }

        var userMessage = new AssistantMessage
        {
            Role = "User",
            Content = DraftMessage.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            Attachments = PendingAttachments.Select(attachment => attachment.Model).ToList()
        };

        _conversation.Messages.Add(userMessage);
        Messages.Add(new AssistantMessageViewModel(userMessage));
        DraftMessage = "";
        PendingAttachments.Clear();
        SendMessageCommand.RaiseCanExecuteChanged();

        var response = await SendAssistantMessageAsync(userMessage);
        var assistantMessage = response.Message;
        _conversation.Messages.Add(assistantMessage);
        Messages.Add(new AssistantMessageViewModel(assistantMessage));

        var savedPath = await _conversationStore.SaveAsync(_conversation, _conversationRoot);
        StatusText = $"Saved assistant transcript: {savedPath}. Source: {response.Source}; correlation: {response.CorrelationId}.";
        CreateSupportTicketDraftCommand.RaiseCanExecuteChanged();
    }

    private async Task<AssistantMessageResponse> SendAssistantMessageAsync(AssistantMessage userMessage)
    {
        try
        {
            return await _assistantApiClient.SendMessageAsync(new AssistantMessageRequest
            {
                ConversationId = _conversation.ConversationId,
                IncludeDiagnostics = IncludeDiagnostics,
                DiagnosticContext = CreateApiDiagnosticContext(),
                UserMessage = CreateApiMessage(userMessage),
                ConversationHistory = CreateApiConversationHistory(),
                LocalTranscriptPath = _conversationRoot
            });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new AssistantMessageResponse
            {
                ConversationId = _conversation.ConversationId,
                Source = "ClientError",
                Message = new AssistantMessage
                {
                    Role = "Assistant",
                    Content = $"The assistant API could not be reached: {exception.Message}{Environment.NewLine}{Environment.NewLine}The transcript and attachments were kept locally. If this needs escalation, create a support bundle.",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            };
        }
    }

    private bool CanCreateSupportTicketDraft()
    {
        return _conversation.Messages.Any(message => message.Role.Equals("User", StringComparison.OrdinalIgnoreCase));
    }

    private async Task CreateSupportTicketDraftAsync()
    {
        if (!CanCreateSupportTicketDraft())
        {
            SupportTicketStatus = "Send at least one message before creating a support ticket draft.";
            return;
        }

        SupportTicketStatus = "Preparing support ticket draft...";
        var uploadedAttachments = await UploadConversationAttachmentsAsync();
        var request = new AssistantSupportTicketRequest
        {
            ConversationId = _conversation.ConversationId,
            IncludeDiagnostics = IncludeDiagnostics,
            DiagnosticContext = CreateApiDiagnosticContext(),
            Subject = CreateSupportTicketSubject(),
            Description = CreateSupportTicketDescription(),
            ConversationHistory = CreateApiConversationHistory(),
            UploadedAttachments = uploadedAttachments,
            LocalTranscriptPath = _conversationRoot
        };

        try
        {
            var response = await _assistantApiClient.CreateSupportTicketDraftAsync(request, _outboxRoot);
            SupportTicketStatus = $"{response.Status}: {response.TicketDraftId}. Source: {response.Source}; correlation: {response.CorrelationId}.";
            StatusText = $"Support ticket draft created: {response.PortalRecordUrl}";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException)
        {
            SupportTicketStatus = $"Support ticket draft failed: {exception.Message}";
        }

        RefreshMessages();
        var savedPath = await _conversationStore.SaveAsync(_conversation, _conversationRoot);
        StatusText = $"{StatusText} Transcript saved: {savedPath}";
    }

    private async Task<List<AssistantUploadedAttachmentReference>> UploadConversationAttachmentsAsync()
    {
        var attachments = _conversation.Messages
            .SelectMany(message => message.Attachments)
            .ToList();
        var references = new List<AssistantUploadedAttachmentReference>();

        if (attachments.Count == 0)
        {
            return references;
        }

        if (!UploadAttachmentsWithHandoff)
        {
            foreach (var attachment in attachments)
            {
                attachment.UploadStatus = "LocalOnly";
                references.Add(CreateAttachmentReference(attachment, "LocalOnly", "", ""));
            }

            return references;
        }

        foreach (var attachment in attachments)
        {
            try
            {
                attachment.UploadStatus = "Uploading";
                var response = await _assistantApiClient.UploadAttachmentAsync(
                    new AssistantAttachmentUploadRequest
                    {
                        ConversationId = _conversation.ConversationId,
                        AttachmentId = attachment.AttachmentId,
                        FileName = attachment.FileName,
                        ContentType = attachment.ContentType,
                        SizeBytes = attachment.SizeBytes,
                        Sha256 = attachment.Sha256,
                        DiagnosticContext = CreateApiDiagnosticContext()
                    },
                    attachment.StoredPath,
                    _outboxRoot);

                attachment.UploadStatus = response.Status;
                attachment.UploadedAttachmentId = response.UploadedAttachmentId;
                attachment.UploadCorrelationId = response.CorrelationId;
                attachment.UploadError = "";
                references.Add(CreateAttachmentReference(attachment, response.Status, response.Source, response.CorrelationId));
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                attachment.UploadStatus = "Failed";
                attachment.UploadError = exception.Message;
                references.Add(CreateAttachmentReference(attachment, "Failed", "Client", ""));
            }
        }

        return references;
    }

    private static AssistantUploadedAttachmentReference CreateAttachmentReference(
        AssistantAttachment attachment,
        string status,
        string source,
        string correlationId)
    {
        return new AssistantUploadedAttachmentReference
        {
            AttachmentId = attachment.AttachmentId,
            UploadedAttachmentId = attachment.UploadedAttachmentId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes,
            Sha256 = attachment.Sha256,
            Source = source,
            Status = status,
            CorrelationId = correlationId
        };
    }

    private string CreateSupportTicketSubject()
    {
        var customer = string.IsNullOrWhiteSpace(_conversation.DiagnosticContext.CustomerName)
            ? "Unknown customer"
            : _conversation.DiagnosticContext.CustomerName;
        return $"PageMaker365 installer assistance - {customer} - {_conversation.DiagnosticContext.CurrentStep}";
    }

    private string CreateSupportTicketDescription()
    {
        var latestUserMessage = _conversation.Messages
            .LastOrDefault(message => message.Role.Equals("User", StringComparison.OrdinalIgnoreCase))
            ?.Content;
        return string.IsNullOrWhiteSpace(latestUserMessage)
            ? "Installer assistant handoff created from the desktop app."
            : latestUserMessage;
    }

    private AssistantDiagnosticContext CreateApiDiagnosticContext()
    {
        var context = _conversation.DiagnosticContext;
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

    private List<AssistantMessage> CreateApiConversationHistory()
    {
        return _conversation.Messages.Select(CreateApiMessage).ToList();
    }

    private AssistantMessage CreateApiMessage(AssistantMessage message)
    {
        return new AssistantMessage
        {
            Role = message.Role,
            Content = _redactionService.Redact(message.Content),
            CreatedAt = message.CreatedAt,
            Attachments = message.Attachments.Select(CreateApiAttachment).ToList()
        };
    }

    private static AssistantAttachment CreateApiAttachment(AssistantAttachment attachment)
    {
        return new AssistantAttachment
        {
            AttachmentId = attachment.AttachmentId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            OriginalPath = "",
            StoredPath = "",
            SizeBytes = attachment.SizeBytes,
            Sha256 = attachment.Sha256,
            IsImage = attachment.IsImage,
            UploadStatus = attachment.UploadStatus,
            UploadedAttachmentId = attachment.UploadedAttachmentId,
            UploadCorrelationId = attachment.UploadCorrelationId,
            UploadError = "",
            CreatedAt = attachment.CreatedAt
        };
    }

    private void RefreshMessages()
    {
        Messages.Clear();
        foreach (var message in _conversation.Messages)
        {
            Messages.Add(new AssistantMessageViewModel(message));
        }
    }

    private static string ShortPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return path;
        }

        return Path.GetFileName(path);
    }

    private void AddAssistantGreeting()
    {
        var greeting = new AssistantMessage
        {
            Role = "Assistant",
            Content = "Paste a screenshot, attach logs or JSON, then describe what the customer is seeing. This local scaffold will organize the issue and save a redacted transcript for support escalation.",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _conversation.Messages.Add(greeting);
        Messages.Add(new AssistantMessageViewModel(greeting));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
