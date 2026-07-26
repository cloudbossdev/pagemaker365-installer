using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.App.ViewModels;

public sealed class CheckResultViewModel
{
    public CheckResultViewModel(InstallerStepResult result)
    {
        Name = result.StepName;
        Code = result.Code;
        Summary = result.Summary;
        Details = result.Details;
        RetrySafe = result.RetrySafe;
        StatusLabel = result.Status.ToString();
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
    public bool RetrySafe { get; }
    public string StatusLabel { get; }
    public string StatusBrush { get; }
}
