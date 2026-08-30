using System.Text.Json.Serialization;

namespace PageMaker365.Installer.Engine.Models;

/// <summary>
/// Immutable, signed location-free customer-install package material shared by
/// the closed 0.5 and 0.6 validators. This model is deliberately separate from
/// the legacy 0.4 customer configuration model: it does not carry an artifact
/// URL, storage locator, or delegated storage credential.
/// </summary>
public sealed class PrivateRuntimeDeliveryPackage
{
    public const string ContractVersionValue = "0.5";
    public const string CapabilityValue = "pagemaker365.customer-install.0.5.protected-acquisition.v1";
    public const string AcquisitionContractVersionValue = "pagemaker365.protected-acquisition.v1";
    public const string SessionPathValue = "/api/onboarding/installer/runtime-delivery-sessions";
    public const string ArtifactPathValue = "/api/onboarding/installer/runtime-artifacts/{artifactKind}";
    public const string ReceiptPathValue = "/api/onboarding/installer/runtime-delivery-receipts";

    public string PackageHash { get; init; } = "";
    public string ContractVersion { get; init; } = ContractVersionValue;
    public string ManifestContractVersion { get; init; } = "2.0";
    public string Product { get; init; } = "PageMaker365";
    public string SigningKeyId { get; init; } = "";
    public string CustomerId { get; init; } = "";
    public string InstallationId { get; init; } = "";
    public string EnvironmentId { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string DeploymentExportId { get; init; } = "";
    public string OnboardingSessionId { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
    public string ManifestSha256 { get; init; } = "";
    public string ReleaseId { get; init; } = "";
    public string RuntimeVersion { get; init; } = "";
    public string SourceCommit { get; init; } = "";
    public PrivateRuntimeArtifact Api { get; init; } = new();
    public PrivateRuntimeArtifact Portal { get; init; } = new();
    public string ApiDeliveryReference { get; init; } = "";
    public string PortalDeliveryReference { get; init; } = "";
    /// <summary>
    /// The producer's canonical, signed package document. It is retained so
    /// the installer can send the exact signed authority to the PageMaker365
    /// API when creating a delivery session; it is never written to a URL,
    /// an artifact request, or a receipt outbox.
    /// </summary>
    public string CanonicalPackageJson { get; init; } = "";
    public byte[] CanonicalSigningPayloadUtf8 { get; init; } = [];

    public PrivateRuntimeArtifact Artifact(string artifactKind) => artifactKind switch
    {
        "api" => Api,
        "portal" => Portal,
        _ => throw new ArgumentOutOfRangeException(nameof(artifactKind), "Artifact kind must be api or portal.")
    };

    public string DeliveryReference(string artifactKind) => artifactKind switch
    {
        "api" => ApiDeliveryReference,
        "portal" => PortalDeliveryReference,
        _ => throw new ArgumentOutOfRangeException(nameof(artifactKind), "Artifact kind must be api or portal.")
    };
}

public sealed class PrivateRuntimeArtifact
{
    public string ArtifactKind { get; init; } = "";
    public string FileName { get; init; } = "";
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = "";
    public string StartupCommand { get; init; } = "";
}

public sealed class PrivateRuntimeDeliverySession
{
    public string DeliverySessionId { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class PrivateRuntimeDeliveryArtifactResult
{
    public string ArtifactKind { get; init; } = "";
    public string FileName { get; init; } = "";
    public string VerifiedPath { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long SizeBytes { get; init; }
    public long BytesReceived { get; init; }
    public int RangeRequestCount { get; init; }
    public string VerificationStatus { get; init; } = "not_verified";
}

public sealed class PrivateRuntimeDeliveryResult
{
    public string Outcome { get; init; } = "failed";
    public string ReceiptStatus { get; init; } = "not_submitted";
    public string SafeErrorCode { get; init; } = "";
    public string SafeErrorMessage { get; init; } = "";
    public string DeliverySessionId { get; init; } = "";
    public string ReceiptOutboxPath { get; init; } = "";
    public IReadOnlyList<PrivateRuntimeDeliveryArtifactResult> Artifacts { get; init; } = [];

    public bool IsVerified => Outcome.Equals("passed", StringComparison.Ordinal);
}

public sealed class PrivateRuntimeDeliveryOptions
{
    public string ApiBaseUrl { get; init; } = "https://api.pagemaker365.com";
    public string ApiKeyEnvironmentVariable { get; init; } = "PM365_ONBOARDING_API_KEY";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>
    /// Package 0.6 / manifest 3.0 acquisition remains disabled unless an
    /// explicitly authorized caller enables the negotiated consumer. Package
    /// 0.5 behavior is unaffected by this gate.
    /// </summary>
    public bool EnablePackageV06 { get; init; }
}

public static class PrivateRuntimeDeliveryPackageV06
{
    public const string ContractVersionValue = "0.6";
    public const string CapabilityValue = "pagemaker365.customer-install.0.6.protected-acquisition.v1";
    public const string ManifestContractVersionValue = "3.0";
}

public sealed class PrivateRuntimeDeliveryReceipt
{
    public const string ContractVersionValue = "pagemaker365.runtime-delivery-receipt.v1";

    [JsonPropertyName("contractVersion")]
    public string ContractVersion { get; init; } = ContractVersionValue;
    [JsonPropertyName("deliverySessionId")]
    public string DeliverySessionId { get; init; } = "";
    [JsonPropertyName("packageHash")]
    public string PackageHash { get; init; } = "";
    [JsonPropertyName("releaseId")]
    public string ReleaseId { get; init; } = "";
    [JsonPropertyName("manifestSha256")]
    public string ManifestSha256 { get; init; } = "";
    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = "";
    [JsonPropertyName("idempotencyKey")]
    public string IdempotencyKey { get; init; } = "";
    [JsonPropertyName("outcome")]
    public string Outcome { get; init; } = "";
    [JsonPropertyName("occurredAt")]
    public DateTimeOffset OccurredAt { get; init; }
    [JsonPropertyName("installerVersion")]
    public string InstallerVersion { get; init; } = "";
    [JsonPropertyName("artifacts")]
    public PrivateRuntimeDeliveryReceiptArtifacts Artifacts { get; init; } = new();
    [JsonPropertyName("safeResult")]
    public PrivateRuntimeDeliverySafeResult SafeResult { get; init; } = new();
}

public sealed class PrivateRuntimeDeliveryReceiptArtifacts
{
    [JsonPropertyName("api")]
    public PrivateRuntimeDeliveryReceiptArtifact Api { get; init; } = new();
    [JsonPropertyName("portal")]
    public PrivateRuntimeDeliveryReceiptArtifact Portal { get; init; } = new();
}

public sealed class PrivateRuntimeDeliveryReceiptArtifact
{
    [JsonPropertyName("artifactKind")]
    public string ArtifactKind { get; init; } = "";
    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = "";
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }
    [JsonPropertyName("verificationOutcome")]
    public string VerificationOutcome { get; init; } = "not_attempted";
    [JsonPropertyName("fullStreamCount")]
    public int FullStreamCount { get; init; }
    [JsonPropertyName("rangeRetryCount")]
    public int RangeRetryCount { get; init; }
    [JsonPropertyName("bytesReceived")]
    public long BytesReceived { get; init; }
}

public sealed class PrivateRuntimeDeliverySafeResult
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = "";
    [JsonPropertyName("state")]
    public string State { get; init; } = "";
}

public sealed class PrivateRuntimeDeliverySafeError
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = "";
    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}
