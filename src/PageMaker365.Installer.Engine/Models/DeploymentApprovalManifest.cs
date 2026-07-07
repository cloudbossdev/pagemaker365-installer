using System.Text.Json.Serialization;

namespace PageMaker365.Installer.Engine.Models;

public sealed class DeploymentApprovalManifest
{
    [JsonPropertyName("contractVersion")]
    public string ContractVersion { get; set; } = "0.1";

    [JsonPropertyName("manifestType")]
    public string ManifestType { get; set; } = "PageMaker365.DeploymentApproval";

    [JsonPropertyName("approvalId")]
    public string ApprovalId { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("installerVersion")]
    public string InstallerVersion { get; set; } = "";

    [JsonPropertyName("workflowMode")]
    public string WorkflowMode { get; set; } = "";

    [JsonPropertyName("targetSummary")]
    public DeploymentApprovalTargetSummary TargetSummary { get; set; } = new();

    [JsonPropertyName("packageSummary")]
    public DeploymentApprovalPackageSummary PackageSummary { get; set; } = new();

    [JsonPropertyName("previewEvidence")]
    public DeploymentApprovalPreviewEvidenceSummary PreviewEvidence { get; set; } = new();

    [JsonPropertyName("confirmationSummary")]
    public DeploymentApprovalConfirmationSummary ConfirmationSummary { get; set; } = new();

    [JsonPropertyName("acknowledgements")]
    public List<DeploymentApprovalAcknowledgement> Acknowledgements { get; set; } = [];
}

public sealed class DeploymentApprovalTargetSummary
{
    [JsonPropertyName("tenantName")]
    public string TenantName { get; set; } = "";

    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = "";

    [JsonPropertyName("azureTenantId")]
    public string AzureTenantId { get; set; } = "";

    [JsonPropertyName("subscriptionId")]
    public string SubscriptionId { get; set; } = "";

    [JsonPropertyName("resourceGroupName")]
    public string ResourceGroupName { get; set; } = "";

    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    [JsonPropertyName("environment")]
    public string Environment { get; set; } = "";

    [JsonPropertyName("sharePointSiteUrl")]
    public string SharePointSiteUrl { get; set; } = "";

    [JsonPropertyName("defaultDocumentLibrary")]
    public string DefaultDocumentLibrary { get; set; } = "";
}

public sealed class DeploymentApprovalPackageSummary
{
    [JsonPropertyName("packagePath")]
    public string PackagePath { get; set; } = "";

    [JsonPropertyName("deploymentExportId")]
    public string DeploymentExportId { get; set; } = "";

    [JsonPropertyName("packageTrustStatus")]
    public string PackageTrustStatus { get; set; } = "";

    [JsonPropertyName("packageTrustSummary")]
    public string PackageTrustSummary { get; set; } = "";

    [JsonPropertyName("declaredPackageHash")]
    public string DeclaredPackageHash { get; set; } = "";

    [JsonPropertyName("computedPackageHash")]
    public string ComputedPackageHash { get; set; } = "";
}

public sealed class DeploymentApprovalPreviewEvidenceSummary
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("hashAlgorithm")]
    public string HashAlgorithm { get; set; } = "SHA-256";

    [JsonPropertyName("evidenceFileFound")]
    public bool EvidenceFileFound { get; set; }

    [JsonPropertyName("resultCount")]
    public int ResultCount { get; set; }

    [JsonPropertyName("warningCount")]
    public int WarningCount { get; set; }

    [JsonPropertyName("failureCount")]
    public int FailureCount { get; set; }
}

public sealed class DeploymentApprovalConfirmationSummary
{
    [JsonPropertyName("approved")]
    public bool Approved { get; set; }

    [JsonPropertyName("confirmationTarget")]
    public string ConfirmationTarget { get; set; } = "";

    [JsonPropertyName("confirmationMatched")]
    public bool ConfirmationMatched { get; set; }

    [JsonPropertyName("confirmedAt")]
    public DateTimeOffset ConfirmedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("rawConfirmationTextPersisted")]
    public bool RawConfirmationTextPersisted { get; set; }
}

public sealed class DeploymentApprovalAcknowledgement
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("accepted")]
    public bool Accepted { get; set; }
}
