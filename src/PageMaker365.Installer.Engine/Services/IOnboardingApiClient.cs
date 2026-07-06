using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public interface IOnboardingApiClient
{
    string ConnectionLabel { get; }

    Task<OnboardingSessionConnection> ConnectAsync(
        OnboardingBootstrapSession session,
        CancellationToken cancellationToken = default);

    Task<OnboardingDiscoverySubmission> SubmitDiscoveryAsync(
        OnboardingBootstrapSession session,
        TenantDiscoveryResult discovery,
        CancellationToken cancellationToken = default);

    Task<OnboardingPortalStatus> GetOnboardingStatusAsync(
        OnboardingBootstrapSession session,
        TenantDiscoveryResult? discovery,
        CustomerInstallConfig? config,
        CancellationToken cancellationToken = default);

    Task<string> SaveStatusAsync(
        OnboardingPortalStatus status,
        string outputRoot,
        CancellationToken cancellationToken = default);

    Task<OnboardingPackageDownloadResult> DownloadPackageAsync(
        OnboardingBootstrapSession session,
        OnboardingPackageReadiness readiness,
        string workspaceRoot,
        CancellationToken cancellationToken = default);
}
