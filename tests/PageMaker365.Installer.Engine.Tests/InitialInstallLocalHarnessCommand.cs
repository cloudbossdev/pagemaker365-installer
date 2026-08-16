using System.Text.Json;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

/// <summary>
/// Test-only local bridge for the cross-repository two-persona harness.
/// It reads local fixture bytes, validates them with the production v1 engine
/// validator, and writes a bounded v1 receipt. It has no HTTP, cloud, package,
/// deployment, 0.4, or runtime-install dependency.
/// </summary>
internal static class InitialInstallLocalHarnessCommand
{
    private const string Command = "--initial-install-local-harness";

    public static async Task<int> RunAsync(string[] args)
    {
        if (!string.Equals(args.FirstOrDefault(), Command, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Unknown test command. Expected {Command}.");
            return 2;
        }

        if (args.Length is < 4 or > 5)
        {
            Console.Error.WriteLine($"Usage: {Command} <delivery-envelope.json> <trusted-public-key.pem> <receipt-output.json> [utc-occurred-at]");
            return 2;
        }

        try
        {
            var deliveryPath = RequireExistingFile(args[1], "delivery envelope");
            var publicKeyPath = RequireExistingFile(args[2], "trusted public key");
            var receiptPath = RequireOutputPath(args[3]);
            var occurredAt = args.Length == 5
                ? ParseUtcTimestamp(args[4])
                : DateTimeOffset.UtcNow;

            var deliveryJson = await File.ReadAllTextAsync(deliveryPath);
            var publicKeyPem = await File.ReadAllTextAsync(publicKeyPath);
            var validation = new InitialInstallDeliveryService().ValidateJson(
                deliveryJson,
                new InitialInstallTrustOptions
                {
                    TrustedPublicKeysById = ReadTrustMap(deliveryJson, publicKeyPem)
                },
                occurredAt);

            var receipt = InitialInstallValidationReceiptFactory.CreateValidated(
                validation,
                installerVersion: "local-two-persona-harness",
                occurredAt);
            var receiptJson = JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = false });
            await using (var output = new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var writer = new StreamWriter(output))
            {
                await writer.WriteAsync(receiptJson + Environment.NewLine);
            }
            Console.WriteLine($"PASS initial-install local harness validated artifact {validation.ArtifactId} and wrote a v1 receipt.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            Console.Error.WriteLine($"FAIL initial-install local harness: {exception.Message}");
            return 1;
        }
    }

    private static Dictionary<string, string> ReadTrustMap(string deliveryJson, string publicKeyPem)
    {
        using var document = JsonDocument.Parse(deliveryJson);
        var keyId = document.RootElement
            .GetProperty("package")
            .GetProperty("signingKeyId")
            .GetString();
        if (string.IsNullOrWhiteSpace(keyId))
        {
            throw new InvalidDataException("Initial-install delivery signingKeyId is required.");
        }
        if (string.IsNullOrWhiteSpace(publicKeyPem) || publicKeyPem.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Initial-install local harness requires one non-private trusted public key.");
        }

        return new Dictionary<string, string>(StringComparer.Ordinal) { [keyId] = publicKeyPem };
    }

    private static string RequireExistingFile(string value, string label)
    {
        var path = Path.GetFullPath(value);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Initial-install local harness {label} was not found.", path);
        }
        return path;
    }

    private static string RequireOutputPath(string value)
    {
        var path = Path.GetFullPath(value);
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("Initial-install local harness receipt output directory does not exist.");
        }
        return path;
    }

    private static DateTimeOffset ParseUtcTimestamp(string value)
    {
        if (!DateTimeOffset.TryParse(value, out var timestamp) || timestamp.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Initial-install local harness timestamp must be a UTC ISO-8601 value.");
        }
        return timestamp;
    }
}
