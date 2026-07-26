using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class UpgradeEvidenceLifecycleService
{
    private static readonly HashSet<string> EventTypes = new(StringComparer.Ordinal)
    {
        InstallerEvidenceEventType.UpgradePackageValidated,
        InstallerEvidenceEventType.UpgradePackageValidationFailed,
        InstallerEvidenceEventType.UpgradeStarted,
        InstallerEvidenceEventType.UpgradeDeploymentCompleted,
        InstallerEvidenceEventType.UpgradeRuntimeConfigured,
        InstallerEvidenceEventType.UpgradeValidationCompleted,
        InstallerEvidenceEventType.UpgradeCompleted,
        InstallerEvidenceEventType.UpgradeFailed
    };

    public void Prepare(
        InstallerEvidenceOutboxState state,
        InstallerEvidenceEvent payload)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(payload);
        ValidateTransition(state, payload.EventType);

        payload.Lifecycle = UpgradeContractService.UpgradeOperation;
        payload.Operation = UpgradeContractService.UpgradeOperation;
        payload.AttemptId = state.InstallAttemptId;
        payload.InstallAttemptId = state.InstallAttemptId;
        payload.UpgradeAttemptId = state.InstallAttemptId;
        payload.Sequence = state.NextSequence;
        ValidatePayload(payload);

        state.NextSequence++;
        state.LastEventType = payload.EventType;
        if (payload.EventType == InstallerEvidenceEventType.UpgradeStarted)
        {
            state.InstallStarted = true;
        }

        if (IsTerminal(payload.EventType))
        {
            state.IsTerminal = true;
        }
    }

    public static void ValidatePayload(InstallerEvidenceEvent payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!payload.Lifecycle.Equals(UpgradeContractService.UpgradeOperation, StringComparison.Ordinal) ||
            !payload.Operation.Equals(UpgradeContractService.UpgradeOperation, StringComparison.Ordinal) ||
            !EventTypes.Contains(payload.EventType) ||
            string.IsNullOrWhiteSpace(payload.AttemptId) ||
            !payload.AttemptId.StartsWith("ua_", StringComparison.Ordinal) ||
            !payload.AttemptId.Equals(payload.InstallAttemptId, StringComparison.Ordinal) ||
            !payload.AttemptId.Equals(payload.UpgradeAttemptId, StringComparison.Ordinal) ||
            payload.Sequence <= 0 ||
            (payload.EventType != InstallerEvidenceEventType.UpgradePackageValidationFailed &&
                (string.IsNullOrWhiteSpace(payload.SourceRuntimeVersion) ||
                    string.IsNullOrWhiteSpace(payload.TargetRuntimeVersion))))
        {
            throw new InvalidDataException("Upgrade evidence lifecycle, attempt identity, version identity, or sequence is invalid.");
        }

        var valid = payload.EventType switch
        {
            InstallerEvidenceEventType.UpgradePackageValidated =>
                IsEvent(payload, "provisioning", "passed", requiresError: false),
            InstallerEvidenceEventType.UpgradePackageValidationFailed =>
                IsEvent(payload, "needs_attention", "warning", requiresError: true),
            InstallerEvidenceEventType.UpgradeStarted =>
                IsEvent(payload, "provisioning", "passed", requiresError: false),
            InstallerEvidenceEventType.UpgradeDeploymentCompleted =>
                IsPassedOrWarningEvent(payload, "provisioning"),
            InstallerEvidenceEventType.UpgradeRuntimeConfigured =>
                IsEvent(payload, "provisioning", "passed", requiresError: false),
            InstallerEvidenceEventType.UpgradeValidationCompleted =>
                IsPassedOrWarningValidationEvent(payload),
            InstallerEvidenceEventType.UpgradeCompleted =>
                IsPassedOrWarningEvent(payload, "completed"),
            InstallerEvidenceEventType.UpgradeFailed =>
                IsEvent(payload, "failed", "failed", requiresError: true),
            _ => false
        };
        if (!valid)
        {
            throw new InvalidDataException("Upgrade evidence status, outcome, and error semantics are inconsistent.");
        }
    }

    public static bool IsTerminal(string eventType)
    {
        return eventType is InstallerEvidenceEventType.UpgradePackageValidationFailed or
            InstallerEvidenceEventType.UpgradeCompleted or
            InstallerEvidenceEventType.UpgradeFailed;
    }

    private static void ValidateTransition(InstallerEvidenceOutboxState state, string eventType)
    {
        if (state.IsTerminal)
        {
            throw new InvalidDataException("A terminal upgrade attempt cannot accept another evidence event.");
        }

        var last = state.LastEventType;
        var allowed = eventType switch
        {
            InstallerEvidenceEventType.UpgradePackageValidated => string.IsNullOrWhiteSpace(last),
            InstallerEvidenceEventType.UpgradePackageValidationFailed => string.IsNullOrWhiteSpace(last),
            InstallerEvidenceEventType.UpgradeStarted => last == InstallerEvidenceEventType.UpgradePackageValidated,
            InstallerEvidenceEventType.UpgradeDeploymentCompleted => last == InstallerEvidenceEventType.UpgradeStarted,
            InstallerEvidenceEventType.UpgradeRuntimeConfigured => last == InstallerEvidenceEventType.UpgradeDeploymentCompleted,
            InstallerEvidenceEventType.UpgradeValidationCompleted =>
                last is InstallerEvidenceEventType.UpgradeRuntimeConfigured or
                    InstallerEvidenceEventType.UpgradeValidationCompleted,
            InstallerEvidenceEventType.UpgradeCompleted => last == InstallerEvidenceEventType.UpgradeValidationCompleted,
            InstallerEvidenceEventType.UpgradeFailed => state.InstallStarted,
            _ => false
        };
        if (!allowed)
        {
            throw new InvalidDataException($"Upgrade evidence event '{eventType}' is out of order after '{last}'.");
        }
    }

    private static bool IsEvent(
        InstallerEvidenceEvent payload,
        string lifecycleStatus,
        string outcome,
        bool requiresError)
    {
        return payload.LifecycleStatus.Equals(lifecycleStatus, StringComparison.Ordinal) &&
            payload.Outcome.Equals(outcome, StringComparison.Ordinal) &&
            (requiresError ? payload.Error is not null : payload.Error is null);
    }

    private static bool IsPassedOrWarningEvent(InstallerEvidenceEvent payload, string lifecycleStatus)
    {
        return payload.LifecycleStatus.Equals(lifecycleStatus, StringComparison.Ordinal) &&
            ((payload.Outcome.Equals("passed", StringComparison.Ordinal) && payload.Error is null) ||
                (payload.Outcome.Equals("warning", StringComparison.Ordinal) && payload.Error is not null));
    }

    private static bool IsPassedOrWarningValidationEvent(InstallerEvidenceEvent payload)
    {
        return (payload.LifecycleStatus.Equals("provisioning", StringComparison.Ordinal) &&
                payload.Outcome.Equals("passed", StringComparison.Ordinal) &&
                payload.Error is null) ||
            (payload.LifecycleStatus.Equals("needs_attention", StringComparison.Ordinal) &&
                payload.Outcome.Equals("warning", StringComparison.Ordinal) &&
                payload.Error is not null);
    }
}
