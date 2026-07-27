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
            await CopyAssistantArtifactsAsync(
                assistantRoot,
                Path.Combine(bundleRoot, "assistant"),
                cancellationToken);
        }

        var bundlePath = Path.Combine(outputRoot, $"{session.SessionId}-support-bundle.zip");
        if (File.Exists(bundlePath))
        {
            File.Delete(bundlePath);
        }

        ZipFile.CreateFromDirectory(bundleRoot, bundlePath, CompressionLevel.Fastest, includeBaseDirectory: false);
        return bundlePath;
    }

    private async Task CopyAssistantArtifactsAsync(
        string sourceDirectory,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var conversationDirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var targetConversation = Path.Combine(targetDirectory, Path.GetFileName(conversationDirectory));
            Directory.CreateDirectory(targetConversation);

            var transcript = Path.Combine(conversationDirectory, "assistant-conversation.redacted.json");
            if (File.Exists(transcript))
            {
                await CopySanitizedTextAsync(
                    transcript,
                    Path.Combine(targetConversation, Path.GetFileName(transcript)),
                    cancellationToken);
            }

            var outbox = Path.Combine(conversationDirectory, "portal-outbox");
            if (Directory.Exists(outbox))
            {
                await CopySanitizedTextTreeAsync(
                    outbox,
                    Path.Combine(targetConversation, "portal-outbox"),
                    cancellationToken);
            }
        }
    }

    private async Task CopySanitizedTextTreeAsync(
        string sourceDirectory,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            if (!AssistantTransferPolicy.IsPortalAttachmentAllowed(file) &&
                !Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await CopySanitizedTextAsync(
                file,
                Path.Combine(targetDirectory, Path.GetFileName(file)),
                cancellationToken);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            await CopySanitizedTextTreeAsync(
                directory,
                Path.Combine(targetDirectory, Path.GetFileName(directory)),
                cancellationToken);
        }
    }

    private async Task CopySanitizedTextAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        await File.WriteAllTextAsync(
            targetPath,
            AssistantTransferPolicy.SanitizeText(content),
            cancellationToken);
    }
}
