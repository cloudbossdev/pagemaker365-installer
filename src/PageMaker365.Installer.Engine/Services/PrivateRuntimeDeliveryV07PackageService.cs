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
/// Strict parser-only consumer for the closed customer-install 0.7 package.
/// It performs no acquisition, extraction, configuration, or deployment.
/// </summary>
public sealed class PrivateRuntimeDeliveryV07PackageService(RuntimeConfigurationCatalogV1Authority catalog)
{
    private const int MaximumPackageBytes = 2 * 1024 * 1024;
    private static readonly Regex Sha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex PackageHash = new("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex SafeToken = new("^[A-Za-z0-9][A-Za-z0-9._:+-]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex SafeFileName = new("^[A-Za-z0-9][A-Za-z0-9._+-]*\\.zip$", RegexOptions.CultureInvariant);
    private static readonly Regex SigningKeyId = new("^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex DeliveryReference = new("^ard_[A-Za-z0-9_-]{24,64}$", RegexOptions.CultureInvariant);
    private static readonly Regex RealUuid = new("^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ForbiddenLocation = new("(blob\\.core\\.windows\\.net|[?&](?:sig|sv|se|sp)=|^https?://downloads\\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public PrivateRuntimeDeliveryPackageV07 ValidateJson(
        string packageJson,
        PackageTrustOptions trustOptions,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(trustOptions);
        if (string.IsNullOrWhiteSpace(packageJson)) throw new InvalidDataException("customer_install_v07_required");
        var utf8 = new UTF8Encoding(false, true).GetBytes(packageJson);
        if (utf8.Length > MaximumPackageBytes) throw new InvalidDataException("customer_install_v07_bounds");
        RejectDuplicateProperties(utf8);

        using var document = JsonDocument.Parse(utf8, RuntimeConfigurationCatalogV1Authority.StrictDocumentOptions);
        var root = document.RootElement;
        RuntimeConfigurationCatalogV1Authority.RequireExactProperties(root,
            "contractVersion", "customer", "installation", "deployment", "controlPlane", "runtimeArtifacts", "protectedAcquisition", "runtimeConfiguration");
        Require(root, "contractVersion", PrivateRuntimeDeliveryPackageV07.ContractVersionValue);

        var customer = root.GetProperty("customer");
        RuntimeConfigurationCatalogV1Authority.RequireExactProperties(customer, "customerId");
        var customerId = RequireUuid(customer, "customerId");

        var installation = root.GetProperty("installation");
        RuntimeConfigurationCatalogV1Authority.RequireExactProperties(installation, "installationId", "environmentId", "tenantId", "azureSubscriptionId");
        var installationId = RequireUuid(installation, "installationId");
        var environmentId = RequireUuid(installation, "environmentId");
        var tenantId = RequireUuid(installation, "tenantId");
        var azureSubscriptionId = RequireUuid(installation, "azureSubscriptionId");

        var deployment = root.GetProperty("deployment");
        RuntimeConfigurationCatalogV1Authority.RequireExactProperties(deployment, "deploymentExportId");
        var deploymentExportId = RequireUuid(deployment, "deploymentExportId");

        var control = root.GetProperty("controlPlane");
        RuntimeConfigurationCatalogV1Authority.RequireExactProperties(control,
            "onboardingSessionId", "expiresAt", "acceptedInstallerCapability", "packageHash", "packageHashAlgorithm",
            "canonicalization", "signatureAlgorithm", "signingKeyId", "signature");
        var onboardingSessionId = Require(control, "onboardingSessionId");
        if (!Regex.IsMatch(onboardingSessionId, "^onb_[A-Za-z0-9_-]{16,96}$", RegexOptions.CultureInvariant)) Fail("customer_install_v07_binding_invalid");
        var expiresAt = RequireCanonicalDate(control, "expiresAt");
        if (expiresAt <= (now ?? DateTimeOffset.UtcNow).ToUniversalTime()) Fail("customer_install_v07_binding_expired");
        Require(control, "acceptedInstallerCapability", PrivateRuntimeDeliveryPackageV07.CapabilityValue);
        var packageHash = Require(control, "packageHash");
        if (!PackageHash.IsMatch(packageHash)) Fail("customer_install_v07_hash_invalid");
        Require(control, "packageHashAlgorithm", "SHA-256");
        Require(control, "canonicalization", "json-c14n-v1");
        Require(control, "signatureAlgorithm", "Ed25519");
        var signingKeyId = Require(control, "signingKeyId");
        if (!SigningKeyId.IsMatch(signingKeyId)) Fail("customer_install_v07_signing_key_invalid");
        var signature = RequireBase64Url(control, "signature", 64);

        var runtime = root.GetProperty("runtimeArtifacts");
        RuntimeConfigurationCatalogV1Authority.RequireExactProperties(runtime,
            "manifestContractVersion", "manifestSha256", "product", "releaseId", "runtimeVersion", "sourceRepository",
            "sourceCommit", "provenanceSchemaVersion", "api", "portal");
        Require(runtime, "manifestContractVersion", PrivateRuntimeDeliveryPackageV07.ManifestVersionValue);
        var manifestSha256 = RequireDigest(runtime, "manifestSha256");
        Require(runtime, "product", "PageMaker365");
        var releaseId = Require(runtime, "releaseId");
        var runtimeVersion = Require(runtime, "runtimeVersion");
        if (!SafeToken.IsMatch(releaseId) || !IsInt32Semver(runtimeVersion)) Fail("customer_install_v07_runtime_binding");
        Require(runtime, "sourceRepository", RuntimeConfigurationCatalogV1Authority.SourceRepository);
        var sourceCommit = Require(runtime, "sourceCommit", RuntimeConfigurationCatalogV1Authority.SourceCommit);
        Require(runtime, "provenanceSchemaVersion", "pagemaker365.runtime-provenance.v1");
        var api = ValidateArtifact(runtime.GetProperty("api"), "api", "node dist/index.js");
        var portal = ValidateArtifact(runtime.GetProperty("portal"), "portal", "node .pm365/start-portal-runtime.mjs");
        if (api.FileName == portal.FileName) Fail("customer_install_v07_artifact_identity");
        var computedManifest = PrivateRuntimeCanonicalJson.Sha256(FormatCanonicalManifest(runtime));
        if (!PrivateRuntimeCanonicalJson.FixedEquals(manifestSha256, computedManifest)) Fail("customer_install_v07_manifest_binding");

        var acquisition = root.GetProperty("protectedAcquisition");
        RuntimeConfigurationCatalogV1Authority.RequireExactProperties(acquisition,
            "contractVersion", "sessionPath", "artifactPath", "receiptPath", "authorizationMode", "expiresAt", "artifactReferences");
        Require(acquisition, "contractVersion", PrivateRuntimeDeliveryPackage.AcquisitionContractVersionValue);
        Require(acquisition, "sessionPath", PrivateRuntimeDeliveryPackage.SessionPathValue);
        Require(acquisition, "artifactPath", PrivateRuntimeDeliveryPackage.ArtifactPathValue);
        Require(acquisition, "receiptPath", PrivateRuntimeDeliveryPackage.ReceiptPathValue);
        Require(acquisition, "authorizationMode", "installer-session-v1");
        if (RequireCanonicalDate(acquisition, "expiresAt") != expiresAt) Fail("customer_install_v07_acquisition_binding");
        var references = acquisition.GetProperty("artifactReferences");
        RuntimeConfigurationCatalogV1Authority.RequireExactProperties(references, "api", "portal");
        var apiReference = Require(references, "api");
        var portalReference = Require(references, "portal");
        if (!DeliveryReference.IsMatch(apiReference) || !DeliveryReference.IsMatch(portalReference) || apiReference == portalReference) Fail("customer_install_v07_delivery_references_invalid");

        var expectedBinding = new RuntimeConfigurationPackageBindingV2
        {
            PackageContractVersion = PrivateRuntimeDeliveryPackageV07.ContractVersionValue,
            CustomerId = customerId,
            InstallationId = installationId,
            EnvironmentId = environmentId,
            TenantId = tenantId,
            AzureSubscriptionId = azureSubscriptionId,
            DeploymentExportId = deploymentExportId,
            RuntimeReleaseId = releaseId,
            RuntimeVersion = runtimeVersion,
            ManifestSha256 = manifestSha256
        };
        var projection = new RuntimeConfigurationProjectionV2Validator(catalog).Validate(root.GetProperty("runtimeConfiguration"), expectedBinding);

        RejectLocationOrCredentialMaterial(root);
        if (!string.Equals(packageJson, FormatCanonicalPackage(root), StringComparison.Ordinal)) Fail("customer_install_v07_noncanonical");
        var signingPayload = PrivateRuntimeCanonicalJson.Canonicalize(root, excludePackageIntegrity: true);
        var computedPackageHash = "sha256:" + PrivateRuntimeCanonicalJson.Sha256(signingPayload);
        if (!PrivateRuntimeCanonicalJson.FixedEquals(packageHash, computedPackageHash)) Fail("customer_install_v07_hash_invalid");
        var trustedKey = trustOptions.TrustedPublicKeysById
            .Where(item => item.Key.Equals(signingKeyId, StringComparison.Ordinal))
            .Select(item => item.Value)
            .SingleOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(trustedKey)) Fail("customer_install_v07_trust_key_id");
        if (!VerifyEd25519(trustedKey, signingPayload, signature)) Fail("customer_install_v07_signature_invalid");

        return new PrivateRuntimeDeliveryPackageV07
        {
            PackageHash = packageHash,
            SigningKeyId = signingKeyId,
            CustomerId = customerId,
            InstallationId = installationId,
            EnvironmentId = environmentId,
            TenantId = tenantId,
            AzureSubscriptionId = azureSubscriptionId,
            DeploymentExportId = deploymentExportId,
            OnboardingSessionId = onboardingSessionId,
            ExpiresAt = expiresAt,
            ManifestSha256 = manifestSha256,
            ReleaseId = releaseId,
            RuntimeVersion = runtimeVersion,
            SourceCommit = sourceCommit,
            Api = api,
            Portal = portal,
            ApiDeliveryReference = apiReference,
            PortalDeliveryReference = portalReference,
            RuntimeConfiguration = projection,
            CanonicalPackageJson = packageJson,
            CanonicalSigningPayloadUtf8 = signingPayload
        };
    }

    public static string FormatCanonicalPackage(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            SkipValidation = false
        }))
        {
            root.WriteTo(writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static byte[] FormatCanonicalManifest(JsonElement runtime)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("contractVersion", "3.0");
            writer.WriteString("product", "PageMaker365");
            writer.WriteString("releaseId", runtime.GetProperty("releaseId").GetString());
            writer.WriteString("runtimeVersion", runtime.GetProperty("runtimeVersion").GetString());
            writer.WriteString("sourceRepository", RuntimeConfigurationCatalogV1Authority.SourceRepository);
            writer.WriteString("sourceCommit", runtime.GetProperty("sourceCommit").GetString());
            writer.WriteString("provenanceSchemaVersion", "pagemaker365.runtime-provenance.v1");
            WriteManifestArtifact(writer, "api", runtime.GetProperty("api"));
            WriteManifestArtifact(writer, "portal", runtime.GetProperty("portal"));
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }

    private static void WriteManifestArtifact(Utf8JsonWriter writer, string name, JsonElement artifact)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteString("fileName", artifact.GetProperty("fileName").GetString());
        writer.WriteNumber("sizeBytes", artifact.GetProperty("sizeBytes").GetInt64());
        writer.WriteString("sha256", artifact.GetProperty("sha256").GetString());
        writer.WriteString("startupCommand", artifact.GetProperty("startupCommand").GetString());
        writer.WriteString("artifactKind", artifact.GetProperty("artifactKind").GetString());
        writer.WriteEndObject();
    }

