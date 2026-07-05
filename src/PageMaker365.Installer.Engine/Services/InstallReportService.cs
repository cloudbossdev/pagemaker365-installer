using System.Text;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class InstallReportService
{
    public async Task<string> CreateMarkdownAsync(
        InstallerSession session,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var report = new StringBuilder();
        report.AppendLine("# PageMaker365 Install Report");
        report.AppendLine();
        report.AppendLine($"Generated: {DateTimeOffset.UtcNow:O}");
        report.AppendLine($"Session: {session.SessionId}");
        report.AppendLine($"Status: {session.Status}");
        report.AppendLine();
        report.AppendLine("## Customer");
        report.AppendLine();
        report.AppendLine($"- Tenant: {session.Config.Customer.TenantName}");
        report.AppendLine($"- Primary contact: {session.Config.Customer.PrimaryContact}");
        report.AppendLine();
        report.AppendLine("## Azure");
        report.AppendLine();
        report.AppendLine($"- Subscription: {session.Config.Azure.SubscriptionId}");
        report.AppendLine($"- Resource group: {session.Config.Azure.ResourceGroupName}");
        report.AppendLine($"- Location: {session.Config.Azure.Location}");
        report.AppendLine($"- Environment: {session.Config.Azure.Environment}");
        report.AppendLine();
        report.AppendLine("## SharePoint");
        report.AppendLine();
        report.AppendLine($"- Site URL: {session.Config.SharePoint.SiteUrl}");
        report.AppendLine($"- Default library: {session.Config.SharePoint.DefaultDocumentLibrary}");
        report.AppendLine();
        report.AppendLine("## Application");
        report.AppendLine();
        report.AppendLine($"- App name: {session.Config.App.AppName}");
        report.AppendLine($"- Custom domain: {session.Config.App.CustomDomain}");
        report.AppendLine($"- Support email: {session.Config.App.SupportEmail}");
        report.AppendLine();
        report.AppendLine("## Step Results");
        report.AppendLine();

        if (session.Results.Count == 0)
        {
            report.AppendLine("No installer steps have completed yet.");
        }
        else
        {
            foreach (var result in session.Results)
            {
                report.AppendLine($"- {result.Status}: {result.StepName} ({result.Code}) - {result.Summary}");
            }
        }

        await File.WriteAllTextAsync(outputPath, report.ToString(), cancellationToken);
        return outputPath;
    }
}

