namespace PageMaker365.Installer.Engine.Models;

public sealed class GraphSignInResult
{
    public InstallerStepResult StepResult { get; set; } = new();
    public string AccessToken { get; set; } = "";
    public DateTimeOffset ExpiresOn { get; set; }
    public string TenantId { get; set; } = "";
    public string Account { get; set; } = "";
    public List<string> Scopes { get; set; } = [];

    public bool HasAccessToken => !string.IsNullOrWhiteSpace(AccessToken);
}
