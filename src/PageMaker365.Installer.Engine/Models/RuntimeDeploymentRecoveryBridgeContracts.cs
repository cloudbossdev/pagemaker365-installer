namespace PageMaker365.Installer.Engine.Models;

internal sealed class RuntimeBridgeSyntheticTestCapability
{
    private RuntimeBridgeSyntheticTestCapability() { }

    internal static RuntimeBridgeSyntheticTestCapability CreateForTestSupport() => new();
}

internal interface IRuntimeBridgeSyntheticTestSeam
{
    RuntimeBridgeSyntheticTestCapability Capability { get; }
}

internal sealed record RuntimeBridgeOwnedStageLease(
    string InvocationId,
    string TrustedRoot,
    string StageRoot,
    byte[] OwnershipMarker);

internal sealed record RuntimeBridgeOwnedStageEntry(string RelativePath, bool IsDirectory);

internal interface IRuntimeBridgeOwnedStageStore : IRuntimeBridgeSyntheticTestSeam
{
    RuntimeBridgeOwnedStageLease Create(string trustedRoot, string invocationId, IReadOnlyList<RuntimeBridgeOwnedStageEntry> inventory);
    void AssertOwned(RuntimeBridgeOwnedStageLease lease);
    void AssertComplete(RuntimeBridgeOwnedStageLease lease);
    void CreateDirectoryExclusive(RuntimeBridgeOwnedStageLease lease, string relativePath);
    void WriteFileExclusive(RuntimeBridgeOwnedStageLease lease, string relativePath, ReadOnlySpan<byte> bytes);
    bool Cleanup(RuntimeBridgeOwnedStageLease lease);
}

internal interface IRuntimeBridgeOwnedStageRaceProbe : IRuntimeBridgeSyntheticTestSeam
{
    void Probe(string operation, string path);
}

internal interface IRuntimeBridgeCancellationProbe : IRuntimeBridgeSyntheticTestSeam
{
    void Probe(string label, string phase);
}

internal sealed class RuntimeBridgeSyntheticTransportException(int statusCode, string? safeCode, int responseBodyBytes)
    : Exception("runtime_bridge_synthetic_transport_terminal")
{
    internal int StatusCode { get; } = statusCode;
    internal string? SafeCode { get; } = safeCode;
    internal int ResponseBodyBytes { get; } = responseBodyBytes;
}

internal sealed record RuntimeBridgeInvocation(
    string InvocationId,
    string CanonicalPackageJson,
    string WorkspaceRoot,
    string InstallerVersion,
    bool Enabled);

internal sealed record RuntimeBridgeHttpHeader(string Name, string Value);

internal sealed record RuntimeBridgeArtifactSession(string SessionId, DateTimeOffset ExpiresAt)
{
    internal string Method { get; init; } = "";
    internal string Path { get; init; } = "";
    internal string Query { get; init; } = "";
    internal string Fragment { get; init; } = "";
    internal IReadOnlyList<RuntimeBridgeHttpHeader> RequestHeaders { get; init; } = [];
    internal string RequestContentType { get; init; } = "";
    internal byte[] RequestBodyUtf8 { get; init; } = [];
    internal int StatusCode { get; init; }
    internal IReadOnlyList<RuntimeBridgeHttpHeader> ResponseHeaders { get; init; } = [];
    internal string ResponseContentType { get; init; } = "";
    internal string? Location { get; init; }
    internal byte[] ResponseBodyUtf8 { get; init; } = [];
}

internal sealed record RuntimeBridgeArtifactRequest(
    string VectorId,
    string ArtifactKind,
    string ArtifactReference,
    string PackageHash,
    string SessionId,
    string IfMatch,
    long? RangeOffset,
    int? RangeLength)
{
    internal string Method { get; init; } = "GET";
    internal string Path { get; init; } = "";
    internal string Query { get; init; } = "";
    internal string Fragment { get; init; } = "";
    internal IReadOnlyList<RuntimeBridgeHttpHeader> OrderedHeaders { get; init; } = [];
    internal string? ContentType { get; init; }
    internal byte[] BodyUtf8 { get; init; } = [];
}

internal sealed record RuntimeBridgeArtifactResponse(
    string ArtifactKind,
    string VectorId,
    string ArtifactReference,
    string PackageHash,
    string SessionId,
    int StatusCode,
    bool IsRange,
    long Offset,
    long TotalLength,
    string Sha256,
    string ETag,
    string? AcceptRanges,
    string? ContentRange,
    long ContentLength,
    string BodyFile,
    string CacheControl,
    string Pragma,
    string ContentTypeOptions,
    bool NoRedirect,
    byte[] Body)
{
    internal IReadOnlyList<RuntimeBridgeHttpHeader> OrderedHeaders { get; init; } = [];
    internal string ContentType { get; init; } = "application/zip";
    internal string? Location { get; init; }
}

