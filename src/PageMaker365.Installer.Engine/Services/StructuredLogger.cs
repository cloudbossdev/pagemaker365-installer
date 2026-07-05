using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class StructuredLogger
{
    private readonly RedactionService _redactionService;

    public StructuredLogger(RedactionService redactionService)
    {
        _redactionService = redactionService;
    }

    public async Task WriteAsync(InstallerSession session, string eventName, object payload, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(session.LogDirectory);
        var logPath = Path.Combine(session.LogDirectory, "install.log");
        var redactedLogPath = Path.Combine(session.LogDirectory, "redacted-install.log");

        var entry = JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow,
            sessionId = session.SessionId,
            eventName,
            payload
        });

        await File.AppendAllTextAsync(logPath, entry + Environment.NewLine, cancellationToken);
        await File.AppendAllTextAsync(redactedLogPath, _redactionService.Redact(entry) + Environment.NewLine, cancellationToken);
    }
}

