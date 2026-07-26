namespace PageMaker365.Installer.Engine.Models;

public sealed class DeploymentExecutionBindingResult
{
    public bool IsValid => Errors.Count == 0;
    public string PackageHash { get; set; } = "";
    public string PreviewEvidenceHash { get; set; } = "";
    public string PreviewArtifactHash { get; set; } = "";
    public List<string> Errors { get; } = [];
}

public sealed class DeploymentExecutionBindingRequest
{
    public string PackagePath { get; set; } = "";
    public string ExpectedPackageHash { get; set; } = "";
    public string PreviewEvidencePath { get; set; } = "";
    public string ExpectedPreviewEvidenceHash { get; set; } = "";
    public string PreviewArtifactPath { get; set; } = "";
    public string ExpectedPreviewArtifactHash { get; set; } = "";
}