internal sealed record RuntimeBridgeArtifactReceipt(
    string SessionId,
    string PackageHash,
    string Status,
    int MutationCount)
{
    internal string Method { get; init; } = "";
    internal string Path { get; init; } = "";
    internal string Query { get; init; } = "";
    internal string Fragment { get; init; } = "";
    internal IReadOnlyList<RuntimeBridgeHttpHeader> RequestHeaders { get; init; } = [];
    internal string RequestContentType { get; init; } = "";
    internal byte[] RequestBodyUtf8 { get; init; } = [];
    internal int StatusCode { get; init; }
    internal IReadOnlyList<RuntimeBridgeHttpHeader> ResponseHeaders { get; init; } = [];
    internal string ResponseContentType { get; init; } = "";
    internal string? Location { get; init; }
    internal byte[] ResponseBodyUtf8 { get; init; } = [];
}

internal interface IRuntimeBridgeArtifactTransport : IRuntimeBridgeSyntheticTestSeam
{
    RuntimeBridgeArtifactSession CreateSession(PrivateRuntimeDeliveryPackageV07 package, CancellationToken cancellationToken);
    RuntimeBridgeArtifactResponse Acquire(PrivateRuntimeDeliveryPackageV07 package, RuntimeBridgeArtifactSession session, RuntimeBridgeArtifactRequest request, CancellationToken cancellationToken);
    RuntimeBridgeArtifactReceipt SubmitReceipt(PrivateRuntimeDeliveryPackageV07 package, RuntimeBridgeArtifactSession session, IReadOnlyList<RuntimeBridgeVerifiedArtifact> artifacts, CancellationToken cancellationToken);
}

internal sealed record RuntimeBridgeProtectedLicenseResponse(
    string ContractVersion,
    string PackageHash,
    string TargetApp,
    string Name,
    string Reference,
    string CacheControl,
    string Pragma,
    string ContentTypeOptions,
    string Vary,
    bool NoRedirect,
    byte[] SignedLicenseUtf8)
{
    internal string Method { get; init; } = "";
    internal string Path { get; init; } = "";
    internal string Query { get; init; } = "";
    internal string Fragment { get; init; } = "";
    internal IReadOnlyList<RuntimeBridgeHttpHeader> RequestHeaders { get; init; } = [];
    internal string RequestContentType { get; init; } = "";
    internal byte[] RequestBodyUtf8 { get; init; } = [];
    internal int StatusCode { get; init; }
    internal IReadOnlyList<RuntimeBridgeHttpHeader> ResponseHeaders { get; init; } = [];
    internal string ResponseContentType { get; init; } = "";
    internal string? Location { get; init; }
    internal byte[] ResponseBodyUtf8 { get; init; } = [];
    internal int ProtectedReadCount { get; init; }
    internal int RedemptionCount { get; init; }
}

internal interface IRuntimeBridgeProtectedLicenseTransport : IRuntimeBridgeSyntheticTestSeam
{
    RuntimeBridgeProtectedLicenseResponse AcquireOnce(
        PrivateRuntimeDeliveryPackageV07 package,
        RuntimeBridgeArtifactSession session,
        RuntimeConfigurationProtectedSettingV2 descriptor,
        CancellationToken cancellationToken);
}

internal sealed record RuntimeBridgeLicenseAuthority(
    string Algorithm,
    string KeyId,
    string PublicKeyPem,
    string PublicKeySha256,
    string Canonicalization,
    string SignedPayloadSha256,
    string SignedPayloadFingerprint,
    string FingerprintDomain,
    string Signature,
    string SubscriptionId);

internal interface IRuntimeBridgeCursorGenerator : IRuntimeConfigurationCursorSecretGenerator, IRuntimeBridgeSyntheticTestSeam;

internal sealed record RuntimeBridgeProtectedWriteRequest(
    string Name,
    string Mode,
    string VaultResourceId,
    string SecretName,
    string PackageHash,
    string ApprovalDigest,
    ReadOnlyMemory<byte> ValueUtf8);

internal sealed record RuntimeBridgeProtectedWriteReceipt(
    string ReceiptId,
    string Name,
    string Mode,
    string VaultResourceId,
    string SecretName,
    string SecretVersion,
    string KeyVaultReference,
    string ContentSha256,
    string PackageHash,
    string ApprovalDigest,
    string Outcome,
    int WriteCount);

internal sealed record RuntimeBridgeProtectedContentBinding(string Name, string ContentSha256);

internal interface IRuntimeBridgeProtectedWriteSink : IRuntimeBridgeSyntheticTestSeam
{
    RuntimeBridgeProtectedWriteReceipt Write(RuntimeBridgeProtectedWriteRequest request, CancellationToken cancellationToken);
}

internal sealed record RuntimeBridgeVerifiedArtifact(
    string ArtifactKind,
    string FileName,
    string Sha256,
    long SizeBytes,
    string StagePath,
    string ExtractedTreeSha256,
    int EntryCount);

internal sealed record RuntimeBridgeProvisionalIntent(
    string PackageHash,
    string ProjectionSha256,
    string ManifestSha256,
    string DeploymentInputSha256,
    IReadOnlyList<RuntimeConfigurationApplicationTypedSettingV2> PublicSettings,
    IReadOnlyList<RuntimeConfigurationApplicationProtectedReferenceV2> ExistingReferences,
    IReadOnlyList<RuntimeBridgeProtectedDestination> PendingDestinations,
    IReadOnlyList<RuntimeBridgeVerifiedArtifact> Artifacts,
    string RecoveryPlanSha256,
    string CanonicalJson,
    string IntentSha256);