    private static PrivateRuntimeArtifact ValidateArtifact(JsonElement value, string kind, string command)
    {
        RuntimeConfigurationCatalogV1Authority.RequireExactProperties(value, "artifactKind", "fileName", "sizeBytes", "sha256", "startupCommand");
        Require(value, "artifactKind", kind);
        var fileName = Require(value, "fileName");
        if (fileName.Length > 255 || !SafeFileName.IsMatch(fileName)) Fail("customer_install_v07_artifact_identity");
        if (!value.GetProperty("sizeBytes").TryGetInt64(out var size) || size is < 1 or > 268_435_456 || value.GetProperty("sizeBytes").GetRawText() != size.ToString(CultureInfo.InvariantCulture)) Fail("customer_install_v07_artifact_identity");
        var digest = RequireDigest(value, "sha256");
        Require(value, "startupCommand", command);
        return new PrivateRuntimeArtifact { ArtifactKind = kind, FileName = fileName, SizeBytes = size, Sha256 = digest, StartupCommand = command };
    }

    private static string Require(JsonElement value, string property, string? expected = null)
    {
        var result = RuntimeConfigurationCatalogV1Authority.RequireString(value, property);
        if (result.Length == 0 || result.Length > 16_384 || result != result.Trim() || result.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029' || (character >= '\u202a' && character <= '\u202e') || (character >= '\u2066' && character <= '\u2069')) ||
            (expected is not null && result != expected)) Fail("customer_install_v07_value");
        return result;
    }

