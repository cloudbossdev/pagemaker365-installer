using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Steps;

public interface IInstallerStep
{
    string Name { get; }
    string Code { get; }
    Task<InstallerStepResult> RunAsync(InstallerSession session, CancellationToken cancellationToken = default);
}

