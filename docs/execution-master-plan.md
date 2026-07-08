# PageMaker365 Installer Execution Master Plan

Status: active execution plan.

Last updated: 2026-07-08.

## Purpose

This plan turns the installer backlog into implementation slices that can be run by one coordinating agent plus isolated worker agents. It captures what is already done, what remains, where subagents can work safely, and where customer or business decisions are required.

The working model is:

- The coordinator owns sequencing, integration, tests, commits, and push.
- Workers own isolated slices with disjoint file scopes.
- Explorers perform read-only gap reviews.
- The customer provides tenant access, production decisions, and acceptance review.

## Current Product State

The installer is a functional alpha desktop application. It has a WPF shell, setup/removal entry point, step workflow, package intake, portal bootstrap handling, read-only discovery contracts, package trust validation, assistant workspace scaffold, persistence, support/evidence artifacts, packaging scaffold, CI verification, and tests.

The installer is not yet a production deployment tool. The remaining work is mainly hardening contracts, proving deployment in a sandbox tenant, replacing mock/scaffolded steps with real Azure/Microsoft 365 operations, adding secure secret handling, completing the uninstaller path, and producing a signed customer distribution.

## Confirmed Done

- WPF installer shell with PageMaker365 branding.
- Setup/removal home screen.
- Guided setup workflow: Welcome, Package, Sign In, Preflight, Preview, Install, Validate, Finish.
- Session resume/persistence.
- Customer package loading from local file and sample package.
- Onboarding bootstrap loading.
- Portal-mode onboarding API client for connect, discovery sync, status/readiness, and package download.
- Fail-closed portal response validation for key package/readiness paths.
- Generated package local validation before marking it usable.
- Portal sync receipt artifact.
- Read-only Azure discovery command and integration.
- Read-only Microsoft Graph and SharePoint discovery command and integration.
- Redacted discovery export.
- Package trust metadata fields.
- Package hash computation and mismatch rejection.
- Raw secret container blocking in customer package validation.
- Deployment approval manifest and final evidence scaffolding.
- Bicep root template for App Service based runtime resources.
- PowerShell module scaffolding for preflight, what-if, deployment, smoke tests, and reports.
- Assistant workspace with mock/portal client scaffolding, attachments, transcripts, support ticket draft flow, and approved local actions.
- Package script that publishes the app and copies modules, infra, rules, AI, samples, schemas, and docs.
- CI workflow that verifies and uploads a package artifact.
- Engine and app command tests for package intake, onboarding API, discovery, state, approval/evidence, PowerShell process behavior, and trust validation.

## Main Production Gaps

- Portal backend implementation is outside this repo and still needs final response schemas, package generation, signing, and evidence ingest.
- Customer install package schema still tolerates alpha compatibility.
- Portal error response schemas are not fully pinned.
- Cryptographic signature verification is not implemented.
- Bicep is monolithic and the resource group creation contract is unclear.
- Real sandbox what-if/deploy has not been proven.
- Smoke tests do not yet consume deployment outputs or validate all runtime endpoints.
- Entra app registration, admin consent, and `Sites.Selected` setup are not implemented end to end.
- Secure secret input, Key Vault writes, and Key Vault reference app settings are not implemented.
- Assistant API lacks contract and safety tests.
- Recommended assistant actions should be hardened against unsafe server-provided action definitions.
- Removal/uninstaller flow is mostly a UX and policy placeholder.
- Distribution format, signing scope, release manifest, and clean-machine validation are undecided.

## Workstream Rules

Use subagents when the write set is isolated. Avoid parallel writes to the same XAML, view model, module file, or test runner.

Good parallel lanes:

- Bicep files under `infra/`.
- PowerShell module commands under `modules/PageMaker365.Install/`.
- Engine service/model tests under `tests/PageMaker365.Installer.Engine.Tests/`.
- App view-model or UI tests under `tests/PageMaker365.Installer.App.Tests/`.
- Documentation under `docs/`.
- Packaging/CI under `scripts/` and `.github/workflows/`.

Risky parallel lanes:

