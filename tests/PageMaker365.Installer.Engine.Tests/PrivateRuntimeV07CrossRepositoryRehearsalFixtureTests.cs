using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

internal static class PrivateRuntimeV07CrossRepositoryRehearsalFixtureTests
{
    private const string FixtureDirectoryName = "private-runtime-v07-cross-repository-rehearsal-v1";
    private const string ProducerRepository = "cloudbossdev/pagemaker365";
    private const string ProducerBaselineCommit = "7eb338e7ea00118018327a80c5dd925e8c1f091c";
    private const string ProducerMergedCommit = "a4d20dda55d9d6836fc370eb8b071e6d9201f442";
    private const string ProducerMergedTree = "fb2bde45d8b2fcd3bd3abfc22344a433b3d87a29";
    private const string FixtureAuthoritySha256 = "9e12db71f4627aae472f6b0e634e7a957d2f77ebaeb853d1bc124f158a21bf86";
    private const string RuntimeCommit = "c31427d0027adb4fd03de142fde18c4209ca44ce";
    private const string PackageKeyId = "test-only-w09-cross-repository-package-a-ed25519";
    private const string LicenseKeyId = "test-only-w09-cross-repository-license-b-ed25519";
    private const string PackagePemSha256 = "567af416d4df29c5712a013d0bfbfaabcb1931b28cd4b1d5e1c6885bd3febda0";
    private const string LicensePemSha256 = "3f55459b7e38204bc2c081f17b801e9f339054185b3d6fd57c06f95ec9032fcd";
    private const string PackageHash = "sha256:82dbb820acd4762e61e39dbda177c7a6f3cda53b1b14cd83ced80151e548964f";
    private const string ManifestSha256 = "964eb317c66b9b1a24880d272598f5fff42daf471fa23e4dc5a14b4196300b99";
    private const string ProjectionSha256 = "39ed1db6bd0be8d1f0af6e82b67e87053054dc9c8075df9a85f13a5583714147";
    private static readonly DateTimeOffset ValidationTime = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] OmittedSettingNames =
    [
        "API_CONNECTOR_ENTITLEMENTS_SYNC_URL", "API_WEB_PART_ENTITLEMENTS_SYNC_URL", "API_PORT", "API_LOG_LEVEL",
        "API_LICENSE_VALIDATION_URL", "API_WEB_PART_CATALOG_MODE", "API_WEB_PART_REGISTRY_MODE", "WEB_ENABLE_WEB_PART_WORKBENCH",
        "PORT", "API_WEBPART_TEST_ARTIFACTS_ENABLED"
    ];
    private static readonly RuntimeNegative[] RuntimeNegatives =
    [
        new("feature-absent", "artifact-download", 404, "runtime_delivery_unavailable", 0, 0, 0),
        new("feature-false", "artifact-download", 404, "runtime_delivery_unavailable", 0, 0, 0),
        new("package-noncanonical", "artifact-download", 404, "runtime_delivery_unavailable", 0, 0, 0),
        new("package-durable-mismatch", "artifact-download", 404, "runtime_delivery_unavailable", 0, 0, 0),
        new("session-mismatch", "artifact-download", 404, "runtime_delivery_unavailable", 0, 0, 0),
        new("authentication-missing", "artifact-download", 401, "runtime_delivery_auth_required", 0, 0, 0),
        new("authentication-invalid", "artifact-download", 404, "runtime_delivery_unavailable", 0, 0, 0),
        new("onboarding-session-mismatch", "artifact-download", 404, "runtime_delivery_unavailable", 0, 0, 0),
        new("onboarding-code-mismatch", "artifact-download", 404, "runtime_delivery_unavailable", 0, 0, 0),
        new("delivery-session-missing", "artifact-download", 401, "runtime_delivery_session_required", 0, 0, 0),
        new("delivery-session-expired", "artifact-download", 410, "runtime_delivery_session_terminal", 0, 0, 0),
        new("reference-missing", "artifact-download", 401, "runtime_delivery_ref_required", 0, 0, 0),
        new("reference-wrong", "artifact-download", 404, "runtime_delivery_unavailable", 0, 0, 0),
        new("reference-cross-kind", "artifact-download", 404, "runtime_delivery_unavailable", 0, 0, 0),
        new("if-match-invalid", "artifact-download", 412, "runtime_delivery_etag_mismatch", 0, 0, 0),
        new("range-malformed", "artifact-download", 416, "runtime_delivery_range_invalid", 0, 0, 0),
        new("range-multiple", "artifact-download", 416, "runtime_delivery_range_invalid", 0, 0, 0),
        new("range-out-of-bounds", "artifact-download", 416, "runtime_delivery_range_invalid", 0, 0, 0),
        new("artifact-short", "artifact-download", 200, "", 1, 0, 1014),
        new("artifact-long", "artifact-download", 200, "", 1, 0, 0),
        new("artifact-hash-mismatch", "artifact-download", 200, "", 1, 0, 1015),
        new("artifact-redirect", "artifact-download", 503, "runtime_delivery_source_unavailable", 0, 0, 0),
        new("package-expired", "artifact-download", 410, "runtime_delivery_session_terminal", 0, 0, 0),
        new("package-revoked", "artifact-download", 404, "runtime_delivery_unavailable", 0, 0, 0),
        new("rate-limited", "artifact-download", 429, "rate_limited", 0, 0, 33),
        new("aborted", "artifact-download", 200, "", 1, 0, 0),
        new("package-race", "artifact-download", 404, "runtime_delivery_unavailable", 0, 0, 0),
        new("session-race", "artifact-download", 410, "runtime_delivery_session_terminal", 0, 0, 0),
        new("concurrent-downloads", "artifact-download", 200, "", 2, 0, 2030),
        new("receipt-binding-mismatch", "receipt", 400, "runtime_delivery_receipt_binding_invalid", 0, 0, 0),
        new("receipt-event-mismatch", "receipt", 409, "runtime_delivery_receipt_conflict", 0, 1, 0),
        new("receipt-replay", "receipt", 200, "", 0, 1, 924)
    ];
    private static readonly ProtectedNegative[] ProtectedNegatives = BuildProtectedNegatives();

    // Contract order. The final self-manifest is deliberately excluded from its own files array.
    private static readonly Pin[] Pins =
    [
        new("customer-install-0.7.json", 13023, "4b77efdefb48a83cd839c07f0a734c39bfaa38b160e3808e5eb35b6c1feeb642"),
        new("customer-install-package-v0.7.schema.json", 8839, "c083ec6e80859316ae9e6c1278a9579eddffac42732c87d0bcbae81c498f571c"),
        new("runtime-configuration-projection-v2.json", 9719, "76f9fa3058fb33c9658ead53bab9f2cc49747c73cce6ea933b285bb75de3fc29"),
        new("runtime-configuration-projection-v2.schema.json", 38045, "ee146ac4d90ad9ccb08be94cb3f27fa8c43b02c85566f8931656fab82570d63b"),
        new("runtime-configuration.catalog.json", 19132, "441a7083b27c6a76a0910b68b0aab3bd47efaf977cf48d594b5a3e48374e9cc6"),
        new("runtime-configuration.schema.json", 2013, "fb0df2a4b19c0dc8b4a951e4aeb4cd9cb21217ee4e91d86399e64809ce8b8f7e"),
        new("runtime-configuration-source-map-v2.json", 9344, "e19f53134d6747d1239bae6136d71d8101268e1d8293cbe7de42aa2765eb40d7"),
        new("spo-runtime-manifest-v3.json", 810, ManifestSha256),
        new("runtime-release-manifest-v3.schema.json", 2967, "4e78056803f5838acf21f5e0aebcad9201c7002cce3adf61b9908f277c3eff48"),
        new("signing-public-key.pem", 113, PackagePemSha256),
        new("signing-trust.json", 268, "95b1fe3469ebbf30b410d427888e6452f4230686c41c07f9e08afc16bf4f1487"),
        new("signature-vector.json", 495, "8c50df280ef15c4dfd6aad1c19d2c348a239f2e127d16a8cc7588692dbebeaca"),
        new("license-signing-public-key.pem", 113, LicensePemSha256),
        new("license-signature-vector.json", 743, "c9a7ac67342c8aa40b4bcb82bfbbcf5b2831de6bcd9e0c676273811b50370982"),
        new("runtime-delivery-http-vectors.json", 17069, "c691dc09049d8aa120854a74997e272860586dc4241e9bd94d334cf208893c0f"),
        new("protected-setting-acquisition-http-vectors.json", 15249, "d0eed76d2fd3e2574c5e2c16ac21d373760bdb892f52828da83a1858091cd16b"),
        new("provenance.json", 881, "66e8348dabfdfb58df917146bd24153bcc2d901769f20b1a8072161d296de4c4"),
        new("rehearsal.json", 977, "b6fae315a12e0cc3cb0f3f8cb5b56986a681d186994eea8d1039aa0febf87898"),
        new("artifacts/api.zip", 1015, "39ef9ad37514c68dfaad09950e88016516a2f2514fba0e3ca409084f5d66d3fc"),
        new("artifacts/portal.zip", 1809, "e947f260d344ed6371fce23e7f53f4d31c33a7ca11ccac66dbe5bcdfe7824862"),
        new("sha256-manifest.json", 3479, "80839ce837276c5b38e5d49824b68f8f000b35878a37f8fa82388adb71b7d035")
    ];

    public static void RunAll()
    {
        var root = FixtureRoot();
        LockClosedTree(root);
        ValidateSelfManifest(root);
        ValidateAuthorityAndPackage(root);
        ValidateStoredArchives(root);
        ValidateProtocolVectors(root);
        ValidateSecurityAndIsolation(root);
        RunOwnedNegativeChecks(root);
        var bundle = FixtureBundle.Load(root);
        var context = ValidateDynamicEnvelope(bundle, AcceptedPackageTrust(root));
        ValidateSourceMapV2(bundle, context);
        ValidateRuntimeManifestV3(bundle, context);
        ValidateRuntimeDeliveryVectorDocument(bundle, context);
        ValidateProtectedVectorAndLicenseDocuments(bundle, context, StrictUtf8(Read(root, "license-signing-public-key.pem")));
        RunFreshSemanticNegativeMatrix(bundle);
    }

    private static void LockClosedTree(string root)
    {
        var authority = new StringBuilder()
            .Append(ProducerRepository).Append('\n')
            .Append(ProducerMergedCommit).Append('\n')
            .Append(ProducerMergedTree).Append('\n');
        foreach (var pin in Pins) authority.Append(pin.Name).Append('\t').Append(pin.Size).Append('\t').Append(pin.Sha256).Append('\n');
        AssertEx.Equal(FixtureAuthoritySha256, Sha256(Encoding.UTF8.GetBytes(authority.ToString())));
        AssertEx.False(PackagePemSha256.Equals(LicensePemSha256, StringComparison.Ordinal));
        RequireNoReparseOrAlternateStreams(root);
        var actual = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var expected = Pins.Select(pin => pin.Name).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        RequireClosedNames(actual, expected);
        foreach (var pin in Pins) RequirePinnedBytes(pin, File.ReadAllBytes(Path.Combine(root, pin.Name.Replace('/', Path.DirectorySeparatorChar))));
    }

    private static void ValidateSelfManifest(string root)
    {
        var bytes = Read(root, "sha256-manifest.json");
        using var document = ParseCanonicalJson(bytes);
        var value = document.RootElement;
        RequireProperties(value, "schemaVersion", "algorithm", "files");
        AssertEx.Equal("pagemaker365.fixture-sha256-manifest.v1", value.GetProperty("schemaVersion").GetString());
        AssertEx.Equal("SHA-256", value.GetProperty("algorithm").GetString());
        var entries = value.GetProperty("files").EnumerateArray().ToArray();
        AssertEx.Equal(20, entries.Length);
        var expected = Pins[..^1];
        for (var index = 0; index < expected.Length; index++)
        {
            RequireProperties(entries[index], "name", "sizeBytes", "sha256");
            AssertEx.Equal(expected[index].Name, entries[index].GetProperty("name").GetString());
            AssertEx.Equal(expected[index].Size, entries[index].GetProperty("sizeBytes").GetInt64());
            AssertEx.Equal(expected[index].Sha256, entries[index].GetProperty("sha256").GetString());
        }
        AssertEx.False(entries.Any(item => item.GetProperty("name").GetString() == "sha256-manifest.json"));
    }

    private static void ValidateAuthorityAndPackage(string root)
    {
        using var provenance = ParseCanonicalJson(Read(root, "provenance.json"));
        var p = provenance.RootElement;
        RequireProperties(p, "schemaVersion", "classification", "producerRepository", "producerBaselineCommit", "fixtureContractVersion", "runtimeContractRepository", "runtimeContractCommit", "generatorPath", "fixedNow", "synthetic", "nondeployable", "authorizesDeployment", "containsCustomerData", "containsCustomerSecret", "containsReusableCredential", "containsPrivateKey", "runtimeArtifactBytesDerivedFromSourceCommit");
        AssertEx.Equal(ProducerRepository, p.GetProperty("producerRepository").GetString());
        AssertEx.Equal(ProducerBaselineCommit, p.GetProperty("producerBaselineCommit").GetString());
        AssertEx.Equal(RuntimeCommit, p.GetProperty("runtimeContractCommit").GetString());
        AssertEx.Equal("pagemaker365.dynamic-local-runtime-handoff.v3", p.GetProperty("fixtureContractVersion").GetString());
        AssertEx.True(p.GetProperty("synthetic").GetBoolean());
        AssertEx.True(p.GetProperty("nondeployable").GetBoolean());
        foreach (var name in new[] { "authorizesDeployment", "containsCustomerData", "containsCustomerSecret", "containsReusableCredential", "containsPrivateKey", "runtimeArtifactBytesDerivedFromSourceCommit" })
            AssertEx.False(p.GetProperty(name).GetBoolean());

        using var rehearsal = ParseCanonicalJson(Read(root, "rehearsal.json"));
        var rehearsalRoot = rehearsal.RootElement;
        AssertEx.Equal("synthetic-test-only", rehearsalRoot.GetProperty("classification").GetString());
        AssertEx.Equal("signing-trust.json", rehearsalRoot.GetProperty("packageSigningTrustFile").GetString());
        AssertEx.Equal("license-signing-public-key.pem", rehearsalRoot.GetProperty("licenseSigningTrustFile").GetString());
        AssertEx.True(rehearsalRoot.GetProperty("requiresInjectedTransport").GetBoolean());
        AssertEx.True(rehearsalRoot.GetProperty("requiresExplicitOptIn").GetBoolean());
        AssertEx.False(rehearsalRoot.GetProperty("authorizesDeployment").GetBoolean());
        AssertEx.False(rehearsalRoot.GetProperty("containsPrivateKey").GetBoolean());

        var packagePemBytes = Read(root, "signing-public-key.pem");
        var licensePemBytes = Read(root, "license-signing-public-key.pem");
        RequirePinnedBytes(Pins.Single(pin => pin.Name == "signing-public-key.pem"), packagePemBytes);
        RequirePinnedBytes(Pins.Single(pin => pin.Name == "license-signing-public-key.pem"), licensePemBytes);
        var packagePem = StrictUtf8(packagePemBytes);
        var licensePem = StrictUtf8(licensePemBytes);
        AssertEx.False(packagePem.Equals(licensePem, StringComparison.Ordinal));

        using var trust = ParseCanonicalJson(Read(root, "signing-trust.json"));
        RequireProperties(trust.RootElement, "schemaVersion", "keyId", "publicKeyFile", "publicKeySha256");
        AssertEx.Equal(PackageKeyId, trust.RootElement.GetProperty("keyId").GetString());
        AssertEx.Equal("signing-public-key.pem", trust.RootElement.GetProperty("publicKeyFile").GetString());
        AssertEx.Equal(PackagePemSha256, trust.RootElement.GetProperty("publicKeySha256").GetString());

        var catalog = RuntimeConfigurationCatalogV1Authority.Create(Read(root, "runtime-configuration.catalog.json"), Read(root, "runtime-configuration.schema.json"));
        var service = new PrivateRuntimeDeliveryV07PackageService(catalog);
        var packageJson = StrictUtf8(Read(root, "customer-install-0.7.json"));
        var packageTrust = new PackageTrustOptions { TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal) { [PackageKeyId] = packagePem } };
        var package = service.ValidateJson(packageJson, packageTrust, ValidationTime);
        AssertEx.Equal("0.7", package.ContractVersion);
        AssertEx.Equal(PackageHash, package.PackageHash);
        AssertEx.Equal(RuntimeCommit, package.SourceCommit);
        AssertEx.Equal(ManifestSha256, package.ManifestSha256);
        AssertEx.Equal(42, package.RuntimeConfiguration.PublicSettings.Count);
        AssertEx.Equal(4, package.RuntimeConfiguration.ProtectedSettings.Count);
        AssertEx.Equal(70, package.RuntimeConfiguration.Catalog.SettingCount);
        AssertEx.False(package.RuntimeConfiguration.ConnectorSynchronization);
        AssertEx.False(package.RuntimeConfiguration.WebPartSynchronization);
        AssertEx.Equal(ProjectionSha256, package.RuntimeConfiguration.ProjectionSha256);
        AssertEx.Equal("pm365-runtime-1.4.3+c31427d", package.ReleaseId);
        AssertEx.Equal("1.4.3", package.RuntimeVersion);
        AssertEx.Equal(Pins.Single(pin => pin.Name == "artifacts/api.zip").Sha256, package.Api.Sha256);
        AssertEx.Equal(Pins.Single(pin => pin.Name == "artifacts/portal.zip").Sha256, package.Portal.Sha256);

        using var packageDocument = ParseCanonicalJson(Read(root, "customer-install-0.7.json"));
        using var projectionDocument = ParseCanonicalJson(Read(root, "runtime-configuration-projection-v2.json"));
        AssertEx.True(PrivateRuntimeCanonicalJson.Canonicalize(projectionDocument.RootElement)
            .SequenceEqual(PrivateRuntimeCanonicalJson.Canonicalize(packageDocument.RootElement.GetProperty("runtimeConfiguration"))));
        var signingPayload = PrivateRuntimeCanonicalJson.Canonicalize(packageDocument.RootElement, excludePackageIntegrity: true);
        using var signatureVector = ParseCanonicalJson(Read(root, "signature-vector.json"));
        AssertEx.Equal(PackageKeyId, signatureVector.RootElement.GetProperty("keyId").GetString());
        AssertEx.Equal(PackageHash, signatureVector.RootElement.GetProperty("packageHash").GetString());
        AssertEx.Equal(Sha256(signingPayload), signatureVector.RootElement.GetProperty("canonicalPayloadSha256").GetString());
        AssertEx.True(Verify(packagePem, signingPayload, DecodeBase64Url(signatureVector.RootElement.GetProperty("signature").GetString()!)));
        AssertEx.False(Verify(licensePem, signingPayload, DecodeBase64Url(signatureVector.RootElement.GetProperty("signature").GetString()!)));

        using var protectedVectors = ParseCanonicalJson(Read(root, "protected-setting-acquisition-http-vectors.json"));
        var signedLicense = protectedVectors.RootElement.GetProperty("positive").GetProperty("response").GetProperty("value");
        var licensePayload = PrivateRuntimeCanonicalJson.Canonicalize(signedLicense.GetProperty("payload"));
        var licenseSignature = DecodeBase64Url(signedLicense.GetProperty("signature").GetProperty("value").GetString()!);
        using var licenseVector = ParseCanonicalJson(Read(root, "license-signature-vector.json"));
        AssertEx.Equal(LicenseKeyId, licenseVector.RootElement.GetProperty("keyId").GetString());
        AssertEx.Equal(LicensePemSha256, licenseVector.RootElement.GetProperty("publicKeySha256").GetString());
        AssertEx.Equal(Sha256(PrivateRuntimeCanonicalJson.Canonicalize(signedLicense)), licenseVector.RootElement.GetProperty("signedPayloadSha256").GetString());
        AssertEx.True(Verify(licensePem, licensePayload, licenseSignature));
        AssertEx.False(Verify(packagePem, licensePayload, licenseSignature));

        AssertEx.True(Read(root, "customer-install-package-v0.7.schema.json").SequenceEqual(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "schemas", "customer-install-v0.7.schema.json"))));
        AssertEx.True(Read(root, "runtime-configuration-projection-v2.schema.json").SequenceEqual(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "schemas", "runtime-configuration-projection-v2.schema.json"))));
        AssertEx.Equal(ManifestSha256, Sha256(Read(root, "spo-runtime-manifest-v3.json")));
        AssertEx.Equal(ProjectionSha256, packageDocument.RootElement.GetProperty("runtimeConfiguration").GetProperty("projectionSha256").GetString());
    }

    private static void ValidateStoredArchives(string root)
    {
        var api = ParseStoredZip(Read(root, "artifacts/api.zip"));
        var portal = ParseStoredZip(Read(root, "artifacts/portal.zip"));
        AssertEx.True(api.Select(entry => entry.Name).SequenceEqual(new[] { ".pm365/provenance.json", "dist/index.js", "package.json" }, StringComparer.Ordinal));
        AssertEx.True(portal.Select(entry => entry.Name).SequenceEqual(new[] { ".pm365/generate-web-runtime-config.mjs", ".pm365/provenance.json", ".pm365/start-portal-runtime.mjs", "auth-redirect.html", "index.html" }, StringComparer.Ordinal));
        foreach (var entry in api.Concat(portal))
        {
            AssertEx.False(entry.Name.Contains("staticwebapp.config.json", StringComparison.OrdinalIgnoreCase));
            AssertEx.False(entry.Name.StartsWith('/') || entry.Name.Contains("..", StringComparison.Ordinal) || entry.Name.Contains('\\'));
        }
        var throwOnly = "throw new Error(\"Synthetic PageMaker365 rehearsal artifact is nondeployable.\");\n";
        AssertEx.Equal(throwOnly, StrictUtf8(api.Single(entry => entry.Name == "dist/index.js").Data));
        AssertEx.Equal(throwOnly, StrictUtf8(portal.Single(entry => entry.Name == ".pm365/generate-web-runtime-config.mjs").Data));
        AssertEx.Equal(throwOnly, StrictUtf8(portal.Single(entry => entry.Name == ".pm365/start-portal-runtime.mjs").Data));
        AssertEx.True(StrictUtf8(api.Single(entry => entry.Name == "package.json").Data).Contains("synthetic-rehearsal-api", StringComparison.Ordinal));
        AssertEx.True(StrictUtf8(portal.Single(entry => entry.Name == "index.html").Data).Contains("nondeployable", StringComparison.OrdinalIgnoreCase));
        AssertEx.True(StrictUtf8(portal.Single(entry => entry.Name == "auth-redirect.html").Data).Contains("nondeployable", StringComparison.OrdinalIgnoreCase));
        foreach (var entry in api.Concat(portal).Where(entry => entry.Name.EndsWith("provenance.json", StringComparison.Ordinal)))
        {
            using var provenance = ParseCanonicalJson(entry.Data);
            AssertEx.Equal("pagemaker365.runtime-provenance.v1", provenance.RootElement.GetProperty("schemaVersion").GetString());
            AssertEx.Equal("cloudbossdev/spo-ui", provenance.RootElement.GetProperty("sourceRepository").GetString());
            AssertEx.Equal(RuntimeCommit, provenance.RootElement.GetProperty("sourceCommit").GetString());
        }
    }

    private static void ValidateProtocolVectors(string root)
    {
        using var runtime = ParseCanonicalJson(Read(root, "runtime-delivery-http-vectors.json"));
        var value = runtime.RootElement;
        RequireProperties(value, "schemaVersion", "classification", "fixedNow", "authorization", "sessionCreation", "artifactDownloads", "receipt", "negativeVectors", "containsReusableCredential", "authorizesDeployment");
        AssertEx.Equal("synthetic-test-only", value.GetProperty("classification").GetString());
        AssertEx.True(value.GetProperty("authorization").GetProperty("requiredHeaderNames").EnumerateArray().Select(item => item.GetString()).SequenceEqual(new[] { "Authorization", "X-PM365-Onboarding-Session", "X-PM365-Onboarding-Code" }));
        AssertEx.False(value.GetProperty("authorization").GetProperty("containsHeaderValues").GetBoolean());
        var downloads = value.GetProperty("artifactDownloads").EnumerateArray().ToArray();
        AssertEx.True(downloads.Select(item => item.GetProperty("id").GetString()).SequenceEqual(new[] { "api-full", "api-range", "portal-full", "portal-range" }));
        foreach (var download in downloads)
        {
            var required = download.GetProperty("requestHeaders").GetProperty("requiredNames").EnumerateArray().Select(item => item.GetString()).ToArray();
            AssertEx.Equal(5, required.Length);
            AssertEx.False(download.GetProperty("requestHeaders").GetProperty("containsHeaderValues").GetBoolean());
            AssertEx.Equal("private, no-store", download.GetProperty("expectedHeaders").GetProperty("Cache-Control").GetString());
            var bodyFile = download.GetProperty("bodyFile").GetString()!;
            var pin = Pins.Single(item => item.Name == bodyFile);
            var offset = download.GetProperty("bodyOffset").GetInt32();
            var length = download.GetProperty("bodyLength").GetInt32();
            AssertEx.True(offset >= 0 && length > 0 && offset + length <= pin.Size);
        }
        RequireNegativeRows(value.GetProperty("negativeVectors"), 32, "expectedArtifactOpenCount", "expectedReceiptMutationCount");
        AssertEx.Equal(1, value.GetProperty("receipt").GetProperty("expected").GetProperty("mutationCount").GetInt32());
        AssertEx.False(value.GetProperty("containsReusableCredential").GetBoolean());
        AssertEx.False(value.GetProperty("authorizesDeployment").GetBoolean());

        using var protectedDocument = ParseCanonicalJson(Read(root, "protected-setting-acquisition-http-vectors.json"));
        var protectedValue = protectedDocument.RootElement;
        var headerNames = protectedValue.GetProperty("authorization").GetProperty("requiredHeaderNames").EnumerateArray().Select(item => item.GetString()).ToArray();
        AssertEx.True(headerNames.SequenceEqual(new[] { "Authorization", "X-PM365-Onboarding-Session", "X-PM365-Onboarding-Code", "X-PM365-Runtime-Delivery-Session" }));
        AssertEx.False(protectedValue.GetProperty("authorization").GetProperty("containsHeaderValues").GetBoolean());
        AssertEx.Equal("Authorization, X-PM365-Onboarding-Session, X-PM365-Runtime-Delivery-Session", protectedValue.GetProperty("positive").GetProperty("expectedHeaders").GetProperty("Vary").GetString());
        AssertEx.Equal("private, no-store", protectedValue.GetProperty("positive").GetProperty("expectedHeaders").GetProperty("Cache-Control").GetString());
        RequireNegativeRows(protectedValue.GetProperty("negativeVectors"), 40, "expectedProtectedReadCount", "expectedRedemptionCount");
        AssertEx.True(protectedValue.GetProperty("containsSyntheticProtectedValue").GetBoolean());
        AssertEx.False(protectedValue.GetProperty("containsCustomerSecret").GetBoolean());
        AssertEx.False(protectedValue.GetProperty("containsReusableCredential").GetBoolean());
        AssertEx.False(protectedValue.GetProperty("authorizesDeployment").GetBoolean());
        var vectorText = StrictUtf8(Read(root, "runtime-delivery-http-vectors.json")) + StrictUtf8(Read(root, "protected-setting-acquisition-http-vectors.json"));
        foreach (var forbidden in new[] { "Bearer ", "oneTimeCode", "setupCode", "access_token", "refresh_token" })
            AssertEx.False(vectorText.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateSecurityAndIsolation(string root)
    {
        var all = Pins.SelectMany(pin => Read(root, pin.Name)).ToArray();
        RequireSafeFixtureBytes(all);
        foreach (var relative in new[] { "src", "modules", "infra", "scripts" })
        {
            var directory = Path.Combine(RepositoryRoot(), relative);
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Where(IsTextSource))
            {
                var text = File.ReadAllText(file);
                AssertEx.False(text.Contains(FixtureDirectoryName, StringComparison.Ordinal));
                AssertEx.False(text.Contains(nameof(PrivateRuntimeV07CrossRepositoryRehearsalFixtureTests), StringComparison.Ordinal));
                AssertEx.False(text.Contains("PM365_DYNAMIC_V07", StringComparison.Ordinal));
            }
        }
        AssertEx.Equal(0, typeof(InstallerEngine).GetMethods().Count(method => method.Name.Contains("V07", StringComparison.OrdinalIgnoreCase)));
        AssertEx.Equal(0, typeof(PrivateRuntimeDeliveryClient).GetMethods().Count(method => method.Name.Contains("V07", StringComparison.OrdinalIgnoreCase)));
    }

    private static void RunOwnedNegativeChecks(string root)
    {
        var expected = Pins.Select(pin => pin.Name).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        AssertEx.Throws<InvalidDataException>(() => RequireClosedNames(expected.Append("unexpected.json").ToArray(), expected));
        AssertEx.Throws<InvalidDataException>(() => RequireClosedNames(expected[..^1], expected));
        foreach (var name in new[] { "customer-install-0.7.json", "runtime-configuration-projection-v2.json", "spo-runtime-manifest-v3.json", "artifacts/api.zip", "runtime-delivery-http-vectors.json", "protected-setting-acquisition-http-vectors.json" })
        {
            var pin = Pins.Single(item => item.Name == name);
            var altered = Read(root, name).ToArray();
            altered[0] ^= 1;
            AssertEx.Throws<InvalidDataException>(() => RequirePinnedBytes(pin, altered));
        }
        var privateKeyMarker = string.Concat("-----BEGIN ", "PRIVATE KEY-----");
        AssertEx.Throws<InvalidDataException>(() => RequireSafeFixtureBytes(Encoding.UTF8.GetBytes(privateKeyMarker)));

        var packagePem = StrictUtf8(Read(root, "signing-public-key.pem"));
        var licensePem = StrictUtf8(Read(root, "license-signing-public-key.pem"));
        var packageJson = StrictUtf8(Read(root, "customer-install-0.7.json"));
        var catalog = RuntimeConfigurationCatalogV1Authority.Create(Read(root, "runtime-configuration.catalog.json"), Read(root, "runtime-configuration.schema.json"));
        var service = new PrivateRuntimeDeliveryV07PackageService(catalog);
        var wrongTrust = new PackageTrustOptions { TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal) { [PackageKeyId] = licensePem } };
        AssertEx.Throws<InvalidDataException>(() => service.ValidateJson(packageJson, wrongTrust, ValidationTime));
        var tamperedSignature = packageJson.Replace("\"signature\": \"K", "\"signature\": \"L", StringComparison.Ordinal);
        AssertEx.False(tamperedSignature.Equals(packageJson, StringComparison.Ordinal));
        var correctTrust = new PackageTrustOptions { TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal) { [PackageKeyId] = packagePem } };
        AssertEx.Throws<InvalidDataException>(() => service.ValidateJson(tamperedSignature, correctTrust, ValidationTime));
    }

    private static PackageTrustOptions AcceptedPackageTrust(string root) => new()
    {
        TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PackageKeyId] = StrictUtf8(Read(root, "signing-public-key.pem"))
        }
    };

    private static SemanticContext ValidateDynamicEnvelope(FixtureBundle bundle, PackageTrustOptions trust)
    {
        var expectedNames = Pins.Select(pin => pin.Name).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        RequireClosedNames(bundle.Files.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray(), expectedNames);
        using var self = ParseCandidateJson(bundle["sha256-manifest.json"]);
        RequireProperties(self.RootElement, "schemaVersion", "algorithm", "files");
        RequireString(self.RootElement, "schemaVersion", "pagemaker365.fixture-sha256-manifest.v1", "fixture_self_manifest_identity");
        RequireString(self.RootElement, "algorithm", "SHA-256", "fixture_self_manifest_identity");
        var rows = self.RootElement.GetProperty("files").EnumerateArray().ToArray();
        if (rows.Length != Pins.Length - 1) Fail("fixture_self_manifest_count");
        for (var index = 0; index < rows.Length; index++)
        {
            RequireProperties(rows[index], "name", "sizeBytes", "sha256");
            var name = RequireString(rows[index], "name", null, "fixture_self_manifest_shape");
            if (name != Pins[index].Name || name == "sha256-manifest.json") Fail("fixture_self_manifest_order");
            var bytes = bundle[name];
            if (RequireInt64(rows[index], "sizeBytes", "fixture_self_manifest_shape") != bytes.LongLength ||
                RequireString(rows[index], "sha256", null, "fixture_self_manifest_shape") != Sha256(bytes))
                Fail("fixture_self_manifest_binding");
        }

        var catalog = RuntimeConfigurationCatalogV1Authority.Create(bundle["runtime-configuration.catalog.json"], bundle["runtime-configuration.schema.json"]);
        var service = new PrivateRuntimeDeliveryV07PackageService(catalog);
        var packageText = StrictUtf8(bundle["customer-install-0.7.json"]);
        var package = service.ValidateJson(packageText, trust, ValidationTime);
        using var packageDocument = ParseCandidateJson(bundle["customer-install-0.7.json"]);
        return new SemanticContext(bundle, service, package, packageDocument.RootElement.Clone());
    }

    private static void ValidateProjectionPair(SemanticContext context)
    {
        using var standalone = ParseCandidateJson(context.Bundle["runtime-configuration-projection-v2.json"]);
        var packageProjection = context.PackageRoot.GetProperty("runtimeConfiguration");
        if (!PrivateRuntimeCanonicalJson.Canonicalize(standalone.RootElement).SequenceEqual(PrivateRuntimeCanonicalJson.Canonicalize(packageProjection)))
            Fail("fixture_projection_package_cross_pair");
        if (context.Package.RuntimeConfiguration.ProjectionSha256 != ProjectionDigest(standalone.RootElement))
            Fail("fixture_projection_digest_binding");
    }

    private static void ValidateSourceMapV2(FixtureBundle bundle, SemanticContext context)
    {
        ValidateProjectionPair(context);
        using var sourceMap = ParseCandidateJson(bundle["runtime-configuration-source-map-v2.json"]);
        var root = sourceMap.RootElement;
        RequireShape(root, "fixture_source_map_root", "schemaVersion", "catalog", "entries");
        RequireString(root, "schemaVersion", "pagemaker365.runtime-configuration-source-map.v2", "fixture_source_map_identity");
        var catalog = RequireObject(root, "catalog", "fixture_source_map_catalog");
        RequireShape(catalog, "fixture_source_map_catalog", "schemaVersion", "sourceRepository", "sourceCommit", "catalogSha256", "catalogSchemaSha256");
        RequireString(catalog, "schemaVersion", "pagemaker365.runtime-configuration.v1", "fixture_source_map_catalog");
        RequireString(catalog, "sourceRepository", "cloudbossdev/spo-ui", "fixture_source_map_catalog");
        RequireString(catalog, "sourceCommit", RuntimeCommit, "fixture_source_map_catalog");
        RequireString(catalog, "catalogSha256", Pins.Single(pin => pin.Name == "runtime-configuration.catalog.json").Sha256, "fixture_source_map_catalog");
        RequireString(catalog, "catalogSchemaSha256", Pins.Single(pin => pin.Name == "runtime-configuration.schema.json").Sha256, "fixture_source_map_catalog");
        var entries = RequireArray(root, "entries", "fixture_source_map_shape").EnumerateArray().ToArray();
        var projection = context.PackageRoot.GetProperty("runtimeConfiguration").GetProperty("publicSettings").EnumerateArray().ToArray();
        if (entries.Length != 42 || projection.Length != 42) Fail("fixture_source_map_count");
        var qualified = new HashSet<string>(StringComparer.Ordinal);
        var sourceBindings = new HashSet<string>(StringComparer.Ordinal);
        var owners = new Dictionary<string, int>(StringComparer.Ordinal);
        var allowedOwners = new HashSet<string>(new[]
        {
            "spo-ui/runtime-release", "spo-ui/producer-constant", "control-plane/rollout-policy",
            "control-plane/consent-result", "control-plane/provisioning-result", "control-plane/customer-profile"
        }, StringComparer.Ordinal);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            RequireShape(entry, "fixture_source_map_shape", "targetApp", "name", "valueType", "ownerSource", "sourceField");
            var target = RequireString(entry, "targetApp", null, "fixture_source_map_shape");
            var name = RequireString(entry, "name", null, "fixture_source_map_shape");
            var type = RequireString(entry, "valueType", null, "fixture_source_map_shape");
            var owner = RequireString(entry, "ownerSource", null, "fixture_source_map_shape");
            var field = RequireString(entry, "sourceField", null, "fixture_source_map_shape");
            if (target is not ("api" or "portal") || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(field)) Fail("fixture_source_map_domain");
            if (!allowedOwners.Contains(owner)) Fail("fixture_source_map_owner");
            var key = target + ":" + name;
            if (!qualified.Add(key) || !sourceBindings.Add(owner + ":" + field)) Fail("fixture_source_map_duplicate");
            if (!field.Equals(name, StringComparison.Ordinal)) Fail("fixture_source_map_source_binding");
            owners[owner] = owners.GetValueOrDefault(owner) + 1;
            var projected = projection[index];
            if (target != projected.GetProperty("targetApp").GetString() || name != projected.GetProperty("name").GetString() || type != projected.GetProperty("valueType").GetString())
                Fail("fixture_source_map_projection_order");
            if (OmittedSettingNames.Contains(name, StringComparer.Ordinal) || name is "DATABASE_URL" or "API_ENTRA_CLIENT_SECRET" or "API_LICENSE_SIGNED_PAYLOAD" or "API_IMAGE_ASSET_CURSOR_SECRET")
                Fail("fixture_source_map_forbidden_setting");
        }
        var expectedOwners = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["spo-ui/runtime-release"] = 3, ["spo-ui/producer-constant"] = 6, ["control-plane/rollout-policy"] = 10,
            ["control-plane/consent-result"] = 8, ["control-plane/provisioning-result"] = 10, ["control-plane/customer-profile"] = 5
        };
        if (owners.Count != expectedOwners.Count || expectedOwners.Any(item => owners.GetValueOrDefault(item.Key) != item.Value))
            Fail("fixture_source_map_owner_partition");
    }

    private static void ValidateRuntimeManifestV3(FixtureBundle bundle, SemanticContext context)
    {
        ValidateProjectionPair(context);
        using var manifestDocument = ParseCandidateJson(bundle["spo-runtime-manifest-v3.json"]);
        var manifest = manifestDocument.RootElement;
        RequireShape(manifest, "fixture_manifest_shape", "contractVersion", "product", "releaseId", "runtimeVersion", "sourceRepository", "sourceCommit", "provenanceSchemaVersion", "api", "portal");
        RequireString(manifest, "contractVersion", "3.0", "fixture_manifest_identity");
        RequireString(manifest, "product", "PageMaker365", "fixture_manifest_identity");
        RequireString(manifest, "releaseId", context.Package.ReleaseId, "fixture_manifest_release_binding");
        RequireString(manifest, "runtimeVersion", context.Package.RuntimeVersion, "fixture_manifest_release_binding");
        RequireString(manifest, "sourceRepository", "cloudbossdev/spo-ui", "fixture_manifest_source");
        RequireString(manifest, "sourceCommit", RuntimeCommit, "fixture_manifest_source");
        RequireString(manifest, "provenanceSchemaVersion", "pagemaker365.runtime-provenance.v1", "fixture_manifest_provenance");
        if (!Regex.IsMatch(context.Package.RuntimeVersion, "^(0|[1-9][0-9]{0,9})\\.(0|[1-9][0-9]{0,9})\\.(0|[1-9][0-9]{0,9})$") ||
            context.Package.RuntimeVersion.Split('.').Any(value => !int.TryParse(value, out _))) Fail("fixture_manifest_version");
        ValidateManifestArtifact(bundle, RequireObject(manifest, "api", "fixture_manifest_artifact_shape"), context.Package.Api, "api", "artifacts/api.zip", "node dist/index.js");
        ValidateManifestArtifact(bundle, RequireObject(manifest, "portal", "fixture_manifest_artifact_shape"), context.Package.Portal, "portal", "artifacts/portal.zip", "node .pm365/start-portal-runtime.mjs");
        var digest = Sha256(bundle["spo-runtime-manifest-v3.json"]);
        if (digest != context.Package.ManifestSha256 || digest != context.Package.RuntimeConfiguration.Binding.ManifestSha256)
            Fail("fixture_manifest_digest_binding");
        var reconstructed = BuildManifestFromPackage(context.PackageRoot.GetProperty("runtimeArtifacts"));
        if (!reconstructed.SequenceEqual(bundle["spo-runtime-manifest-v3.json"])) Fail("fixture_manifest_package_cross_pair");
    }

    private static void ValidateManifestArtifact(FixtureBundle bundle, JsonElement value, PrivateRuntimeArtifact packageArtifact, string kind, string bodyFile, string startup)
    {
        RequireShape(value, "fixture_manifest_artifact_shape", "fileName", "sizeBytes", "sha256", "startupCommand", "artifactKind");
        var fileName = RequireString(value, "fileName", null, "fixture_manifest_artifact_shape");
        var size = RequireInt64(value, "sizeBytes", "fixture_manifest_artifact_shape");
        var hash = RequireString(value, "sha256", null, "fixture_manifest_artifact_shape");
        if (RequireString(value, "artifactKind", null, "fixture_manifest_artifact_shape") != kind ||
            RequireString(value, "startupCommand", null, "fixture_manifest_artifact_shape") != startup) Fail("fixture_manifest_artifact_identity");
        if (fileName.Length > 255 || !Regex.IsMatch(fileName, "^[A-Za-z0-9][A-Za-z0-9._-]*\\.zip$") || fileName.Contains("..", StringComparison.Ordinal))
            Fail("fixture_manifest_artifact_name");
        var body = bundle[bodyFile];
        if (size != body.LongLength || hash != Sha256(body)) Fail("fixture_manifest_artifact_bytes");
        if (packageArtifact.ArtifactKind != kind || packageArtifact.FileName != fileName || packageArtifact.SizeBytes != size || packageArtifact.Sha256 != hash || packageArtifact.StartupCommand != startup)
            Fail("fixture_manifest_package_artifact_cross_pair");
        IReadOnlyList<ZipEntry> entries;
        try { entries = ParseStoredZip(body); } catch (InvalidDataException) { Fail("fixture_archive_structure"); return; }
        var expectedNames = kind == "api"
            ? new[] { ".pm365/provenance.json", "dist/index.js", "package.json" }
            : new[] { ".pm365/generate-web-runtime-config.mjs", ".pm365/provenance.json", ".pm365/start-portal-runtime.mjs", "auth-redirect.html", "index.html" };
        if (!entries.Select(entry => entry.Name).SequenceEqual(expectedNames, StringComparer.Ordinal)) Fail("fixture_archive_kind_cross_pair");
    }

    private static void ValidateRuntimeDeliveryVectorDocument(FixtureBundle bundle, SemanticContext context)
    {
        using var document = ParseCandidateJson(bundle["runtime-delivery-http-vectors.json"]);
        var root = document.RootElement;
        RequireShape(root, "fixture_runtime_vector_root", "schemaVersion", "classification", "fixedNow", "authorization", "sessionCreation", "artifactDownloads", "receipt", "negativeVectors", "containsReusableCredential", "authorizesDeployment");
        RequireString(root, "schemaVersion", "pagemaker365.private-runtime-delivery-rehearsal.v1", "fixture_runtime_vector_identity");
        RequireString(root, "classification", "synthetic-test-only", "fixture_runtime_vector_identity");
        RequireString(root, "fixedNow", "2026-08-30T12:00:00.000Z", "fixture_runtime_vector_identity");
        RequireBoolean(root, "containsReusableCredential", false, "fixture_runtime_vector_security");
        RequireBoolean(root, "authorizesDeployment", false, "fixture_runtime_vector_security");
        var authorization = RequireObject(root, "authorization", "fixture_runtime_vector_authorization");
        RequireShape(authorization, "fixture_runtime_vector_authorization", "mode", "requiredHeaderNames", "containsHeaderValues");
        RequireString(authorization, "mode", "injected-test-only", "fixture_runtime_vector_authorization");
        RequireStringArray(authorization, "requiredHeaderNames", ["Authorization", "X-PM365-Onboarding-Session", "X-PM365-Onboarding-Code"], "fixture_runtime_vector_authorization");
        RequireBoolean(authorization, "containsHeaderValues", false, "fixture_runtime_vector_authorization");

        var session = RequireObject(root, "sessionCreation", "fixture_runtime_vector_session");
        RequireShape(session, "fixture_runtime_vector_session", "method", "path", "request", "expected");
        RequireString(session, "method", "POST", "fixture_runtime_vector_session");
        RequireString(session, "path", "/api/onboarding/installer/runtime-delivery-sessions", "fixture_runtime_vector_session");
        var sessionRequest = RequireObject(session, "request", "fixture_runtime_vector_session");
        RequireShape(sessionRequest, "fixture_runtime_vector_session", "packageFile");
        RequireString(sessionRequest, "packageFile", "customer-install-0.7.json", "fixture_runtime_vector_session");
        var sessionExpected = RequireObject(session, "expected", "fixture_runtime_vector_session");
        RequireShape(sessionExpected, "fixture_runtime_vector_session", "status", "response");
        if (RequireInt32(sessionExpected, "status", "fixture_runtime_vector_session") != 201) Fail("fixture_runtime_vector_session");
        var sessionResponse = RequireObject(sessionExpected, "response", "fixture_runtime_vector_session");
        RequireShape(sessionResponse, "fixture_runtime_vector_session", "ok", "created", "deliverySession");
        RequireBoolean(sessionResponse, "ok", true, "fixture_runtime_vector_session");
        RequireBoolean(sessionResponse, "created", true, "fixture_runtime_vector_session");
        var deliverySession = RequireObject(sessionResponse, "deliverySession", "fixture_runtime_vector_session");
        RequireShape(deliverySession, "fixture_runtime_vector_session", "contractVersion", "deliverySessionId", "expiresAt", "artifactKinds", "status");
        RequireString(deliverySession, "contractVersion", "pagemaker365.runtime-delivery-session.v1", "fixture_runtime_vector_session");
        RequireString(deliverySession, "deliverySessionId", "rds_SYNTHETIC_W09_REHEARSAL_0001", "fixture_runtime_vector_session");
        RequireString(deliverySession, "status", "active", "fixture_runtime_vector_session");
        RequireStringArray(deliverySession, "artifactKinds", ["api", "portal"], "fixture_runtime_vector_session");
        RequireString(deliverySession, "expiresAt", "2099-08-30T12:00:00.000Z", "fixture_runtime_vector_session_expiry_binding");
        RequireFutureUtc(deliverySession, "expiresAt", ValidationTime, "fixture_runtime_vector_session");

        var downloads = RequireArray(root, "artifactDownloads", "fixture_runtime_vector_download").EnumerateArray().ToArray();
        var expectedDownloads = new[]
        {
            new DownloadExpectation("api-full", "api", "artifacts/api.zip", 0, bundle["artifacts/api.zip"].Length, null),
            new DownloadExpectation("api-range", "api", "artifacts/api.zip", 17, 97, "bytes=17-113"),
            new DownloadExpectation("portal-full", "portal", "artifacts/portal.zip", 0, bundle["artifacts/portal.zip"].Length, null),
            new DownloadExpectation("portal-range", "portal", "artifacts/portal.zip", 29, 131, "bytes=29-159")
        };
        if (downloads.Length != expectedDownloads.Length) Fail("fixture_runtime_vector_download_count");
        for (var index = 0; index < downloads.Length; index++) ValidateDownload(bundle, context, downloads[index], expectedDownloads[index]);

        var receipt = RequireObject(root, "receipt", "fixture_runtime_vector_receipt");
        RequireShape(receipt, "fixture_runtime_vector_receipt", "method", "path", "request", "expected");
        RequireString(receipt, "method", "POST", "fixture_runtime_vector_receipt");
        RequireString(receipt, "path", "/api/onboarding/installer/runtime-delivery-receipts", "fixture_runtime_vector_receipt");
        var request = RequireObject(receipt, "request", "fixture_runtime_vector_receipt");
        RequireShape(request, "fixture_runtime_vector_receipt", "contractVersion", "deliverySessionId", "packageHash", "releaseId", "manifestSha256", "eventId", "idempotencyKey", "occurredAt", "installerVersion", "outcome", "artifacts", "safeResult");
        RequireString(request, "contractVersion", "pagemaker365.runtime-delivery-receipt.v1", "fixture_runtime_vector_receipt");
        RequireString(request, "deliverySessionId", "rds_SYNTHETIC_W09_REHEARSAL_0001", "fixture_runtime_vector_receipt");
        RequireString(request, "packageHash", context.Package.PackageHash, "fixture_runtime_vector_receipt_binding");
        RequireString(request, "releaseId", context.Package.ReleaseId, "fixture_runtime_vector_receipt_binding");
        RequireString(request, "manifestSha256", context.Package.ManifestSha256, "fixture_runtime_vector_receipt_binding");
        RequireString(request, "eventId", "synthetic-w09-verified", "fixture_runtime_vector_receipt");
        RequireString(request, "idempotencyKey", "synthetic-w09-receipt", "fixture_runtime_vector_receipt");
        RequireString(request, "occurredAt", "2026-08-30T12:00:00.000Z", "fixture_runtime_vector_receipt");
        RequireString(request, "installerVersion", "0.0.0-synthetic", "fixture_runtime_vector_receipt");
        RequireString(request, "outcome", "completed", "fixture_runtime_vector_receipt");
        var artifacts = RequireObject(request, "artifacts", "fixture_runtime_vector_receipt");
        RequireShape(artifacts, "fixture_runtime_vector_receipt", "api", "portal");
        ValidateReceiptArtifact(RequireObject(artifacts, "api", "fixture_runtime_vector_receipt"), context.Package.Api, 1, 0);
        ValidateReceiptArtifact(RequireObject(artifacts, "portal", "fixture_runtime_vector_receipt"), context.Package.Portal, 1, 0);
        var safe = RequireObject(request, "safeResult", "fixture_runtime_vector_receipt");
        RequireShape(safe, "fixture_runtime_vector_receipt", "code", "state");
        RequireString(safe, "code", "runtime_artifacts_verified", "fixture_runtime_vector_receipt");
        RequireString(safe, "state", "completed", "fixture_runtime_vector_receipt");
        var receiptExpected = RequireObject(receipt, "expected", "fixture_runtime_vector_receipt");
        RequireShape(receiptExpected, "fixture_runtime_vector_receipt", "status", "replayStatus", "mutationCount");
        if (RequireInt32(receiptExpected, "status", "fixture_runtime_vector_receipt") != 201 || RequireInt32(receiptExpected, "replayStatus", "fixture_runtime_vector_receipt") != 200 || RequireInt32(receiptExpected, "mutationCount", "fixture_runtime_vector_receipt") != 1)
            Fail("fixture_runtime_vector_receipt");

        var negatives = RequireArray(root, "negativeVectors", "fixture_runtime_vector_negative").EnumerateArray().ToArray();
        if (negatives.Length != RuntimeNegatives.Length) Fail("fixture_runtime_vector_negative_count");
        for (var index = 0; index < negatives.Length; index++)
        {
            var row = negatives[index];
            var expected = RuntimeNegatives[index];
            RequireShape(row, "fixture_runtime_vector_negative", "id", "operation", "mutation", "expectedStatus", "expectedErrorCode", "expectedArtifactOpenCount", "expectedReceiptMutationCount", "expectedResponseBodyBytes");
            var errorCode = RequireNullableString(row, "expectedErrorCode", "fixture_runtime_vector_negative");
            if (RequireString(row, "id", null, "fixture_runtime_vector_negative") != expected.Id || RequireString(row, "mutation", null, "fixture_runtime_vector_negative") != expected.Id ||
                RequireString(row, "operation", null, "fixture_runtime_vector_negative") != expected.Operation || RequireInt32(row, "expectedStatus", "fixture_runtime_vector_negative") != expected.Status ||
                (expected.Error.Length == 0 ? errorCode is not null : errorCode != expected.Error) || RequireInt32(row, "expectedArtifactOpenCount", "fixture_runtime_vector_negative") != expected.ArtifactOpens ||
                RequireInt32(row, "expectedReceiptMutationCount", "fixture_runtime_vector_negative") != expected.ReceiptMutations || RequireInt32(row, "expectedResponseBodyBytes", "fixture_runtime_vector_negative") != expected.BodyBytes)
                Fail("fixture_runtime_vector_negative_binding");
        }
    }

    private static void ValidateDownload(FixtureBundle bundle, SemanticContext context, JsonElement download, DownloadExpectation expected)
    {
        RequireShape(download, "fixture_runtime_vector_download", "id", "method", "path", "requestHeaders", "expectedStatus", "expectedHeaders", "bodyFile", "bodyOffset", "bodyLength");
        RequireString(download, "id", expected.Id, "fixture_runtime_vector_download");
        RequireString(download, "method", "GET", "fixture_runtime_vector_download");
        RequireString(download, "path", $"/api/onboarding/installer/runtime-artifacts/{expected.Kind}", "fixture_runtime_vector_download");
        RequireString(download, "bodyFile", expected.BodyFile, "fixture_runtime_vector_download_binding");
        if (RequireInt32(download, "bodyOffset", "fixture_runtime_vector_download") != expected.Offset || RequireInt32(download, "bodyLength", "fixture_runtime_vector_download") != expected.Length)
            Fail("fixture_runtime_vector_download_binding");
        var requestHeaders = RequireObject(download, "requestHeaders", "fixture_runtime_vector_download");
        RequireShape(requestHeaders, "fixture_runtime_vector_download", "requiredNames", "artifactReferencePlaceholder", "range", "containsHeaderValues");
        RequireStringArray(requestHeaders, "requiredNames", ["Authorization", "X-PM365-Onboarding-Session", "X-PM365-Onboarding-Code", "X-PM365-Runtime-Delivery-Session", "X-PM365-Runtime-Delivery-Ref"], "fixture_runtime_vector_download");
        RequireString(requestHeaders, "artifactReferencePlaceholder", expected.Kind == "api" ? "ard_AAAAAAAAAAAAAAAAAAAAAAAA" : "ard_BBBBBBBBBBBBBBBBBBBBBBBB", "fixture_runtime_vector_download");
        RequireBoolean(requestHeaders, "containsHeaderValues", false, "fixture_runtime_vector_download");
        if (expected.Range is null) { if (requestHeaders.GetProperty("range").ValueKind != JsonValueKind.Null) Fail("fixture_runtime_vector_range"); }
        else RequireString(requestHeaders, "range", expected.Range, "fixture_runtime_vector_range");
        var headers = RequireObject(download, "expectedHeaders", "fixture_runtime_vector_download");
        RequireShape(headers, "fixture_runtime_vector_download", "Cache-Control", "Pragma", "X-Content-Type-Options", "ETag", "Content-Length", "Content-Range");
        RequireString(headers, "Cache-Control", "private, no-store", "fixture_runtime_vector_download");
        RequireString(headers, "Pragma", "no-cache", "fixture_runtime_vector_download");
        RequireString(headers, "X-Content-Type-Options", "nosniff", "fixture_runtime_vector_download");
        var artifact = context.Package.Artifact(expected.Kind);
        RequireString(headers, "ETag", $"\"sha256:{artifact.Sha256}\"", "fixture_runtime_vector_download_binding");
        RequireString(headers, "Content-Length", expected.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), "fixture_runtime_vector_download_binding");
        var expectedRange = expected.Range is null ? null : $"bytes {expected.Offset}-{expected.Offset + expected.Length - 1}/{artifact.SizeBytes}";
        if (expectedRange is null) { if (headers.GetProperty("Content-Range").ValueKind != JsonValueKind.Null) Fail("fixture_runtime_vector_range"); }
        else RequireString(headers, "Content-Range", expectedRange, "fixture_runtime_vector_range");
        if (RequireInt32(download, "expectedStatus", "fixture_runtime_vector_download") != (expected.Range is null ? 200 : 206)) Fail("fixture_runtime_vector_download");
        var body = bundle[expected.BodyFile];
        if (expected.Offset + expected.Length > body.Length || artifact.Sha256 != Sha256(body) || artifact.SizeBytes != body.Length) Fail("fixture_runtime_vector_download_binding");
    }

    private static void ValidateReceiptArtifact(JsonElement value, PrivateRuntimeArtifact artifact, int full, int range)
    {
        RequireShape(value, "fixture_runtime_vector_receipt", "artifactKind", "sha256", "sizeBytes", "verificationOutcome", "fullStreamCount", "rangeRetryCount", "bytesReceived");
        if (RequireString(value, "artifactKind", null, "fixture_runtime_vector_receipt") != artifact.ArtifactKind || RequireString(value, "sha256", null, "fixture_runtime_vector_receipt") != artifact.Sha256 ||
            RequireInt64(value, "sizeBytes", "fixture_runtime_vector_receipt") != artifact.SizeBytes || RequireString(value, "verificationOutcome", "verified", "fixture_runtime_vector_receipt") != "verified" ||
            RequireInt32(value, "fullStreamCount", "fixture_runtime_vector_receipt") != full || RequireInt32(value, "rangeRetryCount", "fixture_runtime_vector_receipt") != range ||
            RequireInt64(value, "bytesReceived", "fixture_runtime_vector_receipt") != artifact.SizeBytes) Fail("fixture_runtime_vector_receipt_binding");
    }

    private static void ValidateProtectedVectorAndLicenseDocuments(FixtureBundle bundle, SemanticContext context, string licensePem)
    {
        using var document = ParseCandidateJson(bundle["protected-setting-acquisition-http-vectors.json"]);
        var root = document.RootElement;
        RequireShape(root, "fixture_protected_vector_root", "schemaVersion", "classification", "fixedNow", "endpoint", "authorization", "positive", "replay", "negativeVectors", "licenseSignatureVectorFile", "containsSyntheticProtectedValue", "containsCustomerSecret", "containsReusableCredential", "authorizesDeployment");
        RequireString(root, "schemaVersion", "pagemaker365.protected-setting-acquisition-rehearsal.v1", "fixture_protected_vector_identity");
        RequireString(root, "classification", "synthetic-test-only", "fixture_protected_vector_identity");
        RequireString(root, "fixedNow", "2026-08-30T12:00:00.000Z", "fixture_protected_vector_identity");
        RequireString(root, "licenseSignatureVectorFile", "license-signature-vector.json", "fixture_protected_vector_license_binding");
        RequireBoolean(root, "containsSyntheticProtectedValue", true, "fixture_protected_vector_security");
        RequireBoolean(root, "containsCustomerSecret", false, "fixture_protected_vector_security");
        RequireBoolean(root, "containsReusableCredential", false, "fixture_protected_vector_security");
        RequireBoolean(root, "authorizesDeployment", false, "fixture_protected_vector_security");
        var endpoint = RequireObject(root, "endpoint", "fixture_protected_vector_endpoint");
        RequireShape(endpoint, "fixture_protected_vector_endpoint", "method", "path");
        RequireString(endpoint, "method", "POST", "fixture_protected_vector_endpoint");
        RequireString(endpoint, "path", "/api/onboarding/installer/runtime-protected-settings/acquire", "fixture_protected_vector_endpoint");
        var authorization = RequireObject(root, "authorization", "fixture_protected_vector_authorization");
        RequireShape(authorization, "fixture_protected_vector_authorization", "mode", "requiredHeaderNames", "containsHeaderValues");
        RequireString(authorization, "mode", "injected-test-only", "fixture_protected_vector_authorization");
        RequireStringArray(authorization, "requiredHeaderNames", ["Authorization", "X-PM365-Onboarding-Session", "X-PM365-Onboarding-Code", "X-PM365-Runtime-Delivery-Session"], "fixture_protected_vector_authorization");
        RequireBoolean(authorization, "containsHeaderValues", false, "fixture_protected_vector_authorization");
        var positive = RequireObject(root, "positive", "fixture_protected_vector_positive");
        RequireShape(positive, "fixture_protected_vector_positive", "request", "expectedStatus", "expectedHeaders", "response");
        if (RequireInt32(positive, "expectedStatus", "fixture_protected_vector_positive") != 200) Fail("fixture_protected_vector_positive");
        var request = RequireObject(positive, "request", "fixture_protected_vector_positive");
        var response = RequireObject(positive, "response", "fixture_protected_vector_positive");
        foreach (var pair in new[] { (Value: request, Final: "reference"), (Value: response, Final: "value") })
        {
            var value = pair.Value;
            RequireShape(value, "fixture_protected_vector_binding", "contractVersion", "packageHash", "targetApp", "name", pair.Final);
            RequireString(value, "contractVersion", "pagemaker365.protected-setting-acquisition.v1", "fixture_protected_vector_binding");
            RequireString(value, "packageHash", context.Package.PackageHash, "fixture_protected_vector_binding");
            RequireString(value, "targetApp", "api", "fixture_protected_vector_binding");
            RequireString(value, "name", "API_LICENSE_SIGNED_PAYLOAD", "fixture_protected_vector_binding");
        }
        var protectedSetting = context.Package.RuntimeConfiguration.ProtectedSettings.Single(item => item.Name == "API_LICENSE_SIGNED_PAYLOAD");
        RequireString(request, "reference", protectedSetting.Reference.OpaqueReference, "fixture_protected_vector_reference");
        var headers = RequireObject(positive, "expectedHeaders", "fixture_protected_vector_headers");
        RequireShape(headers, "fixture_protected_vector_headers", "Cache-Control", "Pragma", "X-Content-Type-Options", "Vary");
        RequireString(headers, "Cache-Control", "private, no-store", "fixture_protected_vector_headers");
        RequireString(headers, "Pragma", "no-cache", "fixture_protected_vector_headers");
        RequireString(headers, "X-Content-Type-Options", "nosniff", "fixture_protected_vector_headers");
        RequireString(headers, "Vary", "Authorization, X-PM365-Onboarding-Session, X-PM365-Runtime-Delivery-Session", "fixture_protected_vector_headers");
        var replay = RequireObject(root, "replay", "fixture_protected_vector_replay");
        RequireShape(replay, "fixture_protected_vector_replay", "expectedStatus", "expectedErrorCode", "expectedRedemptionCount");
        if (RequireInt32(replay, "expectedStatus", "fixture_protected_vector_replay") != 404 || RequireString(replay, "expectedErrorCode", "private_runtime_protected_setting_unavailable", "fixture_protected_vector_replay") != "private_runtime_protected_setting_unavailable" || RequireInt32(replay, "expectedRedemptionCount", "fixture_protected_vector_replay") != 1)
            Fail("fixture_protected_vector_replay");
        var negatives = RequireArray(root, "negativeVectors", "fixture_protected_vector_negative").EnumerateArray().ToArray();
        if (negatives.Length != ProtectedNegatives.Length) Fail("fixture_protected_vector_negative_count");
        for (var index = 0; index < negatives.Length; index++)
        {
            var row = negatives[index];
            var expected = ProtectedNegatives[index];
            RequireShape(row, "fixture_protected_vector_negative", "id", "mutation", "expectedStatus", "expectedErrorCode", "expectedProtectedReadCount", "expectedRedemptionCount", "expectedResponseBodyBytes");
            if (RequireString(row, "id", null, "fixture_protected_vector_negative") != expected.Id || RequireString(row, "mutation", null, "fixture_protected_vector_negative") != expected.Id ||
                RequireInt32(row, "expectedStatus", "fixture_protected_vector_negative") != expected.Status || RequireString(row, "expectedErrorCode", null, "fixture_protected_vector_negative") != expected.Error ||
                RequireInt32(row, "expectedProtectedReadCount", "fixture_protected_vector_negative") != expected.ProtectedReads || RequireInt32(row, "expectedRedemptionCount", "fixture_protected_vector_negative") != expected.Redemptions ||
                RequireInt32(row, "expectedResponseBodyBytes", "fixture_protected_vector_negative") != expected.BodyBytes) Fail("fixture_protected_vector_negative_binding");
        }
        ValidateLicenseVector(bundle, context, response.GetProperty("value"), licensePem, root.GetProperty("fixedNow").GetString()!);
    }

    private static void ValidateLicenseVector(FixtureBundle bundle, SemanticContext context, JsonElement signedLicense, string licensePem, string fixedNow)
    {
        using var vectorDocument = ParseCandidateJson(bundle["license-signature-vector.json"]);
        var vector = vectorDocument.RootElement;
        RequireShape(vector, "fixture_license_vector_shape", "schemaVersion", "algorithm", "keyId", "publicKeySha256", "canonicalization", "signedPayloadSha256", "signedPayloadFingerprint", "signature", "validFrom", "validTo", "classification");
        RequireString(vector, "schemaVersion", "pagemaker365.license-signature-vector.v1", "fixture_license_vector_identity");
        RequireString(vector, "algorithm", "Ed25519", "fixture_license_vector_algorithm");
        RequireString(vector, "keyId", LicenseKeyId, "fixture_license_vector_key");
        RequireString(vector, "publicKeySha256", Sha256(Encoding.UTF8.GetBytes(licensePem)), "fixture_license_vector_key");
        RequireString(vector, "canonicalization", "json-c14n-v1", "fixture_license_vector_canonicalization");
        RequireString(vector, "classification", "synthetic-test-only-noncustomer-nonreusable-nondeployable", "fixture_license_vector_classification");
        RequireShape(signedLicense, "fixture_license_value_shape", "schemaVersion", "payload", "signature");
        RequireString(signedLicense, "schemaVersion", "pagemaker365.license.v1", "fixture_license_value_shape");
        var payload = RequireObject(signedLicense, "payload", "fixture_license_payload_shape");
        RequireShape(payload, "fixture_license_payload_shape", "product", "licenseId", "activationId", "customerId", "customerKey", "customerDisplayName", "subscriptionId", "installationId", "installationKey", "environmentId", "environmentKey", "environmentType", "planKey", "supportTier", "workspaceLimit", "environmentLimit", "validFrom", "validTo", "issuedAt");
        RequireString(payload, "product", "PageMaker365", "fixture_license_payload_identity");
        RequireString(payload, "licenseId", "synthetic-w09-rehearsal-license", "fixture_license_payload_license_id");
        RequireString(payload, "customerId", context.Package.CustomerId, "fixture_license_payload_binding");
        RequireString(payload, "installationId", context.Package.InstallationId, "fixture_license_payload_binding");
        RequireString(payload, "environmentId", context.Package.EnvironmentId, "fixture_license_payload_binding");
        foreach (var property in new[] { "activationId", "customerId", "installationId", "environmentId" }) RequireCanonicalUuid(payload, property, "fixture_license_payload_uuid");
        if (RequireInt32(payload, "workspaceLimit", "fixture_license_payload_shape") != 1 || RequireInt32(payload, "environmentLimit", "fixture_license_payload_shape") != 1) Fail("fixture_license_payload_limits");
        var validFrom = RequireUtc(payload, "validFrom", "fixture_license_payload_time");
        var validTo = RequireUtc(payload, "validTo", "fixture_license_payload_time");
        var issuedAt = RequireUtc(payload, "issuedAt", "fixture_license_payload_time");
        var now = DateTimeOffset.Parse(fixedNow, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);
        if (validFrom > now || validTo <= now || validTo <= validFrom || issuedAt > now) Fail("fixture_license_payload_time");
        var signature = RequireObject(signedLicense, "signature", "fixture_license_signature_shape");
        RequireShape(signature, "fixture_license_signature_shape", "alg", "kid", "value");
        RequireString(signature, "alg", "Ed25519", "fixture_license_signature_algorithm");
        RequireString(signature, "kid", LicenseKeyId, "fixture_license_signature_key");
        var signatureText = RequireString(signature, "value", null, "fixture_license_signature_shape");
        var signatureBytes = RequireCanonicalBase64Url(signatureText, 64, "fixture_license_signature_canonical");
        var payloadBytes = PrivateRuntimeCanonicalJson.Canonicalize(payload);
        if (!Verify(licensePem, payloadBytes, signatureBytes)) Fail("fixture_license_signature_invalid");
        var signedHash = Sha256(PrivateRuntimeCanonicalJson.Canonicalize(signedLicense));
        RequireString(vector, "signedPayloadSha256", signedHash, "fixture_license_vector_fingerprint");
        RequireString(vector, "signedPayloadFingerprint", signedHash, "fixture_license_vector_fingerprint");
        RequireString(vector, "signature", signatureText, "fixture_license_vector_signature_binding");
        RequireString(vector, "validFrom", payload.GetProperty("validFrom").GetString(), "fixture_license_vector_time");
        RequireString(vector, "validTo", payload.GetProperty("validTo").GetString(), "fixture_license_vector_time");
        var vectorFrom = RequireUtc(vector, "validFrom", "fixture_license_vector_time");
        var vectorTo = RequireUtc(vector, "validTo", "fixture_license_vector_time");
        if (vectorFrom > now || vectorTo <= now || vectorTo <= vectorFrom) Fail("fixture_license_vector_time");
        var projectedPem = context.Package.RuntimeConfiguration.PublicSettings.Single(item => item.Name == "API_LICENSE_PUBLIC_KEY_PEM").Value.GetString();
        if (projectedPem != licensePem || Sha256(Encoding.UTF8.GetBytes(licensePem)) == PackagePemSha256 ||
            context.Package.SigningKeyId == vector.GetProperty("keyId").GetString())
            Fail("fixture_license_package_trust_cross_pair");
    }

    private static void RunFreshSemanticNegativeMatrix(FixtureBundle accepted)
    {
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json", root => root["entries"]![0]!["ownerSource"] = "unknown/owner", "fixture_source_map_owner", ValidateSourceMapV2);
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json", root => root["entries"]![0]!["sourceField"] = "", "fixture_source_map_domain", ValidateSourceMapV2);
        AssertWrongNonemptySourceFieldDeny(accepted);
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json", root => root.Remove("catalog"), "fixture_source_map_root", ValidateSourceMapV2);
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json", root => MovePropertyToEnd(root, "schemaVersion"), "fixture_source_map_root", ValidateSourceMapV2);
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json", root => root["schemaVersion"] = 2, "fixture_source_map_identity", ValidateSourceMapV2);
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json", root => root["entries"]![0]!["targetApp"] = "worker", "fixture_source_map_domain", ValidateSourceMapV2);
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json", root => root["entries"]![0]!["valueType"] = "integer", "fixture_source_map_projection_order", ValidateSourceMapV2);
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json", root => Swap((JsonArray)root["entries"]!, 0, 1), "fixture_source_map_projection_order", ValidateSourceMapV2);
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json", root => ((JsonArray)root["entries"]!).RemoveAt(0), "fixture_source_map_count", ValidateSourceMapV2);
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json", root => ((JsonArray)root["entries"]!).Add(root["entries"]![0]!.DeepClone()), "fixture_source_map_count", ValidateSourceMapV2);
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json", root =>
        {
            root["entries"]![1]!["targetApp"] = root["entries"]![0]!["targetApp"]!.DeepClone();
            root["entries"]![1]!["name"] = root["entries"]![0]!["name"]!.DeepClone();
        }, "fixture_source_map_duplicate", ValidateSourceMapV2);
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json", root =>
        {
            root["entries"]![1]!["ownerSource"] = root["entries"]![0]!["ownerSource"]!.DeepClone();
            root["entries"]![1]!["sourceField"] = root["entries"]![0]!["sourceField"]!.DeepClone();
        }, "fixture_source_map_duplicate", ValidateSourceMapV2);
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json", root => root["catalog"]!["sourceCommit"] = new string('a', 40), "fixture_source_map_catalog", ValidateSourceMapV2);
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json", root => root.Add("unknown", true), "fixture_source_map_root", ValidateSourceMapV2);
        AssertRawDuplicateJsonDeny(accepted, "runtime-configuration-source-map-v2.json", "schemaVersion", "fixture_json_duplicate", ValidateSourceMapV2);
        AssertJsonSemanticDeny(accepted, "runtime-configuration-projection-v2.json", root => root["binding"]!["deploymentExportId"] = "11111111-1111-4111-8111-111111111111", "fixture_projection_package_cross_pair", ValidateSourceMapV2);

        AssertManifestCrossPairDeny(accepted, bundle => bundle.Files["artifacts/api.zip"] = bundle["artifacts/portal.zip"].ToArray(), "fixture_archive_kind_cross_pair");
        AssertManifestCrossPairDeny(accepted, bundle => bundle.Files["artifacts/portal.zip"] = bundle["artifacts/api.zip"].ToArray(), "fixture_archive_kind_cross_pair");
        AssertManifestCrossPairDeny(accepted, bundle =>
        {
            var api = bundle["artifacts/api.zip"].ToArray();
            var portal = bundle["artifacts/portal.zip"].ToArray();
            bundle.Files["artifacts/api.zip"] = portal;
            bundle.Files["artifacts/portal.zip"] = api;
        }, "fixture_archive_kind_cross_pair");
        AssertManifestCrossPairDeny(accepted, bundle => bundle.Files["artifacts/api.zip"] = bundle["artifacts/api.zip"].Append((byte)0).ToArray(), "fixture_archive_structure");
        AssertManifestCrossPairDeny(accepted, bundle => MutateFirstZipEntryContent(bundle.Files["artifacts/api.zip"]), "fixture_archive_structure");
        AssertManifestCrossPairDeny(accepted, bundle => MutateFirstZipCentralOffset(bundle.Files["artifacts/api.zip"]), "fixture_archive_structure");
        AssertManifestCrossPairDeny(accepted, bundle =>
        {
            var entries = ParseStoredZip(bundle["artifacts/api.zip"]).ToArray();
            entries[0] = entries[0] with { Name = "../provenance.json" };
            bundle.Files["artifacts/api.zip"] = BuildStoredZip(entries);
        }, "fixture_archive_structure");
        AssertManifestCrossPairDeny(accepted, bundle =>
        {
            var entries = ParseStoredZip(bundle["artifacts/api.zip"]).Append(new ZipEntry("unexpected.txt", Encoding.UTF8.GetBytes("synthetic\n"))).ToArray();
            bundle.Files["artifacts/api.zip"] = BuildStoredZip(entries);
        }, "fixture_archive_kind_cross_pair");
        AssertManifestCrossPairDeny(accepted, bundle =>
        {
            var entries = ParseStoredZip(bundle["artifacts/api.zip"]).ToArray();
            bundle.Files["artifacts/api.zip"] = BuildStoredZip(entries, firstExternalAttributes: 0xa1ff0000u);
        }, "fixture_archive_structure");
        AssertPackageManifestSemanticDeny(accepted, root => root["sourceCommit"] = new string('a', 40), "customer_install_v07_value");
        AssertPackageManifestSemanticDeny(accepted, root => root["api"]!["startupCommand"] = "node wrong.js", "customer_install_v07_value");
        AssertPackageManifestSemanticDeny(accepted, root => root.Add("unknown", true), "customer_install_v07_manifest_binding");

        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => root["sessionCreation"]!["method"] = "GET", "fixture_runtime_vector_session", ValidateRuntimeDeliveryVectorDocument);
        AssertFutureShiftedSessionExpiryDeny(accepted);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => root.Remove("authorization"), "fixture_runtime_vector_root", ValidateRuntimeDeliveryVectorDocument);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => Swap((JsonArray)root["authorization"]!["requiredHeaderNames"]!, 0, 1), "fixture_runtime_vector_authorization", ValidateRuntimeDeliveryVectorDocument);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => root["sessionCreation"]!["request"]!.AsObject().Add("query", "forbidden"), "fixture_runtime_vector_session", ValidateRuntimeDeliveryVectorDocument);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => root["sessionCreation"]!["expected"]!["status"] = "201", "fixture_runtime_vector_session", ValidateRuntimeDeliveryVectorDocument);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => ((JsonArray)root["artifactDownloads"]!).RemoveAt(0), "fixture_runtime_vector_download_count", ValidateRuntimeDeliveryVectorDocument);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => root["artifactDownloads"]![0]!["path"] = "/wrong", "fixture_runtime_vector_download", ValidateRuntimeDeliveryVectorDocument);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => ((JsonArray)root["artifactDownloads"]![0]!["requestHeaders"]!["requiredNames"]!).Add("X-Secret"), "fixture_runtime_vector_download", ValidateRuntimeDeliveryVectorDocument);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => root["artifactDownloads"]![0]!["requestHeaders"]!["range"] = "bytes=0-1", "fixture_runtime_vector_range", ValidateRuntimeDeliveryVectorDocument);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => root["artifactDownloads"]![0]!["expectedHeaders"]!["Content-Length"] = "1", "fixture_runtime_vector_download_binding", ValidateRuntimeDeliveryVectorDocument);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => Swap((JsonArray)root["artifactDownloads"]!, 0, 1), "fixture_runtime_vector_download", ValidateRuntimeDeliveryVectorDocument);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => root["artifactDownloads"]![1]!["bodyOffset"] = 18, "fixture_runtime_vector_download_binding", ValidateRuntimeDeliveryVectorDocument);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => root["receipt"]!["request"]!["packageHash"] = "sha256:" + new string('a', 64), "fixture_runtime_vector_receipt_binding", ValidateRuntimeDeliveryVectorDocument);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => root["receipt"]!["request"]!["eventId"] = "wrong", "fixture_runtime_vector_receipt", ValidateRuntimeDeliveryVectorDocument);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => root["receipt"]!["request"]!["artifacts"]!["api"]!["bytesReceived"] = 1, "fixture_runtime_vector_receipt_binding", ValidateRuntimeDeliveryVectorDocument);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => root["negativeVectors"]![0]!["expectedArtifactOpenCount"] = 1, "fixture_runtime_vector_negative_binding", ValidateRuntimeDeliveryVectorDocument);
        AssertSuccessfulRuntimeNegativeEmptyErrorCodeDeny(accepted);
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json", root => root.Add("redirect", "https://example.test"), "fixture_runtime_vector_root", ValidateRuntimeDeliveryVectorDocument);
        AssertRawDuplicateJsonDeny(accepted, "runtime-delivery-http-vectors.json", "classification", "fixture_json_duplicate", ValidateRuntimeDeliveryVectorDocument);

        AssertProtectedSemanticDeny(accepted, root => root["endpoint"]!["method"] = "GET", "fixture_protected_vector_endpoint");
        AssertProtectedSemanticDeny(accepted, root => root.Remove("endpoint"), "fixture_protected_vector_root");
        AssertProtectedSemanticDeny(accepted, root => Swap((JsonArray)root["authorization"]!["requiredHeaderNames"]!, 0, 1), "fixture_protected_vector_authorization");
        AssertProtectedSemanticDeny(accepted, root => root["positive"]!.AsObject().Add("query", "forbidden"), "fixture_protected_vector_positive");
        AssertProtectedSemanticDeny(accepted, root => root["positive"]!["expectedStatus"] = "200", "fixture_protected_vector_positive");
        AssertProtectedSemanticDeny(accepted, root => root["positive"]!["expectedHeaders"]!["Vary"] = "Authorization", "fixture_protected_vector_headers");
        AssertProtectedSemanticDeny(accepted, root => root["positive"]!["request"]!["reference"] = "psr_DDDDDDDDDDDDDDDDDDDDDDDD", "fixture_protected_vector_reference");
        AssertProtectedSemanticDeny(accepted, root => root["positive"]!["response"]!["packageHash"] = "sha256:" + new string('a', 64), "fixture_protected_vector_binding");
        AssertProtectedSemanticDeny(accepted, root => root["replay"]!["expectedRedemptionCount"] = 0, "fixture_protected_vector_replay");
        AssertProtectedSemanticDeny(accepted, root => ((JsonArray)root["negativeVectors"]!).RemoveAt(0), "fixture_protected_vector_negative_count");
        AssertProtectedSemanticDeny(accepted, root => root["negativeVectors"]![0]!["expectedProtectedReadCount"] = 1, "fixture_protected_vector_negative_binding");
        AssertProtectedSemanticDeny(accepted, root => root.Add("location", "https://example.test"), "fixture_protected_vector_root");
        AssertRawDuplicateJsonDeny(accepted, "protected-setting-acquisition-http-vectors.json", "classification", "fixture_json_duplicate", ValidateLicenseThroughProtected);
        AssertJsonSemanticDeny(accepted, "license-signature-vector.json", root => root.Remove("algorithm"), "fixture_license_vector_shape", ValidateLicenseThroughProtected);
        AssertJsonSemanticDeny(accepted, "license-signature-vector.json", root => MovePropertyToEnd(root, "schemaVersion"), "fixture_license_vector_shape", ValidateLicenseThroughProtected);
        AssertJsonSemanticDeny(accepted, "license-signature-vector.json", root => root["algorithm"] = 7, "fixture_license_vector_algorithm", ValidateLicenseThroughProtected);
        AssertJsonSemanticDeny(accepted, "license-signature-vector.json", root => root["algorithm"] = "RSA", "fixture_license_vector_algorithm", ValidateLicenseThroughProtected);
        AssertJsonSemanticDeny(accepted, "license-signature-vector.json", root => root["canonicalization"] = "JCS", "fixture_license_vector_canonicalization", ValidateLicenseThroughProtected);
        AssertJsonSemanticDeny(accepted, "license-signature-vector.json", root => root["classification"] = "customer", "fixture_license_vector_classification", ValidateLicenseThroughProtected);
        AssertJsonSemanticDeny(accepted, "license-signature-vector.json", root => root["validTo"] = "2026-08-29T12:00:00.000Z", "fixture_license_vector_time", ValidateLicenseThroughProtected);
        AssertJsonSemanticDeny(accepted, "license-signature-vector.json", root => root["signedPayloadFingerprint"] = new string('0', 64), "fixture_license_vector_fingerprint", ValidateLicenseThroughProtected);
        AssertNonCanonicalLicenseSignatureDeny(accepted);
        AssertJsonSemanticDeny(accepted, "license-signature-vector.json", root => root.Add("unknown", true), "fixture_license_vector_shape", ValidateLicenseThroughProtected);
        AssertRawDuplicateJsonDeny(accepted, "license-signature-vector.json", "algorithm", "fixture_json_duplicate", ValidateLicenseThroughProtected);
        AssertFreshLicenseBindingDeny(accepted, payload => payload["customerId"] = "11111111-1111-4111-8111-111111111111", "fixture_license_payload_binding");
        AssertFreshLicenseBindingDeny(accepted, payload => payload["installationId"] = "11111111-1111-4111-8111-111111111111", "fixture_license_payload_binding");
        AssertFreshLicenseBindingDeny(accepted, payload => payload["environmentId"] = "11111111-1111-4111-8111-111111111111", "fixture_license_payload_binding");
        AssertFreshLicenseBindingDeny(accepted, payload => payload["validTo"] = "2026-08-29T12:00:00.000Z", "fixture_license_payload_time");
        AssertFreshLicenseBindingDeny(accepted, payload => payload["workspaceLimit"] = 2, "fixture_license_payload_limits");
        AssertFreshLicenseIdDeny(accepted);
        AssertWrongLicenseKeyDeny(accepted);
        AssertFreshSameAuthorityDeny(accepted);
    }

    private static void AssertWrongNonemptySourceFieldDeny(FixtureBundle accepted)
    {
        var sourceMap = accepted.JsonObject("runtime-configuration-source-map-v2.json");
        var entry = sourceMap["entries"]![0]!.AsObject();
        var name = entry["name"]!.GetValue<string>();
        var original = entry["sourceField"]!.GetValue<string>();
        if (!original.Equals(name, StringComparison.Ordinal)) Fail("fixture_test_source_binding_precondition");
        var replacement = original + "_ALTERNATE";
        if (string.IsNullOrWhiteSpace(replacement) || replacement.Equals(original, StringComparison.Ordinal)) Fail("fixture_test_source_binding_precondition");
        AssertJsonSemanticDeny(accepted, "runtime-configuration-source-map-v2.json",
            root => root["entries"]![0]!["sourceField"] = replacement,
            "fixture_source_map_source_binding", ValidateSourceMapV2);
    }

    private static void AssertFutureShiftedSessionExpiryDeny(FixtureBundle accepted)
    {
        var vectors = accepted.JsonObject("runtime-delivery-http-vectors.json");
        var original = vectors["sessionCreation"]!["expected"]!["response"]!["deliverySession"]!["expiresAt"]!.GetValue<string>();
        const string replacement = "2099-08-31T12:00:00.000Z";
        if (!original.Equals("2099-08-30T12:00:00.000Z", StringComparison.Ordinal) ||
            !DateTimeOffset.TryParseExact(replacement, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var shifted) ||
            shifted <= ValidationTime)
            Fail("fixture_test_session_expiry_precondition");
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json",
            root => root["sessionCreation"]!["expected"]!["response"]!["deliverySession"]!["expiresAt"] = replacement,
            "fixture_runtime_vector_session_expiry_binding", ValidateRuntimeDeliveryVectorDocument);
    }

    private static void AssertSuccessfulRuntimeNegativeEmptyErrorCodeDeny(FixtureBundle accepted)
    {
        var vectors = accepted.JsonObject("runtime-delivery-http-vectors.json");
        var rows = vectors["negativeVectors"]!.AsArray();
        var index = Enumerable.Range(0, rows.Count).Single(item => rows[item]!["id"]!.GetValue<string>() == "artifact-short");
        if (rows[index]!["expectedErrorCode"] is not null || rows[index]!["expectedStatus"]!.GetValue<int>() != 200)
            Fail("fixture_test_runtime_error_code_precondition");
        AssertJsonSemanticDeny(accepted, "runtime-delivery-http-vectors.json",
            root => root["negativeVectors"]![index]!["expectedErrorCode"] = "",
            "fixture_runtime_vector_negative_binding", ValidateRuntimeDeliveryVectorDocument);
    }

    private static void AssertFreshLicenseIdDeny(FixtureBundle accepted)
    {
        const string replacement = "11111111-1111-4111-8111-111111111111";
        if (!Guid.TryParseExact(replacement, "D", out var parsed) || !parsed.ToString("D").Equals(replacement, StringComparison.Ordinal))
            Fail("fixture_test_license_id_precondition");
        var candidate = accepted.Clone();
        var licenseAuthority = TestAuthority.Create(LicenseKeyId);
        var packageAuthority = TestAuthority.Create("test-only-installer-w09-package-license-id");
        ReissueLicense(candidate, licenseAuthority, payload =>
        {
            if (payload["licenseId"]!.GetValue<string>() != "synthetic-w09-rehearsal-license") Fail("fixture_test_license_id_precondition");
            payload["licenseId"] = replacement;
        });
        RebindProjectionLicensePem(candidate, licenseAuthority.PublicKeyPem);
        ResignPackage(candidate, packageAuthority);
        RefreshProtocolPackageBindings(candidate);
        candidate.RefreshSelfManifest();
        var context = ValidateDynamicEnvelope(candidate, packageAuthority.Trust);
        var vector = candidate.JsonObject("license-signature-vector.json");
        if (vector["publicKeySha256"]!.GetValue<string>() != Sha256(Encoding.UTF8.GetBytes(licenseAuthority.PublicKeyPem)))
            Fail("fixture_test_license_id_precondition");
        AssertOwnedError("fixture_license_payload_license_id",
            () => ValidateProtectedVectorAndLicenseDocuments(candidate, context, licenseAuthority.PublicKeyPem));
    }

    private static void AssertJsonSemanticDeny(FixtureBundle accepted, string file, Action<JsonObject> mutate, string code,
        Action<FixtureBundle, SemanticContext> validator, bool rebindManifestPackage = false)
    {
        var candidate = accepted.Clone();
        candidate.MutateJson(file, mutate);
        PackageTrustOptions trust = AcceptedPackageTrust(FixtureRoot());
        if (rebindManifestPackage)
        {
            var authority = TestAuthority.Create("test-only-installer-w09-package-semantic");
            RebindManifestAndPackage(candidate, authority);
            trust = authority.Trust;
        }
        candidate.RefreshSelfManifest();
        var context = ValidateDynamicEnvelope(candidate, trust);
        AssertOwnedError(code, () => validator(candidate, context));
    }

    private static void AssertManifestCrossPairDeny(FixtureBundle accepted, Action<FixtureBundle> mutateArtifacts, string code)
    {
        var candidate = accepted.Clone();
        mutateArtifacts(candidate);
        var manifest = candidate.JsonObject("spo-runtime-manifest-v3.json");
        foreach (var pair in new[] { (Kind: "api", File: "artifacts/api.zip"), (Kind: "portal", File: "artifacts/portal.zip") })
        {
            manifest[pair.Kind]!["sizeBytes"] = candidate[pair.File].LongLength;
            manifest[pair.Kind]!["sha256"] = Sha256(candidate[pair.File]);
        }
        candidate.SetJson("spo-runtime-manifest-v3.json", manifest);
        var authority = TestAuthority.Create("test-only-installer-w09-package-archive");
        RebindManifestAndPackage(candidate, authority);
        candidate.RefreshSelfManifest();
        var context = ValidateDynamicEnvelope(candidate, authority.Trust);
        AssertOwnedError(code, () => ValidateRuntimeManifestV3(candidate, context));
    }

    private static void AssertPackageManifestSemanticDeny(FixtureBundle accepted, Action<JsonObject> mutateManifest, string code)
    {
        var candidate = accepted.Clone();
        candidate.MutateJson("spo-runtime-manifest-v3.json", mutateManifest);
        var authority = TestAuthority.Create("test-only-installer-w09-package-manifest");
        RebindManifestAndPackage(candidate, authority);
        candidate.RefreshSelfManifest();
        AssertOwnedError(code, () => ValidateDynamicEnvelope(candidate, authority.Trust));
    }

    private static void AssertRawDuplicateJsonDeny(FixtureBundle accepted, string file, string property, string code,
        Action<FixtureBundle, SemanticContext> validator)
    {
        var candidate = accepted.Clone();
        var text = StrictUtf8(candidate[file]);
        var marker = $"  \"{property}\":";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) Fail("fixture_test_duplicate_marker");
        var end = text.IndexOf('\n', start);
        if (end < 0) Fail("fixture_test_duplicate_marker");
        var propertyLine = text[start..(end + 1)];
        candidate.Files[file] = Encoding.UTF8.GetBytes(text.Insert(end + 1, propertyLine));
        candidate.RefreshSelfManifest();
        var context = ValidateDynamicEnvelope(candidate, AcceptedPackageTrust(FixtureRoot()));
        AssertOwnedError(code, () => validator(candidate, context));
    }

    private static void AssertProtectedSemanticDeny(FixtureBundle accepted, Action<JsonObject> mutate, string code) =>
        AssertJsonSemanticDeny(accepted, "protected-setting-acquisition-http-vectors.json", mutate, code, ValidateLicenseThroughProtected);

    private static void ValidateLicenseThroughProtected(FixtureBundle bundle, SemanticContext context) =>
        ValidateProtectedVectorAndLicenseDocuments(bundle, context, StrictUtf8(bundle["license-signing-public-key.pem"]));

    private static void AssertFreshLicenseBindingDeny(FixtureBundle accepted, Action<JsonObject> mutatePayload, string code)
    {
        var candidate = accepted.Clone();
        var licenseAuthority = TestAuthority.Create(LicenseKeyId);
        var packageAuthority = TestAuthority.Create("test-only-installer-w09-package-license");
        ReissueLicense(candidate, licenseAuthority, mutatePayload);
        RebindProjectionLicensePem(candidate, licenseAuthority.PublicKeyPem);
        ResignPackage(candidate, packageAuthority);
        RefreshProtocolPackageBindings(candidate);
        candidate.RefreshSelfManifest();
        var context = ValidateDynamicEnvelope(candidate, packageAuthority.Trust);
        AssertOwnedError(code, () => ValidateProtectedVectorAndLicenseDocuments(candidate, context, licenseAuthority.PublicKeyPem));
    }

    private static void AssertFreshSameAuthorityDeny(FixtureBundle accepted)
    {
        var candidate = accepted.Clone();
        var authority = TestAuthority.Create(LicenseKeyId);
        ReissueLicense(candidate, authority, _ => { });
        RebindProjectionLicensePem(candidate, authority.PublicKeyPem);
        ResignPackage(candidate, authority, LicenseKeyId);
        RefreshProtocolPackageBindings(candidate);
        candidate.RefreshSelfManifest();
        var context = ValidateDynamicEnvelope(candidate, authority.Trust);
        AssertOwnedError("fixture_license_package_trust_cross_pair", () => ValidateProtectedVectorAndLicenseDocuments(candidate, context, authority.PublicKeyPem));
    }

    private static void AssertNonCanonicalLicenseSignatureDeny(FixtureBundle accepted)
    {
        var candidate = accepted.Clone();
        var protectedVectors = candidate.JsonObject("protected-setting-acquisition-http-vectors.json");
        var signed = protectedVectors["positive"]!["response"]!["value"]!.AsObject();
        var padded = signed["signature"]!["value"]!.GetValue<string>() + "=";
        signed["signature"]!["value"] = padded;
        candidate.SetJson("protected-setting-acquisition-http-vectors.json", protectedVectors);
        var vector = candidate.JsonObject("license-signature-vector.json");
        vector["signature"] = padded;
        using var signedDocument = JsonDocument.Parse(signed.ToJsonString());
        var fingerprint = Sha256(PrivateRuntimeCanonicalJson.Canonicalize(signedDocument.RootElement));
        vector["signedPayloadSha256"] = fingerprint;
        vector["signedPayloadFingerprint"] = fingerprint;
        candidate.SetJson("license-signature-vector.json", vector);
        candidate.RefreshSelfManifest();
        var context = ValidateDynamicEnvelope(candidate, AcceptedPackageTrust(FixtureRoot()));
        AssertOwnedError("fixture_license_signature_canonical", () => ValidateLicenseThroughProtected(candidate, context));
    }

    private static void AssertWrongLicenseKeyDeny(FixtureBundle accepted)
    {
        var candidate = accepted.Clone();
        var wrongAuthority = TestAuthority.Create(LicenseKeyId);
        ReissueLicense(candidate, wrongAuthority, _ => { });
        var acceptedPem = StrictUtf8(accepted["license-signing-public-key.pem"]);
        candidate.Files["license-signing-public-key.pem"] = Encoding.UTF8.GetBytes(acceptedPem);
        var vector = candidate.JsonObject("license-signature-vector.json");
        vector["publicKeySha256"] = Sha256(Encoding.UTF8.GetBytes(acceptedPem));
        candidate.SetJson("license-signature-vector.json", vector);
        candidate.RefreshSelfManifest();
        var context = ValidateDynamicEnvelope(candidate, AcceptedPackageTrust(FixtureRoot()));
        AssertOwnedError("fixture_license_signature_invalid", () => ValidateProtectedVectorAndLicenseDocuments(candidate, context, acceptedPem));
    }

    private static void RebindManifestAndPackage(FixtureBundle bundle, TestAuthority authority)
    {
        var manifest = bundle.JsonObject("spo-runtime-manifest-v3.json");
        var package = bundle.JsonObject("customer-install-0.7.json");
        var runtime = package["runtimeArtifacts"]!.AsObject();
        runtime["manifestContractVersion"] = manifest["contractVersion"]!.DeepClone();
        foreach (var name in new[] { "product", "releaseId", "runtimeVersion", "sourceRepository", "sourceCommit", "provenanceSchemaVersion" })
            runtime[name] = manifest[name]!.DeepClone();
        foreach (var kind in new[] { "api", "portal" })
        {
            var from = manifest[kind]!.AsObject();
            runtime[kind] = new JsonObject
            {
                ["artifactKind"] = from["artifactKind"]!.DeepClone(), ["fileName"] = from["fileName"]!.DeepClone(),
                ["sizeBytes"] = from["sizeBytes"]!.DeepClone(), ["sha256"] = from["sha256"]!.DeepClone(),
                ["startupCommand"] = from["startupCommand"]!.DeepClone()
            };
        }
        var manifestSha = Sha256(bundle["spo-runtime-manifest-v3.json"]);
        runtime["manifestSha256"] = manifestSha;
        package["runtimeConfiguration"]!["binding"]!["manifestSha256"] = manifestSha;
        bundle.SetJson("customer-install-0.7.json", package);
        ResignPackage(bundle, authority);
    }

    private static void ResignPackage(FixtureBundle bundle, TestAuthority authority, string? keyId = null)
    {
        var package = bundle.JsonObject("customer-install-0.7.json");
        package["controlPlane"]!["signingKeyId"] = keyId ?? authority.KeyId;
        RefreshProjectionNode(package["runtimeConfiguration"]!.AsObject());
        var projection = package["runtimeConfiguration"]!.DeepClone().AsObject();
        bundle.SetJson("runtime-configuration-projection-v2.json", projection);
        using var unsigned = JsonDocument.Parse(FormatNode(package));
        var payload = PrivateRuntimeCanonicalJson.Canonicalize(unsigned.RootElement, excludePackageIntegrity: true);
        package["controlPlane"]!["packageHash"] = "sha256:" + Sha256(payload);
        package["controlPlane"]!["signature"] = EncodeBase64Url(Sign(authority.PrivateKey, payload));
        bundle.SetJson("customer-install-0.7.json", package);
        using var proof = ParseCandidateJson(bundle["customer-install-0.7.json"]);
        var proofPayload = PrivateRuntimeCanonicalJson.Canonicalize(proof.RootElement, excludePackageIntegrity: true);
        if (proof.RootElement.GetProperty("controlPlane").GetProperty("packageHash").GetString() != "sha256:" + Sha256(proofPayload) ||
            !Verify(authority.PublicKeyPem, proofPayload, DecodeBase64Url(proof.RootElement.GetProperty("controlPlane").GetProperty("signature").GetString()!)))
            Fail("fixture_test_package_reissue_failed");
    }

    private static void ReissueLicense(FixtureBundle bundle, TestAuthority authority, Action<JsonObject> mutatePayload)
    {
        var protectedVectors = bundle.JsonObject("protected-setting-acquisition-http-vectors.json");
        var signed = protectedVectors["positive"]!["response"]!["value"]!.AsObject();
        var payload = signed["payload"]!.AsObject();
        mutatePayload(payload);
        using var payloadDocument = JsonDocument.Parse(payload.ToJsonString());
        var payloadBytes = PrivateRuntimeCanonicalJson.Canonicalize(payloadDocument.RootElement);
        var signature = EncodeBase64Url(Sign(authority.PrivateKey, payloadBytes));
        signed["signature"]!["alg"] = "Ed25519";
        signed["signature"]!["kid"] = LicenseKeyId;
        signed["signature"]!["value"] = signature;
        bundle.SetJson("protected-setting-acquisition-http-vectors.json", protectedVectors);
        bundle.Files["license-signing-public-key.pem"] = Encoding.UTF8.GetBytes(authority.PublicKeyPem);
        using var signedDocument = JsonDocument.Parse(signed.ToJsonString());
        var fingerprint = Sha256(PrivateRuntimeCanonicalJson.Canonicalize(signedDocument.RootElement));
        var vector = bundle.JsonObject("license-signature-vector.json");
        vector["keyId"] = LicenseKeyId;
        vector["publicKeySha256"] = Sha256(Encoding.UTF8.GetBytes(authority.PublicKeyPem));
        vector["signedPayloadSha256"] = fingerprint;
        vector["signedPayloadFingerprint"] = fingerprint;
        vector["signature"] = signature;
        vector["validFrom"] = payload["validFrom"]!.DeepClone();
        vector["validTo"] = payload["validTo"]!.DeepClone();
        bundle.SetJson("license-signature-vector.json", vector);
        if (!Verify(authority.PublicKeyPem, payloadBytes, DecodeBase64Url(signature))) Fail("fixture_test_license_reissue_failed");
    }

    private static void RebindProjectionLicensePem(FixtureBundle bundle, string publicKeyPem)
    {
        var package = bundle.JsonObject("customer-install-0.7.json");
        var settings = package["runtimeConfiguration"]!["publicSettings"]!.AsArray();
        settings.Select(item => item!.AsObject()).Single(item => item["name"]!.GetValue<string>() == "API_LICENSE_PUBLIC_KEY_PEM")["value"] = publicKeyPem;
        bundle.SetJson("customer-install-0.7.json", package);
    }

    private static void RefreshProtocolPackageBindings(FixtureBundle bundle)
    {
        var package = bundle.JsonObject("customer-install-0.7.json");
        var packageHash = package["controlPlane"]!["packageHash"]!.GetValue<string>();
        var manifestSha = package["runtimeArtifacts"]!["manifestSha256"]!.GetValue<string>();
        var releaseId = package["runtimeArtifacts"]!["releaseId"]!.GetValue<string>();
        var runtime = bundle.JsonObject("runtime-delivery-http-vectors.json");
        runtime["receipt"]!["request"]!["packageHash"] = packageHash;
        runtime["receipt"]!["request"]!["manifestSha256"] = manifestSha;
        runtime["receipt"]!["request"]!["releaseId"] = releaseId;
        bundle.SetJson("runtime-delivery-http-vectors.json", runtime);
        var protectedVectors = bundle.JsonObject("protected-setting-acquisition-http-vectors.json");
        protectedVectors["positive"]!["request"]!["packageHash"] = packageHash;
        protectedVectors["positive"]!["response"]!["packageHash"] = packageHash;
        bundle.SetJson("protected-setting-acquisition-http-vectors.json", protectedVectors);
    }

    private static void RefreshProjectionNode(JsonObject projection)
    {
        projection.Remove("projectionSha256");
        using var document = JsonDocument.Parse(projection.ToJsonString());
        projection["projectionSha256"] = Sha256(PrivateRuntimeCanonicalJson.Canonicalize(document.RootElement));
    }

    private static string ProjectionDigest(JsonElement projection) =>
        Sha256(PrivateRuntimeCanonicalJson.CanonicalizeObjectWithoutProperty(projection, "projectionSha256"));

    private static byte[] BuildManifestFromPackage(JsonElement runtime)
    {
        var manifest = new JsonObject
        {
            ["contractVersion"] = runtime.GetProperty("manifestContractVersion").GetString(),
            ["product"] = runtime.GetProperty("product").GetString(), ["releaseId"] = runtime.GetProperty("releaseId").GetString(),
            ["runtimeVersion"] = runtime.GetProperty("runtimeVersion").GetString(), ["sourceRepository"] = runtime.GetProperty("sourceRepository").GetString(),
            ["sourceCommit"] = runtime.GetProperty("sourceCommit").GetString(), ["provenanceSchemaVersion"] = runtime.GetProperty("provenanceSchemaVersion").GetString(),
            ["api"] = ManifestNode(runtime.GetProperty("api")), ["portal"] = ManifestNode(runtime.GetProperty("portal"))
        };
        return Encoding.UTF8.GetBytes(FormatNode(manifest));
    }

    private static JsonObject ManifestNode(JsonElement artifact) => new()
    {
        ["fileName"] = artifact.GetProperty("fileName").GetString(), ["sizeBytes"] = artifact.GetProperty("sizeBytes").GetInt64(),
        ["sha256"] = artifact.GetProperty("sha256").GetString(), ["startupCommand"] = artifact.GetProperty("startupCommand").GetString(),
        ["artifactKind"] = artifact.GetProperty("artifactKind").GetString()
    };

    private static ProtectedNegative[] BuildProtectedNegatives()
    {
        var ids = new[]
        {
            "feature-absent", "feature-false", "configuration-absent", "configuration-false", "authentication-missing", "authentication-invalid",
            "onboarding-session-mismatch", "onboarding-code-mismatch", "delivery-session-mismatch", "query-forbidden", "range-forbidden",
            "idempotency-forbidden", "retry-forbidden", "reference-header-forbidden", "package-mismatch", "reference-missing", "reference-wrong",
            "target-mismatch", "name-mismatch", "reference-inactive", "reference-expired", "reference-redeemed", "package-stale", "package-revoked",
            "session-revoked", "export-drift", "activation-drift", "payload-corrupt", "license-signature-invalid", "license-fingerprint-invalid",
            "license-wrong-key", "license-status-invalid", "license-expired", "rate-limited", "aborted", "package-race", "session-race",
            "activation-race", "reference-race", "concurrent-redemption"
        };
        var readsOne = new HashSet<string>(new[] { "activation-drift", "payload-corrupt", "license-signature-invalid", "license-fingerprint-invalid", "license-wrong-key", "license-expired", "package-race", "session-race", "activation-race", "reference-race" }, StringComparer.Ordinal);
        return ids.Select(id => id switch
        {
            "rate-limited" => new ProtectedNegative(id, 429, "rate_limited", 0, 0, 33),
            "aborted" => new ProtectedNegative(id, 499, "private_runtime_protected_setting_aborted", 0, 0, 139),
            "session-revoked" => new ProtectedNegative(id, 410, "private_runtime_protected_setting_unavailable", 0, 0, 143),
            "session-race" => new ProtectedNegative(id, 410, "private_runtime_protected_setting_unavailable", 1, 0, 143),
            "concurrent-redemption" => new ProtectedNegative(id, 404, "private_runtime_protected_setting_unavailable", 2, 1, 143),
            _ => new ProtectedNegative(id, 404, "private_runtime_protected_setting_unavailable", readsOne.Contains(id) ? 1 : 0, 0, 143)
        }).ToArray();
    }

    private static void RequireNegativeRows(JsonElement rows, int count, params string[] counters)
    {
        var values = rows.EnumerateArray().ToArray();
        AssertEx.Equal(count, values.Length);
        AssertEx.Equal(count, values.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        foreach (var row in values)
        {
            AssertEx.True(row.GetProperty("expectedStatus").GetInt32() is >= 200 and <= 599);
            AssertEx.True(row.GetProperty("expectedResponseBodyBytes").GetInt32() >= 0);
            foreach (var counter in counters) AssertEx.True(row.GetProperty(counter).GetInt32() >= 0);
        }
    }

    private static void MutateFirstZipEntryContent(byte[] bytes)
    {
        var nameLength = U16(bytes, 26);
        var dataOffset = 30 + nameLength;
        if (dataOffset >= bytes.Length) Fail("fixture_test_zip_mutation");
        bytes[dataOffset] ^= 0x01;
    }

    private static void MutateFirstZipCentralOffset(byte[] bytes)
    {
        var eocd = bytes.Length - 22;
        var centralOffset = checked((int)U32(bytes, eocd + 16));
        if (centralOffset < 0 || centralOffset + 46 > eocd) Fail("fixture_test_zip_mutation");
        WriteU32(bytes, centralOffset + 42, U32(bytes, centralOffset + 42) + 1);
    }

    private static byte[] BuildStoredZip(IReadOnlyList<ZipEntry> entries, uint firstExternalAttributes = 0x81a40000u)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var offsets = new List<uint>();
        foreach (var entry in entries)
        {
            var name = Encoding.UTF8.GetBytes(entry.Name);
            offsets.Add(checked((uint)stream.Position));
            writer.Write(0x04034b50u); writer.Write((ushort)0x0014); writer.Write((ushort)0x0800); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(Crc32(entry.Data)); writer.Write((uint)entry.Data.Length);
            writer.Write((uint)entry.Data.Length); writer.Write((ushort)name.Length); writer.Write((ushort)0); writer.Write(name); writer.Write(entry.Data);
        }
        var centralOffset = checked((uint)stream.Position);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var name = Encoding.UTF8.GetBytes(entry.Name);
            writer.Write(0x02014b50u); writer.Write((ushort)0x0314); writer.Write((ushort)0x0014); writer.Write((ushort)0x0800);
            writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(Crc32(entry.Data));
            writer.Write((uint)entry.Data.Length); writer.Write((uint)entry.Data.Length); writer.Write((ushort)name.Length); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(index == 0 ? firstExternalAttributes : 0x81a40000u);
            writer.Write(offsets[index]); writer.Write(name);
        }
        var centralSize = checked((uint)stream.Position - centralOffset);
        writer.Write(0x06054b50u); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)entries.Count);
        writer.Write((ushort)entries.Count); writer.Write(centralSize); writer.Write(centralOffset); writer.Write((ushort)0);
        writer.Flush();
        return stream.ToArray();
    }

    private static IReadOnlyList<ZipEntry> ParseStoredZip(byte[] bytes)
    {
        if (bytes.Length < 22) throw new InvalidDataException("fixture_zip_eocd");
        var eocd = bytes.Length - 22;
        var eocdCount = Enumerable.Range(0, bytes.Length - 3).Count(index => U32(bytes, index) == 0x06054b50);
        if (eocdCount != 1 || U32(bytes, eocd) != 0x06054b50 || U16(bytes, eocd + 4) != 0 || U16(bytes, eocd + 6) != 0 || U16(bytes, eocd + 20) != 0)
            throw new InvalidDataException("fixture_zip_eocd");
        var count = U16(bytes, eocd + 10);
        var centralSize = checked((int)U32(bytes, eocd + 12));
        var centralOffset = checked((int)U32(bytes, eocd + 16));
        if (U16(bytes, eocd + 8) != count || centralOffset + centralSize != eocd) throw new InvalidDataException("fixture_zip_central");
        var result = new List<ZipEntry>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var cursor = centralOffset;
        var expectedLocalOffset = 0;
        for (var index = 0; index < count; index++)
        {
            if (U32(bytes, cursor) != 0x02014b50) throw new InvalidDataException("fixture_zip_central");
            var nameLength = U16(bytes, cursor + 28);
            var extraLength = U16(bytes, cursor + 30);
            var commentLength = U16(bytes, cursor + 32);
            var localOffset = checked((int)U32(bytes, cursor + 42));
            var size = checked((int)U32(bytes, cursor + 24));
            var name = StrictUtf8(bytes.AsSpan(cursor + 46, nameLength).ToArray());
            if (!names.Add(name) || localOffset != expectedLocalOffset || name.StartsWith('/') || name.Contains("..", StringComparison.Ordinal) || name.Contains('\\') || name.Contains(':'))
                throw new InvalidDataException("fixture_zip_entry");
            if (U16(bytes, cursor + 4) != 0x0314 || U16(bytes, cursor + 6) != 0x0014 || U16(bytes, cursor + 8) != 0x0800 ||
                U16(bytes, cursor + 10) != 0 || U16(bytes, cursor + 12) != 0 || U16(bytes, cursor + 14) != 0 || extraLength != 0 || commentLength != 0 ||
                U16(bytes, cursor + 34) != 0 || U16(bytes, cursor + 36) != 0 || U32(bytes, cursor + 38) != 0x81a40000)
                throw new InvalidDataException("fixture_zip_fields");
            if (U32(bytes, localOffset) != 0x04034b50 || U16(bytes, localOffset + 4) != 0x0014 || U16(bytes, localOffset + 6) != 0x0800 ||
                U16(bytes, localOffset + 8) != 0 || U16(bytes, localOffset + 10) != 0 || U16(bytes, localOffset + 12) != 0 || U16(bytes, localOffset + 28) != 0)
                throw new InvalidDataException("fixture_zip_local_fields");
            var localNameLength = U16(bytes, localOffset + 26);
            var dataOffset = localOffset + 30 + localNameLength;
            if (dataOffset + size > centralOffset) throw new InvalidDataException("fixture_zip_bounds");
            var data = bytes.AsSpan(dataOffset, size).ToArray();
            if (localNameLength != nameLength || StrictUtf8(bytes.AsSpan(localOffset + 30, localNameLength).ToArray()) != name ||
                U32(bytes, localOffset + 14) != Crc32(data) || U32(bytes, cursor + 16) != Crc32(data) ||
                U32(bytes, localOffset + 18) != size || U32(bytes, localOffset + 22) != size || U32(bytes, cursor + 20) != size || U32(bytes, cursor + 24) != size)
                throw new InvalidDataException("fixture_zip_entry");
            result.Add(new ZipEntry(name, data));
            expectedLocalOffset += 30 + nameLength + size;
            cursor += 46 + nameLength;
        }
        if (cursor != centralOffset + centralSize || expectedLocalOffset != centralOffset) throw new InvalidDataException("fixture_zip_central");
        return result;
    }

    private static JsonDocument ParseCanonicalJson(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes[0] == 0xef || bytes.Contains((byte)'\r') || bytes[^1] != (byte)'\n') throw new InvalidDataException("fixture_json_noncanonical");
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 });
    }

    private static void RequireProperties(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.EnumerateObject().Select(item => item.Name).SequenceEqual(names, StringComparer.Ordinal))
            throw new InvalidDataException("fixture_json_shape");
    }

    private static void RequirePinnedBytes(Pin pin, byte[] bytes)
    {
        if (bytes.LongLength != pin.Size || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pin.Sha256), SHA256.HashData(bytes)))
            throw new InvalidDataException($"fixture_pin_mismatch:{pin.Name}");
    }

    private static void RequireClosedNames(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal) || actual.Distinct(StringComparer.OrdinalIgnoreCase).Count() != expected.Count)
            throw new InvalidDataException("fixture_tree_shape");
    }

    private static void RequireSafeFixtureBytes(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        foreach (var forbidden in new[] { "BEGIN PRIVATE KEY", "BEGIN RSA PRIVATE KEY", "BEGIN EC PRIVATE KEY", "blob.core.windows.net", "staticwebapp.config.json", "DefaultEndpointsProtocol=", "AccountKey=" })
            if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("fixture_private_or_live_material");
    }

    private static void RequireNoReparseOrAlternateStreams(string root)
    {
        foreach (var path in Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Prepend(root))
        {
            AssertEx.False((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0, $"Fixture path is a reparse point: {path}");
            if (File.Exists(path) && OperatingSystem.IsWindows()) AssertOnlyDefaultDataStream(path);
        }
    }

    private static void AssertOnlyDefaultDataStream(string path)
    {
        var handle = FindFirstStreamW(path, 0, out var data, 0);
        if (handle == new IntPtr(-1)) throw new InvalidDataException("fixture_stream_enumeration_failed");
        try
        {
            AssertEx.Equal("::$DATA", data.StreamName);
            if (FindNextStreamW(handle, out _)) throw new InvalidDataException("fixture_alternate_stream");
        }
        finally { FindClose(handle); }
    }

    private static bool Verify(string pem, byte[] payload, byte[] signature)
    {
        var key = (Ed25519PublicKeyParameters)PublicKeyFactory.CreateKey(PemDer(pem));
        var verifier = new Ed25519Signer();
        verifier.Init(false, key);
        verifier.BlockUpdate(payload, 0, payload.Length);
        return verifier.VerifySignature(signature);
    }

    private static byte[] PemDer(string pem) => Convert.FromBase64String(string.Concat(pem.Split('\n').Where(line => !line.StartsWith("-----", StringComparison.Ordinal) && line.Length > 0)));
    private static byte[] DecodeBase64Url(string value)
    {
        var paddingLength = (4 - value.Length % 4) % 4;
        return Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', paddingLength));
    }
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] Read(string root, string name) => File.ReadAllBytes(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
    private static string StrictUtf8(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes);
    private static JsonDocument ParseCandidateJson(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes[0] == 0xef || bytes.Contains((byte)'\r') || bytes[^1] != (byte)'\n') Fail("fixture_json_noncanonical");
        RequireNoDuplicateJsonProperties(bytes);
        JsonNode node;
        try { node = JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 })!; }
        catch (JsonException) { Fail("fixture_json_invalid"); throw; }
        if (!Encoding.UTF8.GetBytes(FormatNode(node)).SequenceEqual(bytes)) Fail("fixture_json_noncanonical");
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 });
    }

    private static void RequireNoDuplicateJsonProperties(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 });
        var objects = new Stack<HashSet<string>?>();
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new HashSet<string>(StringComparer.Ordinal));
                else if (reader.TokenType == JsonTokenType.StartArray) objects.Push(null);
                else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) objects.Pop();
                else if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var current = objects.Peek();
                    if (current is null || !current.Add(reader.GetString()!)) Fail("fixture_json_duplicate");
                }
            }
        }
        catch (JsonException) { Fail("fixture_json_invalid"); }
        if (objects.Count != 0) Fail("fixture_json_invalid");
    }

    private static string FormatNode(JsonNode node) =>
        node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, TypeInfoResolver = new DefaultJsonTypeInfoResolver() })
            .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

    private static void RequireShape(JsonElement value, string code, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.EnumerateObject().Select(item => item.Name).SequenceEqual(names, StringComparer.Ordinal)) Fail(code);
    }

    private static JsonElement RequireObject(JsonElement parent, string name, string code)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object) Fail(code);
        return value;
    }

    private static JsonElement RequireArray(JsonElement parent, string name, string code)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) Fail(code);
        return value;
    }

    private static string RequireString(JsonElement parent, string name, string? expected, string code)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) Fail(code);
        var result = value.GetString()!;
        if (expected is not null && result != expected) Fail(code);
        return result;
    }

    private static string? RequireNullableString(JsonElement parent, string name, string code)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) Fail(code);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static int RequireInt32(JsonElement parent, string name, string code)
    {
        var result = 0;
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out result) || value.GetRawText() != result.ToString(System.Globalization.CultureInfo.InvariantCulture)) Fail(code);
        return result;
    }

    private static long RequireInt64(JsonElement parent, string name, string code)
    {
        long result = 0;
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out result) || value.GetRawText() != result.ToString(System.Globalization.CultureInfo.InvariantCulture)) Fail(code);
        return result;
    }

    private static void RequireBoolean(JsonElement parent, string name, bool expected, string code)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || value.GetBoolean() != expected) Fail(code);
    }

    private static void RequireStringArray(JsonElement parent, string name, string[] expected, string code)
    {
        var value = RequireArray(parent, name, code);
        var actual = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal)) Fail(code);
    }

    private static DateTimeOffset RequireUtc(JsonElement parent, string name, string code)
    {
        var text = RequireString(parent, name, null, code);
        if (!DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var value) || !text.EndsWith('Z')) Fail(code);
        return value;
    }

    private static void RequireFutureUtc(JsonElement parent, string name, DateTimeOffset now, string code)
    {
        if (RequireUtc(parent, name, code) <= now) Fail(code);
    }

    private static void RequireCanonicalUuid(JsonElement parent, string name, string code)
    {
        var text = RequireString(parent, name, null, code);
        if (!Guid.TryParseExact(text, "D", out var value) || value.ToString("D") != text) Fail(code);
        var bytes = value.ToByteArray();
        var version = (bytes[7] >> 4) & 0xf;
        var variant = (bytes[8] >> 6) & 0x3;
        if (version is < 1 or > 5 || variant != 2) Fail(code);
    }

    private static byte[] RequireCanonicalBase64Url(string value, int size, string code)
    {
        if (!Regex.IsMatch(value, "^[A-Za-z0-9_-]+$") || value.Contains('=') || value.Length % 4 == 1) Fail(code);
        byte[] bytes;
        try { bytes = DecodeBase64Url(value); } catch (FormatException) { Fail(code); throw; }
        if (bytes.Length != size || EncodeBase64Url(bytes) != value) Fail(code);
        return bytes;
    }

    private static byte[] Sign(Ed25519PrivateKeyParameters key, byte[] payload)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, key);
        signer.BlockUpdate(payload, 0, payload.Length);
        return signer.GenerateSignature();
    }

    private static string EncodeBase64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static void Swap(JsonArray values, int left, int right)
    {
        var leftValue = values[left]!.DeepClone();
        var rightValue = values[right]!.DeepClone();
        values[left] = rightValue;
        values[right] = leftValue;
    }

    private static void MovePropertyToEnd(JsonObject value, string property)
    {
        var node = value[property]?.DeepClone() ?? throw new InvalidDataException("fixture_test_reorder_property");
        if (!value.Remove(property)) throw new InvalidDataException("fixture_test_reorder_property");
        value.Add(property, node);
    }
    private static void AssertOwnedError(string code, Action action)
    {
        var error = AssertEx.Throws<InvalidDataException>(action);
        AssertEx.Equal(code, error.Message);
    }
    private static void Fail(string code) => throw new InvalidDataException(code);
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static void WriteU32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xffffffffu;
        foreach (var item in bytes)
        {
            crc ^= item;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
        }
        return ~crc;
    }

    private static bool IsTextSource(string path) => new[] { ".cs", ".ps1", ".psm1", ".bicep", ".json", ".md", ".yml", ".yaml" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static string FixtureRoot() => Path.Combine(RepositoryRoot(), "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", FixtureDirectoryName);
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PageMaker365.Installer.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate installer repository root.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindStreamData
    {
        public long StreamSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] public string StreamName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstStreamW(string fileName, int infoLevel, out Win32FindStreamData data, int flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindNextStreamW(IntPtr handle, out Win32FindStreamData data);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindClose(IntPtr handle);

    private sealed record Pin(string Name, long Size, string Sha256);
    private sealed record ZipEntry(string Name, byte[] Data);
    private sealed record RuntimeNegative(string Id, string Operation, int Status, string Error, int ArtifactOpens, int ReceiptMutations, int BodyBytes);
    private sealed record ProtectedNegative(string Id, int Status, string Error, int ProtectedReads, int Redemptions, int BodyBytes);
    private sealed record DownloadExpectation(string Id, string Kind, string BodyFile, int Offset, int Length, string? Range);
    private sealed record SemanticContext(FixtureBundle Bundle, PrivateRuntimeDeliveryV07PackageService Service, PrivateRuntimeDeliveryPackageV07 Package, JsonElement PackageRoot);

    private sealed class FixtureBundle
    {
        public Dictionary<string, byte[]> Files { get; }
        private FixtureBundle(Dictionary<string, byte[]> files) => Files = files;
        public byte[] this[string name] => Files.TryGetValue(name, out var value) ? value : throw new InvalidDataException("fixture_bundle_missing");
        public static FixtureBundle Load(string root) => new(Pins.ToDictionary(pin => pin.Name, pin => Read(root, pin.Name), StringComparer.Ordinal));
        public FixtureBundle Clone() => new(Files.ToDictionary(item => item.Key, item => item.Value.ToArray(), StringComparer.Ordinal));
        public JsonObject JsonObject(string name) => JsonNode.Parse(this[name])!.AsObject();
        public void SetJson(string name, JsonNode value) => Files[name] = Encoding.UTF8.GetBytes(FormatNode(value));
        public void MutateJson(string name, Action<JsonObject> mutate)
        {
            var value = JsonObject(name);
            mutate(value);
            SetJson(name, value);
        }
        public void RefreshSelfManifest()
        {
            var manifest = JsonObject("sha256-manifest.json");
            var rows = manifest["files"]!.AsArray();
            if (rows.Count != Pins.Length - 1) Fail("fixture_test_manifest_shape");
            for (var index = 0; index < rows.Count; index++)
            {
                var name = Pins[index].Name;
                rows[index]!["name"] = name;
                rows[index]!["sizeBytes"] = this[name].LongLength;
                rows[index]!["sha256"] = Sha256(this[name]);
            }
            SetJson("sha256-manifest.json", manifest);
        }
    }

    private sealed record TestAuthority(string KeyId, Ed25519PrivateKeyParameters PrivateKey, string PublicKeyPem, PackageTrustOptions Trust)
    {
        public static TestAuthority Create(string keyId)
        {
            var seed = RandomNumberGenerator.GetBytes(32);
            try
            {
                var privateKey = new Ed25519PrivateKeyParameters(seed, 0);
                var publicKey = privateKey.GeneratePublicKey();
                var der = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey).GetDerEncoded();
                var base64 = Convert.ToBase64String(der);
                var lines = Enumerable.Range(0, (base64.Length + 63) / 64).Select(index => base64.Substring(index * 64, Math.Min(64, base64.Length - index * 64)));
                var pem = "-----BEGIN PUBLIC KEY-----\n" + string.Join("\n", lines) + "\n-----END PUBLIC KEY-----\n";
                var trust = new PackageTrustOptions { TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal) { [keyId] = pem } };
                return new TestAuthority(keyId, privateKey, pem, trust);
            }
            finally { CryptographicOperations.ZeroMemory(seed); }
        }
    }
}
