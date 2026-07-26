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
        ValidateOutcomes(payload.RemovalOutcomes);

        payload.Lifecycle = InstallerEvidenceLifecycle.Removal;
        payload.AttemptId = state.RemovalAttemptId;
        payload.RemovalAttemptId = state.RemovalAttemptId;
        payload.InstallAttemptId = state.RemovalAttemptId;
        payload.EventId = string.IsNullOrWhiteSpace(payload.EventId)
            ? $"evt_{Guid.NewGuid():N}"
            : payload.EventId;
        payload.Sequence = state.NextSequence++;
        if (payload.OccurredAt == default)
        {
            payload.OccurredAt = DateTimeOffset.UtcNow;
        }

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
            InstallerEvidenceEventType.RemovalInventoryCompleted => state.RemovalStarted && !state.InventoryCompleted,
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

    private static void ValidateOutcomes(RemovalEvidenceOutcomeSummary? outcomes)
    {
        if (outcomes is null)
        {
            return;
        }

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
    }
}
