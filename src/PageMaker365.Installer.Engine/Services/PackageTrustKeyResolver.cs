using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.X509;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class PackageTrustKeyResolver
{
    private const string LicenseJwksPath = "/.well-known/pagemaker365-license-jwks.json";

    private static readonly HashSet<string> AllowedJwksHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagemaker365.com",
        "api.pagemaker365.com",
        "staging.pagemaker365.com",
        "api-staging.pagemaker365.com",
        "localhost",
        "127.0.0.1",
        "::1"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public PackageTrustKeyResolver(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<PackageTrustOptions> ResolveAsync(
        CustomerInstallConfig config,
        PackageTrustOptions? baseOptions = null,
        CancellationToken cancellationToken = default)
    {
        var keys = new Dictionary<string, string>(
            baseOptions?.TrustedPublicKeysById ?? PackageTrustOptions.FromEnvironment().TrustedPublicKeysById,
            StringComparer.OrdinalIgnoreCase);
        var publicKeyId = config.ControlPlane.PublicKeyId.Trim();
        if (string.IsNullOrWhiteSpace(publicKeyId) || keys.ContainsKey(publicKeyId))
        {
            return new PackageTrustOptions { TrustedPublicKeysById = keys };
        }

        if (string.IsNullOrWhiteSpace(config.ControlPlane.JwksUrl))
        {
            return new PackageTrustOptions { TrustedPublicKeysById = keys };
        }

        var jwksUri = ValidateTrustedJwksUrl(config.ControlPlane.JwksUrl);
        using var response = await _httpClient.GetAsync(jwksUri, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidDataException(
                $"Package signing JWKS request returned {(int)response.StatusCode} {response.StatusCode} from '{jwksUri}'.");
        }

        var jwks = JsonSerializer.Deserialize<JsonWebKeySet>(body, JsonOptions)
            ?? throw new InvalidDataException("Package signing JWKS response was empty.");
        var key = jwks.Keys.FirstOrDefault(candidate =>
            publicKeyId.Equals(candidate.Kid, StringComparison.OrdinalIgnoreCase) &&
            candidate.Kty.Equals("OKP", StringComparison.OrdinalIgnoreCase) &&
            candidate.Crv.Equals("Ed25519", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(candidate.Alg) || candidate.Alg.Equals("EdDSA", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(candidate.X));

        if (key is null)
        {
            throw new InvalidDataException(
                $"Package signing JWKS '{jwksUri}' did not contain Ed25519 key '{publicKeyId}'.");
        }

        keys[publicKeyId] = ConvertEd25519JwkToPem(key);
        return new PackageTrustOptions { TrustedPublicKeysById = keys };
    }

    private static Uri ValidateTrustedJwksUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && IsLocalHost(uri.Host)))
        {
            throw new InvalidDataException("Package signing JWKS URL must be an absolute HTTPS PageMaker365 URL.");
        }

        if (!AllowedJwksHosts.Contains(uri.Host))
        {
            throw new InvalidDataException($"Package signing JWKS host '{uri.Host}' is not trusted.");
        }

        if (!uri.AbsolutePath.Equals(LicenseJwksPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Package signing JWKS URL must use '{LicenseJwksPath}'.");
        }

        return uri;
    }

    private static bool IsLocalHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }

    private static string ConvertEd25519JwkToPem(JsonWebKey key)
    {
        var raw = DecodeBase64Url(key.X);
        if (raw.Length != 32)
        {
            throw new InvalidDataException("Package signing JWKS Ed25519 key must contain a 32-byte x coordinate.");
        }

        var publicKey = new Ed25519PublicKeyParameters(raw, 0);
        var der = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey).GetDerEncoded();
        return ToPem("PUBLIC KEY", der);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Trim()
            .Replace('-', '+')
            .Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => "",
            _ => throw new InvalidDataException("Invalid JWKS base64url value.")
        };
        return Convert.FromBase64String(normalized);
    }

    private static string ToPem(string label, byte[] der)
    {
        var body = Convert.ToBase64String(der);
        var builder = new StringBuilder();
        builder.AppendLine($"-----BEGIN {label}-----");
        for (var index = 0; index < body.Length; index += 64)
        {
            builder.AppendLine(body.Substring(index, Math.Min(64, body.Length - index)));
        }

        builder.AppendLine($"-----END {label}-----");
        return builder.ToString();
    }

    private sealed class JsonWebKeySet
    {
        public List<JsonWebKey> Keys { get; set; } = [];
    }

    private sealed class JsonWebKey
    {
        public string Kty { get; set; } = "";
        public string Crv { get; set; } = "";
        public string X { get; set; } = "";
        public string Kid { get; set; } = "";
        public string Use { get; set; } = "";
        public string Alg { get; set; } = "";
    }
}
