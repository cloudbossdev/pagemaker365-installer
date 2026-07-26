# PageMaker365 Installer User Story Catalog

Status: canonical product requirements

Tracking issue: [#3](https://github.com/cloudbossdev/pagemaker365-installer/issues/3)

## Actors

- Customer administrator: owns the Azure subscription and Microsoft 365 tenant.
- Installer operator: runs the guided desktop workflow and records approvals.
- Security reviewer: evaluates requested access, deployed resources, data handling, and evidence.
- PageMaker365 control plane: issues customer-bound packages and records lifecycle evidence.
- PageMaker365 support: receives customer-approved sanitized evidence.
- Test operator: runs repeatable sandbox lifecycle and recovery scenarios.

## US-01 Start Or Resume A Workflow

As an installer operator, I want to start the correct workflow or safely resume an incomplete session so that I do not repeat work or unknowingly reuse stale authorization.

Acceptance criteria:

- The operator can choose install/update or Azure-only removal.
- A saved active session identifies the customer, environment, workflow, last step, and save time.
- The operator can resume, start new, or forget the saved session.
- Resume never restores access tokens, secrets, or destructive approval.
- Completed sessions do not automatically become active sessions.

## US-02 Acquire And Verify The Customer Package

As an installer operator, I want to choose one PageMaker365 setup file and have the installer retrieve and verify the approved customer package so that I do not manually assemble deployment inputs.

Acceptance criteria:

- The normal path requires one setup-file selection.
- The installer connects to the bound onboarding session, waits when generation is pending, and downloads the package when ready.
- Session, tenant, discovery, deployment export, schema, hash, signature, and content type are validated before the package becomes active.
- Expired, reused, malformed, mismatched, or untrusted handoffs remain retryable without terminating the app.
- Local package loading is identified as a controlled support/test path, not the default customer action.

## US-03 Discover Missing Onboarding Information

As an installer operator, I want the installer to collect only missing install-readiness metadata when the portal cannot generate a package so that onboarding can continue without broad tenant export.

Acceptance criteria:

- Discovery appears only when the portal identifies missing onboarding fields and the bootstrap policy allows it.
- Azure and Graph discovery are read-only.
- Discovery excludes secrets, tokens, document content, mailbox content, files, and broad user exports.
- The operator can review discovery status and retry synchronization.
- A newly generated package must still pass the complete US-02 trust contract.

## US-04 Authenticate To The Correct Customer Context

As a customer administrator, I want separate, explicit Azure and Microsoft Graph sign-ins so that each action runs in the intended tenant with the required consent.

Acceptance criteria:

- Azure and Graph status are displayed independently.
- A workflow requiring both services cannot advance after only one successful sign-in.
- Azure tenant/subscription and Graph tenant must match the customer package.
- Device-code sign-in displays a copyable code and expiry without persisting it as session authorization.
- Cancellation, timeout, token expiry, missing consent, and wrong-context results remain visible and retryable.

## US-05 Run Preflight And Resolve Blockers

As an installer operator, I want all deterministic readiness checks completed before preview or mutation so that avoidable failures do not leave partial resources.

Acceptance criteria:

- Package, workstation, Azure, Entra, Graph, SharePoint, quota/capacity, ownership, and Key Vault recovery checks run before deployment.
- Checks distinguish passed, warning, and blocking results.
- Blocking results prevent preview or install as appropriate and provide a corrective action.
- Warning acceptance is explicit and recorded when progression is allowed.
- Rerunning a corrected check does not require restarting the workflow.

## US-06 Preview And Approve Azure Changes

As a customer administrator, I want an Azure What-If preview and explicit approval gate so that I know what the installer will create, modify, retain, or delete.

Acceptance criteria:

- Preview performs no deployment mutation.
- Structured create, modify, deploy, delete, ignore, unknown, and blocked counts are shown and saved.
- Unstructured or incomplete What-If output is a visible warning, not false structured evidence.
- Install requires current preview evidence, approval, and exact resource-group confirmation.
- Approval is cleared after restart or relevant package/preview changes.

## US-07 Perform A Clean Installation

As an installer operator, I want the approved PageMaker365 resources and runtime deployed into the customer subscription so that the customer receives a working environment with auditable evidence.

Acceptance criteria:

- Deployment is bound to the approved package, tenant, subscription, resource group, export, and preview.
- Only the dedicated PageMaker365 resource group is created or adopted under the ownership policy.
- Progress remains visible and duplicate deployment execution is disabled.
- Partial failure records the Azure correlation ID and owned resources without exposing secrets.
- Azure resource completion does not imply runtime or final installation success.

## US-08 Upgrade An Existing Installation

As a customer administrator, I want a supported PageMaker365 version upgrade to preserve customer-owned data and make every infrastructure or configuration change reviewable.

Acceptance criteria:

- Supported installer, package, and runtime source/target versions are explicit.
- Preview distinguishes unchanged, modified, added, retained, and unsupported resources.
- SharePoint content and documented retained customer data are not removed.
- Configuration migration, health validation, failure recovery, and rollback boundaries are defined.
- Unsupported or skipped version transitions fail before mutation.

This story is planned under [#6](https://github.com/cloudbossdev/pagemaker365-installer/issues/6); current install behavior must not be described as a verified upgrade contract.

## US-09 Validate The Deployed Runtime

As an installer operator, I want deployment-bound smoke tests so that an older site, unrelated custom domain, or Azure default page cannot be reported as the new PageMaker365 installation.

Acceptance criteria:

- Validation uses URLs and resource group identity from the current deployment artifact.
- API health returns `ok: true`, `product: PageMaker365`, and the current deployment export ID.
- Portal content identifies PageMaker365 and is not Azure default content.
- Required SharePoint site and library access are validated through the signed-in Graph context.
- Failure blocks runtime configuration and final completion evidence.

## US-10 Finish And Synchronize Lifecycle Evidence

As a customer administrator, I want a final report and reliable portal status so that local records and the customer dashboard accurately describe the outcome.

Acceptance criteria:

- Final success requires verified package, preview, deployment, runtime, and smoke-test evidence.
- The verified customer URL is displayed at completion.
- Lifecycle callbacks use stable event IDs, attempt IDs, idempotency keys, and monotonic sequence.
- Callback data excludes secrets, tokens, raw logs, files, document content, and broad tenant exports.
- Portal or network failure leaves the callback in a retry outbox and does not change the Azure result.

## US-11 Recover From Interruption Or Failure

As an installer operator, I want failed and interrupted actions to recover safely so that I can continue without hidden duplication or unsafe manual cleanup.

Acceptance criteria:

- Every long-running action has active progress, cancellation handling, timeout handling, and a retryable terminal state.
- Resumed state is reconciled against live Azure and portal state before mutation.
- A failed partial installation can enter the ownership-proven removal workflow.
- Rerunning a completed idempotent action reports current state rather than duplicating work.
- Recovery preserves sanitized correlation and evidence history.

## US-12 Troubleshoot And Request Support

As an installer operator, I want clear explanations and a sanitized support package so that I can resolve common blockers or escalate with useful evidence.

Acceptance criteria:

- Guidance identifies the failed step, known cause, and safe next action.
- The assistant is advisory and cannot bypass approval or execute destructive operations.
- Support output includes correlation and evidence references needed for diagnosis.
- Support output excludes prohibited secrets, tokens, raw customer content, and broad tenant data.
- Customer approval is required before handing evidence to PageMaker365 support.

## US-13 Inventory And Approve Azure-Only Removal

As a customer administrator, I want an inventory-first removal preview so that only unambiguously owned PageMaker365 Azure resources can be approved for deletion.

Acceptance criteria:

- Removal is bound to the original package tenant, subscription, resource group, application tag, and deployment export.
- Inventory performs no deletion.
- Ownership tags, every contained resource, and active deployments are checked.
- Missing or conflicting ownership blocks removal without an override.
- Preview explicitly lists removed, retained, skipped, blocked, and ambiguous items.

## US-14 Execute And Verify Azure-Only Removal

As a customer administrator, I want approved resources removed and retained resources documented so that uninstall is deterministic, limited, and auditable.

Acceptance criteria:

- Approval requires a current safe inventory and exact resource-group confirmation.
- Ownership and activity are rechecked immediately before deletion.
- The dedicated resource group is deleted and absence is validated.
- SharePoint content, shared or ambiguous Azure resources, and external customer resources are never modified.
- Key Vault is never purged; soft-deleted recoverability is retained and reported.
- Removal callbacks remain disabled until the hardened contract in issue #9 is implemented.

## US-15 Reinstall After Removal

As a test operator or customer administrator, I want a new immutable deployment package after removal so that reinstallation does not depend on purging or reusing the old Key Vault.

Acceptance criteria:

- The portal generates a new deployment export and install attempt.
- The new package uses a different globally unique Key Vault name.
- A retained old vault is evidence, not a new deployment dependency.
- A package that reuses the old vault name is blocked during preflight.
- Repeated install/remove/reinstall runs preserve correct ownership, evidence, session, and portal state.

## Out Of Scope For V1 Removal

- Deleting or changing SharePoint content, libraries, sites, permissions, or customer data.
- Purging or automatically recovering a soft-deleted Key Vault.
- Deleting shared Azure resources or resources with ambiguous ownership.
- General Entra application, service-principal, or tenant-consent cleanup until immutable ownership IDs are recorded during install.
- Customer DNS, certificate, portal-account, or commercial offboarding outside the dedicated resource group.
