using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.App.ViewModels;

public sealed class PreviewResultViewModel
{
    public PreviewResultViewModel(InstallerStepResult result)
    {
        Name = result.StepName;
        Code = result.Code;
        Summary = result.Summary;
        Details = string.IsNullOrWhiteSpace(result.Details) ? "No additional details returned." : result.Details;
        StatusLabel = result.Status.ToString();
        RetrySafeLabel = result.RetrySafe ? "Retry safe" : "Review required";
        StatusBrush = result.Status switch
        {
            InstallStatus.Passed => "#42D8A0",
            InstallStatus.Warning => "#FFB84D",
            InstallStatus.Failed => "#FF5C7A",
            InstallStatus.Running => "#19D8E9",
            _ => "#8290AA"
        };
    }

    public string Name { get; }
    public string Code { get; }
    public string Summary { get; }
    public string Details { get; }
    public string StatusLabel { get; }
    public string RetrySafeLabel { get; }
    public string StatusBrush { get; }
}
