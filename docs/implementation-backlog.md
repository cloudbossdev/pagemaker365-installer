# Installer Implementation Backlog

Status: active backlog. See `docs/execution-master-plan.md` for execution order, subagent lanes, dependencies, and customer decisions.

Last updated: 2026-07-08.

## Milestone 1 - Deployment Contract

Goal: make the customer install package deterministic enough for the installer and runtime agents.

- [x] Define onboarding bootstrap session contract.
- [x] Define tenant discovery result contract.
- [x] Add mock onboarding API client.
- [x] Add mock discovery payload generation.
- [x] Add redacted local discovery export.
- [x] Document installer/control-plane/customer-runtime boundary.
- [x] Add first customer install package schema.
- [x] Add PowerShell contract preflight check.
- [x] Add production PageMaker365 onboarding API client.
- [x] Add read-only Azure discovery command.
- [x] Add read-only Microsoft Graph tenant/domain discovery command.
- [x] Add read-only SharePoint site/library discovery command.
- [ ] Align control-plane deployment export with `schemas/customer-install.schema.json`.
- [x] Add installer-side package hash validation.
- [x] Add signed export metadata fields and immutable export ID to the installer contract.
- [ ] Add package hash generation in the control plane export.
- [ ] Add cryptographic signature verification.
- [x] Enforce bootstrap operation policy in installer commands.
- [x] Add runtime schema validation for bootstrap, readiness/status, and generated packages.
- [x] Bind generated package provenance to onboarding session, discovery, tenant, and export metadata.

Acceptance criteria:

- The other app agent can generate a package without relying on free-form notes.
- The installer can warn on missing optional contract fields.
- The installer fails when raw secret values are present.
- The installer fails when a declared package hash does not match the local package contents.

## Milestone 2 - Production Bicep

Goal: replace the placeholder Bicep entry point with deployable customer runtime infrastructure.

- [x] Confirm Linux App Service as the v1 runtime hosting model or choose an alternative.
- [x] Add resource modules for Key Vault, Log Analytics, Application Insights, storage, managed identity, runtime API host, and runtime frontend host.
- [x] Add naming rules and Azure name validation.
- [x] Add tags from the customer package.
- [x] Add deployment outputs consumed by smoke tests.
- [x] Decide whether installer creates the resource group or requires a pre-existing resource group.
- [ ] Run `Invoke-PM365WhatIf` against a real sandbox subscription.
  - Blocked until `rg-pagemaker365-cloudboss-sandbox` exists.
  - Blocked until a real CloudBoss `customer-install` package is downloaded from the portal `install-package` endpoint.

Acceptance criteria:

- Bicep builds locally.
- What-if succeeds in sandbox.
- Outputs include URLs, resource IDs, Key Vault URI, and managed identity principal ID.

## Milestone 3 - Entra And SharePoint Consent

Goal: make customer administrator consent explicit and verifiable.

- [ ] Finalize app registration mode: create or reuse.
- [ ] Finalize Graph application permissions and delegated scopes.
- [ ] Decide `Sites.Selected` flow and required admin actions.
- [ ] Add app registration creation/reuse command.
- [ ] Add admin consent verification command.
- [ ] Add selected SharePoint site permission verification command.

Acceptance criteria:

- Installer can prove required consent exists after admin approval.
- Installer can prove runtime app has access only to the intended SharePoint site when `Sites.Selected` is used.

## Milestone 4 - Secrets And App Settings

Goal: configure the runtime app without writing secrets into logs or control-plane exports.

- [ ] Define required secret prompts.
- [ ] Add secure secret input UI.
- [ ] Write supplied/generated secrets to customer Key Vault.
- [ ] Configure runtime app settings as Key Vault references where supported.
- [ ] Redact all secret names and values from logs according to policy.

Acceptance criteria:

- No raw secret appears in deployment package, logs, report, or support bundle.
- Runtime app can start with Key Vault backed settings.

## Milestone 5 - Deployment And Smoke Tests

Goal: prove the complete install path in a sandbox tenant.

- [ ] Run preflight with a real package.
- [ ] Run what-if.
- [ ] Deploy infrastructure.
- [ ] Configure app settings.
- [ ] Run database/runtime migrations if required.
- [ ] Run smoke tests.
- [ ] Generate deployment evidence.
- [ ] Produce support bundle.

Acceptance criteria:

- Sandbox runtime is reachable.
- Runtime license and entitlement sync pass.
- SharePoint access is scoped and validated.
- Evidence can be attached to the customer record.

## Milestone 6 - AI Diagnostics

Goal: provide operator help without allowing unsafe automation.

- [ ] Decide AI endpoint: PageMaker365 backend or approved local diagnostic endpoint.
- [ ] Send only redacted diagnostic payloads.
- [ ] Map known errors to approved remediation playbooks.
- [ ] Generate customer admin message drafts.
- [ ] Add evaluation cases for wrong tenant, missing RBAC, missing Graph consent, SharePoint denial, and deployment failure.
- [ ] Add assistant API contract tests.
- [ ] Harden recommended action allowlist and approval requirements.

Acceptance criteria:

- AI cannot run shell commands or modify Azure, Entra, or SharePoint.
- AI output is advisory and traceable to known evidence.

## Milestone 7 - Package And Sign

Goal: produce a customer-ready installer distribution.

- [x] Add package script scaffold.
- [ ] Select distribution format: signed ZIP, MSIX, MSI, or setup bootstrapper.
- [ ] Acquire production code-signing certificate.
- [ ] Sign executable and package.
- [ ] Add version metadata and release notes.
- [ ] Test install on a clean Windows 11 workstation.

Acceptance criteria:

- Installer launches on a clean workstation.
- SmartScreen/signing behavior is acceptable for customer delivery.
- Package includes app, modules, Bicep, rules, AI policy files, and README.
