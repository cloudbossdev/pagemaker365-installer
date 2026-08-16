using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

/// <summary>
/// Validates only the immutable, approval-gated initial Sandbox handoff. It is
/// deliberately separate from CustomerConfigService and the 0.4 deploy flow.
/// </summary>
public sealed class InitialInstallDeliveryService
{
    public const string PayloadSchemaVersion = "pagemaker365.initial-install.v1";
    public const string AllowedOperation = "initial_sandbox_install";
    public const string SandboxEnvironmentKey = "sandbox";
    private const string SignatureAlgorithm = "Ed25519";

    private static readonly string[] DeliveryFields = ["contractVersion", "deliverySessionId", "correlationId", "package"];
    private static readonly string[] PackageFields = ["payload", "payloadSha256", "signatureAlgorithm", "signingKeyId", "signature"];
    private static readonly string[] PayloadFields = ["schemaVersion", "artifactId", "allowedOperation", "issuedAt", "expiresAt", "customer", "installation", "commercial", "preflight", "onboarding", "technicalFacts", "runtimeArtifacts"];
    private static readonly string[] CustomerFields = ["id", "key"];
    private static readonly string[] InstallationFields = ["intakeId", "installationId", "environmentId", "environmentKey"];
    private static readonly string[] CommercialFields = ["engagementId", "scopeRevisionId", "scopeDigest", "sandboxAuthorizationId"];
    private static readonly string[] PreflightFields = ["sessionId", "discoveryId", "technicalInputDigest"];
    private static readonly string[] OnboardingFields = ["customer", "azure", "entra", "sharePoint", "supportAndOffboarding"];
    private static readonly string[] OnboardingSectionFields = ["id", "version", "status", "digest"];
    private static readonly string[] TechnicalFactsFields = ["azure", "entra", "sharePoint", "discovery"];
    private static readonly string[] RuntimeArtifactFields = ["contractVersion", "releaseId", "runtimeVersion", "sourceCommit", "api", "portal"];
    private static readonly string[] RuntimeArtifactAssetFields = ["fileName", "sizeBytes", "downloadUrl", "sha256", "startupCommand"];
    private static readonly HashSet<string> AllowedTechnicalAzureFields = new(StringComparer.Ordinal)
    {
        "tenantId", "azureTenantId", "subscriptionId", "azureSubscriptionId", "region", "azureRegion", "location"
    };
    private static readonly HashSet<string> AllowedTechnicalEntraFields = new(StringComparer.Ordinal)
    {
        "tenantId", "entraTenantId", "permissionMode", "appRegistrationMode", "delegatedPermissions"
    };
    private static readonly HashSet<string> AllowedTechnicalSharePointFields = new(StringComparer.Ordinal)
    {
        "tenantHost", "tenantHostname", "sharePointTenantHost", "siteUrl", "primarySiteUrl", "initialSharePointSiteUrls"
    };
    private static readonly HashSet<string> AllowedTechnicalDiscoveryFields = new(StringComparer.Ordinal)
    {
        "tenantId", "selectedSubscriptionId", "recommendedLocation", "permissionMode", "appRegistrationMode", "tenantHostname", "siteUrl"
    };
    private static readonly Regex LowerHexDigest = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex CanonicalPositiveInteger = new("^(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Token = new("^[A-Za-z0-9._:-]{1,220}$", RegexOptions.CultureInvariant);
    private static readonly Regex CustomerKey = new("^[a-z0-9][a-z0-9_-]{0,159}$", RegexOptions.CultureInvariant);
    private static readonly Regex SafeReleaseId = new("^[A-Za-z0-9._+-]{1,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex StableVersion = new("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);
    private static readonly Regex SourceCommit = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex ZipFileName = new("^[A-Za-z0-9._+-]+\\.zip$", RegexOptions.CultureInvariant);
    private static readonly Regex Base64Url = new("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex ProhibitedPayloadKey = new("(?:payment|stripe|subscription|license|workspace|template|page|navigation|branding|production|frontdoor|dns|publish|connector|secret|token|password|connectionstring|private|raw|document)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public InitialInstallDeliveryValidationResult ValidateJson(
        string deliveryJson,
        InitialInstallTrustOptions trustOptions,
        DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(deliveryJson))
        {
            throw new InvalidDataException("Initial-install delivery is required.");
        }

        ArgumentNullException.ThrowIfNull(trustOptions);
        var utf8 = new UTF8Encoding(false, true).GetBytes(deliveryJson);
        RejectDuplicateProperties(utf8);
        using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });

