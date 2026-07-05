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
        get
        {
            var baseUri = new Uri(PortalApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            return new Uri(baseUri, MessageEndpointPath.TrimStart('/'));
        }
    }

    public Uri AttachmentEndpoint
    {
        get
        {
            var baseUri = new Uri(PortalApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            return new Uri(baseUri, AttachmentEndpointPath.TrimStart('/'));
        }
    }

    public Uri SupportTicketEndpoint
    {
        get
        {
            var baseUri = new Uri(PortalApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            return new Uri(baseUri, SupportTicketEndpointPath.TrimStart('/'));
        }
    }
}