- `src/PageMaker365.Installer.App/ViewModels/InstallerWizardViewModel.cs`.
- `src/PageMaker365.Installer.App/MainWindow.xaml`.
- `tests/PageMaker365.Installer.Engine.Tests/Program.cs`.
- `modules/PageMaker365.Install/PageMaker365.Install.psm1`.

Only one worker should own any risky file at a time.

## Execution Sequence

### Phase 1 - Contract Hardening

Goal: make installer/package/portal behavior deterministic before live sandbox deployment.

Owner pattern: one coordinator plus one worker for engine tests and one worker for schema/docs if needed.

#### Slice 1.1 - Bootstrap Policy Enforcement

Status: completed in installer code and app tests.

Scope:

- Enforce `allowedOperations`.
- Enforce `discoveryPolicy.allowPortalSync`.
- Gate discovery, sync, status, and package download based on bootstrap policy.
- Persist visible user-facing errors and portal receipts when policy blocks an action.

Likely files:

- `src/PageMaker365.Installer.App/ViewModels/InstallerWizardViewModel.cs`
- `src/PageMaker365.Installer.Engine/Models/OnboardingBootstrapSession.cs`
- `tests/PageMaker365.Installer.App.Tests/Program.cs`
- `tests/PageMaker365.Installer.Engine.Tests/Program.cs`

Acceptance criteria:

- Installer blocks operations disallowed by bootstrap policy.
- Blocked actions do not call the portal client.
- User gets a clear footer/status message.
- Receipt captures the policy-denied state where applicable.
- Tests cover allowed and denied operations.

Needs customer: no.

#### Slice 1.2 - Runtime Schema Validation

Status: completed in engine runtime validation, status schema, sample schema verification, and engine tests.

Scope:

- Add runtime validation for bootstrap session, onboarding status/readiness, and customer install package.
- Add schema or contract tests for malformed status/readiness responses.
- Keep alpha compatibility explicit and visible.

Likely files:

- `schemas/`
- `src/PageMaker365.Installer.Engine/Services/`
- `tests/PageMaker365.Installer.Engine.Tests/Program.cs`
- `docs/onboarding-discovery-contract.md`

Acceptance criteria:

- Malformed bootstrap/status/package payloads fail before mutating installer state.
- Status response schema requires session match, correlation ID, package readiness, and package URL when ready.
- Generated package schema validation runs before package trust validation.

Needs customer: no, unless portal agent has final schema decisions ready.

#### Slice 1.3 - Package Provenance Binding

Status: completed in engine download validation, app generated-package validation, and focused engine/app tests.

Scope:

- Bind generated package metadata to onboarding session, discovery ID, deployment export ID, and tenant.
- Fail when generated package clearly belongs to a different session/customer/tenant.

Likely files:

- `src/PageMaker365.Installer.Engine/Services/CustomerConfigService.cs`
- `src/PageMaker365.Installer.App/ViewModels/InstallerWizardViewModel.cs`
- `tests/PageMaker365.Installer.App.Tests/Program.cs`
- `tests/PageMaker365.Installer.Engine.Tests/Program.cs`

Acceptance criteria:

- A downloaded package with mismatched onboarding session or tenant is rejected.
- Receipt records the mismatch without exposing secrets.
- Tests cover valid, mismatched session, mismatched tenant, and missing metadata cases.

Needs customer: no for installer enforcement. Portal package generation must continue returning the required metadata fields.

### Phase 2 - Deployment Infrastructure

Goal: make Azure deployment behavior explicit, modular, testable, and sandbox-ready.

Owner pattern: one Bicep worker, one PowerShell worker, coordinator integrates.

#### Slice 2.1 - Runtime Hosting Decision

Status: completed in deployment contract and backlog documentation.

Decision: Linux App Service is the selected v1 runtime hosting model for the first production/customer pilot build. Static Web Apps plus API App Service and Container Apps are deferred alternatives.

Reason: Linux App Service gives customer IT teams a simpler first-version operating model, aligns with the current Bicep direction, supports straightforward Key Vault managed identity integration, and makes Application Insights, App Service, and deployment outputs easier to consume in installer evidence and smoke tests. It also avoids introducing Container Apps or split static/API hosting operations before the first pilot deployment path is proven.

