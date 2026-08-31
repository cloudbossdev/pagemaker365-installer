using System.Text.Json;

namespace PageMaker365.Installer.Engine.Models;

/// <summary>
/// Closed, signed package 0.7 authority. It deliberately exposes only parsed
/// identities and descriptors; it has no acquisition, configuration, or
/// deployment behavior.
/// </summary>
public sealed class PrivateRuntimeDeliveryPackageV07
{
    public const string ContractVersionValue = "0.7";
    public const string CapabilityValue = "pagemaker365.customer-install.0.7.protected-acquisition.v1";
    public const string ProjectionVersionValue = "pagemaker365.runtime-configuration-projection.v2";
    public const string ManifestVersionValue = "3.0";

    public string ContractVersion { get; init; } = ContractVersionValue;
    public string PackageHash { get; init; } = "";
    public string SigningKeyId { get; init; } = "";
    public string CustomerId { get; init; } = "";
    public string InstallationId { get; init; } = "";
    public string EnvironmentId { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string AzureSubscriptionId { get; init; } = "";
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
    public RuntimeConfigurationProjectionV2 RuntimeConfiguration { get; init; } = new();
    public string CanonicalPackageJson { get; init; } = "";
    public byte[] CanonicalSigningPayloadUtf8 { get; init; } = [];

    public PrivateRuntimeArtifact Artifact(string artifactKind) => artifactKind switch
    {
        "api" => Api,
        "portal" => Portal,
        _ => throw new ArgumentOutOfRangeException(nameof(artifactKind), "Artifact kind must be api or portal.")
    };
}

public sealed class RuntimeConfigurationProjectionV2
{
    public string SchemaVersion { get; init; } = PrivateRuntimeDeliveryPackageV07.ProjectionVersionValue;
    public RuntimeConfigurationCatalogBindingV2 Catalog { get; init; } = new();
    public RuntimeConfigurationPackageBindingV2 Binding { get; init; } = new();
    public bool ConnectorSynchronization { get; init; }
    public bool WebPartSynchronization { get; init; }
    public IReadOnlyList<RuntimeConfigurationPublicSettingV2> PublicSettings { get; init; } = [];
    public IReadOnlyList<RuntimeConfigurationProtectedSettingV2> ProtectedSettings { get; init; } = [];
    public string ProjectionSha256 { get; init; } = "";
}

public sealed class RuntimeConfigurationCatalogBindingV2
{
    public string SchemaVersion { get; init; } = "";
    public string SourceRepository { get; init; } = "";
    public string SourceCommit { get; init; } = "";
    public string CatalogSha256 { get; init; } = "";
    public string CatalogSchemaSha256 { get; init; } = "";
    public string TargetQualifiedOrderSha256 { get; init; } = "";
    public int SettingCount { get; init; }
}

public sealed class RuntimeConfigurationPackageBindingV2
{
    public string PackageContractVersion { get; init; } = "";
    public string CustomerId { get; init; } = "";
    public string InstallationId { get; init; } = "";
    public string EnvironmentId { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string AzureSubscriptionId { get; init; } = "";
    public string DeploymentExportId { get; init; } = "";
    public string RuntimeReleaseId { get; init; } = "";
    public string RuntimeVersion { get; init; } = "";
    public string ManifestSha256 { get; init; } = "";
}

public sealed class RuntimeConfigurationPublicSettingV2
{
    public string TargetApp { get; init; } = "";
    public string Name { get; init; } = "";
    public string ValueType { get; init; } = "";
    public JsonElement Value { get; init; }
}

/// <summary>
/// A closed protected-setting descriptor. There is intentionally no generic
/// value or raw-value member.
/// </summary>
public sealed class RuntimeConfigurationProtectedSettingV2
{
    public string TargetApp { get; init; } = "";
    public string Name { get; init; } = "";
    public string Mode { get; init; } = "";
    public RuntimeConfigurationProtectedReferenceV2 Reference { get; init; } = new();
}

public sealed class RuntimeConfigurationProtectedReferenceV2
{
    public string VaultResourceId { get; init; } = "";
    public string SecretName { get; init; } = "";
    public string? SecretVersion { get; init; }
    public string? ContractVersion { get; init; }
    public string? OpaqueReference { get; init; }
    public string? GenerationAlgorithm { get; init; }
    public int? MinimumEntropyBytes { get; init; }
}
