using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class MockOnboardingApiClient : IOnboardingApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string ConnectionLabel => "Local mock onboarding API";

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

    public Task<OnboardingPortalStatus> GetOnboardingStatusAsync(
        OnboardingBootstrapSession session,
        TenantDiscoveryResult? discovery,
        CustomerInstallConfig? config,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var portalBaseUrl = string.IsNullOrWhiteSpace(session.PortalBaseUrl)
            ? "https://pagemaker365.com"
            : session.PortalBaseUrl.TrimEnd('/');
        var missingFields = GetMissingFields(session, discovery, config);
        var ready = missingFields.Count == 0;
        var readinessStatus = ready ? "Ready" : "NeedsCustomerInput";

        return Task.FromResult(new OnboardingPortalStatus
        {
            SessionId = session.SessionId,
            CustomerName = session.CustomerName,
            Status = readinessStatus,
            PortalRecordUrl = $"{portalBaseUrl}/admin/onboarding/{session.SessionId}",
            CorrelationId = $"mock-status-{Guid.NewGuid():N}",
            Message = ready
                ? "Mock portal has enough onboarding data to generate the install package."
                : "Mock portal is waiting for required onboarding fields before package generation.",
            MissingFields = missingFields,
            PackageReadiness = new OnboardingPackageReadiness
            {
                Status = ready ? "Ready" : "NeedsCustomerInput",
                PackageVersion = ready ? "0.2-mock" : "",
                PackageDownloadUrl = ready
                    ? $"{portalBaseUrl}/api/onboarding/installer/{session.SessionId}/install-package"
                    : "",
                ReadyAt = ready ? DateTimeOffset.UtcNow : null,
                Message = ready
                    ? "Generated package is ready for installer download."
                    : "Complete missing fields in the portal or sync discovery values from the installer."
            }
        });
    }

    public async Task<string> SaveStatusAsync(
        OnboardingPortalStatus status,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(outputRoot, "onboarding", status.SessionId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "portal-status.mock.json");
        var json = JsonSerializer.Serialize(status, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    public Task<string> SaveMockStatusAsync(
        OnboardingPortalStatus status,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        return SaveStatusAsync(status, outputRoot, cancellationToken);
    }

    public async Task<OnboardingPackageDownloadResult> DownloadPackageAsync(
        OnboardingBootstrapSession session,
        OnboardingPackageReadiness readiness,
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!readiness.Status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
        {
            return new OnboardingPackageDownloadResult
            {
                Status = "NotReady",
                SessionId = session.SessionId,
                PackageVersion = readiness.PackageVersion,
                CorrelationId = $"mock-download-{Guid.NewGuid():N}",
                Message = "Package is not ready for download."
            };
        }

        var sourcePackage = Path.Combine(workspaceRoot, "samples", "contoso.customer.install.json");
        if (!File.Exists(sourcePackage))
        {
            throw new FileNotFoundException("Mock package source was not found.", sourcePackage);
        }

        var outputDirectory = Path.Combine(workspaceRoot, "support-bundle", "onboarding", session.SessionId, "generated-package");
        Directory.CreateDirectory(outputDirectory);
        var packagePath = Path.Combine(outputDirectory, $"{session.SessionId}.customer.install.json");

        await using (var source = File.OpenRead(sourcePackage))
        await using (var target = File.Create(packagePath))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        return new OnboardingPackageDownloadResult
        {
            Status = "Downloaded",
            SessionId = session.SessionId,
            PackagePath = packagePath,
            PackageVersion = string.IsNullOrWhiteSpace(readiness.PackageVersion) ? "0.2-mock" : readiness.PackageVersion,
            CorrelationId = $"mock-download-{Guid.NewGuid():N}",
            Message = "Mock generated package copied locally and ready to load."
        };
    }

    private static List<OnboardingMissingField> GetMissingFields(
        OnboardingBootstrapSession session,
        TenantDiscoveryResult? discovery,
        CustomerInstallConfig? config)
    {
        IReadOnlyList<string> required = session.DiscoveryPolicy.RequiredFields.Count == 0
            ? new[] { "tenantId", "tenantName", "azureSubscriptionId", "sharePointSiteUrl", "sharePointTenantHostname" }
            : session.DiscoveryPolicy.RequiredFields;

        return required
            .Where(field => string.IsNullOrWhiteSpace(GetFieldValue(field, discovery, config, session)))
            .Select(field => new OnboardingMissingField
            {
                FieldKey = field,
                Label = LabelForField(field),
                Source = "InstallerDiscovery",
                Notes = "Provide this in the portal or run discovery/sync from the installer."
            })
            .ToList();
    }

    private static string GetFieldValue(
        string field,
        TenantDiscoveryResult? discovery,
        CustomerInstallConfig? config,
        OnboardingBootstrapSession session)
    {
        return field switch
        {
            "tenantId" => First(discovery?.Customer.TenantId, config?.Customer.TenantId, session.ExpectedTenantId),
            "tenantName" => First(discovery?.Customer.TenantName, config?.Customer.TenantName, session.CustomerName),
            "azureSubscriptionId" => First(discovery?.Azure.SelectedSubscriptionId, config?.Azure.SubscriptionId),
            "sharePointSiteUrl" => First(discovery?.SharePoint.SiteUrl, config?.SharePoint.SiteUrl),
            "sharePointTenantHostname" => First(discovery?.SharePoint.TenantHostname, HostFromSharePointUrl(config?.SharePoint.SiteUrl)),
            "resourceGroupName" => First(discovery?.Azure.TargetResourceGroupName, config?.Azure.ResourceGroupName),
            "azureLocation" => First(discovery?.Azure.RecommendedLocation, config?.Azure.Location),
            "primaryContact" => First(discovery?.Customer.PrimaryContact, config?.Customer.PrimaryContact),
            _ => ""
        };
    }

    private static string First(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static string LabelForField(string field)
    {
        return field switch
        {
            "tenantId" => "Tenant ID",
            "tenantName" => "Tenant name",
            "azureSubscriptionId" => "Azure subscription ID",
            "sharePointSiteUrl" => "SharePoint site URL",
            "sharePointTenantHostname" => "SharePoint tenant hostname",
            "resourceGroupName" => "Azure resource group",
            "azureLocation" => "Azure location",
            "primaryContact" => "Primary contact",
            _ => field
        };
    }

    private static string HostFromSharePointUrl(string? siteUrl)
    {
        return Uri.TryCreate(siteUrl, UriKind.Absolute, out var uri) ? uri.Host : "";
    }
}
