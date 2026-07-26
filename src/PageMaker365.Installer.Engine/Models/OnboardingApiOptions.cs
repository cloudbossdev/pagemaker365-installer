namespace PageMaker365.Installer.Engine.Models;

public sealed class OnboardingApiOptions
{
    public string Mode { get; set; } = "Mock";
    public string ApiBaseUrl { get; set; } = "https://api.pagemaker365.com";
    public string ConnectEndpointPath { get; set; } = "/api/onboarding/installer/connect";
    public string DiscoveryEndpointPath { get; set; } = "/api/onboarding/installer/discovery";
    public string StatusEndpointPath { get; set; } = "/api/onboarding/installer/status";
    public string EvidenceEndpointPath { get; set; } = "/api/onboarding/installer/evidence";
    public string PackageEndpointPathTemplate { get; set; } = "/api/onboarding/installer/{sessionId}/install-package";
    public string ApiKeyEnvironmentVariable { get; set; } = "PM365_ONBOARDING_API_KEY";
    public int TimeoutSeconds { get; set; } = 30;
    public bool FallbackToMockOnFailure { get; set; }

    public bool UseMock => !Mode.Equals("Portal", StringComparison.OrdinalIgnoreCase);

    public Uri ConnectEndpoint(OnboardingBootstrapSession session) => BuildEndpoint(session, ConnectEndpointPath);

    public Uri DiscoveryEndpoint(OnboardingBootstrapSession session) => BuildEndpoint(session, DiscoveryEndpointPath);

    public Uri StatusEndpoint(OnboardingBootstrapSession session) => BuildEndpoint(session, StatusEndpointPath);

    public Uri EvidenceEndpoint(OnboardingBootstrapSession session) => BuildEndpoint(session, EvidenceEndpointPath);

    public Uri PackageEndpoint(OnboardingBootstrapSession session, string? packageDownloadUrl)
    {
        var path = PackageEndpointPathTemplate.Replace("{sessionId}", Uri.EscapeDataString(session.SessionId));
        var defaultEndpoint = BuildEndpoint(session, path);
        if (!string.IsNullOrWhiteSpace(packageDownloadUrl) &&
            Uri.TryCreate(packageDownloadUrl, UriKind.Absolute, out var packageUri) &&
            SameOrigin(packageUri, defaultEndpoint))
        {
            return packageUri;
        }

        return defaultEndpoint;
    }

    private Uri BuildEndpoint(OnboardingBootstrapSession session, string path)
    {
        var baseUrl = string.IsNullOrWhiteSpace(session.ApiBaseUrl)
            ? ApiBaseUrl
            : session.ApiBaseUrl;
        var validatedBaseUri = PageMaker365.Installer.Engine.Services.TrustedPageMaker365EndpointPolicy.ValidateBaseUrl(
            baseUrl,
            "Portal onboarding API base URL");
        var baseUri = new Uri(validatedBaseUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, path.TrimStart('/'));
    }

    private static bool SameOrigin(Uri left, Uri right)
    {
        return left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase) &&
            left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase) &&
            left.Port == right.Port;
    }
}
