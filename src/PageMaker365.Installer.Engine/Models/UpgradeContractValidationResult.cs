namespace PageMaker365.Installer.Engine.Models;

public sealed class UpgradeContractValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public bool IsUpgrade { get; set; }
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
}
