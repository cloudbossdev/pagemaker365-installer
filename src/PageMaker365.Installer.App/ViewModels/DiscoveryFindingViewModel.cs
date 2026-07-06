using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.App.ViewModels;

public sealed class DiscoveryFindingViewModel
{
    public DiscoveryFindingViewModel(DiscoveryFinding finding)
    {
        Severity = NormalizeSeverity(finding.Severity);
        Code = finding.Code;
        Summary = finding.Summary;
        Details = finding.Details;
        StatusBrush = Severity switch
        {
            "Blocked" => "#FF5C7A",
            "Warning" => "#FFB84D",
            "Skipped" => "#8290AA",
            _ => "#42D8A0"
        };
    }

    public string Severity { get; }
    public string Code { get; }
    public string Summary { get; }
    public string Details { get; }
    public string StatusBrush { get; }

    private static string NormalizeSeverity(string severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
        {
            return "Info";
        }

        return severity.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
            severity.Equals("Blocked", StringComparison.OrdinalIgnoreCase)
                ? "Blocked"
                : severity;
    }
}
