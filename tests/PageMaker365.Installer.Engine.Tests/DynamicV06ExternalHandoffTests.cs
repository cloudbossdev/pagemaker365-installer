using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

/// <summary>
/// Opt-in, test-only consumer for the accepted Wave 05 control-plane handoff.
/// The existing runner invokes this one explicit test entry. All implementation
/// remains isolated in this assignment-specific source file.
/// </summary>
internal static class DynamicV06ExternalHandoffTests
{
    private const string OptInVariable = "PM365_DYNAMIC_V06_HANDOFF_DIR";
    private const string SchemaVersion = "pagemaker365.dynamic-local-runtime-handoff.v2";
    private const string SyntheticTrust = "synthetic-test-only-never-production";
    private const string ExpectedKeyId = "test-only-w05-v06-ed25519";
    private const string ExpectedPublicKeySha256 = "7f2d9ed0b71b8e5a6c5cf30e647d6e20b5bca6dac8071f11abe3fef8014db610";
    private const string ExpectedReleaseId = "pm365-runtime-1.4.3+c31427d";
    private const string ExpectedRuntimeVersion = "1.4.3";
    private const string ExpectedSourceCommit = "c31427d0027adb4fd03de142fde18c4209ca44ce";
    private const string ExpectedApiOrigin = "https://api.fixture.invalid";
    private const string ExpectedPortalOrigin = "https://portal.fixture.invalid";
    private const string LocalApiOrigin = "https://localhost";
    private const string TestApiKeyVariable = "PM365_W05_DYNAMIC_V06_UNSET_API_KEY";
    private const string DeliverySessionId = "rds_W05DynamicV06LocalTest001";
    private const string PublicKeyPem = "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEA11qYAYKxCrfVS/7TyWQHOg7hcvPapiMlrwIaaPcHURo=\n-----END PUBLIC KEY-----\n";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task RunFromEnvironmentAsync()
    {
        var handoffDirectory = Environment.GetEnvironmentVariable(OptInVariable);
        if (string.IsNullOrWhiteSpace(handoffDirectory))
        {
            Console.WriteLine("SKIP Dynamic local runtime v0.6 handoff: explicit v0.6 opt-in is not set; no external handoff was read.");
            return;
        }

        await RunAsync(handoffDirectory);
    }

    private static async Task RunAsync(string handoffDirectory)
    {
        Require(Path.IsPathFullyQualified(handoffDirectory), "Dynamic v0.6 handoff path must be absolute.");
        var acceptedRoot = Path.GetFullPath(handoffDirectory);
        var loaded = LoadClosedHandoff(acceptedRoot);
        await AssertDefaultTransportDeniedAsync(loaded);
        await AssertInjectedAcquisitionAsync(loaded, TransportMode.Success);
        await AssertInjectedAcquisitionAsync(loaded, TransportMode.AlteredApi);
        await AssertInjectedAcquisitionAsync(loaded, TransportMode.TruncatedApi);
        await AssertInjectedAcquisitionAsync(loaded, TransportMode.CacheableApi);
        await AssertInjectedAcquisitionAsync(loaded, TransportMode.RedirectApi);
        await AssertInjectedAcquisitionAsync(loaded, TransportMode.InvalidPortalRange);
        AssertNegativeHandoffs(acceptedRoot);
        AssertNoDeploymentSurface();
    }

