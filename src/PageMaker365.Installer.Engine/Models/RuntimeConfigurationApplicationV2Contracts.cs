namespace PageMaker365.Installer.Engine.Models;

public sealed class RuntimeConfigurationApplicationV2Plan
{
    public const string ContractVersionValue = "pagemaker365.runtime-configuration-application.v2";

    public string ContractVersion { get; init; } = ContractVersionValue;
    public string PackageHash { get; init; } = "";
    public string ProjectionSha256 { get; init; } = "";
    public RuntimeConfigurationApplicationBindingV2 Binding { get; init; } = new();
    public IReadOnlyList<RuntimeConfigurationApplicationSettingV2> ApiPublicSettings { get; init; } = [];
    public IReadOnlyList<RuntimeConfigurationApplicationSettingV2> PortalPublicSettings { get; init; } = [];
    public IReadOnlyList<RuntimeConfigurationApplicationProtectedReferenceV2> ApiProtectedSettingReferences { get; init; } = [];
    public RuntimeConfigurationApplicationLicenseDescriptorV2 LicenseAcquisition { get; init; } = new();
    public RuntimeConfigurationApplicationCursorDescriptorV2 CursorGeneration { get; init; } = new();
    public RuntimeConfigurationRollbackPlanV2 Rollback { get; init; } = new();
    public string CanonicalJson { get; init; } = "";
    public string PlanSha256 { get; init; } = "";
}

public sealed class RuntimeConfigurationApplicationBindingV2
{
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

public sealed class RuntimeConfigurationApplicationSettingV2
{
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
}

public sealed class RuntimeConfigurationApplicationProtectedReferenceV2
{
    public string Name { get; init; } = "";
    public string Mode { get; init; } = "";
    public string KeyVaultReference { get; init; } = "";
}

public sealed class RuntimeConfigurationApplicationLicenseDescriptorV2
{
    public string ContractVersion { get; init; } = "";
    public string OpaqueReference { get; init; } = "";
    public string VaultResourceId { get; init; } = "";
    public string SecretName { get; init; } = "";
}

public sealed class RuntimeConfigurationApplicationCursorDescriptorV2
{
    public string GenerationAlgorithm { get; init; } = "";
    public int MinimumEntropyBytes { get; init; }
    public string VaultResourceId { get; init; } = "";
    public string SecretName { get; init; } = "";
}

public sealed class RuntimeConfigurationRollbackPlanV2
{
    public string Strategy { get; init; } = "restore-previous-app-setting-state";
    public IReadOnlyList<string> TargetQualifiedSettings { get; init; } = [];
    public bool ContainsValues { get; init; }
}

public interface IRuntimeConfigurationCursorSecretGenerator
{
    byte[] Generate(int entropyBytes);
}

public interface IRuntimeConfigurationProtectedSecretCallback
{
    void Accept(
        RuntimeConfigurationApplicationCursorDescriptorV2 destination,
        ReadOnlyMemory<byte> base64UrlSecretUtf8);
}
