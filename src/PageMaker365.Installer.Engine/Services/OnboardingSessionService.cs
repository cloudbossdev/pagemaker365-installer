using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class OnboardingSessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<OnboardingBootstrapSession> LoadBootstrapAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var session = await JsonSerializer.DeserializeAsync<OnboardingBootstrapSession>(stream, JsonOptions, cancellationToken);
        return session ?? throw new InvalidOperationException("The onboarding bootstrap session could not be read.");
    }

    public ConfigValidationResult Validate(OnboardingBootstrapSession session)
    {
        var result = new ConfigValidationResult();

        Require(session.SessionId, "Onboarding session ID is required.", result);
        Require(session.CustomerName, "Customer name is required.", result);
        Require(session.PortalBaseUrl, "Portal base URL is required.", result);
        Require(session.ApiBaseUrl, "API base URL is required.", result);
        Require(session.OneTimeCode, "One-time onboarding code is required.", result);

        if (!string.IsNullOrWhiteSpace(session.PortalBaseUrl) &&
            !Uri.TryCreate(session.PortalBaseUrl, UriKind.Absolute, out _))
        {
            result.Errors.Add("Portal base URL must be an absolute URL.");
        }

        if (!string.IsNullOrWhiteSpace(session.ApiBaseUrl) &&
            !Uri.TryCreate(session.ApiBaseUrl, UriKind.Absolute, out _))
        {
            result.Errors.Add("API base URL must be an absolute URL.");
        }

        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            result.Errors.Add("The onboarding bootstrap session is expired.");
        }

        if (session.AllowedOperations.Count == 0)
        {
            result.Warnings.Add("No allowed operations are listed in the bootstrap session.");
        }

        if (!session.DiscoveryPolicy.AllowAzureDiscovery &&
            !session.DiscoveryPolicy.AllowGraphDiscovery &&
            !session.DiscoveryPolicy.AllowSharePointDiscovery)
        {
            result.Warnings.Add("All discovery providers are disabled for this bootstrap session.");
        }

        return result;
    }

    public static string ToJson(OnboardingBootstrapSession session) => JsonSerializer.Serialize(session, JsonOptions);

    public static OnboardingBootstrapSession CreateFallbackSession()
    {
        return new OnboardingBootstrapSession
        {
            SessionId = "onb_contoso_sandbox_001",
            CustomerName = "Contoso Intranet",
            ExpectedTenantId = "00000000-0000-0000-0000-000000000000",
            PortalBaseUrl = "https://pagemaker365.com",
            ApiBaseUrl = "https://api.pagemaker365.com",
            OneTimeCode = "PM365-CONTOSO-DEMO",
            RequestedBy = "admin@contoso.com",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            AllowedOperations =
            [
                "TenantDiscovery",
                "InstallPackageGeneration",
                "InstallStatusSync"
            ],
            DiscoveryPolicy =
            {
                RequiredFields =
                [
                    "tenantId",
                    "tenantName",
                    "azureSubscriptionId",
                    "sharePointSiteUrl",
                    "sharePointTenantHostname"
                ],
                ExcludedDataTypes =
                [
                    "documentContent",
                    "mailboxContent",
                    "userFiles",
                    "rawSecrets",
                    "broadUserProfileExport"
                ]
            }
        };
    }

    private static void Require(string value, string message, ConfigValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result.Errors.Add(message);
        }
    }
}
