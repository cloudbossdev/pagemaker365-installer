using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class RemovalEvidenceLifecycleService
{
    private static readonly HashSet<string> AllowedRetainedCategories = new(StringComparer.Ordinal)
    {
        "key_vault_soft_deleted",
        "sharepoint_content_unchanged",
        "local_evidence_retained",
        "ambiguous_resource_retained"
    };

    private static readonly HashSet<string> AllowedSkippedCategories = new(StringComparer.Ordinal)
    {
        "resource_group_already_absent",
        "not_applicable"
    };

    public string StartNewAttempt(RemovalEvidenceOutboxState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.RemovalAttemptId = $"ra_{Guid.NewGuid():N}";
        state.NextSequence = 1;
        state.RemovalStarted = false;
        state.InventoryCompleted = false;
        state.ExecutionCompleted = false;
        state.ValidationCompleted = false;
        state.IsTerminal = false;
        state.LastEventType = "";
        return state.RemovalAttemptId;
    }

    public PendingInstallerEvidenceEvent Queue(
        RemovalEvidenceOutboxState state,
        InstallerEvidenceEvent payload)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrWhiteSpace(state.RemovalAttemptId))
        {
            throw new InvalidOperationException("Start a removal evidence attempt before queueing an event.");
        }

        ValidateTransition(state, payload.EventType);

        payload.Lifecycle = InstallerEvidenceLifecycle.Removal;
        payload.AttemptId = state.RemovalAttemptId;
        payload.RemovalAttemptId = state.RemovalAttemptId;
        payload.InstallAttemptId = state.RemovalAttemptId;
        payload.EventId = string.IsNullOrWhiteSpace(payload.EventId)
            ? $"evt_{Guid.NewGuid():N}"
            : payload.EventId;
        payload.Sequence = state.NextSequence;
        if (payload.OccurredAt == default)
        {
            payload.OccurredAt = DateTimeOffset.UtcNow;
        }
        ValidatePayload(payload);
        state.NextSequence++;

        var pending = new PendingInstallerEvidenceEvent
        {
            IdempotencyKey = $"{payload.AttemptId}:{payload.Sequence}:{payload.EventId}",
            Payload = payload
        };
        state.PendingEvents.Add(pending);
        ApplyTransition(state, payload.EventType);
        return pending;
    }

    private static void ValidateTransition(RemovalEvidenceOutboxState state, string eventType)
    {
        if (state.IsTerminal)
        {
            throw new InvalidOperationException("A terminal removal attempt cannot accept another event.");
        }

        var allowed = eventType switch
        {
            InstallerEvidenceEventType.RemovalStarted => !state.RemovalStarted && string.IsNullOrWhiteSpace(state.LastEventType),
            InstallerEvidenceEventType.RemovalInventoryCompleted => state.RemovalStarted && !state.ExecutionCompleted,
            InstallerEvidenceEventType.RemovalExecutionCompleted => state.InventoryCompleted && !state.ExecutionCompleted,
            InstallerEvidenceEventType.RemovalValidationCompleted => state.ExecutionCompleted && !state.ValidationCompleted,
            InstallerEvidenceEventType.RemovalCompleted => state.ValidationCompleted,
            InstallerEvidenceEventType.RemovalBlocked or InstallerEvidenceEventType.RemovalFailed => state.RemovalStarted,
            _ => false
        };
        if (!allowed)
        {
            throw new InvalidOperationException($"Removal event '{eventType}' is not valid after '{state.LastEventType}'.");
        }
    }

    private static void ApplyTransition(RemovalEvidenceOutboxState state, string eventType)
    {
        state.LastEventType = eventType;
        switch (eventType)
        {
            case InstallerEvidenceEventType.RemovalStarted:
                state.RemovalStarted = true;
                break;
            case InstallerEvidenceEventType.RemovalInventoryCompleted:
                state.InventoryCompleted = true;
                break;
            case InstallerEvidenceEventType.RemovalExecutionCompleted:
                state.ExecutionCompleted = true;
                break;
            case InstallerEvidenceEventType.RemovalValidationCompleted:
                state.ValidationCompleted = true;
                break;
            case InstallerEvidenceEventType.RemovalCompleted:
            case InstallerEvidenceEventType.RemovalBlocked:
            case InstallerEvidenceEventType.RemovalFailed:
                state.IsTerminal = true;
                break;
        }
    }

    public static void ValidatePayload(InstallerEvidenceEvent payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!payload.Lifecycle.Equals(InstallerEvidenceLifecycle.Removal, StringComparison.Ordinal) ||
            !payload.EventType.StartsWith("removal_", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Removal evidence must use the removal lifecycle and an allowed removal event type.");
        }

        var outcomes = payload.RemovalOutcomes ??
            throw new InvalidDataException("Removal evidence must include a sanitized outcome summary.");
        ValidateOutcomes(outcomes);

        var valid = payload.EventType switch
        {
            InstallerEvidenceEventType.RemovalStarted =>
                IsEvent(payload, "removing", "passed", requiresError: false) &&
                OutcomeTotal(outcomes) == 0,
            InstallerEvidenceEventType.RemovalInventoryCompleted =>
                IsEvent(payload, "removing", "passed", requiresError: false) &&
                outcomes.Blocked == 0 && outcomes.Failed == 0,
            InstallerEvidenceEventType.RemovalExecutionCompleted =>
                payload.Outcome is "passed" or "skipped" &&
                payload.LifecycleStatus.Equals("removing", StringComparison.Ordinal) &&
                payload.Error is null && outcomes.Blocked == 0 && outcomes.Failed == 0,
            InstallerEvidenceEventType.RemovalValidationCompleted =>
                IsEvent(payload, "removing", "passed", requiresError: false) &&
                outcomes.Blocked == 0 && outcomes.Failed == 0,
            InstallerEvidenceEventType.RemovalCompleted =>
                IsEvent(payload, "removed", "passed", requiresError: false) &&
                outcomes.Blocked == 0 && outcomes.Failed == 0,
            InstallerEvidenceEventType.RemovalBlocked =>
                IsEvent(payload, "needs_attention", "blocked", requiresError: true) &&
                outcomes.Blocked > 0 && outcomes.Removed == 0 && outcomes.Failed == 0,
            InstallerEvidenceEventType.RemovalFailed =>
                IsEvent(payload, "failed", "failed", requiresError: true) &&
                outcomes.Failed > 0,
            _ => false
        };
        if (!valid)
        {
            throw new InvalidDataException("Removal evidence event status, outcome, error, and disposition counts are inconsistent.");
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

    private static int OutcomeTotal(RemovalEvidenceOutcomeSummary outcomes)
    {
        return outcomes.Removed + outcomes.Retained + outcomes.Skipped + outcomes.Blocked + outcomes.Failed;
    }

    private static void ValidateOutcomes(RemovalEvidenceOutcomeSummary outcomes)
    {
        if (outcomes.Removed < 0 || outcomes.Retained < 0 || outcomes.Skipped < 0 ||
            outcomes.Blocked < 0 || outcomes.Failed < 0)
        {
            throw new InvalidDataException("Removal outcome counts cannot be negative.");
        }

        var invalidRetained = outcomes.RetainedCategories.FirstOrDefault(value => !AllowedRetainedCategories.Contains(value));
        var invalidSkipped = outcomes.SkippedCategories.FirstOrDefault(value => !AllowedSkippedCategories.Contains(value));
        if (invalidRetained is not null || invalidSkipped is not null)
        {
            throw new InvalidDataException("Removal evidence contains a non-allowlisted outcome category.");
        }

        if (outcomes.RetainedCategories.Count != outcomes.RetainedCategories.Distinct(StringComparer.Ordinal).Count() ||
            outcomes.SkippedCategories.Count != outcomes.SkippedCategories.Distinct(StringComparer.Ordinal).Count() ||
            outcomes.RetainedCategories.Count > outcomes.Retained ||
            outcomes.SkippedCategories.Count > outcomes.Skipped)
        {
            throw new InvalidDataException("Removal evidence outcome categories do not match their disposition counts.");
        }
    }
}
