using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class AssistantApiOptionsService
{
    public AssistantApiOptions Load(string workspaceRoot)
    {
        var options = LoadFromFile(workspaceRoot) ?? new AssistantApiOptions();
        ApplyEnvironmentOverrides(options);
        Normalize(options);
        return options;
    }

    private static AssistantApiOptions? LoadFromFile(string workspaceRoot)
    {
        foreach (var path in EnumerateCandidatePaths(workspaceRoot))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AssistantApiOptions>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string workspaceRoot)
    {
        yield return Path.Combine(workspaceRoot, "assistant-api.json");
        yield return Path.Combine(workspaceRoot, "config", "assistant-api.json");
        yield return Path.Combine(AppContext.BaseDirectory, "assistant-api.json");
    }

    private static void ApplyEnvironmentOverrides(AssistantApiOptions options)
    {
        ApplyString("PM365_ASSISTANT_MODE", value => options.Mode = value);
        ApplyString("PM365_ASSISTANT_API_BASE_URL", value => options.PortalApiBaseUrl = value);
        ApplyString("PM365_ASSISTANT_ENDPOINT_PATH", value => options.MessageEndpointPath = value);
        ApplyString("PM365_ASSISTANT_ATTACHMENT_ENDPOINT_PATH", value => options.AttachmentEndpointPath = value);
        ApplyString("PM365_ASSISTANT_SUPPORT_TICKET_ENDPOINT_PATH", value => options.SupportTicketEndpointPath = value);
        ApplyString("PM365_ASSISTANT_API_KEY_ENV", value => options.ApiKeyEnvironmentVariable = value);

        var timeout = Environment.GetEnvironmentVariable("PM365_ASSISTANT_TIMEOUT_SECONDS");
        if (int.TryParse(timeout, out var timeoutSeconds) && timeoutSeconds > 0)
        {
            options.TimeoutSeconds = timeoutSeconds;
        }

        var maxAttachmentBytes = Environment.GetEnvironmentVariable("PM365_ASSISTANT_MAX_ATTACHMENT_BYTES");
        if (long.TryParse(maxAttachmentBytes, out var bytes) && bytes > 0)
        {
            options.MaxAttachmentBytes = bytes;
        }

        var fallback = Environment.GetEnvironmentVariable("PM365_ASSISTANT_FALLBACK_TO_MOCK");
        if (bool.TryParse(fallback, out var fallbackToMock))
        {
            options.FallbackToMockOnFailure = fallbackToMock;
        }
    }

    private static void ApplyString(string environmentVariable, Action<string> apply)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            apply(value);
        }
    }

    private static void Normalize(AssistantApiOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Mode))
        {
            options.Mode = "Mock";
        }

        if (string.IsNullOrWhiteSpace(options.PortalApiBaseUrl))
        {
            options.PortalApiBaseUrl = "https://pagemaker365.com";
        }

        if (string.IsNullOrWhiteSpace(options.MessageEndpointPath))
        {
            options.MessageEndpointPath = "/api/installer/assistant/messages";
        }

        if (string.IsNullOrWhiteSpace(options.AttachmentEndpointPath))
        {
            options.AttachmentEndpointPath = "/api/installer/assistant/attachments";
        }

        if (string.IsNullOrWhiteSpace(options.SupportTicketEndpointPath))
        {
            options.SupportTicketEndpointPath = "/api/installer/support-tickets";
        }

        if (string.IsNullOrWhiteSpace(options.ApiKeyEnvironmentVariable))
        {
            options.ApiKeyEnvironmentVariable = "PM365_ASSISTANT_API_KEY";
        }

        if (options.TimeoutSeconds <= 0)
        {
            options.TimeoutSeconds = 30;
        }

        if (options.MaxAttachmentBytes <= 0)
        {
            options.MaxAttachmentBytes = 10 * 1024 * 1024;
        }
    }
}