        var root = document.RootElement;
        RequireExactObject(root, DeliveryFields, "delivery");
        RequireString(root, "contractVersion", InitialInstallDeliveryEnvelope.ContractVersionValue);
        var deliverySessionId = RequireToken(root, "deliverySessionId");
        var correlationId = RequireToken(root, "correlationId");
        var package = RequireObject(root, "package");
        RequireExactObject(package, PackageFields, "delivery.package");

        var payload = RequireObject(package, "payload");
        ValidatePayload(payload, now ?? DateTimeOffset.UtcNow, out var artifactId, out var customerId, out var issuedAt, out var expiresAt);
        var canonicalPayload = CanonicalizePayload(payload);
        var payloadSha256 = RequireDigest(package, "payloadSha256");
        var computedSha256 = Convert.ToHexString(SHA256.HashData(canonicalPayload)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(payloadSha256), Encoding.ASCII.GetBytes(computedSha256)))
        {
            throw new InvalidDataException("Initial-install delivery payloadSha256 does not match the canonical payload.");
        }

        RequireString(package, "signatureAlgorithm", SignatureAlgorithm);
        var signingKeyId = RequireToken(package, "signingKeyId");
        var trustedPublicKey = trustOptions.GetTrustedPublicKey(signingKeyId);
        if (string.IsNullOrWhiteSpace(trustedPublicKey))
        {
            throw new InvalidDataException("Initial-install delivery signingKeyId is not configured in the installer trust map.");
        }

        var signature = RequireBase64Url(package, "signature", expectedByteLength: 64);
        if (!VerifyEd25519Signature(trustedPublicKey, canonicalPayload, signature))
        {
            throw new InvalidDataException("Initial-install delivery signature verification failed.");
        }

        return new InitialInstallDeliveryValidationResult
        {
            Delivery = new InitialInstallDeliveryEnvelope
            {
                ContractVersion = InitialInstallDeliveryEnvelope.ContractVersionValue,
                DeliverySessionId = deliverySessionId,
                CorrelationId = correlationId,
                Package = new InitialInstallSignedPackage
                {
                    PayloadJson = payload.GetRawText(),
                    PayloadSha256 = payloadSha256,
                    SignatureAlgorithm = SignatureAlgorithm,
                    SigningKeyId = signingKeyId,
                    Signature = RequireString(package, "signature")
                }
            },
            ArtifactId = artifactId,
            CustomerId = customerId,
            EnvironmentKey = SandboxEnvironmentKey,
            AllowedOperation = AllowedOperation,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            CanonicalPayloadUtf8 = canonicalPayload
        };
    }

    /// <summary>Node producer parity: recursively sorted object keys, stable array order, compact UTF-8 JSON.</summary>
    public static byte[] CanonicalizePayload(JsonElement payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            SkipValidation = false
        }))
        {
            WriteCanonicalJson(writer, payload);
        }
        return stream.ToArray();
    }

    private static void ValidatePayload(
        JsonElement payload,
        DateTimeOffset now,
        out string artifactId,
        out string customerId,
        out DateTimeOffset issuedAt,
        out DateTimeOffset expiresAt)
    {
        RequireExactObject(payload, PayloadFields, "payload");
        RejectProhibitedKeys(payload);
        RequireString(payload, "schemaVersion", PayloadSchemaVersion);
        RequireString(payload, "allowedOperation", AllowedOperation);
        artifactId = RequireUuid(payload, "artifactId");
        issuedAt = RequireUtcIsoDate(payload, "issuedAt");
        expiresAt = RequireUtcIsoDate(payload, "expiresAt");
        if (expiresAt <= issuedAt || expiresAt <= now)
        {
            throw new InvalidDataException("Initial-install delivery has expired or has an invalid issue/expiry range.");
        }
        if (issuedAt > now.AddMinutes(5))
        {
            throw new InvalidDataException("Initial-install delivery issue time is too far in the future.");
        }

        var customer = RequireObject(payload, "customer");
        RequireExactObject(customer, CustomerFields, "payload.customer");
        customerId = RequireUuid(customer, "id");
        if (!CustomerKey.IsMatch(RequireString(customer, "key")))
        {
            throw new InvalidDataException("Initial-install payload.customer.key is invalid.");
        }

        var installation = RequireObject(payload, "installation");
        RequireExactObject(installation, InstallationFields, "payload.installation");
        RequireUuid(installation, "intakeId");
        RequireUuid(installation, "installationId");
        RequireUuid(installation, "environmentId");
        RequireString(installation, "environmentKey", SandboxEnvironmentKey);

        var commercial = RequireObject(payload, "commercial");
        RequireExactObject(commercial, CommercialFields, "payload.commercial");
        RequireUuid(commercial, "engagementId");
        RequireUuid(commercial, "scopeRevisionId");
        RequireDigest(commercial, "scopeDigest");
        RequireUuid(commercial, "sandboxAuthorizationId");

        var preflight = RequireObject(payload, "preflight");
        RequireExactObject(preflight, PreflightFields, "payload.preflight");
        RequireToken(preflight, "sessionId");
        RequireToken(preflight, "discoveryId");
        RequireDigest(preflight, "technicalInputDigest");

        var onboarding = RequireObject(payload, "onboarding");
        RequireExactObject(onboarding, OnboardingFields, "payload.onboarding");
        foreach (var sectionName in OnboardingFields)
        {
            var section = RequireObject(onboarding, sectionName);
            RequireExactObject(section, OnboardingSectionFields, $"payload.onboarding.{sectionName}");
            RequireUuid(section, "id");
            RequirePositiveInteger(section, "version");
            RequireToken(section, "status");
            RequireDigest(section, "digest");
        }

        var technicalFacts = RequireObject(payload, "technicalFacts");
        RequireExactObject(technicalFacts, TechnicalFactsFields, "payload.technicalFacts");
        ValidateTechnicalFacts(RequireObject(technicalFacts, "azure"), AllowedTechnicalAzureFields, "payload.technicalFacts.azure");
        ValidateTechnicalFacts(RequireObject(technicalFacts, "entra"), AllowedTechnicalEntraFields, "payload.technicalFacts.entra");
        ValidateTechnicalFacts(RequireObject(technicalFacts, "sharePoint"), AllowedTechnicalSharePointFields, "payload.technicalFacts.sharePoint");
        ValidateTechnicalFacts(RequireObject(technicalFacts, "discovery"), AllowedTechnicalDiscoveryFields, "payload.technicalFacts.discovery");

        ValidateRuntimeArtifacts(RequireObject(payload, "runtimeArtifacts"));
    }

    private static void ValidateTechnicalFacts(JsonElement facts, ISet<string> allowedFields, string path)
    {
        RequireAllowedObject(facts, allowedFields, path);
        foreach (var property in facts.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                RequireSafeString(property.Value.GetString() ?? "", $"{path}.{property.Name}", 500);
                continue;
            }
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                var count = 0;
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (++count > 50 || item.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidDataException($"{path}.{property.Name} must contain at most 50 safe strings.");
                    }
                    RequireSafeString(item.GetString() ?? "", $"{path}.{property.Name}", 300);
                }
                continue;
            }
            throw new InvalidDataException($"{path}.{property.Name} must be a safe string or string array.");
        }
    }

    private static void ValidateRuntimeArtifacts(JsonElement runtimeArtifacts)
    {
        RequireExactObject(runtimeArtifacts, RuntimeArtifactFields, "payload.runtimeArtifacts");
        RequireString(runtimeArtifacts, "contractVersion", "1.0");
        var releaseId = RequireString(runtimeArtifacts, "releaseId");
        if (!SafeReleaseId.IsMatch(releaseId)) throw new InvalidDataException("payload.runtimeArtifacts.releaseId is invalid.");
        var runtimeVersion = RequireString(runtimeArtifacts, "runtimeVersion");
        if (!StableVersion.IsMatch(runtimeVersion)) throw new InvalidDataException("payload.runtimeArtifacts.runtimeVersion is invalid.");
        var sourceCommit = RequireString(runtimeArtifacts, "sourceCommit");
        if (!SourceCommit.IsMatch(sourceCommit)) throw new InvalidDataException("payload.runtimeArtifacts.sourceCommit is invalid.");

        var apiUrl = ValidateRuntimeArtifact(RequireObject(runtimeArtifacts, "api"), "api", "node dist/index.js");
        var portalUrl = ValidateRuntimeArtifact(RequireObject(runtimeArtifacts, "portal"), "portal", "node .pm365/start-portal-runtime.mjs");
        if (!GetReleaseDirectory(apiUrl).Equals(GetReleaseDirectory(portalUrl), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Initial-install runtime artifacts must share one release directory.");
        }
    }

    private static string ValidateRuntimeArtifact(JsonElement asset, string kind, string requiredStartupCommand)
    {
        RequireExactObject(asset, RuntimeArtifactAssetFields, $"payload.runtimeArtifacts.{kind}");
        var fileName = RequireString(asset, "fileName");
        if (!ZipFileName.IsMatch(fileName)) throw new InvalidDataException($"payload.runtimeArtifacts.{kind}.fileName is invalid.");
        var sizeBytes = RequirePositiveInteger(asset, "sizeBytes");
        if (sizeBytes > 268_435_456) throw new InvalidDataException($"payload.runtimeArtifacts.{kind}.sizeBytes exceeds the approved limit.");
        var downloadUrl = RequireString(asset, "downloadUrl");
        RequireDigest(asset, "sha256");
        RequireString(asset, "startupCommand", requiredStartupCommand);

        if (downloadUrl.Length > 2048 || downloadUrl.Contains('%') ||
            !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal) || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            uri.Host is not ("downloads.pagemaker365.com" or "downloads-staging.pagemaker365.com") ||
            uri.AbsolutePath.Contains("//", StringComparison.Ordinal) ||
            !Regex.IsMatch(uri.AbsolutePath, "^/[A-Za-z0-9._+/-]*$", RegexOptions.CultureInvariant) ||
            !uri.AbsoluteUri.Equals(downloadUrl, StringComparison.Ordinal) ||
            !uri.AbsolutePath.EndsWith("/" + fileName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"payload.runtimeArtifacts.{kind}.downloadUrl is not an approved immutable artifact URL.");
        }
        return downloadUrl;
    }

    private static string GetReleaseDirectory(string artifactUrl)
    {
        var uri = new Uri(artifactUrl, UriKind.Absolute);
        var path = uri.AbsolutePath;
        var lastSlash = path.LastIndexOf('/');
        return $"{uri.Scheme}://{uri.Host}{path[..(lastSlash + 1)]}";
    }

    private static bool VerifyEd25519Signature(string publicKeyPem, byte[] payload, byte[] signature)
    {
        try
        {
            var publicKey = PublicKeyFactory.CreateKey(DecodePemOrBase64(publicKeyPem));
            if (publicKey is not Ed25519PublicKeyParameters ed25519PublicKey) return false;
            var verifier = new Ed25519Signer();
            verifier.Init(false, ed25519PublicKey);
            verifier.BlockUpdate(payload, 0, payload.Length);
            return verifier.VerifySignature(signature);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidDataException or IOException)
        {
            return false;
        }
    }

    private static byte[] DecodePemOrBase64(string value)
    {
        var normalized = value.Trim().Replace("\\n", "\n", StringComparison.Ordinal);
        if (!normalized.Contains("-----BEGIN", StringComparison.Ordinal)) return Convert.FromBase64String(normalized);
        var body = string.Concat(normalized
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("-----BEGIN", StringComparison.Ordinal) && !line.StartsWith("-----END", StringComparison.Ordinal)));
        return Convert.FromBase64String(body);
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 });
        var objectProperties = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    objectProperties.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.PropertyName:
                    if (objectProperties.Count == 0 || !objectProperties.Peek().Add(reader.GetString() ?? ""))
                    {
                        throw new InvalidDataException("Initial-install delivery contains a duplicate JSON property.");
                    }
                    break;
                case JsonTokenType.EndObject:
                    objectProperties.Pop();
                    break;
            }
        }
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonicalJson(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                var rawNumber = element.GetRawText();
                if (!CanonicalPositiveInteger.IsMatch(rawNumber))
                {
                    throw new InvalidDataException("Initial-install payload numeric values must be canonical positive integers.");
                }
                writer.WriteRawValue(rawNumber, skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException("Initial-install payload contains an unsupported JSON value.");
        }
    }

    private static void RequireExactObject(JsonElement element, IReadOnlyCollection<string> expectedFields, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{path} must be an object.");
        }
        var actual = element.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var expected = expectedFields.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"{path} must contain only its exact approved fields.");
        }
    }

    private static void RequireAllowedObject(JsonElement element, ISet<string> allowedFields, string path)
    {
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Any(property => !allowedFields.Contains(property.Name)))
        {
            throw new InvalidDataException($"{path} contains an unapproved field.");
        }
    }

    private static JsonElement RequireObject(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var result) || result.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{property} must be an object.");
        }
        return result;
    }

    private static string RequireString(JsonElement parent, string property, string? expected = null)
    {
        if (!parent.TryGetProperty(property, out var result) || result.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"{property} must be a string.");
        }
        var value = result.GetString() ?? "";
        RequireSafeString(value, property, 2048);
        if (expected is not null && !value.Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{property} is not the required contract value.");
        }
        return value;
    }

    private static string RequireToken(JsonElement parent, string property)
    {
        var value = RequireString(parent, property);
        if (!Token.IsMatch(value)) throw new InvalidDataException($"{property} is invalid.");
        return value;
    }

    private static string RequireUuid(JsonElement parent, string property)
    {
        var value = RequireString(parent, property);
        if (!Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty || !value.Equals(parsed.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{property} must be a canonical UUID.");
        }
        return value;
    }

    private static string RequireDigest(JsonElement parent, string property)
    {
        var value = RequireString(parent, property);
        if (!LowerHexDigest.IsMatch(value)) throw new InvalidDataException($"{property} must be a lowercase SHA-256 digest.");
        return value;
    }

    private static long RequirePositiveInteger(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var result) || result.ValueKind != JsonValueKind.Number ||
            !CanonicalPositiveInteger.IsMatch(result.GetRawText()) || !result.TryGetInt64(out var parsed) || parsed < 1)
        {
            throw new InvalidDataException($"{property} must be a canonical positive integer.");
        }
        return parsed;
    }

    private static DateTimeOffset RequireUtcIsoDate(JsonElement parent, string property)
    {
        var value = RequireString(parent, property);
        if (!Regex.IsMatch(value, "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3}Z$", RegexOptions.CultureInvariant) ||
            !DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            throw new InvalidDataException($"{property} must be a UTC ISO-8601 timestamp with milliseconds.");
        }
        return parsed;
    }

    private static byte[] RequireBase64Url(JsonElement parent, string property, int expectedByteLength)
    {
        var value = RequireString(parent, property);
        if (!Base64Url.IsMatch(value) || value.Contains('=')) throw new InvalidDataException($"{property} must be unpadded base64url.");
        var padding = (value.Length % 4) switch { 0 => "", 2 => "==", 3 => "=", _ => throw new InvalidDataException($"{property} is invalid base64url.") };
        try
        {
            var decoded = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + padding);
            if (decoded.Length != expectedByteLength) throw new InvalidDataException($"{property} has an invalid byte length.");
            var canonical = Convert.ToBase64String(decoded).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            if (!canonical.Equals(value, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{property} is not canonical base64url.");
            }
            return decoded;
        }
        catch (FormatException)
        {
            throw new InvalidDataException($"{property} is invalid base64url.");
        }
    }

    private static void RequireSafeString(string value, string path, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value != value.Trim() ||
            value.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029' ||
                (character >= '\u202a' && character <= '\u202e') || (character >= '\u2066' && character <= '\u2069')))
        {
            throw new InvalidDataException($"{path} must be a trimmed safe string.");
        }
    }

    private static void RejectProhibitedKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) RejectProhibitedKeys(item);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (var property in value.EnumerateObject())
        {
            var normalized = new string(property.Name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (normalized is not "azuresubscriptionid" and not "selectedsubscriptionid" && ProhibitedPayloadKey.IsMatch(normalized))
            {
                throw new InvalidDataException($"Initial-install payload contains prohibited field {property.Name}.");
            }
            RejectProhibitedKeys(property.Value);
        }
    }
}