    private static string RequireUuid(JsonElement value, string property)
    {
        var result = Require(value, property);
        var nibbles = result.ToLowerInvariant().Replace("-", "", StringComparison.Ordinal).ToCharArray();
        if (!RealUuid.IsMatch(result) || !Guid.TryParseExact(result, "D", out var parsed) || parsed == Guid.Empty ||
            nibbles.Where((_value, index) => index is not (12 or 16)).Distinct().Count() <= 1) Fail("customer_install_v07_binding_invalid");
        return result;
    }

    private static string RequireDigest(JsonElement value, string property)
    {
        var result = Require(value, property);
        if (!Sha256.IsMatch(result)) Fail("customer_install_v07_digest");
        return result;
    }

    private static DateTimeOffset RequireCanonicalDate(JsonElement value, string property)
    {
        var text = Require(value, property);
        var parsed = default(DateTimeOffset);
        if (!Regex.IsMatch(text, "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3}Z$", RegexOptions.CultureInvariant) ||
            !DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
            Fail("customer_install_v07_date");
        return parsed;
    }

    private static byte[] RequireBase64Url(JsonElement value, string property, int length)
    {
        var text = Require(value, property);
        if (!Regex.IsMatch(text, "^[A-Za-z0-9_-]{40,256}$", RegexOptions.CultureInvariant)) Fail("customer_install_v07_signature_invalid");
        try
        {
            var padding = (text.Length % 4) switch { 0 => "", 2 => "==", 3 => "=", _ => throw new FormatException() };
            var result = Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + padding);
            if (result.Length != length) Fail("customer_install_v07_signature_invalid");
            var canonical = Convert.ToBase64String(result).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            if (!string.Equals(text, canonical, StringComparison.Ordinal)) Fail("customer_install_v07_signature_invalid");
            return result;
        }
        catch (FormatException) { Fail("customer_install_v07_signature_invalid"); return []; }
    }

