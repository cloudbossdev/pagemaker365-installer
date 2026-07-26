namespace PageMaker365.Installer.Engine.Models;

public sealed class GraphDeviceCodePrompt
{
    public string Message { get; set; } = "";
    public string UserCode { get; set; } = "";
    public string VerificationUrl { get; set; } = "";
    public DateTimeOffset ExpiresOn { get; set; }
}