Scope:

- Document App Service as the first production hosting model.
- Update backlog/docs to stop treating this as undecided.
- Capture what would trigger a later move to Container Apps.

Likely files:

- `docs/deployment-contract.md`
- `docs/implementation-backlog.md`

Acceptance criteria:

- Deployment contract states App Service as the selected v1 hosting model.
- Bicep expectations and package resource names align with that decision.

Needs customer: none for this slice. The v1 decision is now recorded.

#### Slice 2.2 - Bicep Modularization And Naming Validation

Status: completed in code and documentation.

Decision: v1 deployments require a pre-existing customer resource group. The installer keeps a `resourceGroup` scoped Bicep entry point and does not attempt subscription-scope resource group creation in this slice.

Scope:

- Split `infra/main.bicep` into modules.
- Add Azure name length/character constraints where Bicep can enforce them.
- Add explicit outputs required by install evidence and smoke tests.
- Decide resource group behavior: pre-existing resource group versus subscription-scope creation.

Likely files:

- `infra/main.bicep`
- `infra/modules/*.bicep`
- `modules/PageMaker365.Install/Private/New-PM365TemplateParameterObject.ps1`
- `modules/PageMaker365.Install/Public/Invoke-PM365BicepBuild.ps1`
- `scripts/verify.ps1`

Acceptance criteria:

- `Invoke-PM365BicepBuild` passes locally.
- Bicep outputs include API URL, portal URL, Key Vault URI, resource IDs, managed identity principal ID, telemetry references, and storage ID.
- Resource group behavior is unambiguous in preflight and deployment.

Needs customer: none for this slice. Sandbox credentials are needed for Slice 2.3.

#### Slice 2.3 - Sandbox What-If

Status: blocked by sandbox readiness. See `docs/sandbox-whatif-readiness.md`.

Scope:

- Use a real sandbox package.
- Run what-if against a sandbox subscription.
- Save and review redacted evidence.
- Document expected warnings or blockers.

Acceptance criteria:

- `Invoke-PM365WhatIf` succeeds against sandbox.
- What-if output is saved and redacted.
- Any blockers become concrete backlog items.

Needs customer: create the target sandbox resource group and provide a real CloudBoss installer package contract from the portal `install-package` endpoint.

### Phase 3 - Entra And SharePoint

Goal: make Microsoft 365 consent explicit, auditable, and least-privilege.

Owner pattern: one PowerShell worker, one docs/tests worker.

#### Slice 3.1 - App Registration Mode

Scope:

- Decide create versus reuse.
- Decide one app registration versus separate API/portal registrations.
- Finalize required delegated scopes and application permissions.
- Update package contract fields.

Acceptance criteria:

- Installer can explain exactly what admin consent is needed.
- Package can describe create/reuse mode.
- Preflight detects missing app registration inputs.

Needs customer: app registration strategy.

#### Slice 3.2 - Consent Verification

Scope:

- Add commands to verify admin consent and required Graph permissions.
- Detect whether current admin role can grant or verify consent.
- Surface actionable guidance.

Acceptance criteria:

- Installer can prove consent exists or explain what is missing.
- No consent is granted without explicit admin action.

Needs customer: sandbox admin account.

#### Slice 3.3 - `Sites.Selected` SharePoint Flow

Scope:

- Verify target SharePoint site and library.
- Add selected-site permission grant or verification flow.
- Keep broad SharePoint permissions out of the default path.

Acceptance criteria:

- Runtime app has access only to the intended SharePoint site.
- Installer validates the selected site/library before deployment.

Needs customer: final preferred SharePoint permission model.

### Phase 4 - Secrets And Runtime Configuration

Goal: configure runtime secrets without ever placing raw secrets in packages, logs, or support bundles.

Owner pattern: one UI/view-model worker, one PowerShell/engine worker, coordinator reviews redaction.

#### Slice 4.1 - Secret Prompt Model And UI

Scope:

- Define required/generated/supplied secret prompts from package.
- Add secure secret entry UI.
- Avoid persistence of raw values.

Acceptance criteria:

