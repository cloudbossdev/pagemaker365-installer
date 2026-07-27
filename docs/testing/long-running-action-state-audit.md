# Long-Running Action State Audit

Status: automated contract implemented; interactive release-candidate review pending

Tracking issue: [#4](https://github.com/cloudbossdev/pagemaker365-installer/issues/4)

## Purpose

This audit defines the UI contract for commands that wait on a portal, Azure, Microsoft Graph, PowerShell, local evidence generation, or file processing. It prevents an operator from mistaking an active command for a frozen application or starting overlapping work that can mutate the same session.

## Required State Contract

Every long-running command must:

- expose an indeterminate activity indicator for the entire awaited operation;
- show a short, current activity label;
- reject duplicate execution while it is active;
- return the UI to an idle, retryable state after success, handled failure, or cancellation;
- retain a useful sanitized terminal message and applicable correlation or evidence reference;
- keep portal synchronization failure separate from the completed Azure or local operation.

Instant navigation, clipboard copy, opening a local window or URL, and in-memory selection changes do not require an activity indicator.

## Surface Inventory

| Surface | Long-running actions | Visible state | Automated evidence | Live status |
| --- | --- | --- | --- | --- |
| Welcome | Resume saved session | Main activity strip and current phase | App resume tests; `RelayCommand` running-state test | Release-candidate walkthrough pending |
| Package | Setup-file validation, portal connect/readiness/poll/download, discovery, sync, evidence retry, local package validation | Main activity strip plus Package acquisition bar and readiness text | Engine/API and app package workflow tests | Fresh staging download blocked by `cloudbossdev/pagemaker365#5` |
| Sign In | Azure sign-in and Graph device-code sign-in | Main activity strip, independent service status, visible Graph code state | Authentication cancellation, expiry, context, and dual-sign-in tests | Interactive staging variants pending |
| Preflight | Package trust, Azure, Graph, SharePoint, dependency, provider, SKU, quota, and Key Vault checks | Main activity strip, current phase, streamed check results | Preflight contract suites | Permission and policy variants pending |
| Preview | Azure What-If and preview evidence | Main activity strip plus Preview bar and status | What-If fallback and deployment-artifact tests | Staging warning/no-change variants pending |
| Install | Approval binding, Azure deployment, runtime configuration, evidence queue/flush | Main activity strip plus Install bar and streamed deployment output | Deployment, protected-input, artifact, callback, and timeout tests | Runtime producer and signed-package gates pending |
| Validate | Runtime health, release identity, portal content, and SharePoint access | Main activity strip plus Validate bar and status | Runtime smoke-test contracts | Runtime artifact deployment pending |
| Finish | Final report, manifest, evidence archive, and callback flush | Main activity strip and current phase | Final-evidence and callback tests | Clean lifecycle campaign pending |
| Remove | Inventory, removal, absence validation, final evidence, and callback flush | Main activity strip plus Remove/Validate bars and status | Cleanup safety and removal lifecycle tests | Staging removal lifecycle pending |
| Guidance | Explain issue, admin message, support bundle | Main activity strip and current phase | Support-bundle and assistant callback tests | Interactive review pending |
| Assistant Workspace | Attachment import, message send, recommended action, attachment upload, and support-ticket draft | Assistant footer activity bar and operation label; other assistant operations disabled | Assistant long-running state and sanitized-failure tests | Staging assistant handoff pending |

## Retry And Cancellation Boundary

The activity indicator reports work; it does not promise that every external operation can be canceled after the provider accepts it. Authentication cancellation, PowerShell cancellation/timeouts, portal package retry, and callback outbox retry follow their specific contracts. Azure mutation results must be reconciled from evidence before the operator retries or removes resources.

## Interactive Review

On the signed release candidate, exercise at least one delayed success and one handled failure on every surface above. Confirm the animation remains visible, action labels fit at the minimum supported window size, duplicate actions remain unavailable, the UI returns to idle, and failure text contains no token, setup code, secret, local path, or raw customer content.
