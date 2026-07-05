using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class AssistantApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly AssistantApiOptions _options;
    private readonly HttpClient _httpClient;
    private readonly MockAssistantService _mockAssistantService = new();

    public AssistantApiClient(AssistantApiOptions options, HttpClient? httpClient = null)
    {
        _options = options;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public string ConnectionLabel => _options.UseMock
        ? "Local mock assistant"
        : $"Portal assistant API: {_options.MessageEndpoint}";

    public async Task<AssistantMessageResponse> SendMessageAsync(
        AssistantMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_options.UseMock)
        {
            return await SendToMockAsync(request, usedFallback: false, cancellationToken);
        }

        try
        {
            return await SendToPortalAsync(request, cancellationToken);
        }
        catch (Exception) when (_options.FallbackToMockOnFailure)
        {
            return await SendToMockAsync(request, usedFallback: true, cancellationToken);
        }
    }

    public async Task<AssistantAttachmentUploadResponse> UploadAttachmentAsync(
        AssistantAttachmentUploadRequest request,
        string storedPath,
        string outboxRoot,
        CancellationToken cancellationToken = default)
    {
        ValidateAttachmentForUpload(request, storedPath);

        if (_options.UseMock)
        {
            return await UploadAttachmentToMockAsync(request, storedPath, outboxRoot, usedFallback: false, cancellationToken);
        }

        try
        {
            return await UploadAttachmentToPortalAsync(request, storedPath, cancellationToken);
        }
        catch (Exception) when (_options.FallbackToMockOnFailure)
        {
            return await UploadAttachmentToMockAsync(request, storedPath, outboxRoot, usedFallback: true, cancellationToken);
        }
    }

    public async Task<AssistantSupportTicketResponse> CreateSupportTicketDraftAsync(
        AssistantSupportTicketRequest request,
        string outboxRoot,
        CancellationToken cancellationToken = default)
    {
        if (_options.UseMock)
        {
            return await CreateSupportTicketDraftInMockAsync(request, outboxRoot, usedFallback: false, cancellationToken);
        }

        try
        {
            return await PostJsonAsync<AssistantSupportTicketRequest, AssistantSupportTicketResponse>(
                _options.SupportTicketEndpoint,
                request,
                fallbackSource: "PortalApi",
                cancellationToken);
        }
        catch (Exception) when (_options.FallbackToMockOnFailure)
        {
            return await CreateSupportTicketDraftInMockAsync(request, outboxRoot, usedFallback: true, cancellationToken);
        }
    }

    private async Task<AssistantMessageResponse> SendToMockAsync(
        AssistantMessageRequest request,
        bool usedFallback,
        CancellationToken cancellationToken)
    {
        var conversation = new AssistantConversation
        {
            ConversationId = request.ConversationId,
            DiagnosticContext = request.DiagnosticContext,
            Messages = request.ConversationHistory
        };
        var message = await _mockAssistantService.CreateResponseAsync(
            conversation,
            request.UserMessage,
            request.IncludeDiagnostics,
            cancellationToken);

        return new AssistantMessageResponse
        {
            ConversationId = request.ConversationId,
            Source = usedFallback ? "LocalMockFallback" : "LocalMock",
            UsedFallback = usedFallback,
            Message = message,
            RecommendedActions = BuildMockRecommendedActions(request)
        };
    }

    private async Task<AssistantMessageResponse> SendToPortalAsync(
        AssistantMessageRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.MessageEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };
        ApplyAuthorization(httpRequest);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Assistant API returned {(int)response.StatusCode}: {body}");
        }

        var assistantResponse = JsonSerializer.Deserialize<AssistantMessageResponse>(body, JsonOptions);
        if (assistantResponse is null)
        {
            throw new InvalidOperationException("Assistant API returned an empty response.");
        }

        assistantResponse.Source = string.IsNullOrWhiteSpace(assistantResponse.Source)
            ? "PortalApi"
            : assistantResponse.Source;
        return assistantResponse;
    }

    private async Task<AssistantAttachmentUploadResponse> UploadAttachmentToPortalAsync(
        AssistantAttachmentUploadRequest request,
        string storedPath,
        CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(storedPath);
        using var content = new MultipartFormDataContent();
        content.Add(
            new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json"),
            "metadata");
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType);
        content.Add(fileContent, "file", request.FileName);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.AttachmentEndpoint)
        {
            Content = content
        };
        ApplyAuthorization(httpRequest);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Assistant attachment API returned {(int)response.StatusCode}: {body}");
        }

        var uploadResponse = JsonSerializer.Deserialize<AssistantAttachmentUploadResponse>(body, JsonOptions);
        if (uploadResponse is null)
        {
            throw new InvalidOperationException("Assistant attachment API returned an empty response.");
        }

        uploadResponse.Source = string.IsNullOrWhiteSpace(uploadResponse.Source)
            ? "PortalApi"
            : uploadResponse.Source;
        return uploadResponse;
    }

    private async Task<AssistantAttachmentUploadResponse> UploadAttachmentToMockAsync(
        AssistantAttachmentUploadRequest request,
        string storedPath,
        string outboxRoot,
        bool usedFallback,
        CancellationToken cancellationToken)
    {
        var attachmentRoot = Path.Combine(outboxRoot, "attachments");
        Directory.CreateDirectory(attachmentRoot);

        var safeFileName = SafeFileName(request.FileName);
        var outboxFilePath = Path.Combine(attachmentRoot, $"{request.AttachmentId}-{safeFileName}");
        await using (var source = File.OpenRead(storedPath))
        await using (var target = File.Create(outboxFilePath))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        var response = new AssistantAttachmentUploadResponse
        {
            ConversationId = request.ConversationId,
            AttachmentId = request.AttachmentId,
            UploadedAttachmentId = $"mock-upload-{request.AttachmentId}",
            Source = usedFallback ? "LocalMockFallback" : "LocalMock",
            UsedFallback = usedFallback,
            Status = "Uploaded",
            Message = $"Attachment copied to local portal outbox: {outboxFilePath}"
        };

        var manifestPath = Path.Combine(attachmentRoot, $"{request.AttachmentId}.upload.json");
        await WriteJsonAsync(
            manifestPath,
            new
            {
                request,
                response,
                outboxFile = Path.GetFileName(outboxFilePath)
            },
            cancellationToken);
        return response;
    }

    private async Task<AssistantSupportTicketResponse> CreateSupportTicketDraftInMockAsync(
        AssistantSupportTicketRequest request,
        string outboxRoot,
        bool usedFallback,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outboxRoot);
        var ticketDraftId = $"mock-ticket-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var response = new AssistantSupportTicketResponse
        {
            ConversationId = request.ConversationId,
            TicketDraftId = ticketDraftId,
            PortalRecordUrl = Path.Combine(outboxRoot, "support-ticket-draft.json"),
            Source = usedFallback ? "LocalMockFallback" : "LocalMock",
            UsedFallback = usedFallback,
            Status = "Drafted",
            Message = "Support ticket draft was written to the local portal outbox.",
            UploadedAttachments = request.UploadedAttachments
        };

        await WriteJsonAsync(
            Path.Combine(outboxRoot, "support-ticket-draft.json"),
            new
            {
                request,
                response
            },
            cancellationToken);
        return response;
    }

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        Uri endpoint,
        TRequest request,
        string fallbackSource,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };
        ApplyAuthorization(httpRequest);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Assistant API returned {(int)response.StatusCode}: {body}");
        }

        var result = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
        if (result is null)
        {
            throw new InvalidOperationException("Assistant API returned an empty response.");
        }

        if (result is AssistantSupportTicketResponse ticketResponse
            && string.IsNullOrWhiteSpace(ticketResponse.Source))
        {
            ticketResponse.Source = fallbackSource;
        }

        return result;
    }

    private void ValidateAttachmentForUpload(AssistantAttachmentUploadRequest request, string storedPath)
    {
        if (!File.Exists(storedPath))
        {
            throw new FileNotFoundException("Assistant attachment file does not exist.", storedPath);
        }

        if (request.SizeBytes > _options.MaxAttachmentBytes)
        {
            throw new InvalidOperationException($"Attachment exceeds the configured upload limit of {_options.MaxAttachmentBytes} bytes.");
        }
    }

    private void ApplyAuthorization(HttpRequestMessage httpRequest)
    {
        var apiKey = Environment.GetEnvironmentVariable(_options.ApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    private static async Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static string SafeFileName(string fileName)
    {
        return string.Concat(fileName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
    }

    private static List<AssistantRecommendedAction> BuildMockRecommendedActions(AssistantMessageRequest request)
    {
        var failedCheck = request.DiagnosticContext.Checks
            .FirstOrDefault(check => check.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase));

        var actions = new List<AssistantRecommendedAction>
        {
            new()
            {
                ActionId = "create-support-bundle",
                Label = "Create support bundle",
                Description = "Package the redacted installer session, logs, assistant transcript, and attachments for escalation.",
                Category = "Support",
                RequiresApproval = false
            }
        };

        if (failedCheck is not null)
        {
            actions.Add(new AssistantRecommendedAction
            {
                ActionId = "draft-admin-message",
                Label = "Draft admin message",
                Description = $"Create an administrator-facing note for {failedCheck.Name}.",
                Category = "Communication",
                RequiresApproval = false
            });
            actions.Add(new AssistantRecommendedAction
            {
                ActionId = "rerun-preflight",
                Label = "Rerun preflight",
                Description = "Retry the preflight check after the blocker has been resolved.",
                Category = "Installer",
                RequiresApproval = true
            });
        }

        return actions;
    }
}
