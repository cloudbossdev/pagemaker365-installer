namespace PageMaker365.Installer.Engine.Models;

public sealed class ConfigValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public bool HasTrustWarnings => !PackageTrustStatus.Equals("Verified", StringComparison.OrdinalIgnoreCase);
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public string PackageTrustStatus { get; set; } = "Not checked";
    public string PackageTrustSummary { get; set; } = "Package trust has not been checked.";
    public string DeclaredPackageHash { get; set; } = "";
    public string ComputedPackageHash { get; set; } = "";
    public string DeploymentExportId { get; set; } = "";
    public string SigningKeyId { get; set; } = "";
    public string TrustMode { get; set; } = "";
}
