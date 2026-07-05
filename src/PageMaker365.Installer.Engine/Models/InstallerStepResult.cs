namespace PageMaker365.Installer.Engine.Models;

public sealed class InstallerStepResult
{
    public string StepName { get; set; } = "";
    public string Code { get; set; } = "";
    public InstallStatus Status { get; set; }
    public string Summary { get; set; } = "";
    public string Details { get; set; } = "";
    public List<string> Warnings { get; set; } = [];
    public Dictionary<string, string> Data { get; set; } = [];
    public bool RetrySafe { get; set; }
    public bool RequiresApproval { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }

    public static InstallerStepResult Passed(string stepName, string code, string summary, string details = "") =>
        Create(stepName, code, InstallStatus.Passed, summary, details, retrySafe: true);

    public static InstallerStepResult Warning(string stepName, string code, string summary, string details = "") =>
        Create(stepName, code, InstallStatus.Warning, summary, details, retrySafe: true);

    public static InstallerStepResult Failed(string stepName, string code, string summary, string details = "", bool retrySafe = false) =>
        Create(stepName, code, InstallStatus.Failed, summary, details, retrySafe);

    private static InstallerStepResult Create(
        string stepName,
        string code,
        InstallStatus status,
        string summary,
        string details,
        bool retrySafe)
    {
        var now = DateTimeOffset.UtcNow;
        return new InstallerStepResult
        {
            StepName = stepName,
            Code = code,
            Status = status,
            Summary = summary,
            Details = details,
            RetrySafe = retrySafe,
            StartedAt = now,
            CompletedAt = now
        };
    }
}

