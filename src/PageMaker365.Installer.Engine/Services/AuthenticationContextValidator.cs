using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public static class AuthenticationContextValidator
{
    public static InstallerStepResult? ValidateAzureSignIn(
        CustomerInstallConfig config,
        IReadOnlyCollection<InstallerStepResult> results)
    {
        var completed = results.LastOrDefault(result =>
            result.Code.Equals("AzureSignInCompleted", StringComparison.OrdinalIgnoreCase) &&
            result.Status == InstallStatus.Passed);
        if (completed is null)
        {
            return Failure(
                "AzureSignInContextMissing",
                "Azure sign-in did not return a verified tenant and subscription context.",
                "Retry Azure sign-in and confirm the target tenant and subscription before continuing.");
        }

        completed.Data.TryGetValue("tenantId", out var actualTenantId);
        completed.Data.TryGetValue("subscriptionId", out var actualSubscriptionId);
        var expectedTenantId = FirstNonPlaceholder(config.Azure.TenantId, config.Customer.TenantId);
        var expectedSubscriptionId = NormalizeExpectedIdentifier(config.Azure.SubscriptionId);

        if (!MatchesExpectedIdentifier(expectedTenantId, actualTenantId))
        {
            return Failure(
                "AzureTenantMismatch",
                "The signed-in Azure tenant does not match the customer package.",
                $"Expected tenant {Display(expectedTenantId)} but Azure returned {Display(actualTenantId)}.");
        }

        if (!MatchesExpectedIdentifier(expectedSubscriptionId, actualSubscriptionId))
        {
            return Failure(
                "AzureSubscriptionMismatch",
                "The signed-in Azure subscription does not match the customer package.",
                $"Expected subscription {Display(expectedSubscriptionId)} but Azure returned {Display(actualSubscriptionId)}.");
        }

        return null;
    }

    public static InstallerStepResult? ValidateGraphSignIn(
        CustomerInstallConfig config,
        GraphSignInResult result,
        DateTimeOffset? now = null)
    {
        if (!result.HasAccessToken)
        {
            return Failure(
                "GraphAccessTokenMissing",
                "Microsoft Graph sign-in did not return an access token.",
                "Retry Graph sign-in and complete the Microsoft device-login flow.");
        }

        var currentTime = now ?? DateTimeOffset.UtcNow;
        if (result.ExpiresOn == default || result.ExpiresOn <= currentTime)
        {
            return Failure(
                "GraphAccessTokenExpired",
                "The Microsoft Graph sign-in token is expired.",
                "Retry Graph sign-in to obtain a current in-memory token.");
        }

        var expectedTenantId = FirstNonPlaceholder(config.Customer.TenantId, config.Azure.TenantId);
        if (!MatchesExpectedIdentifier(expectedTenantId, result.TenantId))
        {
            return Failure(
                "GraphTenantMismatch",
                "The signed-in Microsoft Graph tenant does not match the customer package.",
                $"Expected tenant {Display(expectedTenantId)} but Microsoft Graph returned {Display(result.TenantId)}.");
        }

        var missingScopes = GraphDeviceCodeAuthenticator.RequiredScopes
            .Where(required => !result.Scopes.Contains(required, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (missingScopes.Length > 0)
        {
            return Failure(
                "GraphConsentScopesMissing",
                "The Microsoft Graph sign-in is missing required scopes.",
                "Missing scopes: " + string.Join(", ", missingScopes));
        }

        return null;
    }

    private static InstallerStepResult Failure(string code, string summary, string details)
    {
        return InstallerStepResult.Failed(
            "Authentication Context",
            code,
            summary,
            details,
            retrySafe: true);
    }

    private static string FirstNonPlaceholder(params string[] values)
    {
        return values
            .Select(NormalizeExpectedIdentifier)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static string NormalizeExpectedIdentifier(string value)
    {
        var normalized = value?.Trim() ?? "";
        return IsPlaceholderIdentifier(normalized) ? "" : normalized;
    }

    private static bool MatchesExpectedIdentifier(string expected, string? actual)
    {
        return string.IsNullOrWhiteSpace(expected) ||
            (!string.IsNullOrWhiteSpace(actual) &&
             expected.Equals(actual.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPlaceholderIdentifier(string value)
    {
        return value.Equals("00000000-0000-0000-0000-000000000000", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("11111111-1111-1111-1111-111111111111", StringComparison.OrdinalIgnoreCase);
    }

    private static string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "not available" : value.Trim();
    }
}
