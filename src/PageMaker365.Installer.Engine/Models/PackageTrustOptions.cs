using System.Text.Json;

namespace PageMaker365.Installer.Engine.Models;

public sealed class PackageTrustOptions
{
    public IReadOnlyDictionary<string, string> TrustedPublicKeysById { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static PackageTrustOptions Empty { get; } = new();

    public static PackageTrustOptions FromEnvironment()
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var json = Environment.GetEnvironmentVariable("PAGEMAKER365_INSTALLER_TRUSTED_PUBLIC_KEYS_JSON");
        if (!string.IsNullOrWhiteSpace(json))
        {
            Dictionary<string, string>? parsed = null;
            try
            {
                parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            }
            catch (JsonException)
            {
                parsed = null;
            }

            foreach (var entry in parsed ?? new Dictionary<string, string>())
            {
                if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                {
                    keys[entry.Key.Trim()] = entry.Value.Trim();
                }
            }
        }

        var keyId = Environment.GetEnvironmentVariable("PAGEMAKER365_INSTALL_PACKAGE_PUBLIC_KEY_ID");
        var publicKeyPem = Environment.GetEnvironmentVariable("PAGEMAKER365_INSTALL_PACKAGE_PUBLIC_KEY_PEM");
        if (!string.IsNullOrWhiteSpace(keyId) && !string.IsNullOrWhiteSpace(publicKeyPem))
        {
            keys[keyId.Trim()] = publicKeyPem.Trim();
        }

        return new PackageTrustOptions
        {
            TrustedPublicKeysById = keys
        };
    }

    public string GetTrustedPublicKey(string publicKeyId)
    {
        if (string.IsNullOrWhiteSpace(publicKeyId))
        {
            return "";
        }

        foreach (var entry in TrustedPublicKeysById)
        {
            if (entry.Key.Equals(publicKeyId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return "";
    }
}
