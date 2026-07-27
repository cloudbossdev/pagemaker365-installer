# PageMaker365 Installer User Guide

Status: controlled draft; not approved for customer publication

Tracking issue: [#11](https://github.com/cloudbossdev/pagemaker365-installer/issues/11)

Document revision: 2026-07-27

Release version: not assigned

## Purpose And Current Boundary

This guide is for an authorized customer operator who installs, validates, removes, or reinstalls PageMaker365 with the Windows desktop installer. The normal workflow does not require the operator to run deployment scripts.

The current alpha can provision the Azure foundation and protected runtime configuration, but the production PageMaker365 API and portal application artifacts are not yet available from this repository. A customer installation must not be represented as complete until the released runtime passes deployment-bound validation and the installer displays its verified URL. Upgrade is not a supported customer workflow until issue [#6](https://github.com/cloudbossdev/pagemaker365-installer/issues/6) completes its control-plane and staging gates.

## Before You Begin

Confirm all of the following before opening the installer:

- Use a supported Windows 11 workstation approved by your organization.
- Obtain the signed PageMaker365 Installer ZIP and its SHA-256 file through the approved delivery channel.
- Obtain a current PageMaker365 setup file for the intended customer and environment. Treat this file as sensitive because it contains a short-lived one-time onboarding code.
- Use an Azure account in the package tenant with `Owner`, or `Contributor` plus either `Role Based Access Control Administrator` or `User Access Administrator`, at the target subscription.
- Use a Microsoft 365 account in the same tenant that can complete the requested Microsoft Graph consent.
- Allow HTTPS access to the PageMaker365, Microsoft identity, Microsoft Graph, Azure management, customer SharePoint, and deployed application destinations listed in the technical and security guide.
- Close or complete any other installer session on the workstation so the saved-session prompt is unambiguous.

Do not email the setup file in an unprotected thread, commit it to source control, paste it into chat, or include it in a support bundle.

## Verify And Start The Installer

1. Place the ZIP, its `.sha256` file, and the PageMaker365 verifier in an approved local folder.
2. Follow [Installer Distribution Verification](installer-distribution-verification.md). Verification must pass the archive checksum, exact inventory, file hashes, release-manifest signature, and official publisher/certificate checks.
3. Do not launch an unsigned development build for a customer installation.
4. Open `app\PageMaker365.Installer.exe` from the verified extracted folder.
5. At Welcome, choose **Deploy PageMaker365** for a new install or **Clean up PageMaker365** for Azure-only removal.

## Start, Resume, Or Forget A Session

When the installer finds an active saved session:

- Choose **Resume Session** only when the displayed customer and workflow match the work you intend to continue.
- Choose **Start New** to leave the saved record untouched and begin a different workflow.
- Choose **Forget Saved Session** only when the displayed session is no longer needed. This deletes local resumable state; it does not delete Azure resources, SharePoint content, or completed evidence.

After resume, sign in again when requested. Azure and Graph tokens, protected runtime values, approval checkboxes, and typed destructive confirmations are never restored from saved state.

## Install PageMaker365

### 1. Get The Customer Package

1. On Package, choose the PageMaker365 setup file once.
2. The installer connects to the approved onboarding API, checks readiness, waits when package generation is still running, downloads the customer package, and validates it locally.
3. Continue only when the package is loaded for the expected customer, tenant, subscription, resource group, SharePoint site, and environment.
4. Open **Technical details** when support asks for the session ID, deployment export ID, package hash, or trust result.

Expected trust for a production package is `Verified`. Stop when the package is expired, mismatched, malformed, unsigned when signing is required, or bound to another onboarding session. Obtain a new setup file rather than editing the package.

The installer automatically retries a short-lived portal rate limit or service interruption while Package remains busy. If the bounded retry window is exhausted, the step stays retryable and shows a sanitized error; do not repeatedly click the action or restart the application. Authorization, package identity, and trust failures are not automatically retried.

If the portal reports missing onboarding fields, the installer may offer a read-only discovery recovery flow. Complete the requested Azure and Graph sign-ins, run discovery, synchronize the restricted readiness snapshot, and retry package generation. Discovery does not read document, mailbox, or file content.

### 2. Sign In To Both Services

1. Choose **Sign In Azure** and complete sign-in to the package tenant and subscription.
2. Choose **Sign In Graph**. Copy the displayed device code, enter it only in the Microsoft sign-in window opened for this attempt, and complete consent with the intended tenant account.
3. Confirm that both Azure and Microsoft Graph show `Signed in` before continuing.

Sign-in order does not matter. If either sign-in is canceled, expires, or uses the wrong tenant, retry that sign-in. A stale device code is cleared and must not be reused.

### 3. Run Preflight

1. Choose **Run Preflight**.
2. Keep the installer open while the activity indicator is running.
3. Review every result:
   - `Passed` means the check met its current requirement.
   - `Warning` requires review but may permit the workflow to continue under the displayed policy.
   - `Blocked` or `Failed` stops Preview until the cause is corrected and Preflight is rerun.
4. Correct blockers using the result's next action or the approved administrator message. The installer does not grant itself Azure roles or Microsoft consent.

Preflight checks tooling, package fields, Azure context and roles, provider registration, App Service SKU and quota signals, Key Vault recovery state, Graph scopes, and access to the configured SharePoint site and library. Passing App Service readiness does not reserve regional capacity; Azure can still return a retryable allocation failure during deployment.

### 4. Review Deployment Preview

1. Choose **Run Deployment Preview**.
2. Review the Azure What-If counts and each warning before approval.
3. Confirm that the target subscription and resource group match the package.
4. Do not approve unexpected deletes, unknown changes, ownership warnings, or a target change.

Changing or replacing the package or target invalidates the previous preview and approval. Run Preview again after any such change.

### 5. Approve And Run Install

1. Review the preview acknowledgement.
2. Enter each protected runtime value requested by the verified package. Values are held only for the current attempt and are cleared when the attempt ends or the installer closes.
3. Select the approval checkbox.
4. Type the target resource-group name exactly.
5. Choose **Run Install** once and keep the installer open while progress is active.

The installer deploys only the package-named PageMaker365 resources. A successful Azure deployment is not final customer success; runtime configuration and validation must also pass.

For a transient App Service capacity conflict, retain the evidence and retry only after Azure indicates capacity is available or a newly approved package targets another supported region/resource group. Do not rename signed package resources manually.

### 6. Validate The Runtime

1. Choose **Run Validation**.
2. Keep the installer open while smoke tests run.
3. Continue only when the deployed API reports the expected PageMaker365 product and deployment identity, the portal returns PageMaker365 content rather than the Azure default page, and the configured SharePoint target is accessible.
4. Treat HTTP 200 by itself as insufficient. An older custom domain, unrelated application, wrong deployment export, or Azure default page must fail validation.

### 7. Finish And Open The Site

1. Choose **Create Final Evidence** after validation passes or after reviewed warnings permitted by policy.
2. Record the verified customer URL displayed by the installer.
3. Open the URL and confirm it matches the customer and environment.
4. Retain the final report, manifest, and evidence ZIP according to your organization's policy.
5. If Current Session reports pending portal sync, choose **Retry Portal Sync**. A sync failure does not change a successful Azure result and must not trigger a second deployment.

## Understand Progress, Warnings, And Failures

- A running animation means the current command is active. Its button remains unavailable to prevent duplicate execution.
- A warning is not a silent pass. Review its details and evidence before continuing.
- A blocker prevents the next mutation boundary. Correct the cause and rerun the current check.
- Canceling a non-destructive action returns the step to a retryable state.
- Closing the app never preserves authentication, protected runtime values, or destructive approval.
- Never use **Continue** or a later step to bypass an incomplete Package, Sign In, Preflight, Preview, Install, or Validate gate.

## Recover From A Partial Or Interrupted Install

1. Reopen the same installer build and choose **Resume Session** when the saved customer and package are correct.
2. Sign in to Azure and Graph again.
3. Rerun inventory or Preflight so the installer reconciles live Azure state.
4. Retry only when the UI identifies the operation as retry-safe and the package identity is unchanged.
5. If ownership is ambiguous or cleanup is required, switch to the removal workflow and use the original customer package. Do not delete individual resources by guesswork.
6. Create a support bundle before external escalation when the installer cannot establish a safe next action.

## Upgrade Contract Under Acceptance

The installer-side upgrade contract is implemented on the issue #6 branch but is not yet a supported customer workflow. Signed upgrade packages identify source and target runtime versions; unsupported, stale, mismatched, or changed package/preview identities stop before mutation. Azure What-If and explicit approval remain required, customer SharePoint content and Key Vault values are preserved, and partial target-state recovery is restricted to the original package-bound saved session.

Do not use this section as upgrade authorization. Customer instructions will be added only after the control plane generates the signed contract, the portal accepts exact lifecycle receipts, and patch/minor, preservation, interruption, and outbox behavior pass staging acceptance.

## Remove PageMaker365 Azure Resources

Removal is Azure-only. It does not delete or modify SharePoint content, lists, libraries, pages, or documents.

1. At Welcome, choose **Clean up PageMaker365**.
2. Load the original package for the deployment being removed.
3. Sign in to the package Azure tenant and subscription.
4. Run removal inventory and review the exact resource group, ownership tags, resources, active deployments, and retained items.
5. Stop if the group is foreign-owned, contains an unexpected resource, has an active deployment, or cannot be matched to the package.
6. Approve removal and type the resource-group name exactly.
7. Run removal once and wait for Azure deletion to finish.
8. Run removal validation. An already-absent resource group is a successful idempotent result.
9. Create final removal evidence and retry portal sync separately when needed.

The installer never purges Key Vault. When inventory proves the package-named vault existed before successful resource-group deletion, Azure retains it in soft-deleted state for the configured retention period. If the vault was never created or its prior state cannot be proven, evidence does not invent a retained-vault claim.

## Reinstall After Removal

1. Obtain a new setup file and newly generated customer package.
2. Confirm that the package has a new deployment export and a new globally unique disposable Key Vault name.
3. Start a new install session rather than resuming the completed removal session.
4. Repeat Package through Finish.

Do not reuse the old Key Vault name while it remains soft-deleted. Preflight blocks the collision. Existing SharePoint content remains in SharePoint and can be used by the newly installed runtime after validation.

## Evidence And Local Data

Default local locations include:

| Location | Contents |
| --- | --- |
| `%LOCALAPPDATA%\PageMaker365\Installer\sessions\` | Resumable sanitized session state and callback outbox. |
| `{installer workspace}\logs\` | Structured local operation logs. |
| `{installer workspace}\support-bundle\preview` | Deployment preview evidence and Azure What-If artifact. |
| `{installer workspace}\support-bundle\install` | Deployment and runtime-configuration evidence. |
| `{installer workspace}\support-bundle\validate` | Smoke-test evidence. |
| `{installer workspace}\support-bundle\remove` | Removal inventory, execution, and validation evidence. |
| `{installer workspace}\support-bundle\final` | Final report, manifest, and evidence ZIP. |

The installer does not automatically delete these customer-controlled files. Apply your organization's retention and secure-deletion policy after audit and support needs are complete. **Forget Saved Session** removes the selected resumable state only.

## Request Support

1. Select the failed or warning result and choose **Explain Issue** when available.
2. Use **Generate Admin Message** for an administrator request that contains the sanitized action and correlation context.
3. Choose **Create Support Bundle** only after reviewing the proposed handoff scope.
4. Never add setup files, secret values, tokens, document content, or unrelated tenant exports to the bundle.
5. Provide the installer version, customer/environment label, session ID, deployment export ID, event or correlation ID, failed step, time, and sanitized support-bundle path through the approved support channel.

Support guidance cannot bypass package verification, sign-in, approval, typed confirmation, ownership checks, or any destructive control.

## Assistant And Support Handoff

The assistant can explain evidence, prepare an administrator message, and create a reviewable support-ticket draft. It cannot install, remove, grant consent, mutate Azure, submit a final ticket, or lower any local approval requirement.

- Portal recommendations are matched to the installer's local action registry. Unknown or duplicate actions are ignored, and the local label and approval requirement remain authoritative.
- Attachments remain local by default. Explicit handoff consent permits only a redacted text copy with an approved `.txt`, `.log`, `.json`, or `.md` type and size.
- Screenshots, executables, archives, and other binary attachments are local-only and are not represented as remote ticket attachments.
- The portal receives an opaque attachment name, recalculated size/hash, sanitized operator text, and selected diagnostic context. It does not receive local paths or the original filename.
- A successful portal response creates status `Drafted`. Review the draft in the approved portal process before any separate final submission.
- Authentication, authorization, validation, contract, and cancellation failures remain failures. They are not replaced by local mock success.

Do not enable attachment handoff until the portal retention period, deletion authority, audit trail, and final ticket-submission owner have been approved for the release.

## Frequently Asked Questions

**Why do I sign in twice?**
Azure authentication controls the subscription deployment. Microsoft Graph authentication performs read-only Entra and SharePoint readiness checks. Both must match the package tenant.

**Why did Preflight pass but Azure later report no App Service capacity?**
Preflight checks provider, SKU, and quota signals but cannot reserve regional capacity. Retain the evidence and follow the retry guidance displayed by the installer.

**Does uninstall remove SharePoint data?**
No. Removal deletes only the dedicated, ownership-proven PageMaker365 Azure resource group. SharePoint continues to operate normally.

**Why can I not reuse the old Key Vault name?**
The installer never purges Key Vault. Azure reserves a soft-deleted vault name until the vault is recovered, separately purged under an approved process, or its retention period expires.

**Can I edit the package to fix a name or setting?**
No. Editing invalidates the approved hash/signature and provenance. Request a newly generated package.

## Publication Gates

Before this controlled draft becomes customer-facing, it still requires:

- verified production runtime deployment and customer URL behavior from issue #5;
- a supported upgrade decision and instructions from issue #6;
- live protected runtime configuration evidence from issue #7;
- live removal callback acceptance from issue #9;
- clean-workstation and repeated lifecycle results from issue #10;
- approved support-handoff behavior from issue #28;
- production certificate identity, signed release, sanitized release-candidate screenshots, and clean launch evidence from issue #13;
- clean-operator walkthrough and recorded product, engineering, security, and operations approvals.
