# PageMaker365 Removal Evidence Callback Contract

Status: installer v0.3 implemented; portal/control-plane acceptance and staging
proof pending under issue #9.

## Purpose

The removal lifecycle reports sanitized Azure-only uninstall milestones without
reusing install events and without allowing portal availability to redefine the
Azure result. It does not upload raw inventory, logs, artifacts, files, tenant
exports, SharePoint content, or secret material.

## Endpoint And Authorization

Removal uses the existing lifecycle evidence endpoint:

```text
POST /api/onboarding/installer/evidence
```

Required headers remain `Idempotency-Key`, `X-PM365-Onboarding-Session`,
`X-PM365-Onboarding-Code`, and `Content-Type: application/json`, plus the
environment bearer token when configured. The bootstrap must authorize
`RemovalStatusSync`, `discoveryPolicy.allowPortalSync` must be true, and the
package onboarding session and deployment export must match the active setup
file. Without that authorization, the installer performs no removal callback.

## v0.3 Lifecycle Envelope

The endpoint must accept these fields for both install and removal evidence:

| Field | Removal value | Rule |
| --- | --- | --- |
| `lifecycle` | `removal` | Separates removal state from install state. |
| `attemptId` | `ra_<opaque-id>` | Authoritative generic lifecycle attempt. |
| `removalAttemptId` | Same as `attemptId` | Required removal-specific identity. |
| `installAttemptId` | Same as `attemptId` | Temporary v0.2 compatibility alias only; it does not make this an install attempt. |
| `eventId` | Stable opaque ID | Reused unchanged for delivery retries. |
| `sequence` | Positive integer | Starts at 1 and increases only for a new observed removal event. |
| `onboardingSessionId` | Active setup session | Must match the session header. |
| `deploymentExportId` | Original package export | Binds removal to the deployed package. |
| `lifecycleStatus` | See below | Drives portal removal state. |
| `outcome` | See below | Describes the individual event result. |
| `removalOutcomes` | Sanitized counts/categories | Never contains raw resource inventory. |

The portal response must use `contractVersion: "0.3"` and `status: "Accepted"`
and must echo `lifecycle`, `attemptId`, `removalAttemptId`, `eventId`,
`eventType`, `sequence`, `lifecycleStatus`, `outcome`, session, and correlation
ID. During the v0.3 migration it may also echo the compatibility
`installAttemptId`. The installer keeps the event queued when any echoed
identity or semantic field differs, even when the HTTP response is successful.

## Event Order

Normal successful order within one `removalAttemptId`:

1. `removal_started`
2. `removal_inventory_completed`
3. `removal_execution_completed`
4. `removal_validation_completed`
5. `removal_completed`

Read-only inventory may be refreshed more than once before execution. Each
successful refresh emits another `removal_inventory_completed` event with the
same attempt ID and the next sequence. This prevents an active, nonterminal
attempt from being abandoned merely because the operator refreshed safety
evidence.

Terminal alternatives:

- `removal_blocked` follows `removal_started` when inventory cannot prove a
  safe deletion boundary.
- `removal_failed` follows the last successful milestone when Azure execution
  or cleanup validation fails.

`removal_completed`, `removal_blocked`, and `removal_failed` are terminal. No
later event is valid for that attempt. Retrying after a terminal blocker or
failure creates a new `removalAttemptId` and resets sequence to 1; pending
events from the earlier attempt remain unchanged in the outbox.

The portal must reject a terminal event before `removal_started`, execution
before successful inventory, validation before execution, completion before
validation, and any non-idempotent event after a terminal event.

## Status And Outcome

| Event state | `lifecycleStatus` | `outcome` |
| --- | --- | --- |
| Started or successful intermediate milestone | `removing` | `passed` or `skipped` |
| Inventory safety blocker | `needs_attention` | `blocked` |
| Execution or validation failure | `failed` | `failed` |
| Validated final removal | `removed` | `passed` |

Portal install and removal state machines are independent. A removal callback
must never rewrite install attempt ordering, and a prior `install_completed`
event must not prevent a later authorized removal attempt.

## Sanitized Removal Outcomes

`removalOutcomes` contains non-negative counts:

```json
{
  "removed": 1,
  "retained": 3,
  "skipped": 0,
  "blocked": 0,
  "failed": 0,
  "retainedCategories": [
    "key_vault_soft_deleted",
    "sharepoint_content_unchanged",
    "local_evidence_retained"
  ],
  "skippedCategories": []
}
```

Allowed retained categories are `key_vault_soft_deleted`,
`sharepoint_content_unchanged`, `local_evidence_retained`, and
`ambiguous_resource_retained`. Allowed skipped categories are
`resource_group_already_absent` and `not_applicable`. Counts and category codes
provide customer-visible disposition without transmitting subscription
inventory, resource IDs, file names, paths, or provider responses.

Disposition categories are emitted only after they are established. Started,
inventory, blocked, and failed events do not claim final retained resources.
`key_vault_soft_deleted` is reported only when inventory found the package-named
vault before a successful resource-group deletion. When the resource group was
already absent, the installer reports the idempotent skip but does not invent a
Key Vault disposition. When the vault was never created, `not_applicable` is
reported instead.

## Sanitized Errors

Blocked and failed events use the existing error object with stable codes:

- `REMOVAL_OWNERSHIP_BLOCKED`
- `REMOVAL_INVENTORY_BLOCKED`
- `REMOVAL_EXECUTION_FAILED`
- `REMOVAL_VALIDATION_FAILED`

Only a mapped message, category, and retryable flag are sent. Raw exceptions,
Azure responses, logs, stack traces, tokens, secrets, connection strings,
one-time codes, artifact contents, and customer content are prohibited.

## Idempotency, Conflict, And Stale Events

The key format is `<attemptId>:<sequence>:<eventId>`. The installer rejects a
persisted or supplied key that does not exactly match those payload fields,
persists the complete payload and key before delivery, and reuses them unchanged
for every retry. Portal synchronization failures leave the event queued and do
not change inventory, deletion, validation, or final local evidence results.

The portal must:

- return the stored receipt for an exact duplicate key and payload;
- reject a reused key or event ID with materially different content;
- reject a non-idempotent duplicate sequence;
- reject stale lower sequences except exact duplicates;
- isolate install and removal attempts by `lifecycle` plus `attemptId`;
- preserve terminal state while still returning idempotent duplicate receipts.

## Portal Implementation And Test Handoff

The portal/API implementation is ready for installer staging validation when:

1. The v0.3 fields, removal event/status/outcome enums, and outcome summary are
   accepted and stored without changing the v0.2 install behavior.
2. The lifecycle state machine enforces the order and terminal rules above.
3. Customer/admin surfaces show Removing, Needs Attention, Failed, Removed, and
   Sync Pending without exposing raw inventory.
4. API tests cover exact duplicate, idempotency conflict, duplicate/stale
   sequence, out-of-order, post-terminal, install/removal coexistence, prohibited
   payload, offline delivery, and successful retry.
5. A staging setup file authorizes `RemovalStatusSync`, and a complete sandbox
   uninstall proves ordered callbacks plus local-outbox recovery.
