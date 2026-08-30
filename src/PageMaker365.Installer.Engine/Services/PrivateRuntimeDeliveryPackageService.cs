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
/// Strict consumer for the location-free customer-install 0.5 package. The
/// legacy 0.4 parser intentionally remains separate because 0.5 cannot fall
/// back to a public artifact location.
/// </summary>
public sealed class PrivateRuntimeDeliveryPackageService
{
    private const int MaximumPackageBytes = 2 * 1024 * 1024;
    private static readonly Regex LowerDigest = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex PackageHash = new("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex ReleaseId = new("^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex SemVer = new("^(0|[1-9][0-9]{0,9})\\.(0|[1-9][0-9]{0,9})\\.(0|[1-9][0-9]{0,9})$", RegexOptions.CultureInvariant);
    private static readonly Regex SourceCommit = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex OnboardingSession = new("^onb_[A-Za-z0-9_-]{16,96}$", RegexOptions.CultureInvariant);
    private static readonly Regex DeliveryReference = new("^ard_[A-Za-z0-9_-]{24,64}$", RegexOptions.CultureInvariant);
    private static readonly Regex SigningKeyId = new("^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex Base64Url = new("^[A-Za-z0-9_-]{40,256}$", RegexOptions.CultureInvariant);
    private static readonly Regex SettingName = new("^[A-Z][A-Z0-9_]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex V06Uuid = new("^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SafeArtifactFileName = new("^[A-Za-z0-9][A-Za-z0-9._+-]*\\.zip$", RegexOptions.CultureInvariant);
    private static readonly Regex ForbiddenSettingName = new("(SECRET|TOKEN|PASSWORD|PRIVATE|CONNECTION|SAS|STORAGE|BLOB)", RegexOptions.CultureInvariant);
    private static readonly Regex ForbiddenLocationOrCredential = new("(blob\\.core\\.windows\\.net|[?&](?:sig|sv|se|sp)=|^https?://downloads\\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] TopLevelFields = ["contractVersion", "customer", "installation", "deployment", "controlPlane", "runtimeArtifacts", "protectedAcquisition", "runtimeConfiguration"];
    private static readonly string[] ControlPlaneFields = ["onboardingSessionId", "expiresAt", "acceptedInstallerCapability", "packageHash", "packageHashAlgorithm", "canonicalization", "signatureAlgorithm", "signingKeyId", "signature"];
    private static readonly string[] RuntimeV05Fields = ["manifestContractVersion", "manifestSha256", "releaseId", "runtimeVersion", "sourceRepository", "sourceCommit", "provenanceSchemaVersion", "api", "portal"];
    private static readonly string[] RuntimeV06Fields = ["manifestContractVersion", "manifestSha256", "product", "releaseId", "runtimeVersion", "sourceRepository", "sourceCommit", "provenanceSchemaVersion", "api", "portal"];
    private static readonly string[] ArtifactFields = ["artifactKind", "fileName", "sizeBytes", "sha256", "startupCommand"];
    private static readonly string[] AcquisitionFields = ["contractVersion", "sessionPath", "artifactPath", "receiptPath", "authorizationMode", "expiresAt", "artifactReferences"];

    public PrivateRuntimeDeliveryPackage ValidateJson(
        string packageJson,
        PackageTrustOptions trustOptions,
        DateTimeOffset? now = null) =>
        ValidateJson(packageJson, trustOptions, ContractProfile.V05, now);

    internal static PrivateRuntimeDeliveryPackage ValidateV06Json(
        string packageJson,
        PackageTrustOptions trustOptions,
        DateTimeOffset? now = null) =>
        ValidateJson(packageJson, trustOptions, ContractProfile.V06, now);

    private static PrivateRuntimeDeliveryPackage ValidateJson(
        string packageJson,
        PackageTrustOptions trustOptions,
        ContractProfile profile,
        DateTimeOffset? now)
    {
        ArgumentNullException.ThrowIfNull(trustOptions);
        if (string.IsNullOrWhiteSpace(packageJson))
        {
            throw new InvalidDataException($"Customer-install {profile.PackageVersion} package is required.");
        }

        var utf8 = new UTF8Encoding(false, true).GetBytes(packageJson);
        if (utf8.Length > MaximumPackageBytes)
        {
            throw new InvalidDataException($"Customer-install {profile.PackageVersion} package exceeds its approved size.");
        }

        RejectDuplicateProperties(utf8, profile.PackageVersion);
        using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });

        var root = document.RootElement;
        RequireExactObject(root, TopLevelFields, $"customer-install {profile.PackageVersion} package");
        RequireString(root, "contractVersion", profile.PackageVersion);

        var customer = RequireObject(root, "customer");
        RequireExactObject(customer, ["customerId"], "customer-install customer");
        var customerId = RequireUuid(customer, "customerId", profile.IsV06);

        var installation = RequireObject(root, "installation");
        RequireExactObject(installation, ["installationId", "environmentId", "tenantId"], "customer-install installation");
        var installationId = RequireUuid(installation, "installationId", profile.IsV06);
        var environmentId = RequireUuid(installation, "environmentId", profile.IsV06);
        var tenantId = RequireUuid(installation, "tenantId", profile.IsV06);

        var deployment = RequireObject(root, "deployment");
        RequireExactObject(deployment, ["deploymentExportId"], "customer-install deployment");
        var deploymentExportId = RequireUuid(deployment, "deploymentExportId", profile.IsV06);

        var controlPlane = RequireObject(root, "controlPlane");
        RequireExactObject(controlPlane, ControlPlaneFields, "customer-install control plane");
        var onboardingSessionId = RequireString(controlPlane, "onboardingSessionId");
        if (!OnboardingSession.IsMatch(onboardingSessionId)) Fail("onboardingSessionId is invalid.");
        var expiresAt = RequireCanonicalUtcDate(controlPlane, "expiresAt");
        var utcNow = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (expiresAt <= utcNow) Fail($"Customer-install {profile.PackageVersion} package has expired.");
        RequireString(controlPlane, "acceptedInstallerCapability", profile.Capability);
        var packageHash = RequireString(controlPlane, "packageHash");
        if (!PackageHash.IsMatch(packageHash)) Fail("packageHash is invalid.");
        RequireString(controlPlane, "packageHashAlgorithm", "SHA-256");
        RequireString(controlPlane, "canonicalization", "json-c14n-v1");
        RequireString(controlPlane, "signatureAlgorithm", "Ed25519");
        var signingKeyId = RequireString(controlPlane, "signingKeyId");
        if (!SigningKeyId.IsMatch(signingKeyId)) Fail("signingKeyId is invalid.");
        var signature = RequireBase64Url(controlPlane, "signature", 64);

        var runtime = RequireObject(root, "runtimeArtifacts");
        RequireExactObject(runtime, profile.RuntimeFields, "customer-install runtime artifacts");
        RequireString(runtime, "manifestContractVersion", profile.ManifestVersion);
        var manifestSha256 = RequireDigest(runtime, "manifestSha256");
        if (profile.RequiresProduct) RequireString(runtime, "product", "PageMaker365");
        var releaseId = RequireString(runtime, "releaseId");
        if (!ReleaseId.IsMatch(releaseId)) Fail("runtimeArtifacts.releaseId is invalid.");
        var runtimeVersion = RequireString(runtime, "runtimeVersion");
        if (profile.IsV06 ? !IsSupportedV3RuntimeVersion(runtimeVersion) : !SemVer.IsMatch(runtimeVersion)) Fail("runtimeArtifacts.runtimeVersion is invalid.");
        RequireString(runtime, "sourceRepository", "cloudbossdev/spo-ui");
        var sourceCommit = RequireString(runtime, "sourceCommit");
        if (!SourceCommit.IsMatch(sourceCommit)) Fail("runtimeArtifacts.sourceCommit is invalid.");
        RequireString(runtime, "provenanceSchemaVersion", "pagemaker365.runtime-provenance.v1");
        var api = ValidateArtifact(RequireObject(runtime, "api"), "api", releaseId, "node dist/index.js", profile.IsV06);
        var portal = ValidateArtifact(RequireObject(runtime, "portal"), "portal", releaseId, "node .pm365/start-portal-runtime.mjs", profile.IsV06);
        if (api.FileName.Equals(portal.FileName, StringComparison.Ordinal) || (!profile.IsV06 && api.Sha256.Equals(portal.Sha256, StringComparison.Ordinal)))
        {
            Fail("Runtime artifact identities must be distinct.");
        }
        if (profile.IsV06)
        {
            var computedManifestSha256 = Convert.ToHexString(SHA256.HashData(FormatCanonicalManifestV3(runtime))).ToLowerInvariant();
            if (!FixedTimeEquals(manifestSha256, computedManifestSha256))
            {
                Fail("runtimeArtifacts.manifestSha256 does not bind the exact canonical manifest 3.0 identity.");
            }
        }

        var acquisition = RequireObject(root, "protectedAcquisition");
        RequireExactObject(acquisition, AcquisitionFields, "customer-install protected acquisition");
        RequireString(acquisition, "contractVersion", PrivateRuntimeDeliveryPackage.AcquisitionContractVersionValue);
        RequireString(acquisition, "sessionPath", PrivateRuntimeDeliveryPackage.SessionPathValue);
        RequireString(acquisition, "artifactPath", PrivateRuntimeDeliveryPackage.ArtifactPathValue);
        RequireString(acquisition, "receiptPath", PrivateRuntimeDeliveryPackage.ReceiptPathValue);
        RequireString(acquisition, "authorizationMode", "installer-session-v1");
        if (RequireCanonicalUtcDate(acquisition, "expiresAt") != expiresAt) Fail("Protected acquisition expiry does not match the package expiry.");
        var references = RequireObject(acquisition, "artifactReferences");
        RequireExactObject(references, ["api", "portal"], "customer-install artifact references");
        var apiReference = RequireString(references, "api");
        var portalReference = RequireString(references, "portal");
        if (!DeliveryReference.IsMatch(apiReference) || !DeliveryReference.IsMatch(portalReference) || apiReference.Equals(portalReference, StringComparison.Ordinal))
        {
            Fail("Protected acquisition delivery references are invalid.");
        }

        ValidateRuntimeConfiguration(RequireObject(root, "runtimeConfiguration"));
        RejectLocationOrCredentialMaterial(root, profile.PackageVersion);
        if (!packageJson.Equals(FormatCanonicalPackage(root, profile), StringComparison.Ordinal))
        {
            Fail($"Customer-install {profile.PackageVersion} package is not in its required canonical form.");
        }

        var canonicalSigningPayload = CanonicalizeSigningPayload(root);
        var computedPackageHash = "sha256:" + Convert.ToHexString(SHA256.HashData(canonicalSigningPayload)).ToLowerInvariant();
        if (!FixedTimeEquals(packageHash, computedPackageHash))
        {
            Fail($"Customer-install {profile.PackageVersion} packageHash does not match the canonical signed payload.");
        }

        var trustedPublicKey = trustOptions.GetTrustedPublicKey(signingKeyId);
        if (string.IsNullOrWhiteSpace(trustedPublicKey))
        {
            Fail($"Customer-install {profile.PackageVersion} signingKeyId is not configured in the installer trust map.");
        }
        if (!VerifyEd25519Signature(trustedPublicKey, canonicalSigningPayload, signature))
        {
            Fail($"Customer-install {profile.PackageVersion} signature verification failed.");
        }

        return new PrivateRuntimeDeliveryPackage
        {
            ContractVersion = profile.PackageVersion,
            ManifestContractVersion = profile.ManifestVersion,
            Product = "PageMaker365",
            PackageHash = packageHash,
            SigningKeyId = signingKeyId,
            CustomerId = customerId,
            InstallationId = installationId,
            EnvironmentId = environmentId,
            TenantId = tenantId,
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
            CanonicalPackageJson = packageJson,
            CanonicalSigningPayloadUtf8 = canonicalSigningPayload
        };
    }

    /// <summary>Node producer parity: lexicographically sorted object keys, stable arrays, no integrity fields.</summary>
    public static byte[] CanonicalizeSigningPayload(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            SkipValidation = false
        }))
        {
            WriteCanonicalJson(writer, root, excludeIntegrityFields: true);
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Mirrors the PageMaker365 producer's canonical package formatter. The
    /// parser compares its input with these exact bytes after strict shape
    /// validation so whitespace or property-order variations cannot create a
    /// second representation of the same signed authority.
    /// </summary>
    public static string FormatCanonicalPackage(JsonElement root) => FormatCanonicalPackage(root, ContractProfile.V05);

    internal static string FormatCanonicalV06Package(JsonElement root) => FormatCanonicalPackage(root, ContractProfile.V06);

    private static string FormatCanonicalPackage(JsonElement root, ContractProfile profile)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            SkipValidation = false
        }))
        {
            WritePackageObject(writer, root, "root", profile);
        }
        // The producer locks canonical package bytes with LF line endings.
        // Utf8JsonWriter's indented mode follows the host newline convention,
        // so normalize it before comparing or forwarding signed authority.
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static PrivateRuntimeArtifact ValidateArtifact(JsonElement artifact, string kind, string releaseId, string startupCommand, bool isV06)
    {
        RequireExactObject(artifact, ArtifactFields, $"customer-install {kind} artifact");
        RequireString(artifact, "artifactKind", kind);
        var fileName = RequireString(artifact, "fileName");
        if (isV06)
        {
            if (fileName.Length > 255 || !SafeArtifactFileName.IsMatch(fileName)) Fail($"runtimeArtifacts.{kind}.fileName is invalid.");
        }
        else if (!fileName.Equals($"pagemaker365-{kind}-{releaseId}.zip", StringComparison.Ordinal))
        {
            Fail($"runtimeArtifacts.{kind}.fileName is invalid.");
        }
        var sizeBytes = RequirePositiveInteger(artifact, "sizeBytes");
        if (sizeBytes > 268_435_456) Fail($"runtimeArtifacts.{kind}.sizeBytes exceeds the approved limit.");
        var sha256 = RequireDigest(artifact, "sha256");
        RequireString(artifact, "startupCommand", startupCommand);
        return new PrivateRuntimeArtifact { ArtifactKind = kind, FileName = fileName, SizeBytes = sizeBytes, Sha256 = sha256, StartupCommand = startupCommand };
    }

    private static byte[] FormatCanonicalManifestV3(JsonElement runtime)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            SkipValidation = false
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("contractVersion", "3.0");
            writer.WriteString("product", "PageMaker365");
            writer.WriteString("releaseId", runtime.GetProperty("releaseId").GetString());
            writer.WriteString("runtimeVersion", runtime.GetProperty("runtimeVersion").GetString());
            writer.WriteString("sourceRepository", "cloudbossdev/spo-ui");
            writer.WriteString("sourceCommit", runtime.GetProperty("sourceCommit").GetString());
            writer.WriteString("provenanceSchemaVersion", "pagemaker365.runtime-provenance.v1");
            WriteManifestArtifact(writer, "api", runtime.GetProperty("api"));
            WriteManifestArtifact(writer, "portal", runtime.GetProperty("portal"));
            writer.WriteEndObject();
        }
        var canonical = Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        return Encoding.UTF8.GetBytes(canonical);
    }

    private static void WriteManifestArtifact(Utf8JsonWriter writer, string propertyName, JsonElement artifact)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        writer.WriteString("fileName", artifact.GetProperty("fileName").GetString());
        writer.WriteNumber("sizeBytes", artifact.GetProperty("sizeBytes").GetInt64());
        writer.WriteString("sha256", artifact.GetProperty("sha256").GetString());
        writer.WriteString("startupCommand", artifact.GetProperty("startupCommand").GetString());
        writer.WriteString("artifactKind", artifact.GetProperty("artifactKind").GetString());
        writer.WriteEndObject();
    }

    private static bool IsSupportedV3RuntimeVersion(string value)
    {
        var match = Regex.Match(value, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);
        return match.Success && match.Groups.Cast<Group>().Skip(1).All(group => int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    private static void ValidateRuntimeConfiguration(JsonElement configuration)
    {
        RequireExactObject(configuration, ["schemaVersion", "projectionSha256", "publicSettings"], "customer-install runtime configuration");
        RequireString(configuration, "schemaVersion", "pagemaker365.runtime-configuration-projection.v1");
        var projectionSha = RequireDigest(configuration, "projectionSha256");
        if (!configuration.TryGetProperty("publicSettings", out var settings) || settings.ValueKind != JsonValueKind.Array || settings.GetArrayLength() > 128)
        {
            Fail("runtimeConfiguration.publicSettings is invalid.");
        }

        string? previousKey = null;
        foreach (var setting in settings.EnumerateArray())
        {
            RequireExactObject(setting, ["targetApp", "name", "value"], "customer-install runtime configuration setting");
            var targetApp = RequireString(setting, "targetApp");
            if (targetApp is not ("api" or "portal")) Fail("runtimeConfiguration targetApp is invalid.");
            var name = RequireString(setting, "name");
            if (!SettingName.IsMatch(name) || ForbiddenSettingName.IsMatch(name)) Fail("runtimeConfiguration setting name is invalid.");
            var key = $"{targetApp}:{name}";
            if (previousKey is not null && string.CompareOrdinal(previousKey, key) >= 0) Fail("runtimeConfiguration settings are not in canonical order.");
            previousKey = key;

            if (!setting.TryGetProperty("value", out var value) ||
                (value.ValueKind == JsonValueKind.String && (string.IsNullOrEmpty(value.GetString()) || value.GetString()!.Length > 2048 || value.GetString()!.Contains('\r') || value.GetString()!.Contains('\n'))) ||
                (value.ValueKind == JsonValueKind.Number && (!Regex.IsMatch(value.GetRawText(), "^(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant) || !value.TryGetInt32(out _))) ||
                value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
            {
                Fail("runtimeConfiguration setting value is invalid.");
            }
        }

        var projection = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = configuration.GetProperty("schemaVersion"),
            ["publicSettings"] = settings
        };
        var expectedProjectionHash = Convert.ToHexString(SHA256.HashData(CanonicalizeProjection(projection))).ToLowerInvariant();
        if (!FixedTimeEquals(projectionSha, expectedProjectionHash)) Fail("runtimeConfiguration projection hash is invalid.");
    }

    private static byte[] CanonicalizeProjection(IReadOnlyDictionary<string, JsonElement> projection)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            foreach (var pair in projection.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(pair.Key);
                WriteCanonicalJson(writer, pair.Value, excludeIntegrityFields: false);
            }
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void RejectLocationOrCredentialMaterial(JsonElement value, string packageVersion)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) RejectLocationOrCredentialMaterial(item, packageVersion);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name is "downloadUrl" or "url" or "host" or "origin" or "objectLocator" or "objectRef" or "sas" or "storageAccount" or "blob" or "token")
            {
                Fail($"Customer-install {packageVersion} package contains a forbidden location or credential field.");
            }
            if (property.Value.ValueKind == JsonValueKind.String && ForbiddenLocationOrCredential.IsMatch(property.Value.GetString() ?? ""))
            {
                Fail($"Customer-install {packageVersion} package contains a forbidden location or credential value.");
            }
            RejectLocationOrCredentialMaterial(property.Value, packageVersion);
        }
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element, bool excludeIntegrityFields)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().Where(property => !excludeIntegrityFields || property.Name is not ("packageHash" or "signature")).OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value, excludeIntegrityFields);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonicalJson(writer, item, excludeIntegrityFields);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                throw new InvalidDataException("Customer-install 0.5 package contains an unsupported JSON value.");
        }
    }

    private static void WritePackageObject(Utf8JsonWriter writer, JsonElement element, string path, ContractProfile profile)
    {
        writer.WriteStartObject();
        foreach (var field in PackageFieldsForPath(path, profile))
        {
            writer.WritePropertyName(field);
            var value = element.GetProperty(field);
            if (value.ValueKind == JsonValueKind.Object)
            {
                WritePackageObject(writer, value, path == "root" ? field : $"{path}.{field}", profile);
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        WritePackageObject(writer, item, $"{path}.{field}[]", profile);
                    }
                    else
                    {
                        item.WriteTo(writer);
                    }
                }
                writer.WriteEndArray();
            }
            else
            {
                value.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }

    private static IReadOnlyList<string> PackageFieldsForPath(string path, ContractProfile profile) => path switch
    {
        "root" => TopLevelFields,
        "customer" => ["customerId"],
        "installation" => ["installationId", "environmentId", "tenantId"],
        "deployment" => ["deploymentExportId"],
        "controlPlane" => ControlPlaneFields,
        "runtimeArtifacts" => profile.RuntimeFields,
        "runtimeArtifacts.api" or "runtimeArtifacts.portal" => ArtifactFields,
        "protectedAcquisition" => AcquisitionFields,
        "protectedAcquisition.artifactReferences" => ["api", "portal"],
        "runtimeConfiguration" => ["schemaVersion", "projectionSha256", "publicSettings"],
        "runtimeConfiguration.publicSettings[]" => ["targetApp", "name", "value"],
        _ => throw new InvalidDataException("Customer-install 0.5 package contains an unsupported canonical object.")
    };

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> utf8, string packageVersion)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 });
        var properties = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    properties.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.PropertyName:
                    if (properties.Count == 0 || !properties.Peek().Add(reader.GetString() ?? "")) Fail($"Customer-install {packageVersion} package contains a duplicate JSON property.");
                    break;
                case JsonTokenType.EndObject:
                    properties.Pop();
                    break;
            }
        }
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
        var body = string.Concat(normalized.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("-----BEGIN", StringComparison.Ordinal) && !line.StartsWith("-----END", StringComparison.Ordinal)));
        return Convert.FromBase64String(body);
    }

    private static void RequireExactObject(JsonElement element, IReadOnlyCollection<string> expectedFields, string path)
    {
        if (element.ValueKind != JsonValueKind.Object) Fail($"{path} must be an object.");
        var actual = element.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var expected = expectedFields.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal)) Fail($"{path} must contain only its exact approved fields.");
    }

    private static JsonElement RequireObject(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object) Fail($"{property} must be an object.");
        return value;
    }

    private static string RequireString(JsonElement parent, string property, string? expected = null)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) Fail($"{property} must be a string.");
        var result = value.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(result) || result.Length > 2048 || result != result.Trim() || result.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029' || (character >= '\u202a' && character <= '\u202e') || (character >= '\u2066' && character <= '\u2069')))
        {
            Fail($"{property} must be a trimmed safe string.");
        }
        if (expected is not null && !result.Equals(expected, StringComparison.Ordinal)) Fail($"{property} is not the required contract value.");
        return result;
    }

    private static string RequireUuid(JsonElement parent, string property, bool requireV06Shape = false)
    {
        var value = RequireString(parent, property);
        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty ||
            (requireV06Shape
                ? !V06Uuid.IsMatch(value)
                : !Regex.IsMatch(value, "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
        {
            Fail($"{property} must be a UUID.");
        }
        return value;
    }

    private static string RequireDigest(JsonElement parent, string property)
    {
        var value = RequireString(parent, property);
        if (!LowerDigest.IsMatch(value)) Fail($"{property} must be a lowercase SHA-256 digest.");
        return value;
    }

    private static long RequirePositiveInteger(JsonElement parent, string property)
    {
        var parsed = 0L;
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !Regex.IsMatch(value.GetRawText(), "^(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant) || !value.TryGetInt64(out parsed) || parsed < 1)
        {
            Fail($"{property} must be a canonical positive integer.");
        }
        return parsed;
    }

    private static DateTimeOffset RequireCanonicalUtcDate(JsonElement parent, string property)
    {
        var value = RequireString(parent, property);
        var parsed = default(DateTimeOffset);
        if (!Regex.IsMatch(value, "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3}Z$", RegexOptions.CultureInvariant) ||
            !DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
        {
            Fail($"{property} must be a canonical UTC ISO-8601 timestamp.");
        }
        return parsed;
    }

    private static byte[] RequireBase64Url(JsonElement parent, string property, int expectedByteLength)
    {
        var value = RequireString(parent, property);
        if (!Base64Url.IsMatch(value)) Fail($"{property} must be unpadded base64url.");
        try
        {
            var padding = (value.Length % 4) switch { 0 => "", 2 => "==", 3 => "=", _ => throw new FormatException() };
            var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + padding);
            if (bytes.Length != expectedByteLength) Fail($"{property} has an invalid byte length.");
            return bytes;
        }
        catch (FormatException)
        {
            Fail($"{property} is invalid base64url.");
            return [];
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static void Fail(string message) => throw new InvalidDataException(message);

    private sealed record ContractProfile(
        string PackageVersion,
        string Capability,
        string ManifestVersion,
        IReadOnlyList<string> RuntimeFields,
        bool RequiresProduct,
        bool IsV06)
    {
        public static readonly ContractProfile V05 = new(
            PrivateRuntimeDeliveryPackage.ContractVersionValue,
            PrivateRuntimeDeliveryPackage.CapabilityValue,
            "2.0",
            RuntimeV05Fields,
            false,
            false);

        public static readonly ContractProfile V06 = new(
            PrivateRuntimeDeliveryPackageV06.ContractVersionValue,
            PrivateRuntimeDeliveryPackageV06.CapabilityValue,
            PrivateRuntimeDeliveryPackageV06.ManifestContractVersionValue,
            RuntimeV06Fields,
            true,
            true);
    }
}

/// <summary>
/// Closed package 0.6 / rich manifest 3.0 validator. It is intentionally
/// separate from the historical package 0.5 entry point.
/// </summary>
public sealed class PrivateRuntimeDeliveryV06PackageService
{
    public PrivateRuntimeDeliveryPackage ValidateJson(
        string packageJson,
        PackageTrustOptions trustOptions,
        DateTimeOffset? now = null) =>
        PrivateRuntimeDeliveryPackageService.ValidateV06Json(packageJson, trustOptions, now);

    public static string FormatCanonicalPackage(JsonElement root) =>
        PrivateRuntimeDeliveryPackageService.FormatCanonicalV06Package(root);
}
