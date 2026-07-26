using Microsoft.Identity.Client;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class GraphDeviceCodeAuthenticator
{
    public const string DefaultClientId = "14d82eec-204b-4c2f-b7e8-296a70dab67e";

    public static readonly string[] RequiredScopes =
    [
        "User.Read",
        "Domain.Read.All",
        "RoleManagement.Read.Directory",
        "Sites.Read.All"
    ];

    public async Task<GraphSignInResult> SignInAsync(
        string tenantId,
        string clientId,
        IProgress<GraphDeviceCodePrompt>? promptProgress = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveClientId = string.IsNullOrWhiteSpace(clientId) ? DefaultClientId : clientId.Trim();
        var authorityTenant = IsPlaceholderGuid(tenantId) ? "organizations" : tenantId.Trim();
        var app = PublicClientApplicationBuilder
            .Create(effectiveClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, authorityTenant)
            .WithRedirectUri("http://localhost")
            .Build();

        var result = await app
            .AcquireTokenWithDeviceCode(RequiredScopes, deviceCode =>
            {
                promptProgress?.Report(new GraphDeviceCodePrompt
                {
                    Message = deviceCode.Message,
                    UserCode = deviceCode.UserCode,
                    VerificationUrl = deviceCode.VerificationUrl,
                    ExpiresOn = deviceCode.ExpiresOn
                });

                return Task.CompletedTask;
            })
            .ExecuteAsync(cancellationToken);

        return new GraphSignInResult
        {
            AccessToken = result.AccessToken,
            ExpiresOn = result.ExpiresOn,
            TenantId = result.TenantId,
            Account = result.Account?.Username ?? "",
            Scopes = result.Scopes.ToList()
        };
    }

    private static bool IsPlaceholderGuid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = value.Trim();
        return normalized.Equals("00000000-0000-0000-0000-000000000000", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("11111111-1111-1111-1111-111111111111", StringComparison.OrdinalIgnoreCase);
    }
}
