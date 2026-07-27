using System.Reflection;

namespace PageMaker365.Installer.App;

internal static class InstallerBuildInfo
{
    private static readonly Assembly Assembly = typeof(InstallerBuildInfo).Assembly;

    public static string Version =>
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public static string DisplayVersion => $"Version {Version}";
}
