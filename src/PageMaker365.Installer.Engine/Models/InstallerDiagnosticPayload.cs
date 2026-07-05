namespace PageMaker365.Installer.Engine.Models;

public sealed class InstallerDiagnosticPayload
{
    public string SessionId { get; set; } = "";
    public string Phase { get; set; } = "";
    public string FailedStep { get; set; } = "";
    public string ErrorCode { get; set; } = "";
    public string RedactedLog { get; set; } = "";
    public Dictionary<string, string> Facts { get; set; } = [];
}

