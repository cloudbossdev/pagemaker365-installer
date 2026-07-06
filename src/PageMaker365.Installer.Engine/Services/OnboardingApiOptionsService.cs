using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class OnboardingApiOptionsService
{
    public OnboardingApiOptions Load(string workspaceRoot)
    {
        var options = LoadFromFile(workspaceRoot) ?? new OnboardingApiOptions();
        ApplyEnvironmentOverrides(options);
        Normalize(options);
        return options;
    }

    private static OnboardingApiOptions? LoadFromFile(string workspaceRoot)
    {
        foreach (var path in EnumerateCandidatePaths(workspaceRoot))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<OnboardingApiOptions>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string workspaceRoot)
    {
        yield return Path.Combine(workspaceRoot, "onboarding-api.json");
        yield return Path.Combine(workspaceRoot, "config", "onboarding-api.json");
        yield return Path.Combine(AppContext.BaseDirectory, "onboarding-api.json");
    }

    private static void ApplyEnvironmentOverrides(OnboardingApiOptions options)
    {
        ApplyString("PM365_ONBOARDING_MODE", value => options.Mode = value);
        ApplyString("PM365_ONBOARDING_API_BASE_URL", value => options.ApiBaseUrl = value);
        ApplyString("PM365_ONBOARDING_CONNECT_ENDPOINT_PATH", value => options.ConnectEndpointPath = value);
        ApplyString("PM365_ONBOARDING_DISCOVERY_ENDPOINT_PATH", value => options.DiscoveryEndpointPath = value);
        ApplyString("PM365_ONBOARDING_STATUS_ENDPOINT_PATH", value => options.StatusEndpointPath = value);
        ApplyString("PM365_ONBOARDING_PACKAGE_ENDPOINT_TEMPLATE", value => options.PackageEndpointPathTemplate = value);
        ApplyString("PM365_ONBOARDING_API_KEY_ENV", value => options.ApiKeyEnvironmentVariable = value);

        var timeout = Environment.GetEnvironmentVariable("PM365_ONBOARDING_TIMEOUT_SECONDS");
        if (int.TryParse(timeout, out var timeoutSeconds) && timeoutSeconds > 0)
        {
            options.TimeoutSeconds = timeoutSeconds;
        }

        var fallback = Environment.GetEnvironmentVariable("PM365_ONBOARDING_FALLBACK_TO_MOCK");
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

    private static void Normalize(OnboardingApiOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Mode))
        {
            options.Mode = "Mock";
        }

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            options.ApiBaseUrl = "https://api.pagemaker365.com";
        }

        if (string.IsNullOrWhiteSpace(options.ConnectEndpointPath))
        {
            options.ConnectEndpointPath = "/api/onboarding/installer/connect";
        }

        if (string.IsNullOrWhiteSpace(options.DiscoveryEndpointPath))
        {
            options.DiscoveryEndpointPath = "/api/onboarding/installer/discovery";
        }

        if (string.IsNullOrWhiteSpace(options.StatusEndpointPath))
        {
            options.StatusEndpointPath = "/api/onboarding/installer/status";
        }

        if (string.IsNullOrWhiteSpace(options.PackageEndpointPathTemplate))
        {
            options.PackageEndpointPathTemplate = "/api/onboarding/installer/{sessionId}/install-package";
        }

        if (string.IsNullOrWhiteSpace(options.ApiKeyEnvironmentVariable))
        {
            options.ApiKeyEnvironmentVariable = "PM365_ONBOARDING_API_KEY";
        }

        if (options.TimeoutSeconds <= 0)
        {
            options.TimeoutSeconds = 30;
        }
    }
}
