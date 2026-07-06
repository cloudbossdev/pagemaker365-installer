using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class OnboardingApiClient : IOnboardingApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly OnboardingApiOptions _options;
    private readonly HttpClient _httpClient;
    private readonly MockOnboardingApiClient _mockClient = new();

    public OnboardingApiClient(OnboardingApiOptions options, HttpClient? httpClient = null)
    {
        _options = options;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public string ConnectionLabel => _options.UseMock
        ? _mockClient.ConnectionLabel
        : $"Portal onboarding API: {_options.ApiBaseUrl}";

    public async Task<OnboardingSessionConnection> ConnectAsync(
        OnboardingBootstrapSession session,
        CancellationToken cancellationToken = default)
    {
        if (_options.UseMock)
        {
            return await _mockClient.ConnectAsync(session, cancellationToken);
        }

        try
        {
            var request = new OnboardingSessionConnectRequest
            {
                SessionId = session.SessionId,
                OneTimeCode = session.OneTimeCode,
                RequestedBy = session.RequestedBy,
                CustomerName = session.CustomerName
            };
            var response = await PostJsonAsync<OnboardingSessionConnectRequest, OnboardingSessionConnection>(
                _options.ConnectEndpoint(session),
                request,
                session,
                cancellationToken);
            response.SessionId = string.IsNullOrWhiteSpace(response.SessionId) ? session.SessionId : response.SessionId;
            response.Status = string.IsNullOrWhiteSpace(response.Status) ? "Connected" : response.Status;
            return response;
        }
        catch (Exception exception) when (ShouldFallback(exception))
        {
            var response = await _mockClient.ConnectAsync(session, cancellationToken);
            response.Message = $"Portal onboarding API connect failed; using local mock fallback. {response.Message}";
            return response;
        }
    }

    public async Task<OnboardingDiscoverySubmission> SubmitDiscoveryAsync(
        OnboardingBootstrapSession session,
        TenantDiscoveryResult discovery,
        CancellationToken cancellationToken = default)
    {
        if (_options.UseMock)
        {
            return await _mockClient.SubmitDiscoveryAsync(session, discovery, cancellationToken);
        }

        try
        {
            var request = new OnboardingDiscoverySubmitRequest
            {
                SessionId = session.SessionId,
                OneTimeCode = session.OneTimeCode,
                Discovery = discovery
            };
            var response = await PostJsonAsync<OnboardingDiscoverySubmitRequest, OnboardingDiscoverySubmission>(
                _options.DiscoveryEndpoint(session),
                request,
                session,
                cancellationToken);
            response.SessionId = string.IsNullOrWhiteSpace(response.SessionId) ? session.SessionId : response.SessionId;
            response.DiscoveryId = string.IsNullOrWhiteSpace(response.DiscoveryId) ? discovery.DiscoveryId : response.DiscoveryId;
            response.Status = string.IsNullOrWhiteSpace(response.Status) ? "Accepted" : response.Status;
            return response;
        }
        catch (Exception exception) when (ShouldFallback(exception))
        {
            var response = await _mockClient.SubmitDiscoveryAsync(session, discovery, cancellationToken);
            response.Message = $"Portal onboarding API discovery sync failed; using local mock fallback. {response.Message}";
            return response;
        }
    }

    public async Task<OnboardingPortalStatus> GetOnboardingStatusAsync(
        OnboardingBootstrapSession session,
        TenantDiscoveryResult? discovery,
        CustomerInstallConfig? config,
        CancellationToken cancellationToken = default)
    {
        if (_options.UseMock)
        {
            return await _mockClient.GetOnboardingStatusAsync(session, discovery, config, cancellationToken);
        }

        try
        {
            var request = new OnboardingStatusRequest
            {
                SessionId = session.SessionId,
                OneTimeCode = session.OneTimeCode,
                Discovery = discovery,
                LoadedPackage = CreatePackageContext(config)
            };
            var response = await PostJsonAsync<OnboardingStatusRequest, OnboardingPortalStatus>(
                _options.StatusEndpoint(session),
                request,
                session,
                cancellationToken);
            response.SessionId = string.IsNullOrWhiteSpace(response.SessionId) ? session.SessionId : response.SessionId;
            response.CustomerName = string.IsNullOrWhiteSpace(response.CustomerName) ? session.CustomerName : response.CustomerName;
            response.Status = string.IsNullOrWhiteSpace(response.Status) ? "Unknown" : response.Status;
            return response;
        }
        catch (Exception exception) when (ShouldFallback(exception))
        {
            var response = await _mockClient.GetOnboardingStatusAsync(session, discovery, config, cancellationToken);
            response.Message = $"Portal onboarding API status check failed; using local mock fallback. {response.Message}";
            return response;
        }
    }

    public async Task<string> SaveStatusAsync(
        OnboardingPortalStatus status,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        if (_options.UseMock)
        {
            return await _mockClient.SaveStatusAsync(status, outputRoot, cancellationToken);
        }

        var directory = Path.Combine(outputRoot, "onboarding", status.SessionId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "portal-status.json");
        var json = JsonSerializer.Serialize(status, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    public async Task<OnboardingPackageDownloadResult> DownloadPackageAsync(
        OnboardingBootstrapSession session,
        OnboardingPackageReadiness readiness,
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        if (_options.UseMock)
        {
            return await _mockClient.DownloadPackageAsync(session, readiness, workspaceRoot, cancellationToken);
        }

        try
        {
            return await DownloadPackageFromPortalAsync(session, readiness, workspaceRoot, cancellationToken);
        }
        catch (Exception exception) when (ShouldFallback(exception))
        {
            var response = await _mockClient.DownloadPackageAsync(session, readiness, workspaceRoot, cancellationToken);
            response.Message = $"Portal onboarding API package download failed; using local mock fallback. {response.Message}";
            return response;
        }
    }

    private async Task<OnboardingPackageDownloadResult> DownloadPackageFromPortalAsync(
        OnboardingBootstrapSession session,
        OnboardingPackageReadiness readiness,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (!readiness.Status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
        {
            return new OnboardingPackageDownloadResult
            {
                Status = "NotReady",
                SessionId = session.SessionId,
                PackageVersion = readiness.PackageVersion,
                Message = "Package is not ready for download."
            };
        }

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            _options.PackageEndpoint(session, readiness.PackageDownloadUrl));
        ApplyAuthorization(httpRequest, session);

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = response.IsSuccessStatusCode ? "" : await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Onboarding package API returned {(int)response.StatusCode}: {body}");
        }

        var outputDirectory = Path.Combine(workspaceRoot, "support-bundle", "onboarding", session.SessionId, "generated-package");
        Directory.CreateDirectory(outputDirectory);
        var packagePath = Path.Combine(outputDirectory, SafeFileName(GetDownloadFileName(response, session)));

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = File.Create(packagePath))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        return new OnboardingPackageDownloadResult
        {
            Status = "Downloaded",
            SessionId = session.SessionId,
            PackagePath = packagePath,
            PackageVersion = readiness.PackageVersion,
            CorrelationId = GetCorrelationId(response),
            Message = "Generated install package downloaded from the PageMaker365 portal."
        };
    }

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        Uri endpoint,
        TRequest request,
        OnboardingBootstrapSession session,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };
        ApplyAuthorization(httpRequest, session);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Onboarding API returned {(int)response.StatusCode}: {body}");
        }

        var result = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
        return result ?? throw new InvalidOperationException("Onboarding API returned an empty response.");
    }

    private void ApplyAuthorization(HttpRequestMessage httpRequest, OnboardingBootstrapSession session)
    {
        var apiKey = Environment.GetEnvironmentVariable(_options.ApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        if (!string.IsNullOrWhiteSpace(session.SessionId))
        {
            httpRequest.Headers.TryAddWithoutValidation("X-PM365-Onboarding-Session", session.SessionId);
        }

        if (!string.IsNullOrWhiteSpace(session.OneTimeCode))
        {
            httpRequest.Headers.TryAddWithoutValidation("X-PM365-Onboarding-Code", session.OneTimeCode);
        }
    }

    private bool ShouldFallback(Exception exception)
    {
        return _options.FallbackToMockOnFailure && exception is not OperationCanceledException;
    }

    private static OnboardingPackageContext? CreatePackageContext(CustomerInstallConfig? config)
    {
        if (config is null)
        {
            return null;
        }

        return new OnboardingPackageContext
        {
            TenantId = First(config.Customer.TenantId, config.Azure.TenantId),
            TenantName = config.Customer.TenantName,
            AzureSubscriptionId = config.Azure.SubscriptionId,
            AzureLocation = config.Azure.Location,
            ResourceGroupName = config.Azure.ResourceGroupName,
            SharePointSiteUrl = config.SharePoint.SiteUrl,
            SharePointTenantHostname = HostFromSharePointUrl(config.SharePoint.SiteUrl),
            PrimaryContact = config.Customer.PrimaryContact,
            EnvironmentId = config.ControlPlane.EnvironmentId,
            PackageHash = config.ControlPlane.PackageHash
        };
    }

    private static string GetDownloadFileName(HttpResponseMessage response, OnboardingBootstrapSession session)
    {
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName;
        fileName = fileName?.Trim('"');
        return string.IsNullOrWhiteSpace(fileName)
            ? $"{session.SessionId}.customer.install.json"
            : fileName;
    }

    private static string GetCorrelationId(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("X-Correlation-ID", out var values)
            ? values.FirstOrDefault() ?? ""
            : "";
    }

    private static string First(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static string HostFromSharePointUrl(string? siteUrl)
    {
        return Uri.TryCreate(siteUrl, UriKind.Absolute, out var uri) ? uri.Host : "";
    }

    private static string SafeFileName(string fileName)
    {
        return string.Concat(fileName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
    }
}
