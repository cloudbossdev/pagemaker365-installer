namespace PageMaker365.Installer.Engine.PowerShell;

public sealed class PowerShellExecutionResult
{
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = "";
    public string StandardError { get; set; } = "";
    public bool TimedOut { get; set; }
    public bool Canceled { get; set; }
    public string FailureReason { get; set; } = "";
    public bool Succeeded => ExitCode == 0 && !TimedOut && !Canceled;
}
