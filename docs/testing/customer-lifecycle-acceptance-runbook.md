# Customer Lifecycle Acceptance Runbook

Status: controlled test procedure; no production customer use

Tracking issues: [#10 lifecycle acceptance](https://github.com/cloudbossdev/pagemaker365-installer/issues/10), [#11 user guide](https://github.com/cloudbossdev/pagemaker365-installer/issues/11), and [#12 technical/security guide](https://github.com/cloudbossdev/pagemaker365-installer/issues/12)

## Objective

Prove on a clean Windows 11 workstation that an authorized operator can verify and launch the release candidate, install and validate PageMaker365, recover from supported interruptions, remove only the owned Azure deployment, preserve SharePoint and soft-deleted Key Vault state, reinstall with new immutable packages, and obtain portal/evidence outcomes that agree with Azure.

This runbook supplies live evidence for the canonical scenarios in `docs/install-uninstall-test-matrix.md`. It does not replace automated CI, security review, or production code-signing approval.

Release-critical checkpoints explicitly include W01, W06, S03, P01, A01, F01, D01, D07, L01, L02, L03, L04, L05, L06, L07, L08, L09, R01, R09, R13, R14, R15, E06, E08, and T04. Scenario ranges in later phases add the remaining approved test coverage.

## Required Roles

| Role | Responsibility |
| --- | --- |
| Test coordinator | Assigns run ID, package set, fault injections, and stop decisions. |
| Clean operator | Uses only the signed release candidate and customer user guide; does not use repository scripts or developer assistance during the clean path. |
| Azure/identity administrator | Supplies approved roles/consent and executes only separately approved tenant administration. |
| Portal/API observer | Confirms lifecycle events, ordering, idempotency, and terminal dashboard state. |
| Security reviewer | Reviews network, local files, support bundle, callback payloads, and prohibited-data scan. |
| Evidence reviewer | Reconciles installer, Azure, SharePoint, portal, and retained-resource outcomes. |

One person may hold multiple roles, but the clean operator must not have authored the tested workflow changes.

## Stop Rules

Stop the run and preserve evidence when any of these conditions occurs:

- distribution verification fails or official publisher/thumbprint cannot be independently confirmed;
- setup-file, package, tenant, subscription, environment, resource-group, deployment-export, or Key Vault identity is ambiguous;
- a secret, token, one-time code, document content, or unrelated tenant export appears in logs, callbacks, screenshots, or support artifacts;
- the installer proposes SharePoint mutation, Key Vault purge, deletion outside the dedicated PageMaker365 resource group, or bypass of typed approval;
- portal status claims success before deployment-bound validation or disagrees with the local/Azure terminal outcome;
- an operator must edit a signed package or use an undocumented workaround to proceed.

Do not purge a Key Vault to unblock this test. Generate a new package with a new disposable vault name.

## Test Inputs

Create a run folder named `PM365-ACCEPT-{yyyyMMdd-HHmm}-{releaseVersion}` in the approved evidence store. Record only sanitized identifiers in this file; store setup files and secret values separately under the approved sensitive-material policy.

| Input | Required value |
| --- | --- |
| Release version and commit | Exact release candidate version and source commit. |
| ZIP SHA-256 | Value from the delivered `.sha256` file. |
| Official publisher and certificate thumbprint | Obtained outside the ZIP from the approved release record. |
| Workstation | Windows edition/build, device ID alias, patch date, proxy/inspection state. |
| Customer/environment alias | Synthetic staging alias only. |
| Tenant and subscription aliases | Sanitized aliases; full IDs remain in restricted evidence. |
| SharePoint test marker | Non-sensitive unique page/list/document name used only to prove preservation. |
| Portal/API build | Staging deployment version or commit. |

Prepare at least four independently generated signed packages:

| Package | Purpose | Required identity |
| --- | --- | --- |
| `INSTALL-01` | First clean install | New deployment export, resource group, and Key Vault. |
| `INSTALL-02` | Reinstall cycle 2 | New deployment export and Key Vault; never reuse cycle 1 vault. |
| `INSTALL-03` | Reinstall cycle 3 | New deployment export and Key Vault. |
| `RECOVERY-01` | Partial-failure cleanup and reinstall | New deployment export and Key Vault dedicated to the recovery test. |

Each package record must include package hash, onboarding session alias, deployment export ID, resource-group name, Key Vault name, target runtime version, issue/PR that generated it, and portal readiness result.

## Evidence Layout

Use this layout in the approved evidence store:

```text
PM365-ACCEPT-{run-id}/
  00-run-control/
  01-distribution/
  02-clean-install/
  03-recovery/
  04-cycle-01/
  05-cycle-02/
  06-cycle-03/
  07-negative-paths/
  08-security-review/
  09-final-reconciliation/
```

For each scenario, retain the installer version, UTC start/end, result, sanitized screenshot, relevant installer evidence, Azure deployment/correlation ID, portal event IDs/sequences, reviewer, and deviation reference. Never place a setup file or raw protected value in this evidence tree.

## Phase 1: Distribution And Clean Workstation

Scenarios: W01-W07 and S01.

1. Begin from a Windows 11 workstation with no PageMaker365 installer state, repository checkout, developer certificate, locally built executable, or preinstalled unpublished dependency.
2. Record the workstation baseline and confirm `%LOCALAPPDATA%\PageMaker365\Installer\sessions` is absent or empty.
3. Verify the release ZIP with the external SHA-256, official publisher, and official certificate thumbprint by following `docs/customer/installer-distribution-verification.md`.
4. Capture the verifier result and extracted exact inventory.
5. Launch the installer and confirm product name, icon, file version, release label, Welcome layout, and no stale customer session.
6. Close and relaunch before loading customer data; confirm no false resume prompt.
7. In a separate negative copy, modify one manifest-listed test file and confirm verification fails before launch. Delete the negative copy after recording the result.

Pass when W01-W07 evidence is complete and the verified installer launches without repository tooling or an unpublished trust decision.

## Phase 2: Clean Install And Finish

Scenarios: P01-P02, A01-A03, F01, F11-F12, D01-D06, V01, V07, E01, E03-E05, E07, T01-T04, and L01.

1. Use `INSTALL-01` and follow the customer user guide without undocumented assistance.
2. Confirm one setup-file action connects, waits when necessary, downloads, verifies, loads, and advances to Sign In.
3. Complete Azure sign-in first, then Graph. Confirm neither single sign-in unlocks Preflight.
4. Run Preflight. Resolve a planned non-destructive warning/blocker, rerun, and capture the before/after results.
5. Run Preview and reconcile What-If counts with the named resource group. Confirm approval is unavailable until preview completes.
6. Enter test-only protected values through the installer. Do not record those values in screenshots or evidence.
7. Approve, type the exact resource group, and run Install once. Confirm visible activity and duplicate-command prevention.
8. Validate the runtime identity, portal content, SharePoint access, and customer URL.
9. Create final evidence and retry portal sync if needed.
10. In Azure, reconcile every expected package-named resource, ownership tag, managed identity, Key Vault reference, and deployment correlation.
11. In the portal, reconcile lifecycle order, stable attempt ID, monotonic sequence, idempotency keys, terminal status, and displayed customer/environment.

Pass only when Azure, runtime validation, final evidence, portal state, and the verified customer URL agree. Azure resource creation without deployment-bound validation is not a pass.

## Phase 3: Authentication, Resume, And Portal-Outage Recovery

Scenarios: S02-S05, A02, A06-A09, P03, P12-P13, E06-E07, and L05-L06.

1. Start a new non-destructive session and complete Graph before Azure. Confirm both remain required.
2. Cancel one Azure sign-in and one Graph device-code attempt. Confirm the UI returns to idle, clears stale code, and does not advance.
3. Allow a dedicated Graph code to expire. Confirm a new sign-in is required.
4. Close the installer after package validation, resume, and confirm customer/package state returns but tokens and approval do not.
5. Close after Preview, resume, and confirm typed confirmation and destructive approval are cleared; rerun any preview that the UI marks stale.
6. Use an expired/used setup file and confirm the operator is directed to obtain a new file.
7. During a successful non-Azure portal callback, isolate the PageMaker365 portal/API destination under the approved test fault. Confirm the local Azure result is unchanged and the exact event remains queued.
8. Restore connectivity and choose **Retry Portal Sync**. Confirm stable payload identity and one valid portal transition.
9. Forget the disposable active session and confirm Azure resources and completed evidence are untouched.

Pass when all recovery actions are explicit, retryable where specified, and cannot restore authorization or duplicate mutation.

## Phase 4: Partial Failure, Cleanup, And Reinstall

Scenarios: D07-D10, L04, R01-R12, R14-R15, and E09-E14.

1. Use `RECOVERY-01` and an approved fault that causes a partial Azure deployment without modifying installer code or package content.
2. Confirm the installer reports a sanitized failure, retains correlation/evidence, and does not emit terminal install success.
3. Resume or enter removal with the same package, sign in again, and run inventory only.
4. Confirm inventory identifies only the dedicated ownership-proven PageMaker365 resource group and records the Key Vault's observed pre-delete disposition.
5. Exercise the planned safety negatives before deletion: incorrect confirmation, foreign/missing ownership tag in a disposable fixture, unexpected contained resource, and active deployment. Each must block without deletion.
6. Restore the known owned state, refresh inventory in the same active removal attempt, approve, type the exact resource group, and run removal.
7. Validate absence and rerun cleanup to prove the already-absent result is idempotent.
8. Confirm SharePoint test content remains and the installer did not request or perform SharePoint mutation.
9. Confirm Key Vault was not purged. Evidence may claim soft-deleted retention only when inventory observed the package-named vault before successful deletion.
10. Attempt a package using the retained vault name and confirm Preflight blocks. Use a newly generated package/vault and complete reinstall.
11. Reconcile removal event ordering, `ra_` attempt identity, retry behavior, exact terminal receipt, and portal status.

Pass when cleanup is ownership-bound, SharePoint is unchanged, Key Vault is unpurged, portal/local/Azure outcomes agree, and the new immutable package installs successfully.

## Phase 5: Three Consecutive Lifecycle Cycles

Scenarios: L02-L03, R03, R09-R15, and the applicable install/validation/evidence scenarios from Phase 2.

Run this table without resetting the workstation between cycles. Use a new package and Key Vault every cycle.

The successful `INSTALL-01` deployment from Phase 2 is the install half of cycle 1; do not deploy that package a second time. Begin this phase by removing and validating the Phase 2 deployment, then run complete install/remove cycles with `INSTALL-02` and `INSTALL-03`.

| Cycle | Install package | Install/validate/finish | Remove/validate/finish | SharePoint marker preserved | Old vault unpurged | Portal terminal states agree |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `INSTALL-01` |  |  |  |  |  |
| 2 | `INSTALL-02` |  |  |  |  |  |
| 3 | `INSTALL-03` |  |  |  |  |  |

Before each install, verify that no previous package, export, resource-group confirmation, approval, token, callback attempt, or Key Vault name is silently reused. After each removal, rerun validation to prove the group is absent and inspect the next Welcome/Package session for stale-state drift.

Pass only when all three cycles complete with unique identities and no naming, ownership, resume, evidence, or portal-state drift.

## Phase 6: Runtime False-Success Tests

Scenarios: V02-V06 and L07-L09.

Using approved disposable endpoints or fault injection, prove independently that Finish remains blocked when:

- API health is unavailable, malformed, or reports unhealthy;
- API product identity is not `PageMaker365`;
- API deployment export ID differs from the active package;
- the portal returns Azure default App Service content;
- a custom domain returns an older PageMaker365 runtime.

Confirm no `runtime_configured`, `smoke_tests_completed`, or `install_completed` event is accepted for a failed deployment identity.

## Phase 7: Security And Evidence Review

Scenarios: E08, T03-T06, and W05.

Execute [assistant-support-handoff.md](assistant-support-handoff.md) against the approved staging portal before approving the assistant and support-handoff boundary.

1. Review callback request captures, outbox files, session state, logs, screenshots, final evidence, support bundles, process command lines, and child-process environment observations available to the test harness.
2. Search for the test-only secret markers, setup one-time code, bearer-token prefix/value, database password/connection value, Entra client secret, runtime session secret, and any prohibited document-content marker.
3. Confirm runtime protected values exist in customer Key Vault and App Service contains only Key Vault references resolved by managed identity.
4. Confirm the support handoff identifies selected artifacts, transfer destination, retention/ownership, and correlation IDs without silently uploading files.
5. Confirm publisher/thumbprint evidence came from the approved release record, not only from values declared inside the ZIP.

Any prohibited match is a failed release gate. Preserve the affected artifact under restricted incident handling and do not attach its contents to a public issue.

## Result Record

Complete one row for every executed scenario:

| Scenario | UTC start/end | Result | Evidence path/link | Installer/Azure/portal correlation | Deviation or issue | Reviewer |
| --- | --- | --- | --- | --- | --- | --- |
| Example: W01 |  | Pass/Fail/Blocked |  |  |  |  |

Allowed results:

- `Pass`: expected behavior and evidence agree.
- `Fail`: observed behavior violates the scenario or security boundary.
- `Blocked`: an external prerequisite prevented execution; this is not a pass.
- `Not run`: outside the approved run scope; this is not a pass.

## Final Reconciliation And Approval

The run is acceptable only when:

- every release-required scenario has `Pass` with evidence;
- CI is green for the exact release commit;
- the signed ZIP passes clean-workstation verification;
- three lifecycle cycles and the partial-failure cycle pass;
- installer, Azure, SharePoint, Key Vault, portal, and evidence outcomes agree;
- security review finds no prohibited data;
- all deviations have linked issues and an explicit release decision;
- the clean operator confirms the customer user guide required no undocumented assistance;
- product, installer engineering, runtime/API engineering, identity/security, and operations/support approvals are recorded.

Link the completed run from issue #10 and `docs/installer-requirements-traceability.md`. Do not mark the customer guides approved until this run and all other publication dependencies pass.

Start each campaign from [results/customer-lifecycle-result-template.md](results/customer-lifecycle-result-template.md). Commit only the sanitized result record; keep screenshots, setup material, raw logs, and protected evidence in the approved evidence system.

Create the corresponding machine-readable result from `docs/testing/results/customer-lifecycle-result.template.json`. Before approval, run:

```powershell
.\scripts\validate-customer-lifecycle-result.ps1 -Path <sanitized-result.json> -RequireApproval
```

Approval requires all policy-defined live scenarios, each repeated-cycle scenario in cycles 1-3, entry gates, package identities, reconciliation checks, security checks, deviation decisions, and named approvals to validate. The canonical policy is `config/customer-lifecycle-acceptance.json`; do not reduce it in an individual campaign.