- Secret prompts render from the package.
- Raw values are never written to state, logs, evidence, or support bundle.
- Tests cover redaction and state persistence.

Needs customer: final required secret list from runtime app.

#### Slice 4.2 - Key Vault Secret Write And App Settings

Scope:

- Write generated/supplied secrets to customer Key Vault.
- Configure runtime app settings as Key Vault references where supported.
- Verify managed identity can read required references.

Acceptance criteria:

- Runtime app starts using Key Vault backed settings.
- No raw secret appears in deployment artifacts.

Needs customer: sandbox Key Vault/RBAC validation.

### Phase 5 - Deployment, Validation, And Evidence

Goal: run a complete install path in a sandbox tenant and produce customer-supportable evidence.

Owner pattern: coordinator drives sandbox run; subagents can harden smoke tests and evidence parser.

#### Slice 5.1 - Deployment Output Bridge

Scope:

- Persist deployment outputs into install evidence.
- Make validation prefer deployment outputs over package custom domain guesses.
- Feed smoke tests from deployment outputs.

Acceptance criteria:

- Validation uses actual deployed URLs and resource IDs.
- Evidence records deployment operation IDs and output summary.

Needs customer: no for local mocked tests; yes for live sandbox proof.

#### Slice 5.2 - Smoke Test Expansion

Scope:

- API health endpoint.
- Portal load.
- SharePoint site/library resolution.
- Key Vault managed identity access.
- License validation endpoint.
- Entitlement sync endpoint.
- Application Insights telemetry event check.

Acceptance criteria:

- Smoke tests produce structured pass/fail results.
- Blocking failures stop final evidence from marking deployment successful.

Needs customer: actual runtime endpoints and sandbox deployment.

#### Slice 5.3 - Final Evidence Upload Contract

Scope:

- Define portal evidence ingest payload.
- Submit redacted final evidence to portal when configured.
- Keep local support bundle as fallback.

Acceptance criteria:

- Evidence can be attached to customer record.
- Correlation IDs link portal, installer, deployment, and support bundle.

Needs customer: portal endpoint from portal agent.

### Phase 6 - AI Deployment Assistant

Goal: make assistant helpful while keeping it advisory and non-destructive.

Owner pattern: one engine-test worker, one app/UX worker after safety tests exist.

#### Slice 6.1 - Assistant Contract Tests

Scope:

- Test portal message request/response.
- Test attachment upload metadata.
- Test support ticket draft.
- Test auth headers.
- Test fallback and no-fallback behavior.
- Test max attachment enforcement.

Acceptance criteria:

- Assistant portal mode has automated contract tests for all documented endpoints.
- Failures are visible and do not run local commands.

Needs customer: no.

#### Slice 6.2 - Diagnostics Redaction And Attachment Policy

Scope:

- Prove assistant payloads omit secrets, bearer tokens, connection strings, and local paths.
- Decide whether text/log attachments are redacted locally before upload.
- Keep screenshot/file upload explicit and human initiated.

Acceptance criteria:

- Tests include secrets, tokens, local paths, and connection strings.
- Redaction behavior matches documented policy.

Needs customer: attachment policy decision.

#### Slice 6.3 - Recommended Action Hardening

Scope:

- Add local action registry.
- Local labels, descriptions, enabled states, and approval requirements override server risk settings.
- Unknown actions never execute.

Acceptance criteria:

- Server cannot downgrade an approval-required action.
- Unknown or duplicated action IDs are ignored.
- Risky actions require explicit local approval.

Needs customer: no.

### Phase 7 - Removal/Uninstaller

Goal: provide a trustworthy removal path that inventories before removing and never deletes ambiguous customer resources.

Owner pattern: one policy/docs worker, one PowerShell worker after policy is approved.

#### Slice 7.1 - Removal Policy

Scope:

- Define what can be removed automatically.
- Define what requires explicit approval.
- Define what is never removed.
- Define retained evidence and logs.

Acceptance criteria:

- Removal policy is visible in app/docs.
- Installer refuses removal without inventory and approval.

Needs customer: policy approval.

#### Slice 7.2 - Removal Inventory And Preview

Scope:

- Discover PageMaker365-owned resources by tags/export IDs.
- Show removal plan.
- Save preview artifact.

