namespace PageMaker365.Installer.Engine.Models;

public sealed class AssistantApiOptions
{
    public string Mode { get; set; } = "Mock";
    public string PortalApiBaseUrl { get; set; } = "https://pagemaker365.com";
    public string MessageEndpointPath { get; set; } = "/api/installer/assistant/messages";
    public string AttachmentEndpointPath { get; set; } = "/api/installer/assistant/attachments";
    public string SupportTicketEndpointPath { get; set; } = "/api/installer/support-tickets";
    public string ApiKeyEnvironmentVariable { get; set; } = "PM365_ASSISTANT_API_KEY";
    public int TimeoutSeconds { get; set; } = 30;
    public long MaxAttachmentBytes { get; set; } = 10 * 1024 * 1024;
    public bool FallbackToMockOnFailure { get; set; } = true;

    public bool UseMock => !Mode.Equals("Portal", StringComparison.OrdinalIgnoreCase);

    public Uri MessageEndpoint
    {
        get => ResolveEndpoint(MessageEndpointPath, "assistant message endpoint");
    }

    public Uri AttachmentEndpoint
    {
        get => ResolveEndpoint(AttachmentEndpointPath, "assistant attachment endpoint");
    }

    public Uri SupportTicketEndpoint
    {
        get => ResolveEndpoint(SupportTicketEndpointPath, "assistant support-ticket endpoint");
    }

    private Uri ResolveEndpoint(string path, string label)
    {
        var baseUri = PageMaker365.Installer.Engine.Services.TrustedPageMaker365EndpointPolicy.ValidateBaseUrl(
            PortalApiBaseUrl,
            "Assistant API base URL");
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith("/", StringComparison.Ordinal) ||
            Uri.TryCreate(path, UriKind.Absolute, out _))
        {
            throw new InvalidDataException($"The {label} must be a root-relative path.");
        }

        var endpoint = new Uri(baseUri, path);
        if (!endpoint.Scheme.Equals(baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !endpoint.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
            endpoint.Port != baseUri.Port)
        {
            throw new InvalidDataException($"The {label} must use the configured trusted PageMaker365 origin.");
        }

        return endpoint;
    }
}
