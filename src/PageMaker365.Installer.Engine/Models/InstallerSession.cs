using System.Text.Json.Serialization;

namespace PageMaker365.Installer.Engine.Models;

public sealed class InstallerSession
{
    public string SessionId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public CustomerInstallConfig Config { get; set; } = new();
    [JsonIgnore]
    public string ValidatedPackagePayloadSha256 { get; set; } = "";
    public string CurrentPhase { get; set; } = "Not started";
    public InstallStatus Status { get; set; } = InstallStatus.NotStarted;
    public string LogDirectory { get; set; } = "";
    public List<InstallerStepResult> Results { get; set; } = [];

    public static InstallerSession Create(
        CustomerInstallConfig config,
        string workspaceRoot,
        string validatedPackagePayloadSha256 = "")
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var sessionId = $"pm365-install-{timestamp}";
        return new InstallerSession
        {
            SessionId = sessionId,
            CreatedAt = DateTimeOffset.UtcNow,
            Config = config,
            ValidatedPackagePayloadSha256 = validatedPackagePayloadSha256,
            LogDirectory = Path.Combine(workspaceRoot, "logs", sessionId)
        };
    }
}