    private static LoadedHandoff LoadClosedHandoff(string root)
    {
        Require(Directory.Exists(root), "Dynamic v0.6 handoff directory is unavailable.");
        RejectLink(root, "handoff root");
        RequireExactEntries(root, ["artifacts", "customer-install.v0.6.json", "handoff.json"]);
        var artifactDirectory = Contained(root, "artifacts");
        Require(Directory.Exists(artifactDirectory), "Dynamic v0.6 artifact directory is missing.");
        RejectLink(artifactDirectory, "artifact directory");
        RequireExactEntries(artifactDirectory, ["api.zip", "portal.zip"]);

        var handoffPath = RequireRegularFile(root, "handoff.json");
        var packagePath = RequireRegularFile(root, "customer-install.v0.6.json");
        var apiPath = RequireRegularFile(artifactDirectory, "api.zip");
        var portalPath = RequireRegularFile(artifactDirectory, "portal.zip");

        var handoffBytes = File.ReadAllBytes(handoffPath);
        var envelope = ParseCanonicalEnvelope(handoffBytes);
        Require(envelope.PackageFile == "customer-install.v0.6.json", "Dynamic handoff package file is not the closed v0.6 name.");
        Require(envelope.SigningKeyId == ExpectedKeyId, "Dynamic handoff key ID differs from the independent pin.");
        Require(FixedEquals(envelope.SigningPublicKeySha256, ExpectedPublicKeySha256), "Dynamic handoff public-key digest differs from the independent pin.");
        Require(FixedEquals(Sha256(StrictUtf8.GetBytes(PublicKeyPem)), ExpectedPublicKeySha256), "Pinned public material digest is invalid.");

        var packageBytes = File.ReadAllBytes(packagePath);
        Require(FixedEquals(Sha256(packageBytes), envelope.PackageSha256), "Dynamic handoff package digest is invalid.");
        var packageJson = StrictUtf8.GetString(packageBytes);
        var trust = new PackageTrustOptions
        {
            TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ExpectedKeyId] = PublicKeyPem
            }
        };
        var package = new PrivateRuntimeDeliveryV06PackageService().ValidateJson(packageJson, trust);
        Require(package.ContractVersion == "0.6" && package.ManifestContractVersion == "3.0", "Dynamic package is not the closed v0.6/v3 profile.");
        Require(package.Product == "PageMaker365", "Dynamic package product identity differs.");
        Require(package.ReleaseId == envelope.ReleaseId && package.RuntimeVersion == envelope.RuntimeVersion && package.SourceCommit == envelope.SourceCommit, "Dynamic package runtime identity differs from the envelope.");
        Require(package.OnboardingSessionId == envelope.SessionId && package.TenantId == envelope.ExpectedTenantId, "Dynamic package onboarding binding differs from the envelope.");
        Require(package.ExpiresAt == envelope.ExpiresAt && package.ExpiresAt > DateTimeOffset.UtcNow, "Dynamic package expiry differs or has elapsed.");
        Require(package.ApiDeliveryReference == "ard_AAAAAAAAAAAAAAAAAAAAAAAA" && package.PortalDeliveryReference == "ard_BBBBBBBBBBBBBBBBBBBBBBBB", "Dynamic delivery references differ from the accepted closed fixture.");
        Require(package.Api.FileName != package.Portal.FileName, "Dynamic artifact names are not distinct.");
        Require(FileLength(apiPath) == package.Api.SizeBytes && FixedEquals(Sha256File(apiPath), package.Api.Sha256), "Dynamic API artifact differs from the signed package.");
        Require(FileLength(portalPath) == package.Portal.SizeBytes && FixedEquals(Sha256File(portalPath), package.Portal.Sha256), "Dynamic portal artifact differs from the signed package.");

        return new LoadedHandoff(root, envelope, packageJson, package, trust, apiPath, portalPath);
    }

    private static DynamicEnvelope ParseCanonicalEnvelope(byte[] bytes)
    {
        Require(bytes.Length is > 1 and <= 32_768, "Dynamic handoff envelope size is invalid.");
        Require(bytes[0] is not 0xef || bytes.Length < 3 || bytes[1] != 0xbb || bytes[2] != 0xbf, "Dynamic handoff envelope must not contain a UTF-8 BOM.");
        var json = StrictUtf8.GetString(bytes);
        Require(!json.Contains('\r'), "Dynamic handoff envelope must use LF line endings.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 4 });
        var root = document.RootElement;
        var expected = new[] { "schemaVersion", "trust", "releaseId", "runtimeVersion", "sourceCommit", "packageFile", "packageSha256", "signingKeyId", "signingPublicKeySha256", "sessionId", "expectedTenantId", "oneTimeCode", "apiBaseUrl", "portalBaseUrl", "expiresAt" };
        var actual = root.EnumerateObject().Select(property => property.Name).ToArray();
        Require(actual.SequenceEqual(expected, StringComparer.Ordinal), "Dynamic handoff envelope fields or order differ.");
        Require(actual.Distinct(StringComparer.Ordinal).Count() == expected.Length, "Dynamic handoff envelope contains duplicate fields.");

        string Value(string name)
        {
            var property = root.GetProperty(name);
            Require(property.ValueKind == JsonValueKind.String, $"Dynamic handoff {name} must be a string.");
            return property.GetString()!;
        }

        var expiresText = Value("expiresAt");
        Require(DateTimeOffset.TryParseExact(expiresText, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var expiresAt), "Dynamic handoff expiry is not canonical UTC.");
        expiresAt = expiresAt.ToUniversalTime();
        var envelope = new DynamicEnvelope(
            Value("schemaVersion"), Value("trust"), Value("releaseId"), Value("runtimeVersion"), Value("sourceCommit"),
            Value("packageFile"), Value("packageSha256"), Value("signingKeyId"), Value("signingPublicKeySha256"),
            Value("sessionId"), Value("expectedTenantId"), Value("oneTimeCode"), Value("apiBaseUrl"), Value("portalBaseUrl"), expiresAt);
        ValidateEnvelopeValues(envelope);
        Require(FixedEquals(json, FormatEnvelope(envelope)), "Dynamic handoff envelope is not canonical JSON.");
        return envelope;
    }

    private static void ValidateEnvelopeValues(DynamicEnvelope envelope)
    {
        Require(envelope.SchemaVersion == SchemaVersion && envelope.Trust == SyntheticTrust, "Dynamic handoff schema or synthetic trust marker differs.");
        Require(envelope.ReleaseId == ExpectedReleaseId && envelope.RuntimeVersion == ExpectedRuntimeVersion && envelope.SourceCommit == ExpectedSourceCommit, "Dynamic handoff runtime identity differs.");
        Require(Regex.IsMatch(envelope.PackageSha256, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant), "Dynamic handoff package digest is invalid.");
        Require(Regex.IsMatch(envelope.SigningPublicKeySha256, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant), "Dynamic handoff public-key digest is invalid.");
        Require(Regex.IsMatch(envelope.SessionId, "^onb_[A-Za-z0-9_-]{16,64}$", RegexOptions.CultureInvariant), "Dynamic handoff session ID is invalid.");
        Require(Regex.IsMatch(envelope.ExpectedTenantId, "^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", RegexOptions.CultureInvariant), "Dynamic handoff tenant ID is invalid.");
        Require(Regex.IsMatch(envelope.OneTimeCode, "^[A-Za-z0-9._~-]{16,128}$", RegexOptions.CultureInvariant), "Dynamic handoff setup code is invalid.");
        Require(envelope.ApiBaseUrl == ExpectedApiOrigin && envelope.PortalBaseUrl == ExpectedPortalOrigin, "Dynamic handoff synthetic origins differ.");
        Require(envelope.ExpiresAt > DateTimeOffset.UtcNow, "Dynamic handoff has expired.");
    }

    private static string FormatEnvelope(DynamicEnvelope envelope)
    {
        var canonical = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = envelope.SchemaVersion,
            ["trust"] = envelope.Trust,
            ["releaseId"] = envelope.ReleaseId,
            ["runtimeVersion"] = envelope.RuntimeVersion,
            ["sourceCommit"] = envelope.SourceCommit,
            ["packageFile"] = envelope.PackageFile,
            ["packageSha256"] = envelope.PackageSha256,
            ["signingKeyId"] = envelope.SigningKeyId,
            ["signingPublicKeySha256"] = envelope.SigningPublicKeySha256,
            ["sessionId"] = envelope.SessionId,
            ["expectedTenantId"] = envelope.ExpectedTenantId,
            ["oneTimeCode"] = envelope.OneTimeCode,
            ["apiBaseUrl"] = envelope.ApiBaseUrl,
            ["portalBaseUrl"] = envelope.PortalBaseUrl,
            ["expiresAt"] = envelope.ExpiresAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture)
        };
        return JsonSerializer.Serialize(canonical, CanonicalJsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static async Task AssertDefaultTransportDeniedAsync(LoadedHandoff loaded)
    {
        var outputRoot = NewTemporaryRoot("default-denied");
        try
        {
            using var client = new PrivateRuntimeDeliveryClient(new PrivateRuntimeDeliveryOptions { EnablePackageV06 = true, Timeout = TimeSpan.FromSeconds(30) });
            var denied = false;
            try
            {
                await client.AcquireV06Async(loaded.PackageJson, loaded.Trust, CreateSession(loaded.Envelope), outputRoot, "0.6.0");
            }
            catch (InvalidOperationException)
            {
                denied = true;
            }
            Require(denied, "Dynamic v0.6 default HTTP transport was not denied.");
            Require(!Directory.Exists(outputRoot), "Default transport denial created acquisition output.");
        }
        finally
        {
            DeleteOwnedRoot(outputRoot);
        }
    }

    private static async Task AssertInjectedAcquisitionAsync(LoadedHandoff loaded, TransportMode mode)
    {
        var outputRoot = NewTemporaryRoot("injected-" + mode.ToString().ToLowerInvariant());
        var requests = new List<string>();
        try
        {
            if (mode == TransportMode.Success || mode == TransportMode.InvalidPortalRange)
            {
                var partial = Path.Combine(outputRoot, "runtime-acquisition", $"portal-{loaded.Package.Portal.Sha256}.partial");
                Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
                var portalBytes = File.ReadAllBytes(loaded.PortalPath);
                await File.WriteAllBytesAsync(partial, portalBytes[..(portalBytes.Length / 2)]);
            }

            using var handler = new DynamicArtifactHandler(loaded, mode, requests);
            using var client = new PrivateRuntimeDeliveryClient(new PrivateRuntimeDeliveryOptions
            {
                ApiBaseUrl = LocalApiOrigin,
                ApiKeyEnvironmentVariable = TestApiKeyVariable,
                EnablePackageV06 = true,
                Timeout = TimeSpan.FromMinutes(2)
            }, handler);
            var result = await client.AcquireV06Async(loaded.PackageJson, loaded.Trust, CreateSession(loaded.Envelope), outputRoot, "0.6.0");

            if (mode == TransportMode.Success)
            {
                Require(result.IsVerified && result.ReceiptStatus == "submitted" && result.Artifacts.Count == 2, "Injected dynamic acquisition did not produce two verified artifacts and an accepted receipt.");
                var api = result.Artifacts.Single(artifact => artifact.ArtifactKind == "api");
                var portal = result.Artifacts.Single(artifact => artifact.ArtifactKind == "portal");
                Require(FixedEquals(Sha256File(api.VerifiedPath), loaded.Package.Api.Sha256) && FileLength(api.VerifiedPath) == loaded.Package.Api.SizeBytes, "Verified API output differs from the accepted external bytes.");
                Require(FixedEquals(Sha256File(portal.VerifiedPath), loaded.Package.Portal.Sha256) && FileLength(portal.VerifiedPath) == loaded.Package.Portal.SizeBytes, "Verified portal output differs from the accepted external bytes.");
                Require(portal.RangeRequestCount == 1, "Injected portal acquisition did not exercise exact range continuation.");
                Require(requests.Count == 4 && requests.All(path => !path.Contains("ard_", StringComparison.Ordinal) && !path.Contains('?', StringComparison.Ordinal)), "Injected requests leaked a reference into a URL or used an unexpected request count.");
            }
            else
            {
                Require(!result.IsVerified && !string.IsNullOrWhiteSpace(result.SafeErrorCode), "Unsafe injected response did not fail closed.");
                Require(!result.Artifacts.Any(artifact => artifact.ArtifactKind == "portal" && artifact.VerificationStatus == "passed"), "Unsafe injected response produced a verified portal result.");
            }
        }
        finally
        {
            DeleteOwnedRoot(outputRoot);
        }
    }

    private static OnboardingBootstrapSession CreateSession(DynamicEnvelope envelope) => new()
    {
        ContractVersion = "0.1",
        SessionId = envelope.SessionId,
        ExpectedTenantId = envelope.ExpectedTenantId,
        OneTimeCode = envelope.OneTimeCode,
        ApiBaseUrl = LocalApiOrigin,
        PortalBaseUrl = ExpectedPortalOrigin,
        ExpiresAt = envelope.ExpiresAt
    };

    private static void AssertNegativeHandoffs(string acceptedRoot)
    {
        Mutated("extra-file", root => File.WriteAllText(Path.Combine(root, "unexpected.txt"), "denied", StrictUtf8), root => ExpectLoadDenied(root));
        Mutated("trust-file", root => File.WriteAllText(Path.Combine(root, "signing-trust.pem"), "untrusted", StrictUtf8), root => ExpectLoadDenied(root));
        Mutated("missing-file", root => File.Delete(Path.Combine(root, "artifacts", "api.zip")), root => ExpectLoadDenied(root));
        Mutated("renamed-file", root => File.Move(Path.Combine(root, "artifacts", "api.zip"), Path.Combine(root, "artifacts", "renamed.zip")), root => ExpectLoadDenied(root));
        MutatedEnvelope("pin-mismatch", envelope => envelope.SigningPublicKeySha256 = new string('0', 64));
        MutatedEnvelope("key-mismatch", envelope => envelope.SigningKeyId = "untrusted-w05-key");
        MutatedEnvelope("digest-mismatch", envelope => envelope.PackageSha256 = new string('0', 64));
        MutatedEnvelope("identity-mismatch", envelope => envelope.RuntimeVersion = "1.4.4");
        MutatedEnvelope("session-mismatch", envelope => envelope.SessionId = "onb_W05DifferentSession0001");
        MutatedEnvelope("origin-mismatch", envelope => envelope.ApiBaseUrl = "https://other.fixture.invalid");
        MutatedEnvelope("expiry-mismatch", envelope => envelope.ExpiresAt = envelope.ExpiresAt.AddSeconds(1));

        Mutated("signature-mismatch", root =>
        {
            var packagePath = Path.Combine(root, "customer-install.v0.6.json");
            var packageNode = JsonNode.Parse(File.ReadAllText(packagePath, StrictUtf8))!.AsObject();
            var controlPlane = packageNode["controlPlane"]!.AsObject();
            var signature = controlPlane["signature"]!.GetValue<string>();
            controlPlane["signature"] = (signature[0] == 'A' ? "B" : "A") + signature[1..];
            using var document = JsonDocument.Parse(packageNode.ToJsonString());
            var packageJson = PrivateRuntimeDeliveryV06PackageService.FormatCanonicalPackage(document.RootElement);
            File.WriteAllText(packagePath, packageJson, StrictUtf8);
            RewriteEnvelope(root, envelope => envelope.PackageSha256 = Sha256(StrictUtf8.GetBytes(packageJson)));
        }, root => ExpectLoadDenied(root));

        Mutated("mixed-version", root =>
        {
            var packagePath = Path.Combine(root, "customer-install.v0.6.json");
            var packageJson = File.ReadAllText(packagePath, StrictUtf8).Replace("\"contractVersion\": \"0.6\"", "\"contractVersion\": \"0.5\"", StringComparison.Ordinal);
            File.WriteAllText(packagePath, packageJson, StrictUtf8);
            RewriteEnvelope(root, envelope => envelope.PackageSha256 = Sha256(StrictUtf8.GetBytes(packageJson)));
        }, root => ExpectLoadDenied(root));

        var linkRoot = CloneAccepted("symlink");
        try
        {
            var linked = Path.Combine(linkRoot, "handoff.json");
            File.Delete(linked);
            try
            {
                File.CreateSymbolicLink(linked, Path.Combine(acceptedRoot, "handoff.json"));
            }
            catch (IOException)
            {
                var linkedArtifacts = Path.Combine(linkRoot, "artifacts");
                Directory.Delete(linkedArtifacts, recursive: true);
                CreateWindowsJunction(linkedArtifacts, Path.Combine(acceptedRoot, "artifacts"));
            }
            ExpectLoadDenied(linkRoot);
        }
        finally
        {
            var linkedFile = Path.Combine(linkRoot, "handoff.json");
            if (File.Exists(linkedFile) && (File.GetAttributes(linkedFile) & FileAttributes.ReparsePoint) != 0) File.Delete(linkedFile);
            var linkedDirectory = Path.Combine(linkRoot, "artifacts");
            if (Directory.Exists(linkedDirectory) && (File.GetAttributes(linkedDirectory) & FileAttributes.ReparsePoint) != 0) Directory.Delete(linkedDirectory, recursive: false);
            DeleteOwnedRoot(linkRoot);
        }

        void MutatedEnvelope(string name, Action<MutableEnvelope> mutate) => Mutated(name, root => RewriteEnvelope(root, mutate), root => ExpectLoadDenied(root));
        void Mutated(string name, Action<string> mutate, Action<string> assert)
        {
            var root = CloneAccepted(name);
            try
            {
                mutate(root);
                assert(root);
            }
            finally
            {
                DeleteOwnedRoot(root);
            }
        }

        string CloneAccepted(string name)
        {
            var root = NewTemporaryRoot("negative-" + name);
            CopyTree(acceptedRoot, root);
            return root;
        }
    }

    private static void RewriteEnvelope(string root, Action<MutableEnvelope> mutate)
    {
        var path = Path.Combine(root, "handoff.json");
        var original = ParseCanonicalEnvelope(File.ReadAllBytes(path));
        var mutable = new MutableEnvelope(original);
        mutate(mutable);
        File.WriteAllText(path, FormatEnvelope(mutable.ToImmutable()), StrictUtf8);
    }

    private static void ExpectLoadDenied(string root)
    {
        var denied = false;
        try
        {
            _ = LoadClosedHandoff(root);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            denied = true;
        }
        Require(denied, "Mutated dynamic handoff was not denied.");
    }

    private static void AssertNoDeploymentSurface()
    {
        var deploymentNames = typeof(PageMaker365.Installer.Engine.Services.InstallerEngine).GetMethods().Select(method => method.Name).Where(name => name.Contains("V06", StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(deploymentNames.Length == 0, "Dynamic v0.6 acquisition became reachable from the deployment engine.");
    }

    private static void RequireExactEntries(string directory, string[] expected)
    {
        var actual = Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Require(actual.SequenceEqual(expected.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal), "Dynamic handoff directory shape differs from the closed contract.");
    }

    private static string RequireRegularFile(string directory, string name)
    {
        var path = Contained(directory, name);
        Require(File.Exists(path), "Dynamic handoff required file is missing.");
        RejectLink(path, "handoff file");
        Require((File.GetAttributes(path) & FileAttributes.Directory) == 0, "Dynamic handoff expected a regular file.");
        return path;
    }

    private static void RejectLink(string path, string description)
    {
        var info = (FileSystemInfo)(Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path));
        Require((info.Attributes & FileAttributes.ReparsePoint) == 0 && info.LinkTarget is null, $"Dynamic {description} must not be linked.");
    }

    private static string Contained(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, relative));
        Require(candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase), "Dynamic handoff path escaped its root.");
        return candidate;
    }

    private static string NewTemporaryRoot(string name) => Path.Combine(Path.GetTempPath(), $"pm365-w05-v06-{name}-{Guid.NewGuid():N}");

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination, StringComparison.OrdinalIgnoreCase), overwrite: false);
        }
    }

    private static void CreateWindowsJunction(string link, string target)
    {
        Require(OperatingSystem.IsWindows(), "Dynamic handoff link denial requires a supported local link primitive.");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            ArgumentList = { "/d", "/c", "mklink", "/j", link, target },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        Require(process is not null, "Dynamic handoff junction test could not start.");
        process!.WaitForExit();
        Require(process.ExitCode == 0 && Directory.Exists(link), "Dynamic handoff junction test could not establish its local link.");
    }

    private static void DeleteOwnedRoot(string root)
    {
        if (!Directory.Exists(root)) return;
        var full = Path.GetFullPath(root);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Require(full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("pm365-w05-v06-", StringComparison.Ordinal), "Refusing to remove an unowned test directory.");
        Directory.Delete(full, recursive: true);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PageMaker365.Installer.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate installer repository root.");
    }

    private static long FileLength(string path) => new FileInfo(path).Length;
    private static string Sha256File(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static bool FixedEquals(string left, string right) => left.Length == right.Length && CryptographicOperations.FixedTimeEquals(StrictUtf8.GetBytes(left), StrictUtf8.GetBytes(right));
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private sealed class DynamicArtifactHandler(LoadedHandoff loaded, TransportMode mode, List<string> requests) : HttpMessageHandler
    {
        private readonly byte[] _apiBytes = File.ReadAllBytes(loaded.ApiPath);
        private readonly byte[] _portalBytes = File.ReadAllBytes(loaded.PortalPath);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(request.RequestUri is { Host: "localhost", Scheme: "https" } && string.IsNullOrEmpty(request.RequestUri.Query), "Injected acquisition requested an unexpected endpoint.");
            var requestUri = request.RequestUri!;
            requests.Add(requestUri.AbsolutePath);
            RequireHeader(request, "X-PM365-Onboarding-Session", loaded.Envelope.SessionId);
            RequireHeader(request, "X-PM365-Onboarding-Code", loaded.Envelope.OneTimeCode);
            Require(request.Headers.Authorization is null, "Injected acquisition unexpectedly used ambient bearer authorization.");

            HttpResponseMessage response;
            if (requestUri.AbsolutePath == PrivateRuntimeDeliveryPackage.SessionPathValue)
            {
                Require(request.Method == HttpMethod.Post, "Dynamic delivery session method differs.");
                using var body = JsonDocument.Parse(request.Content!.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult());
                Require(body.RootElement.EnumerateObject().Select(property => property.Name).SequenceEqual(["package"], StringComparer.Ordinal), "Dynamic session body is not the closed package envelope.");
                Require(FixedEquals(PrivateRuntimeDeliveryV06PackageService.FormatCanonicalPackage(body.RootElement.GetProperty("package")), loaded.PackageJson), "Dynamic session body differs from the canonical signed package.");
                var sessionExpiry = DateTimeOffset.UtcNow.AddMinutes(10);
                if (sessionExpiry >= loaded.Package.ExpiresAt) sessionExpiry = loaded.Package.ExpiresAt.AddMilliseconds(-1);
                response = JsonResponse(new
                {
                    ok = true,
                    created = true,
                    deliverySession = new
                    {
                        contractVersion = "pagemaker365.runtime-delivery-session.v1",
                        deliverySessionId = DeliverySessionId,
                        expiresAt = sessionExpiry.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture),
                        artifactKinds = new[] { "api", "portal" },
                        status = "active"
                    }
                });
            }
            else if (requestUri.AbsolutePath == PrivateRuntimeDeliveryPackage.ReceiptPathValue)
            {
                Require(request.Method == HttpMethod.Post, "Dynamic receipt method differs.");
                RequireHeader(request, PrivateRuntimeDeliveryClient.DeliverySessionHeader, DeliverySessionId);
                var text = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                Require(!text.Contains(loaded.Package.ApiDeliveryReference, StringComparison.Ordinal) && !text.Contains(loaded.Package.PortalDeliveryReference, StringComparison.Ordinal) && !text.Contains(loaded.Envelope.OneTimeCode, StringComparison.Ordinal), "Dynamic receipt leaked protected delivery material.");
                using var receipt = JsonDocument.Parse(text);
                var item = receipt.RootElement;
                response = JsonResponse(new
                {
                    ok = true,
                    created = true,
                    receipt = new
                    {
                        deliverySessionId = item.GetProperty("deliverySessionId").GetString(),
                        packageHash = item.GetProperty("packageHash").GetString(),
                        releaseId = item.GetProperty("releaseId").GetString(),
                        eventId = item.GetProperty("eventId").GetString(),
                        occurredAt = item.GetProperty("occurredAt").GetString(),
                        installerVersion = item.GetProperty("installerVersion").GetString(),
                        outcome = item.GetProperty("outcome").GetString(),
                        artifacts = item.GetProperty("artifacts"),
                        safeResult = item.GetProperty("safeResult"),
                        createdAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture)
                    }
                });
            }
            else
            {
                Require(request.Method == HttpMethod.Get, "Dynamic artifact method differs.");
                var isApi = requestUri.AbsolutePath.EndsWith("/api", StringComparison.Ordinal);
                var artifact = isApi ? loaded.Package.Api : loaded.Package.Portal;
                var expectedReference = isApi ? loaded.Package.ApiDeliveryReference : loaded.Package.PortalDeliveryReference;
                RequireHeader(request, PrivateRuntimeDeliveryClient.DeliverySessionHeader, DeliverySessionId);
                RequireHeader(request, PrivateRuntimeDeliveryClient.DeliveryReferenceHeader, expectedReference);
                RequireHeader(request, "If-Match", $"\"sha256:{artifact.Sha256}\"");
                if (isApi && mode == TransportMode.RedirectApi)
                {
                    response = new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://redirect.fixture.invalid/artifact") } };
                }
                else
                {
                    var bytes = isApi ? _apiBytes.ToArray() : _portalBytes.ToArray();
                    if (isApi && mode == TransportMode.AlteredApi) bytes[0] ^= 0xff;
                    if (isApi && mode == TransportMode.TruncatedApi) bytes = bytes[..^1];
                    var start = 0L;
                    var status = HttpStatusCode.OK;
                    ContentRangeHeaderValue? range = null;
                    if (!isApi)
                    {
                        var requested = request.Headers.Range?.Ranges.Single().From;
                        Require(requested is > 0, "Dynamic portal request did not carry the seeded range.");
                        start = requested!.Value;
                        bytes = bytes[(int)start..];
                        status = HttpStatusCode.PartialContent;
                        range = mode == TransportMode.InvalidPortalRange
                            ? new ContentRangeHeaderValue(start + 1, artifact.SizeBytes - 1, artifact.SizeBytes)
                            : new ContentRangeHeaderValue(start, artifact.SizeBytes - 1, artifact.SizeBytes);
                    }
                    response = ArtifactResponse(status, bytes, artifact, range, mode == TransportMode.CacheableApi && isApi);
                }
            }

            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        private static void RequireHeader(HttpRequestMessage request, string name, string value)
        {
            Require(request.Headers.TryGetValues(name, out var values) && values.SequenceEqual([value], StringComparer.Ordinal), $"Injected request {name} binding differs.");
        }

        private static HttpResponseMessage JsonResponse(object value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), StrictUtf8, "application/json")
        };

        private static HttpResponseMessage ArtifactResponse(HttpStatusCode status, byte[] bytes, PrivateRuntimeArtifact artifact, ContentRangeHeaderValue? range, bool cacheable)
        {
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) };
            response.Headers.CacheControl = cacheable
                ? new CacheControlHeaderValue { Public = true }
                : new CacheControlHeaderValue { Private = true, NoStore = true };
            response.Headers.ETag = new EntityTagHeaderValue($"\"sha256:{artifact.Sha256}\"");
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            response.Content.Headers.ContentLength = bytes.LongLength;
            response.Content.Headers.ContentRange = range;
            return response;
        }
    }

    private enum TransportMode
    {
        Success,
        AlteredApi,
        TruncatedApi,
        CacheableApi,
        RedirectApi,
        InvalidPortalRange
    }

    private sealed record LoadedHandoff(
        string Root,
        DynamicEnvelope Envelope,
        string PackageJson,
        PrivateRuntimeDeliveryPackage Package,
        PackageTrustOptions Trust,
        string ApiPath,
        string PortalPath);

    private sealed record DynamicEnvelope(
        string SchemaVersion,
        string Trust,
        string ReleaseId,
        string RuntimeVersion,
        string SourceCommit,
        string PackageFile,
        string PackageSha256,
        string SigningKeyId,
        string SigningPublicKeySha256,
        string SessionId,
        string ExpectedTenantId,
        string OneTimeCode,
        string ApiBaseUrl,
        string PortalBaseUrl,
        DateTimeOffset ExpiresAt);

    private sealed class MutableEnvelope(DynamicEnvelope value)
    {
        public string SchemaVersion { get; set; } = value.SchemaVersion;
        public string Trust { get; set; } = value.Trust;
        public string ReleaseId { get; set; } = value.ReleaseId;
        public string RuntimeVersion { get; set; } = value.RuntimeVersion;
        public string SourceCommit { get; set; } = value.SourceCommit;
        public string PackageFile { get; set; } = value.PackageFile;
        public string PackageSha256 { get; set; } = value.PackageSha256;
        public string SigningKeyId { get; set; } = value.SigningKeyId;
        public string SigningPublicKeySha256 { get; set; } = value.SigningPublicKeySha256;
        public string SessionId { get; set; } = value.SessionId;
        public string ExpectedTenantId { get; set; } = value.ExpectedTenantId;
        public string OneTimeCode { get; set; } = value.OneTimeCode;
        public string ApiBaseUrl { get; set; } = value.ApiBaseUrl;
        public string PortalBaseUrl { get; set; } = value.PortalBaseUrl;
        public DateTimeOffset ExpiresAt { get; set; } = value.ExpiresAt;

        public DynamicEnvelope ToImmutable() => new(SchemaVersion, Trust, ReleaseId, RuntimeVersion, SourceCommit, PackageFile, PackageSha256, SigningKeyId, SigningPublicKeySha256, SessionId, ExpectedTenantId, OneTimeCode, ApiBaseUrl, PortalBaseUrl, ExpiresAt);
    }
}
