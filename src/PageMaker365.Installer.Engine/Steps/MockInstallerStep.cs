using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Steps;

public sealed class MockInstallerStep : IInstallerStep
{
    private readonly InstallerStepResult _result;
    private readonly int _delayMs;

    public MockInstallerStep(string name, string code, InstallerStepResult result, int delayMs = 350)
    {
        Name = name;
        Code = code;
        _result = result;
        _delayMs = delayMs;
    }

    public string Name { get; }
    public string Code { get; }

    public async Task<InstallerStepResult> RunAsync(InstallerSession session, CancellationToken cancellationToken = default)
    {
        await Task.Delay(_delayMs, cancellationToken);
        _result.StartedAt = DateTimeOffset.UtcNow.AddMilliseconds(-_delayMs);
        _result.CompletedAt = DateTimeOffset.UtcNow;
        return _result;
    }
}

