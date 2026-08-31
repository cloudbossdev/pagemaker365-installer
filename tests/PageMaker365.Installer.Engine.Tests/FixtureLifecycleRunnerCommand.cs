using System.Text.Json;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

/// <summary>
/// Explicit, fixture-only CLI bridge for the cross-repository lifecycle
/// harness. It is disabled unless the caller separately sets
/// PM365_ENABLE_FIXTURE_LIFECYCLE_RUNNER=1.
/// </summary>
internal static class FixtureLifecycleRunnerCommand
{
    private const string Command = "--fixture-lifecycle-runner";

    public static async Task<int> RunAsync(string[] args)
    {
        if (!string.Equals(args.FirstOrDefault(), Command, StringComparison.Ordinal) || args.Length != 3)
        {
            Console.Error.WriteLine($"Usage: {Command} <fixture-runner.json> <sanitized-result.json>");
            return 2;
        }

        try
        {
            var requestPath = RequireExistingFile(args[1]);
            var outputPath = RequireNewOutputPath(args[2]);
            var request = JsonSerializer.Deserialize<FixtureLifecycleRunnerRequest>(
                await File.ReadAllTextAsync(requestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Fixture lifecycle request is invalid.");

            var result = await new FixtureLifecycleRunner(new StructuredLogger(new RedactionService())).RunAsync(request);
            await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(output, result, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await output.WriteAsync(new byte[] { (byte)'\n' });
            Console.WriteLine($"{result.Status.ToUpperInvariant()} fixture lifecycle runner wrote sanitized result.");
            return result.Status.Equals("passed", StringComparison.Ordinal) ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            Console.Error.WriteLine("FAIL fixture lifecycle runner: request rejected or fixture execution did not complete.");
            return 1;
        }
    }

    private static string RequireExistingFile(string value)
    {
        var path = Path.GetFullPath(value);
        if (!File.Exists(path)) throw new FileNotFoundException("Fixture lifecycle request file was not found.", path);
        return path;
    }

    private static string RequireNewOutputPath(string value)
    {
        var path = Path.GetFullPath(value);
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("Fixture lifecycle result output directory does not exist.");
        }
        if (File.Exists(path))
        {
            throw new IOException("Fixture lifecycle result output already exists.");
        }
        return path;
    }
}
