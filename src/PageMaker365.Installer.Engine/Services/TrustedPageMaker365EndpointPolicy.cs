namespace PageMaker365.Installer.Engine.Services;

public static class TrustedPageMaker365EndpointPolicy
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagemaker365.com",
        "api.pagemaker365.com",
        "staging.pagemaker365.com",
        "api-staging.pagemaker365.com",
        "downloads.pagemaker365.com",
        "downloads-staging.pagemaker365.com",
        "localhost",
        "127.0.0.1",
        "::1"
    };

    public static bool TryValidateBaseUrl(string value, out Uri? uri, out string error)
    {
        uri = null;
        error = "";
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var candidate))
        {
            error = "must be an absolute URL.";
            return false;
        }

        if (!AllowedHosts.Contains(candidate.Host))
        {
            error = $"host '{candidate.Host}' is not a trusted PageMaker365 endpoint.";
            return false;
        }

        var localHost = IsLocalHost(candidate.Host);
        if (candidate.Scheme != Uri.UriSchemeHttps && !(localHost && candidate.Scheme == Uri.UriSchemeHttp))
        {
            error = "must use HTTPS; HTTP is allowed only for local development endpoints.";
            return false;
        }

        if (!localHost && !candidate.IsDefaultPort)
        {
            error = "must use the default HTTPS port 443.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(candidate.UserInfo))
        {
            error = "must not contain embedded credentials.";
            return false;
        }

        uri = candidate;
        return true;
    }

    public static Uri ValidateBaseUrl(string value, string label)
    {
        if (!TryValidateBaseUrl(value, out var uri, out var error))
        {
            throw new InvalidDataException($"{label} {error}");
        }

        return uri!;
    }

    public static bool TryValidateArtifactUrl(string value, out Uri? uri, out string error)
    {
        if (!TryValidateBaseUrl(value, out uri, out error))
        {
            return false;
        }

        if (!IsLocalHost(uri!.Host) &&
            !uri.Host.Equals("downloads.pagemaker365.com", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.Equals("downloads-staging.pagemaker365.com", StringComparison.OrdinalIgnoreCase))
        {
            error = $"host '{uri.Host}' is not an approved PageMaker365 runtime release endpoint.";
            uri = null;
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "must not contain a query string or fragment.";
            uri = null;
            return false;
        }

        return true;
    }

    public static bool IsLocalHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }
}