internal sealed record RuntimeBridgeProtectedDestination(
    string Name,
    string Mode,
    string VaultResourceId,
    string SecretName,
    string DestinationSha256);

internal sealed record RuntimeBridgeWhatIfRequest(
    string Phase,
    string PackageHash,
    string InputSha256,
    string ArtifactIdentitySha256,
    string? PhaseOneApprovalDigest,
    IReadOnlyList<string> ReceiptIdentitySha256s);

internal sealed record RuntimeBridgeWhatIfResult(
    string Phase,
    string Status,
    string RequestSha256,
    string CanonicalJson,
    string PreviewSha256,
    int ResourceWriteCount,
    int DeploymentCount);

internal interface IRuntimeBridgeWhatIf : IRuntimeBridgeSyntheticTestSeam
{
    RuntimeBridgeWhatIfResult Preview(RuntimeBridgeWhatIfRequest request, CancellationToken cancellationToken);
}

internal sealed record RuntimeBridgeApprovalChallenge(
    string Phase,
    string Nonce,
    string PackageHash,
    string InputSha256,
    string PreviewSha256,
    string ArtifactIdentitySha256,
    string RecoveryPlanSha256,
    string? PhaseOneApprovalDigest,
    IReadOnlyList<string> ReceiptIdentitySha256s,
    DateTimeOffset ExpiresAt,
    string ChallengeSha256);

internal sealed record RuntimeBridgeApprovalReceipt(
    string ApprovalId,
    string Phase,
    string ChallengeSha256,
    string Nonce,
    DateTimeOffset ExpiresAt,
    string Outcome,
    int UseCount,
    string ApprovalDigest);

internal interface IRuntimeBridgeApproval : IRuntimeBridgeSyntheticTestSeam
{
    RuntimeBridgeApprovalReceipt Approve(RuntimeBridgeApprovalChallenge challenge, CancellationToken cancellationToken);
}

internal sealed record RuntimeBridgeSimulationRequest(
    string PackageHash,
    string FinalInputSha256,
    string FinalPreviewSha256,
    string PhaseTwoApprovalDigest,
    IReadOnlyList<RuntimeBridgeVerifiedArtifact> Artifacts,
    bool AuthorizesDeployment);

internal sealed record RuntimeBridgeSimulationResult(
    string PackageHash,
    string FinalInputSha256,
    string FinalPreviewSha256,
    string ApprovalBindingSha256,
    string ArtifactIdentitySha256,
    string Status,
    bool AuthorizesDeployment,
    int ResourceCount,
    int WriteCount,
    int DeploymentCount,
    string ResultSha256);

internal interface IRuntimeBridgeSyntheticHandler : IRuntimeBridgeSyntheticTestSeam
{
    RuntimeBridgeSimulationResult Simulate(RuntimeBridgeSimulationRequest request, CancellationToken cancellationToken);
}

internal sealed record RuntimeBridgeRecoveryResult(string ReceiptId, string Status, int RecoveryCount);

internal interface IRuntimeBridgeRecovery : IRuntimeBridgeSyntheticTestSeam
{
    RuntimeBridgeRecoveryResult Recover(RuntimeBridgeProtectedWriteReceipt receipt, CancellationToken cancellationToken);
}

internal sealed record RuntimeBridgeResult(
    string Status,
    string SafeCode,
    bool AuthorizesDeployment,
    int LicenseAcquisitionCount,
    int ProtectedWriteCount,
    int WhatIfCount,
    int ApprovalCount,
    int HandlerCount,
    int RecoveryCount,
    bool StageCleaned,
    string EvidenceJson,
    string EvidenceSha256,
    RuntimeConfigurationFinalizedDeploymentInputV2? FinalInput,
    IReadOnlyList<RuntimeBridgeProtectedWriteReceipt> OwnedReceipts);

internal sealed class RuntimeConfigurationFinalizedDeploymentInputV2
{
    internal const string ContractVersionValue = "pagemaker365.runtime-configuration-finalized-application.v2";

    internal string ContractVersion { get; init; } = ContractVersionValue;
    internal string PackageHash { get; init; } = "";
    internal string ProjectionSha256 { get; init; } = "";
    internal RuntimeConfigurationApplicationBindingV2 Binding { get; init; } = new();
    internal IReadOnlyList<RuntimeConfigurationApplicationTypedSettingV2> ApiPublicSettings { get; init; } = [];
    internal IReadOnlyList<RuntimeConfigurationApplicationTypedSettingV2> PortalPublicSettings { get; init; } = [];
    internal IReadOnlyList<RuntimeConfigurationApplicationProtectedReferenceV2> ApiVersionedProtectedSettingReferences { get; init; } = [];
    internal IReadOnlyList<RuntimeBridgeProtectedContentBinding> ProtectedContentBindings { get; init; } = [];
    internal string CanonicalJson { get; init; } = "";
    internal string InputSha256 { get; init; } = "";
}
