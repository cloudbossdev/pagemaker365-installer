using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class MockOnboardingApiClient
{
    public Task<OnboardingSessionConnection> ConnectAsync(
        OnboardingBootstrapSession session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OnboardingSessionConnection
        {
            Status = "ConnectedMock",
            SessionId = session.SessionId,
            CorrelationId = $"mock-connect-{Guid.NewGuid():N}",
            Message = $"Connected to mock onboarding session for {session.CustomerName}. No network request was made."
        });
    }

    public Task<OnboardingDiscoverySubmission> SubmitDiscoveryAsync(
        OnboardingBootstrapSession session,
        TenantDiscoveryResult discovery,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var portalBaseUrl = string.IsNullOrWhiteSpace(session.PortalBaseUrl)
            ? "https://pagemaker365.com"
            : session.PortalBaseUrl.TrimEnd('/');

        return Task.FromResult(new OnboardingDiscoverySubmission
        {
            Status = "AcceptedMock",
            SessionId = session.SessionId,
            DiscoveryId = discovery.DiscoveryId,
            CorrelationId = $"mock-submit-{Guid.NewGuid():N}",
            PortalRecordUrl = $"{portalBaseUrl}/admin/onboarding/{session.SessionId}",
            Message = "Discovery payload accepted by the mock PageMaker365 API client. No network request was made."
        });
    }
}