Acceptance criteria:

- Preview identifies resources without deleting anything.
- Ambiguous resources are flagged for manual review.

Needs customer: sandbox removal target.

#### Slice 7.3 - Approved Cleanup And Validation

Scope:

- Remove only approved resources.
- Validate cleanup.
- Generate final removal report.

Acceptance criteria:

- Removal report lists removed, retained, skipped, and failed items.
- Cleanup is idempotent and safe to retry.

Needs customer: sandbox cleanup permission.

### Phase 8 - Packaging, Signing, And Release

Goal: ship a customer-ready installer distribution.

Owner pattern: one packaging worker, one CI worker, coordinator verifies locally.

#### Slice 8.1 - Distribution Format Decision

Recommended decision: start with a signed ZIP or setup bootstrapper for alpha/customer pilots, then evaluate MSI/MSIX after install behavior stabilizes.

Scope:

- Decide signed ZIP, MSI, MSIX, or setup bootstrapper.
- Decide whether samples ship in customer package.
- Decide signing scope.

Acceptance criteria:

- Distribution format is documented.
- Package contents are deterministic and intentional.

Needs customer: distribution/signing decision.

#### Slice 8.2 - App Metadata And Icon

Scope:

- Configure executable icon.
- Add product, company, file version, informational version, and copyright metadata.
- Add release notes template.

Acceptance criteria:

- Windows file properties show PageMaker365 metadata.
- Executable icon renders correctly.

Needs customer: no, unless final icon/copyright wording changes.

#### Slice 8.3 - Signing And Release CI

Scope:

- Sign relevant binaries/package.
- Verify signatures.
- Produce checksum/manifest.
- Publish retained artifact or release package.

Acceptance criteria:

- Signed package verifies on a clean Windows 11 machine.
- Release artifact includes version, manifest, checksum, and release notes.

Needs customer: code-signing certificate and CI secret strategy.

## Subagent Backlog

These are safe future subagent assignments:

1. Bicep worker: modularize `infra/main.bicep` and add missing outputs.
2. Engine worker: enforce bootstrap policy and add tests.
3. Assistant worker: add assistant API contract tests.
4. Packaging worker: configure app metadata/icon and release manifest.
5. PowerShell worker: expand smoke tests to consume deployment outputs.
6. Docs worker: update portal handoff prompts for the other app agent.

Do not assign two workers to `InstallerWizardViewModel.cs`, `MainWindow.xaml`, or `tests/PageMaker365.Installer.Engine.Tests/Program.cs` at the same time.

## Customer Decisions Needed

These decisions should be made before the related phase becomes blocking:

1. Portal auth model: bearer key plus one-time code for alpha, or short-lived session token/device flow for production.
2. Final portal package schema and alpha compatibility cutoff.
3. App registration strategy: create, reuse, single app, or separate portal/API apps.
4. SharePoint permission model: confirm `Sites.Selected` as default.
5. Required runtime secrets and which are generated versus customer supplied.
6. Assistant endpoint and attachment policy.
7. Removal policy: auto-remove, approval-required, and never-remove categories.
8. Distribution format and code-signing certificate strategy.

## Recommended Next Coding Slice

Start with Phase 2, Slice 2.3: Sandbox What-If.

Reason:

- Phase 2.2 is complete: Bicep is modularized, resource name validation is wired into PowerShell, and v1 resource group behavior is explicit.
- The next blocker is proving the deployment plan against a real sandbox subscription.
- Phase 2.3 is currently blocked because the target sandbox resource group does not exist and no real CloudBoss installer package contract exists locally.

Suggested subagent use:

- Sandbox runner executes `Invoke-PM365WhatIf` with the generated portal package and captures redacted evidence.
- Reviewer analyzes the what-if artifact for blocked deletes, unexpected modifications, missing providers, policy denials, or permission gaps.
- Coordinator turns findings into backlog items before moving to deployment execution.

Definition of done:

- `Invoke-PM365WhatIf` runs against a real sandbox subscription.
- Redacted what-if evidence is saved in the support bundle.
- Any sandbox blockers are documented as concrete backlog items.
