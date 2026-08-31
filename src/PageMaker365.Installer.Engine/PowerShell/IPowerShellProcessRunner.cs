namespace PageMaker365.Installer.Engine.PowerShell;

/// <summary>
/// Process boundary used by <see cref="Services.InstallerEngine"/>.
/// The production implementation launches PowerShell; fixture harnesses can
/// supply a deterministic local implementation without an Azure connection.
/// </summary>
public interface IPowerShellProcessRunner
{
    Task<PowerShellExecutionResult> RunAsync(
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        IProgress<string>? outputProgress = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        Func<Stream, CancellationToken, Task>? standardInputWriter = null);

    Task<PowerShellExecutionResult> RunInteractiveFileResultAsync(
        string arguments,
        string workingDirectory,
        string resultPath,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null);
}
