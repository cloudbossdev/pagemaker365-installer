using System.IO.Compression;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class SupportBundleService
{
    private readonly RedactionService _redactionService;
    private readonly InstallReportService _installReportService = new();

    public SupportBundleService(RedactionService redactionService)
    {
        _redactionService = redactionService;
    }

    public async Task<string> CreateAsync(InstallerSession session, string outputRoot, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(session.LogDirectory);

        var bundleRoot = Path.Combine(session.LogDirectory, "support-bundle");
        if (Directory.Exists(bundleRoot))
        {
            Directory.Delete(bundleRoot, recursive: true);
        }

        Directory.CreateDirectory(bundleRoot);

        var redactedSession = new
        {
            session.SessionId,
            session.CreatedAt,
            session.CurrentPhase,
            session.Status,
            Config = _redactionService.RedactConfig(session.Config),
            session.Results
        };

        var json = JsonSerializer.Serialize(redactedSession, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(bundleRoot, "install-session.redacted.json"), json, cancellationToken);
        await _installReportService.CreateMarkdownAsync(session, Path.Combine(bundleRoot, "install-report.md"), cancellationToken);

        var redactedLogPath = Path.Combine(session.LogDirectory, "redacted-install.log");
        if (File.Exists(redactedLogPath))
        {
            File.Copy(redactedLogPath, Path.Combine(bundleRoot, "redacted-install.log"), overwrite: true);
        }

        var assistantRoot = Path.Combine(outputRoot, "assistant");
        if (Directory.Exists(assistantRoot))
        {
            CopyDirectory(assistantRoot, Path.Combine(bundleRoot, "assistant"));
        }

        var bundlePath = Path.Combine(outputRoot, $"{session.SessionId}-support-bundle.zip");
        if (File.Exists(bundlePath))
        {
            File.Delete(bundlePath);
        }

        ZipFile.CreateFromDirectory(bundleRoot, bundlePath, CompressionLevel.Fastest, includeBaseDirectory: false);
        return bundlePath;
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(directory, Path.Combine(targetDirectory, Path.GetFileName(directory)));
        }
    }
}
