namespace PageMaker365.Installer.Engine.Models;

public sealed class OnboardingApiOptions
{
    public string Mode { get; set; } = "Mock";
    public string ApiBaseUrl { get; set; } = "https://api.pagemaker365.com";
    public string ConnectEndpointPath { get; set; } = "/api/onboarding/installer/connect";
    public string DiscoveryEndpointPath { get; set; } = "/api/onboarding/installer/discovery";
    public string StatusEndpointPath { get; set; } = "/api/onboarding/installer/status";
    public string PackageEndpointPathTemplate { get; set; } = "/api/onboarding/installer/{sessionId}/install-package";
    public string ApiKeyEnvironmentVariable { get; set; } = "PM365_ONBOARDING_API_KEY";
    public int TimeoutSeconds { get; set; } = 30;
    public bool FallbackToMockOnFailure { get; set; } = true;

    public bool UseMock => !Mode.Equals("Portal", StringComparison.OrdinalIgnoreCase);

    public Uri ConnectEndpoint(OnboardingBootstrapSession session) => BuildEndpoint(session, ConnectEndpointPath);

    public Uri DiscoveryEndpoint(OnboardingBootstrapSession session) => BuildEndpoint(session, DiscoveryEndpointPath);

    public Uri StatusEndpoint(OnboardingBootstrapSession session) => BuildEndpoint(session, StatusEndpointPath);

    public Uri PackageEndpoint(OnboardingBootstrapSession session, string? packageDownloadUrl)
    {
        if (!string.IsNullOrWhiteSpace(packageDownloadUrl) &&
            Uri.TryCreate(packageDownloadUrl, UriKind.Absolute, out var packageUri))
        {
            return packageUri;
        }

        var path = PackageEndpointPathTemplate.Replace("{sessionId}", Uri.EscapeDataString(session.SessionId));
        return BuildEndpoint(session, path);
    }

    private Uri BuildEndpoint(OnboardingBootstrapSession session, string path)
    {
        var baseUrl = string.IsNullOrWhiteSpace(session.ApiBaseUrl)
            ? ApiBaseUrl
            : session.ApiBaseUrl;
        var baseUri = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, path.TrimStart('/'));
    }
}
