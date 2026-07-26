using System.Text.RegularExpressions;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed partial class UpgradeContractService
{
    public const string CurrentInstallerVersion = "0.1.0";
    public const string InstallOperation = "install";
    public const string UpgradeOperation = "upgrade";
    public const string ForwardFixRecovery = "ForwardFix";
    public const string ImmutableResourceNames = "Immutable";
    public const string PreserveSharePointData = "Preserve";

    public UpgradeContractValidationResult ValidatePackageIntent(
        CustomerInstallConfig config,
        bool deploymentSectionPresent,
        bool requireDeploymentIntent)
    {
        ArgumentNullException.ThrowIfNull(config);
        var result = new UpgradeContractValidationResult();
        if (!deploymentSectionPresent || string.IsNullOrWhiteSpace(config.Deployment.Operation))
        {
            var message = "deployment.operation is required to distinguish clean install from upgrade.";
            if (requireDeploymentIntent)
            {
                result.Errors.Add(message);
            }
            else
            {
                result.Warnings.Add(message + " This legacy package can be used only when the target resource group is absent.");
            }

            return result;
        }

        var operation = config.Deployment.Operation.Trim().ToLowerInvariant();
        if (operation is not (InstallOperation or UpgradeOperation))
        {
            result.Errors.Add($"Unsupported deployment.operation '{config.Deployment.Operation}'. Use install or upgrade.");
            return result;
        }

        result.IsUpgrade = operation == UpgradeOperation;
        var target = ParseRequiredVersion(config.Deployment.TargetRuntimeVersion, "deployment.targetRuntimeVersion", result);
        var minimumInstaller = ParseRequiredVersion(config.Deployment.MinimumInstallerVersion, "deployment.minimumInstallerVersion", result);
        if (minimumInstaller is not null &&
            SemanticVersionValue.Parse(CurrentInstallerVersion).CompareTo(minimumInstaller.Value) < 0)
        {
            result.Errors.Add(
                $"This package requires installer {minimumInstaller.Value} or later; the current installer is {CurrentInstallerVersion}.");
        }

        RequirePolicy(config.Deployment.FailureRecovery, ForwardFixRecovery, "deployment.failureRecovery", result);
        RequirePolicy(config.Deployment.ResourceNamePolicy, ImmutableResourceNames, "deployment.resourceNamePolicy", result);
        RequirePolicy(config.Deployment.SharePointDataPolicy, PreserveSharePointData, "deployment.sharePointDataPolicy", result);

        if (!result.IsUpgrade)
        {
            if (!string.IsNullOrWhiteSpace(config.Deployment.SourceRuntimeVersion) ||
                !string.IsNullOrWhiteSpace(config.Deployment.SourceDeploymentExportId))
            {
                result.Errors.Add("Clean-install packages must not declare a source runtime version or source deployment export.");
            }

            return result;
        }

        var source = ParseRequiredVersion(config.Deployment.SourceRuntimeVersion, "deployment.sourceRuntimeVersion", result);
        if (string.IsNullOrWhiteSpace(config.Deployment.SourceDeploymentExportId))
        {
            result.Errors.Add("deployment.sourceDeploymentExportId is required for upgrade packages.");
        }

        if (source is null || target is null)
        {
            return result;
        }

        if (target.Value.CompareTo(source.Value) <= 0)
        {
            result.Errors.Add("Upgrade target runtime version must be greater than the source runtime version.");
        }
        else if (target.Value.Major != source.Value.Major)
        {
            result.Errors.Add("Major-version upgrades are not supported by the v1 installer.");
        }
        else if (target.Value.Minor > source.Value.Minor + 1)
        {
            result.Errors.Add("Upgrade packages cannot skip a minor runtime version.");
        }

        return result;
    }

    private static SemanticVersionValue? ParseRequiredVersion(
        string value,
        string field,
        UpgradeContractValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result.Errors.Add($"{field} is required.");
            return null;
        }

        if (!SemanticVersionValue.TryParse(value, out var parsed))
        {
            result.Errors.Add($"{field} must be a stable semantic version in major.minor.patch form.");
            return null;
        }

        return parsed;
    }

    private static void RequirePolicy(
        string actual,
        string expected,
        string field,
        UpgradeContractValidationResult result)
    {
        if (!actual.Equals(expected, StringComparison.Ordinal))
        {
            result.Errors.Add($"{field} must be '{expected}'.");
        }
    }

    private readonly record struct SemanticVersionValue(int Major, int Minor, int Patch) : IComparable<SemanticVersionValue>
    {
        public int CompareTo(SemanticVersionValue other)
        {
            var major = Major.CompareTo(other.Major);
            if (major != 0)
            {
                return major;
            }

            var minor = Minor.CompareTo(other.Minor);
            return minor != 0 ? minor : Patch.CompareTo(other.Patch);
        }

        public override string ToString() => $"{Major}.{Minor}.{Patch}";

        public static SemanticVersionValue Parse(string value)
        {
            TryParse(value, out var parsed);
            return parsed;
        }

        public static bool TryParse(string value, out SemanticVersionValue parsed)
        {
            var match = StableSemanticVersionRegex().Match(value.Trim());
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out var major) &&
                int.TryParse(match.Groups[2].Value, out var minor) &&
                int.TryParse(match.Groups[3].Value, out var patch))
            {
                parsed = new SemanticVersionValue(major, minor, patch);
                return true;
            }

            parsed = default;
            return false;
        }
    }

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex StableSemanticVersionRegex();
}
