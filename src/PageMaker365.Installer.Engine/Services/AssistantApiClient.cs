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
        var apiKey = Environment.GetEnvironmentVariable(_options.ApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

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
