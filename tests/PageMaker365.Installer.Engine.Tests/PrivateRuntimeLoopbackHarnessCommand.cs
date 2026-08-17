using System.Text.Json;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

/// <summary>
/// Test-only command used by the cross-repository loopback harness. It calls
/// the production private runtime acquisition client over an HTTP listener
/// bound by the P365 test process; it never invokes deployment or PowerShell.
/// </summary>
internal static class PrivateRuntimeLoopbackHarnessCommand
{
    private const string Command = "--private-runtime-loopback-harness";
    private const string BearerEnvironmentVariable = "PM365_PRIVATE_RUNTIME_LOOPBACK_TEST_TOKEN";

    public static async Task<int> RunAsync(string[] args)
    {
        if (!string.Equals(args.FirstOrDefault(), Command, StringComparison.Ordinal) || args.Length != 2)
        {
            Console.Error.WriteLine($"Usage: {Command} <local-harness-config.json>");
            return 2;
        }

        try
        {
            var configPath = RequireExistingFile(args[1], "local harness config");
            var config = await ReadConfigAsync(configPath);
            var packageJson = await File.ReadAllTextAsync(RequireExistingFile(config.PackagePath, "signed package"));
            var publicKeyPem = await File.ReadAllTextAsync(RequireExistingFile(config.PublicKeyPath, "trusted public key"));
            if (publicKeyPem.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The loopback harness requires a public trust key only.");
            }

            var keyId = ReadSigningKeyId(packageJson);
            var trust = new PackageTrustOptions
            {
                TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [keyId] = publicKeyPem
                }
            };
            var package = new PrivateRuntimeDeliveryPackageService().ValidateJson(packageJson, trust);
            var outputRoot = RequireDirectory(config.OutputRoot, "output root");
            await SeedPortalResumeBytesAsync(config.PortalPrefixPath, outputRoot, package);

            using var bearer = new EnvironmentVariableScope(BearerEnvironmentVariable, config.BearerToken);
            using var client = new PrivateRuntimeDeliveryClient(new PrivateRuntimeDeliveryOptions
            {
                ApiBaseUrl = config.ApiBaseUrl,
                ApiKeyEnvironmentVariable = BearerEnvironmentVariable,
                Timeout = TimeSpan.FromMinutes(1)
            });
            var result = await client.AcquireAsync(
                packageJson,
                trust,
                new OnboardingBootstrapSession
                {
                    SessionId = config.OnboardingSessionId,
                    OneTimeCode = config.OnboardingCode,
                    ApiBaseUrl = config.ApiBaseUrl,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                    AllowedOperations = [OnboardingOperation.InstallPackageGeneration]
                },
                outputRoot,
                "0.1.0-loopback",
                CancellationToken.None);

            if (!result.IsVerified || result.ReceiptStatus != "submitted" || result.Artifacts.Count != 2 ||
                result.Artifacts.Single(artifact => artifact.ArtifactKind == "api").RangeRequestCount != 0 ||
                result.Artifacts.Single(artifact => artifact.ArtifactKind == "portal").RangeRequestCount != 1 ||
                result.Artifacts.Any(artifact => !File.Exists(artifact.VerifiedPath)))
            {
                throw new InvalidDataException("The loopback private runtime acquisition did not complete its full and resumed streams.");
            }

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                ok = true,
                outcome = result.Outcome,
                receiptStatus = result.ReceiptStatus,
                artifactKinds = result.Artifacts.Select(artifact => artifact.ArtifactKind).OrderBy(kind => kind, StringComparer.Ordinal).ToArray(),
                portalRangeRequestCount = result.Artifacts.Single(artifact => artifact.ArtifactKind == "portal").RangeRequestCount
            }));
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or HttpRequestException)
        {
            Console.Error.WriteLine($"FAIL private runtime loopback harness: {exception.Message}");
            return 1;
        }
    }

    private static async Task<LoopbackHarnessConfig> ReadConfigAsync(string path)
    {
        var config = JsonSerializer.Deserialize<LoopbackHarnessConfig>(await File.ReadAllTextAsync(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })
            ?? throw new InvalidDataException("Local harness configuration is invalid.");
        if (string.IsNullOrWhiteSpace(config.PackagePath) || string.IsNullOrWhiteSpace(config.PublicKeyPath) ||
            string.IsNullOrWhiteSpace(config.OutputRoot) || string.IsNullOrWhiteSpace(config.PortalPrefixPath) ||
            string.IsNullOrWhiteSpace(config.ApiBaseUrl) || string.IsNullOrWhiteSpace(config.OnboardingSessionId) ||
            string.IsNullOrWhiteSpace(config.OnboardingCode) || string.IsNullOrWhiteSpace(config.BearerToken))
        {
            throw new InvalidDataException("Local harness configuration is incomplete.");
        }
        if (!Uri.TryCreate(config.ApiBaseUrl, UriKind.Absolute, out var endpoint) || !TrustedPageMaker365EndpointPolicy.IsLocalHost(endpoint.Host))
        {
            throw new InvalidDataException("The local harness API endpoint must be bound to loopback.");
        }
        return config;
    }

    private static async Task SeedPortalResumeBytesAsync(string prefixPath, string outputRoot, PrivateRuntimeDeliveryPackage package)
    {
        var prefix = await File.ReadAllBytesAsync(RequireExistingFile(prefixPath, "portal resume bytes"));
        if (prefix.Length < 1 || prefix.LongLength >= package.Portal.SizeBytes)
        {
            throw new InvalidDataException("Portal resume bytes must be a strict prefix of the signed portal artifact.");
        }
        var artifactDirectory = Path.Combine(outputRoot, "runtime-acquisition");
        Directory.CreateDirectory(artifactDirectory);
        var partialPath = Path.Combine(artifactDirectory, $"portal-{package.Portal.Sha256}.partial");
        await File.WriteAllBytesAsync(partialPath, prefix);
    }

    private static string ReadSigningKeyId(string packageJson)
    {
        using var document = JsonDocument.Parse(packageJson);
        var keyId = document.RootElement.GetProperty("controlPlane").GetProperty("signingKeyId").GetString();
        return string.IsNullOrWhiteSpace(keyId) ? throw new InvalidDataException("Signed package key identity is required.") : keyId;
    }

    private static string RequireExistingFile(string value, string label)
    {
        var path = Path.GetFullPath(value);
        if (!File.Exists(path)) throw new FileNotFoundException($"Local harness {label} was not found.", path);
        return path;
    }

    private static string RequireDirectory(string value, string label)
    {
        var path = Path.GetFullPath(value);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"Local harness {label} does not exist.");
        return path;
    }

    private sealed class LoopbackHarnessConfig
    {
        public string PackagePath { get; init; } = "";
        public string PublicKeyPath { get; init; } = "";
        public string OutputRoot { get; init; } = "";
        public string PortalPrefixPath { get; init; } = "";
        public string ApiBaseUrl { get; init; } = "";
        public string OnboardingSessionId { get; init; } = "";
        public string OnboardingCode { get; init; } = "";
        public string BearerToken { get; init; } = "";
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _original;

        public EnvironmentVariableScope(string name, string value)
        {
            _name = name;
            _original = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _original);
    }
}
