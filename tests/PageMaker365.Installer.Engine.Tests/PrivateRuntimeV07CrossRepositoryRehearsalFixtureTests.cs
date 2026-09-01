using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
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
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
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
}