    private static bool IsInt32Semver(string value)
    {
        var match = Regex.Match(value, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);
        return match.Success && match.Groups.Cast<Group>().Skip(1).All(group => int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> value)
    {
        var reader = new Utf8JsonReader(value, new JsonReaderOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 });
        var stack = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) stack.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.PropertyName && (stack.Count == 0 || !stack.Peek().Add(reader.GetString() ?? ""))) Fail("customer_install_v07_json_duplicate");
            else if (reader.TokenType == JsonTokenType.EndObject) stack.Pop();
        }
    }

    private static void RejectLocationOrCredentialMaterial(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) RejectLocationOrCredentialMaterial(item);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name is "downloadUrl" or "url" or "host" or "origin" or "objectLocator" or "objectRef" or "sas" or "storageAccount" or "blob" or "token") Fail("customer_install_v07_forbidden_material");
            if (property.Value.ValueKind == JsonValueKind.String && ForbiddenLocation.IsMatch(property.Value.GetString() ?? "")) Fail("customer_install_v07_forbidden_material");
            RejectLocationOrCredentialMaterial(property.Value);
        }
    }

    private static bool VerifyEd25519(string publicKey, byte[] payload, byte[] signature)
    {
        try
        {
            var normalized = publicKey.Trim().Replace("\\n", "\n", StringComparison.Ordinal);
            var body = string.Concat(normalized.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !line.StartsWith("-----BEGIN", StringComparison.Ordinal) && !line.StartsWith("-----END", StringComparison.Ordinal)));
            var key = PublicKeyFactory.CreateKey(Convert.FromBase64String(body));
            if (key is not Ed25519PublicKeyParameters ed25519) return false;
            var verifier = new Ed25519Signer();
            verifier.Init(false, ed25519);
            verifier.BlockUpdate(payload, 0, payload.Length);
            return verifier.VerifySignature(signature);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or IOException) { return false; }
    }

    private static void Fail(string code) => throw new InvalidDataException(code);
}
