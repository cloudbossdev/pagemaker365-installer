using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class OnboardingApiClient : IOnboardingApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions PrettyJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly CustomerConfigService ConfigService = new();

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
                "connect",
                _options.ConnectEndpoint(session),
                request,
                session,
                cancellationToken,
                root => EnsureRequiredJsonFields(
                    "connect",
                    _options.ConnectEndpoint(session),
                    root,
                    "status",
                    "sessionId",
                    "correlationId"));
            EnsureSessionMatches("connect", _options.ConnectEndpoint(session), session, response.SessionId);
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
                "discovery sync",
                _options.DiscoveryEndpoint(session),
                request,
                session,
                cancellationToken,
                root => EnsureRequiredJsonFields(
                    "discovery sync",
                    _options.DiscoveryEndpoint(session),
                    root,
                    "status",
                    "sessionId",
                    "discoveryId",
                    "correlationId"));
            EnsureSessionMatches("discovery sync", _options.DiscoveryEndpoint(session), session, response.SessionId);
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
                "status check",
                _options.StatusEndpoint(session),
                request,
                session,
                cancellationToken,
                root => ValidateStatusJson(_options.StatusEndpoint(session), root));
            EnsureSessionMatches("status check", _options.StatusEndpoint(session), session, response.SessionId);
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
        var json = JsonSerializer.Serialize(status, PrettyJsonOptions);
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

        var endpoint = _options.PackageEndpoint(session, readiness.PackageDownloadUrl);
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var correlationId = GetCorrelationId(response, body);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException("package download", endpoint, response.StatusCode, body, correlationId);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!IsJsonMediaType(mediaType))
        {
            throw new OnboardingApiException(
                $"Portal package download returned unsupported content type '{mediaType ?? "none"}'. Expected application/json.",
                endpoint,
                response.StatusCode,
                correlationId);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new OnboardingApiException(
                "Portal package download returned an empty package.",
                endpoint,
                response.StatusCode,
                correlationId);
        }

        ValidateDownloadedPackage(body, endpoint, response.StatusCode, correlationId);

        var outputDirectory = Path.Combine(workspaceRoot, "support-bundle", "onboarding", session.SessionId, "generated-package");
        Directory.CreateDirectory(outputDirectory);
        var packagePath = Path.Combine(outputDirectory, SafeFileName(GetDownloadFileName(response, session)));

        await File.WriteAllTextAsync(packagePath, body, Encoding.UTF8, cancellationToken);

        return new OnboardingPackageDownloadResult
        {
            Status = "Downloaded",
            SessionId = session.SessionId,
            PackagePath = packagePath,
            PackageVersion = readiness.PackageVersion,
            CorrelationId = correlationId,
            Message = "Generated install package downloaded from the PageMaker365 portal."
        };
    }

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        string operation,
        Uri endpoint,
        TRequest request,
        OnboardingBootstrapSession session,
        CancellationToken cancellationToken,
        Action<JsonElement>? validateJson = null)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };
        ApplyAuthorization(httpRequest, session);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var correlationId = GetCorrelationId(response, body);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(operation, endpoint, response.StatusCode, body, correlationId);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new OnboardingApiException(
                $"Portal onboarding API {operation} returned an empty response.",
                endpoint,
                response.StatusCode,
                correlationId);
        }

        using var document = ParseJsonDocument(operation, endpoint, response.StatusCode, body, correlationId);
        validateJson?.Invoke(document.RootElement);

        try
        {
            var result = document.RootElement.Deserialize<TResponse>(JsonOptions);
            return result ?? throw new OnboardingApiException(
                $"Portal onboarding API {operation} returned an empty response.",
                endpoint,
                response.StatusCode,
                correlationId);
        }
        catch (JsonException exception)
        {
            throw new OnboardingApiException(
                $"Portal onboarding API {operation} returned invalid JSON. {exception.Message}",
                endpoint,
                response.StatusCode,
                correlationId,
                exception);
        }
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
            DeploymentExportId = config.ControlPlane.DeploymentExportId,
            PackageHashAlgorithm = config.ControlPlane.PackageHashAlgorithm,
            PackageHash = config.ControlPlane.PackageHash,
            TrustMode = config.ControlPlane.TrustMode
        };
    }

    private static void ValidateStatusJson(Uri endpoint, JsonElement root)
    {
        EnsureRequiredJsonFields(
            "status check",
            endpoint,
            root,
            "status",
            "sessionId",
            "correlationId",
            "packageReadiness",
            "packageReadiness.status");

        if (TryGetJsonPath(root, "packageReadiness.status", out var readinessStatus) &&
            readinessStatus.ValueKind == JsonValueKind.String &&
            readinessStatus.GetString()?.Equals("Ready", StringComparison.OrdinalIgnoreCase) == true &&
            IsJsonPathMissingOrBlank(root, "packageReadiness.packageDownloadUrl"))
        {
            throw new OnboardingApiException(
                "Portal onboarding API status check response is missing required field(s): packageReadiness.packageDownloadUrl.",
                endpoint);
        }
    }

    private static void EnsureSessionMatches(
        string operation,
        Uri endpoint,
        OnboardingBootstrapSession session,
        string responseSessionId)
    {
        if (string.IsNullOrWhiteSpace(session.SessionId) ||
            responseSessionId.Equals(session.SessionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new OnboardingApiException(
            $"Portal onboarding API {operation} returned session '{responseSessionId}' for expected session '{session.SessionId}'.",
            endpoint);
    }

    private static void EnsureRequiredJsonFields(
        string operation,
        Uri endpoint,
        JsonElement root,
        params string[] propertyPaths)
    {
        var missing = propertyPaths.Where(path => IsJsonPathMissingOrBlank(root, path)).ToArray();
        if (missing.Length > 0)
        {
            throw new OnboardingApiException(
                $"Portal onboarding API {operation} response is missing required field(s): {string.Join(", ", missing)}.",
                endpoint);
        }
    }

    private static bool IsJsonPathMissingOrBlank(JsonElement root, string propertyPath)
    {
        if (!TryGetJsonPath(root, propertyPath, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        return value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString());
    }

    private static bool TryGetJsonPath(JsonElement root, string propertyPath, out JsonElement value)
    {
        value = root;
        foreach (var propertyName in propertyPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                value = default;
                return false;
            }

            var found = false;
            foreach (var property in value.EnumerateObject())
            {
                if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = property.Value;
                found = true;
                break;
            }

            if (!found)
            {
                value = default;
                return false;
            }
        }

        return true;
    }

    private static JsonDocument ParseJsonDocument(
        string operation,
        Uri endpoint,
        HttpStatusCode? statusCode,
        string body,
        string correlationId)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new OnboardingApiException(
                $"Portal onboarding API {operation} returned invalid JSON. {exception.Message}",
                endpoint,
                statusCode,
                correlationId,
                exception);
        }
    }

    private static void ValidateDownloadedPackage(
        string body,
        Uri endpoint,
        HttpStatusCode statusCode,
        string correlationId)
    {
        CustomerInstallConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<CustomerInstallConfig>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new OnboardingApiException(
                $"Generated package returned by the portal is not valid JSON. {exception.Message}",
                endpoint,
                statusCode,
                correlationId,
                exception);
        }

        if (config is null)
        {
            throw new OnboardingApiException(
                "Generated package returned by the portal is empty.",
                endpoint,
                statusCode,
                correlationId);
        }

        var validation = ConfigService.Validate(config, body);
        if (!validation.IsValid)
        {
            throw new OnboardingApiException(
                $"Generated package returned by the portal failed validation: {string.Join(" ", validation.Errors)}",
                endpoint,
                statusCode,
                correlationId);
        }
    }

    private static bool IsJsonMediaType(string? mediaType)
    {
        return !string.IsNullOrWhiteSpace(mediaType) &&
            (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
                mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }

    private static OnboardingApiException CreateApiException(
        string operation,
        Uri endpoint,
        HttpStatusCode statusCode,
        string body,
        string correlationId)
    {
        var detail = ExtractApiErrorDetail(body, ref correlationId);
        var reason = string.IsNullOrWhiteSpace(detail) ? body.Trim() : detail;
        var message = string.IsNullOrWhiteSpace(reason)
            ? $"Portal onboarding API {operation} returned {(int)statusCode} {statusCode}."
            : $"Portal onboarding API {operation} returned {(int)statusCode} {statusCode}: {reason}";

        return new OnboardingApiException(message, endpoint, statusCode, correlationId);
    }

    private static string ExtractApiErrorDetail(string body, ref string correlationId)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (string.IsNullOrWhiteSpace(correlationId) &&
                TryGetJsonPath(root, "correlationId", out var correlationProperty) &&
                correlationProperty.ValueKind == JsonValueKind.String)
            {
                correlationId = correlationProperty.GetString() ?? "";
            }

            foreach (var path in new[] { "message", "error", "details", "code" })
            {
                if (TryGetJsonPath(root, path, out var property) &&
                    property.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(property.GetString()))
                {
                    return property.GetString() ?? "";
                }
            }
        }
        catch (JsonException)
        {
            return body.Trim();
        }

        return body.Trim();
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

    private static string GetCorrelationId(HttpResponseMessage response, string body = "")
    {
        if (response.Headers.TryGetValues("X-Correlation-ID", out var values))
        {
            return values.FirstOrDefault() ?? "";
        }

        var correlationId = "";
        ExtractApiErrorDetail(body, ref correlationId);
        return correlationId;
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
