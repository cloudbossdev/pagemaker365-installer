using System.Security.Cryptography;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class DeploymentApprovalManifestService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<DeploymentApprovalManifestResult> CreateAsync(
        CustomerInstallConfig config,
        DeploymentApprovalManifestRequest request,
        CancellationToken cancellationToken = default)
    {
        var createdAt = DateTimeOffset.UtcNow;
        var approvalId = CreateApprovalId(createdAt);
        var outputDirectory = Path.Combine(request.OutputRoot, "approval");
        Directory.CreateDirectory(outputDirectory);

        var previewHash = HashFileIfPresent(request.PreviewEvidencePath);
        var previewEvidenceFound = !string.IsNullOrWhiteSpace(previewHash);
        var summary = CreateSummary(approvalId, config, request);
        var manifest = new DeploymentApprovalManifest
        {
            ApprovalId = approvalId,
            CreatedAt = createdAt,
            InstallerVersion = request.InstallerVersion,
            WorkflowMode = request.WorkflowMode,
            TargetSummary = new DeploymentApprovalTargetSummary
            {
                TenantName = config.Customer.TenantName,
                CustomerId = config.Customer.CustomerId,
                AzureTenantId = config.Azure.TenantId,
                SubscriptionId = config.Azure.SubscriptionId,
                ResourceGroupName = config.Azure.ResourceGroupName,
                Location = config.Azure.Location,
                Environment = config.Azure.Environment,
                SharePointSiteUrl = config.SharePoint.SiteUrl,
                DefaultDocumentLibrary = config.SharePoint.DefaultDocumentLibrary
            },
            PackageSummary = new DeploymentApprovalPackageSummary
            {
                PackagePath = request.PackagePath,
                DeploymentExportId = request.PackageExportId,
                PackageTrustStatus = request.PackageTrustStatus,
                PackageTrustSummary = request.PackageTrustSummary,
                DeclaredPackageHash = request.PackageDeclaredHash,
                ComputedPackageHash = request.PackageComputedHash
            },
            PreviewEvidence = new DeploymentApprovalPreviewEvidenceSummary
            {
                Status = request.PreviewStatus,
                Summary = request.PreviewSummary,
                Path = request.PreviewEvidencePath,
                Hash = previewHash,
                EvidenceFileFound = previewEvidenceFound,
                ResultCount = request.PreviewResultCount,
                WarningCount = request.PreviewWarningCount,
                FailureCount = request.PreviewFailureCount
            },
            ConfirmationSummary = new DeploymentApprovalConfirmationSummary
            {
                Approved = request.ApprovalConfirmed && request.ConfirmationMatched,
                ConfirmationTarget = request.ConfirmationTarget,
                ConfirmationMatched = request.ConfirmationMatched,
                ConfirmedAt = createdAt,
                RawConfirmationTextPersisted = false
            },
            Acknowledgements = request.Acknowledgements.Count > 0
                ? request.Acknowledgements.ToList()
                : CreateDefaultAcknowledgements()
        };

        var path = Path.Combine(outputDirectory, $"deployment-approval-{approvalId}.json");
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);

        return new DeploymentApprovalManifestResult
        {
            ApprovalId = approvalId,
            ManifestPath = path,
            Summary = summary,
            Manifest = manifest
        };
    }

    public string HashPreviewEvidence(string previewEvidencePath)
    {
        return HashFileIfPresent(previewEvidencePath);
    }

    private static string HashFileIfPresent(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            string.Equals(path, "Not saved", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
        {
            return "";
        }

        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CreateApprovalId(DateTimeOffset createdAt)
    {
        return $"pm365-approval-{createdAt:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
    }

    private static string CreateSummary(
        string approvalId,
        CustomerInstallConfig config,
        DeploymentApprovalManifestRequest request)
    {
        return $"{approvalId} approved {request.WorkflowMode} deployment to {config.Azure.ResourceGroupName} after {request.PreviewStatus} preview.";
    }

    private static List<DeploymentApprovalAcknowledgement> CreateDefaultAcknowledgements()
    {
        return
        [
            new()
            {
                Code = "PreviewReviewed",
                Summary = "Deployment preview evidence reviewed.",
                Accepted = true
            },
            new()
            {
                Code = "TargetConfirmed",
                Summary = "Deployment target was confirmed without storing raw typed text.",
                Accepted = true
            },
            new()
            {
                Code = "DeploymentApproved",
                Summary = "Installer deployment execution was approved.",
                Accepted = true
            }
        ];
    }
}

public sealed class DeploymentApprovalManifestRequest
{
    public string OutputRoot { get; set; } = "";
    public string InstallerVersion { get; set; } = "";
    public string WorkflowMode { get; set; } = "";
    public string PackagePath { get; set; } = "";
    public string PackageExportId { get; set; } = "";
    public string PackageTrustStatus { get; set; } = "";
    public string PackageTrustSummary { get; set; } = "";
    public string PackageDeclaredHash { get; set; } = "";
    public string PackageComputedHash { get; set; } = "";
    public string PreviewStatus { get; set; } = "";
    public string PreviewSummary { get; set; } = "";
    public string PreviewEvidencePath { get; set; } = "";
    public int PreviewResultCount { get; set; }
    public int PreviewWarningCount { get; set; }
    public int PreviewFailureCount { get; set; }
    public bool ApprovalConfirmed { get; set; }
    public string ConfirmationTarget { get; set; } = "";
    public bool ConfirmationMatched { get; set; }
    public IReadOnlyList<DeploymentApprovalAcknowledgement> Acknowledgements { get; set; } = [];
}

public sealed class DeploymentApprovalManifestResult
{
    public string ApprovalId { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public string Summary { get; set; } = "";
    public DeploymentApprovalManifest Manifest { get; set; } = new();
}
