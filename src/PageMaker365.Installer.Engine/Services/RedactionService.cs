using System.Text.RegularExpressions;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed partial class RedactionService
{
    public string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var redacted = BearerTokenRegex().Replace(value, "$1[REDACTED]");
        redacted = SecretLikeRegex().Replace(redacted, "$1\"[REDACTED]\"");
        redacted = ConnectionStringRegex().Replace(redacted, "$1=[REDACTED]");
        return redacted;
    }

    public CustomerInstallConfig RedactConfig(CustomerInstallConfig config)
    {
        return new CustomerInstallConfig
        {
            Customer =
            {
                TenantName = config.Customer.TenantName,
                TenantId = Mask(config.Customer.TenantId),
                PrimaryContact = config.Customer.PrimaryContact
            },
            Azure =
            {
                SubscriptionId = Mask(config.Azure.SubscriptionId),
                Location = config.Azure.Location,
                ResourceGroupName = config.Azure.ResourceGroupName,
                Environment = config.Azure.Environment
            },
            SharePoint =
            {
                SiteUrl = config.SharePoint.SiteUrl,
                SiteId = Mask(config.SharePoint.SiteId),
                DefaultDocumentLibrary = config.SharePoint.DefaultDocumentLibrary
            },
            App =
            {
                AppName = config.App.AppName,
                CustomDomain = config.App.CustomDomain,
                SupportEmail = config.App.SupportEmail
            },
            Features = config.Features
        };
    }

    private static string Mask(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= 8)
        {
            return "[REDACTED]";
        }

        return $"{value[..4]}...[REDACTED]...{value[^4..]}";
    }

    [GeneratedRegex("(Authorization:\\s*Bearer\\s+)[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("(\"(?:clientSecret|password|secret|token|accessToken|refreshToken)\"\\s*:\\s*)\"[^\"]+\"", RegexOptions.IgnoreCase)]
    private static partial Regex SecretLikeRegex();

    [GeneratedRegex("(AccountKey|SharedAccessKey|Password|ClientSecret)=([^;\\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringRegex();
}

