using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class FinalEvidenceService
{
    public async Task<FinalEvidenceResult> CreateAsync(
        CustomerInstallConfig config,
        FinalEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var evidenceId = timestamp.ToString("yyyyMMdd-HHmmss");
        var finalRoot = Path.Combine(request.OutputRoot, "final", evidenceId);
        Directory.CreateDirectory(finalRoot);

        var copiedEvidence = CopyEvidenceFiles(finalRoot, request);
        var reportPath = Path.Combine(finalRoot, "final-install-report.md");
        var manifestPath = Path.Combine(finalRoot, "final-evidence-manifest.json");

        await File.WriteAllTextAsync(
            reportPath,
            CreateReport(config, request, copiedEvidence, timestamp),
            Encoding.UTF8,
            cancellationToken);

        var manifest = new
        {
            contractVersion = "0.1",
            evidenceId,
            generatedAt = timestamp,
            installerVersion = request.InstallerVersion,
            packagePath = request.PackagePath,
            finalStatus = request.FinalStatus,
            customer = new
            {
                config.Customer.CustomerId,
                config.Customer.TenantName,
                config.Customer.PrimaryContact
            },
            azure = new
            {
                config.Azure.TenantId,
                config.Azure.SubscriptionId,
                config.Azure.ResourceGroupName,
                config.Azure.Location,
                config.Azure.Environment
            },
            sharePoint = new
            {
                config.SharePoint.SiteUrl,
                config.SharePoint.DefaultDocumentLibrary
            },
            app = new
            {
                config.App.AppName,
                config.App.CustomDomain,
                config.App.SupportEmail
            },
            controlPlane = new
            {
                config.ControlPlane.DeploymentExportId,
                config.ControlPlane.ExportedAt,
                config.ControlPlane.Issuer,
                config.ControlPlane.IssuerEnvironment,
                config.ControlPlane.PackageHash,
                config.ControlPlane.PackageHashAlgorithm,
                config.ControlPlane.Canonicalization,
                config.ControlPlane.PublicKeyId,
                config.ControlPlane.SignatureAlgorithm,
                config.ControlPlane.TrustMode,
                config.ControlPlane.CorrelationId
            },
            phases = new
            {
                preview = new
                {
                    status = request.PreviewStatus,
                    sourcePath = request.PreviewEvidencePath,
                    copiedPath = copiedEvidence.PreviewPath
                },
                install = new
                {
                    status = request.DeploymentStatus,
                    sourcePath = request.DeploymentEvidencePath,
                    copiedPath = copiedEvidence.DeploymentPath
                },
                validation = new
                {
                    status = request.ValidationStatus,
                    sourcePath = request.ValidationEvidencePath,
                    copiedPath = copiedEvidence.ValidationPath
                }
            },
            copiedEvidence.MissingEvidence,
            reportPath
        };

        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        var zipPath = Path.Combine(request.OutputRoot, "final", $"pagemaker365-final-evidence-{evidenceId}.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(finalRoot, zipPath, CompressionLevel.Fastest, includeBaseDirectory: true);

        return new FinalEvidenceResult
        {
            EvidenceDirectory = finalRoot,
            ReportPath = reportPath,
            ManifestPath = manifestPath,
            BundlePath = zipPath
        };
    }

    private static CopiedEvidence CopyEvidenceFiles(string finalRoot, FinalEvidenceRequest request)
    {
        var missing = new List<string>();
        var previewPath = CopyIfExists(request.PreviewEvidencePath, finalRoot, "deployment-preview.json", missing);
        var deploymentPath = CopyIfExists(request.DeploymentEvidencePath, finalRoot, "deployment-install.json", missing);
        var validationPath = CopyIfExists(request.ValidationEvidencePath, finalRoot, "deployment-validation.json", missing);

        return new CopiedEvidence(previewPath, deploymentPath, validationPath, missing);
    }

    private static string CopyIfExists(string sourcePath, string targetDirectory, string targetFileName, List<string> missing)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            string.Equals(sourcePath, "Not saved", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(sourcePath))
        {
            missing.Add(targetFileName);
            return "Not copied";
        }

        var targetPath = Path.Combine(targetDirectory, targetFileName);
        File.Copy(sourcePath, targetPath, overwrite: true);
        return targetPath;
    }

    private static string CreateReport(
        CustomerInstallConfig config,
        FinalEvidenceRequest request,
        CopiedEvidence copiedEvidence,
        DateTimeOffset timestamp)
    {
        var report = new StringBuilder();
        report.AppendLine("# PageMaker365 Final Install Evidence");
        report.AppendLine();
        report.AppendLine($"Generated: {timestamp:O}");
        report.AppendLine($"Installer version: {request.InstallerVersion}");
        report.AppendLine($"Final status: {request.FinalStatus}");
        report.AppendLine();
        report.AppendLine("## Customer");
        report.AppendLine();
        report.AppendLine($"- Tenant: {config.Customer.TenantName}");
        report.AppendLine($"- Customer ID: {config.Customer.CustomerId}");
        report.AppendLine($"- Primary contact: {config.Customer.PrimaryContact}");
        report.AppendLine();
        report.AppendLine("## Azure Target");
        report.AppendLine();
        report.AppendLine($"- Tenant ID: {config.Azure.TenantId}");
        report.AppendLine($"- Subscription: {config.Azure.SubscriptionId}");
        report.AppendLine($"- Resource group: {config.Azure.ResourceGroupName}");
        report.AppendLine($"- Location: {config.Azure.Location}");
        report.AppendLine($"- Environment: {config.Azure.Environment}");
        report.AppendLine();
        report.AppendLine("## SharePoint Target");
        report.AppendLine();
        report.AppendLine($"- Site URL: {config.SharePoint.SiteUrl}");
        report.AppendLine($"- Default library: {config.SharePoint.DefaultDocumentLibrary}");
        report.AppendLine();
        report.AppendLine("## Application");
        report.AppendLine();
        report.AppendLine($"- App name: {config.App.AppName}");
        report.AppendLine($"- Custom domain: {config.App.CustomDomain}");
        report.AppendLine($"- Support email: {config.App.SupportEmail}");
        report.AppendLine();
        report.AppendLine("## Package Export");
        report.AppendLine();
        report.AppendLine($"- Deployment export ID: {config.ControlPlane.DeploymentExportId}");
        report.AppendLine($"- Exported at: {config.ControlPlane.ExportedAt}");
        report.AppendLine($"- Issuer: {config.ControlPlane.Issuer}");
        report.AppendLine($"- Issuer environment: {config.ControlPlane.IssuerEnvironment}");
        report.AppendLine($"- Package hash: {config.ControlPlane.PackageHash}");
        report.AppendLine($"- Hash algorithm: {config.ControlPlane.PackageHashAlgorithm}");
        report.AppendLine($"- Canonicalization: {config.ControlPlane.Canonicalization}");
        report.AppendLine($"- Signing key ID: {config.ControlPlane.PublicKeyId}");
        report.AppendLine($"- Signature algorithm: {config.ControlPlane.SignatureAlgorithm}");
        report.AppendLine($"- Trust mode: {config.ControlPlane.TrustMode}");
        report.AppendLine();
        report.AppendLine("## Workflow Evidence");
        report.AppendLine();
        report.AppendLine($"- Preview: {request.PreviewStatus} ({copiedEvidence.PreviewPath})");
        report.AppendLine($"- Install: {request.DeploymentStatus} ({copiedEvidence.DeploymentPath})");
        report.AppendLine($"- Validation: {request.ValidationStatus} ({copiedEvidence.ValidationPath})");
        report.AppendLine();
        report.AppendLine("## Source Paths");
        report.AppendLine();
        report.AppendLine($"- Package: {request.PackagePath}");
        report.AppendLine($"- Preview evidence: {request.PreviewEvidencePath}");
        report.AppendLine($"- Install evidence: {request.DeploymentEvidencePath}");
        report.AppendLine($"- Validation evidence: {request.ValidationEvidencePath}");
        report.AppendLine();
        report.AppendLine("## Recommended Handoff");
        report.AppendLine();
        report.AppendLine("- Retain this final evidence package in PageMaker365 customer records.");
        report.AppendLine("- Confirm the customer has the production URL, support contact, and onboarding next steps.");
        report.AppendLine("- Review any warning status before closing the installation project.");

        if (copiedEvidence.MissingEvidence.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## Missing Evidence");
            report.AppendLine();
            foreach (var item in copiedEvidence.MissingEvidence)
            {
                report.AppendLine($"- {item}");
            }
        }

        return report.ToString();
    }

    private sealed record CopiedEvidence(
        string PreviewPath,
        string DeploymentPath,
        string ValidationPath,
        IReadOnlyList<string> MissingEvidence);
}

public sealed class FinalEvidenceRequest
{
    public string OutputRoot { get; set; } = "";
    public string InstallerVersion { get; set; } = "";
    public string PackagePath { get; set; } = "";
    public string PreviewStatus { get; set; } = "";
    public string PreviewEvidencePath { get; set; } = "";
    public string DeploymentStatus { get; set; } = "";
    public string DeploymentEvidencePath { get; set; } = "";
    public string ValidationStatus { get; set; } = "";
    public string ValidationEvidencePath { get; set; } = "";
    public string FinalStatus { get; set; } = "";
}

public sealed class FinalEvidenceResult
{
    public string EvidenceDirectory { get; set; } = "";
    public string ReportPath { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public string BundlePath { get; set; } = "";
}
