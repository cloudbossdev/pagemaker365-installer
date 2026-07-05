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
    private readonly AssistantConversation _conversation;
    private readonly string _conversationRoot;
    private readonly string _attachmentRoot;
    private string _draftMessage = "";
    private string _statusText;
    private bool _includeDiagnostics = true;

    public AssistantWorkspaceViewModel(
        AssistantDiagnosticContext diagnosticContext,
        string workspaceRoot,
        RedactionService redactionService)
    {
        _conversationStore = new AssistantConversationStore(redactionService);
        var assistantOptions = new AssistantApiOptionsService().Load(workspaceRoot);
        _assistantApiClient = new AssistantApiClient(assistantOptions);
        _conversation = new AssistantConversation
        {
            DiagnosticContext = diagnosticContext
        };
        _conversationRoot = Path.Combine(workspaceRoot, "support-bundle", "assistant", _conversation.ConversationId);
        _attachmentRoot = Path.Combine(_conversationRoot, "attachments");
        _statusText = $"{_assistantApiClient.ConnectionLabel}. Transcript will be saved to {_conversationRoot}";

        AttachFilesCommand = new RelayCommand(AttachFilesAsync);
        PasteClipboardImageCommand = new RelayCommand(PasteClipboardImageAsync);
        SendMessageCommand = new RelayCommand(SendMessageAsync, CanSendMessage);
        RemovePendingAttachmentCommand = new RelayCommand(RemovePendingAttachmentAsync);

        AddAssistantGreeting();
    }

    public ObservableCollection<AssistantMessageViewModel> Messages { get; } = [];
    public ObservableCollection<AssistantAttachmentViewModel> PendingAttachments { get; } = [];
    public RelayCommand AttachFilesCommand { get; }
    public RelayCommand PasteClipboardImageCommand { get; }
    public RelayCommand SendMessageCommand { get; }
    public RelayCommand RemovePendingAttachmentCommand { get; }

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
    }

    private async Task<AssistantMessageResponse> SendAssistantMessageAsync(AssistantMessage userMessage)
    {
        try
        {
            return await _assistantApiClient.SendMessageAsync(new AssistantMessageRequest
            {
                ConversationId = _conversation.ConversationId,
                IncludeDiagnostics = IncludeDiagnostics,
                DiagnosticContext = _conversation.DiagnosticContext,
                UserMessage = userMessage,
                ConversationHistory = _conversation.Messages.ToList(),
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
