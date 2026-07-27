# PageMaker365 Installer

[![CI](https://github.com/cloudbossdev/pagemaker365-installer/actions/workflows/ci.yml/badge.svg)](https://github.com/cloudbossdev/pagemaker365-installer/actions/workflows/ci.yml)

This repository contains the functional alpha build of the PageMaker365 Windows desktop installer.

The installer is intended to provide a polished customer-facing setup experience while keeping deployment logic deterministic and auditable.

```text
Desktop UI guides the customer.
Installer engine manages state, logs, and step execution.
PowerShell and Bicep perform the real install work.
AI explains failures and suggests safe next actions.
```

## Current Status

This is a functional alpha intended for controlled sandbox installation and removal testing.

Implemented:

- WPF desktop app shell.
- Reusable installer engine project.
- Customer install config model.
- Sample customer package.
- Desktop Azure and Microsoft Graph device-code sign-in.
- Deployment contract documentation and package schema.
- Deployment contract preflight check.
- Onboarding bootstrap session contract.
- Tenant discovery result contract.
- Mock PageMaker365 onboarding API client.
- Mock tenant discovery payload generation.
- Redacted local tenant discovery export.
- Fail-closed portal onboarding client with response, error, and package download validation.
- Customer package export metadata, SHA-256 hash, and Ed25519 signature validation.
- One-action portal setup-file handoff, package polling, download, and validation.
- Hardened installer lifecycle evidence callbacks with an offline retry outbox.
- Read-only Azure, Microsoft Graph, and SharePoint discovery commands.
- Discovery command contract tests with mockable Azure and Graph contexts.
- Local active-session resume state.
- PowerShell-backed local prerequisite checks.
- Azure context validation.
- Azure RBAC validation scaffold.
- Microsoft Graph / Entra scope and admin-role readiness checks.
- SharePoint URL, site, and document-library readiness checks.
- Bicep template build check.
- Subscription-scope Bicep deployment, Azure what-if, and deployment evidence.
- Deployment-bound runtime and portal identity smoke-test contracts.
- Dedicated resource-group removal with ownership, tenant, subscription, and active-deployment guards.
- Key Vault soft-delete recovery preflight and no-purge removal policy.
- Install report generation.
- Structured session logging.
- Redaction service.
- Final install/removal evidence and support bundles.
- PowerShell module skeleton.
- Headless support script.
- Release packaging with customer-safe documentation and package hygiene checks.
- Known error and remediation rule files.
- AI diagnostic instruction files.

Not implemented yet:

- Production app registration and consent contract validation.
- Deployment of the PageMaker365 API and portal application code into the provisioned App Services.
- Production runtime `/health` identity response containing the deployment export ID.
- Production custom-domain binding and certificate automation.
- Live AI call.
- Production installer code signing and customer distribution.

## Repository Layout

```text
src/
  PageMaker365.Installer.App/
  PageMaker365.Installer.Engine/
modules/
  PageMaker365.Install/
scripts/
  install.ps1
infra/
  main.bicep
rules/
ai/
samples/
schemas/
docs/
logs/
support-bundle/
```

## Deployment Contract

Start here before wiring the production payload:

- `docs/customer-readiness-program.md`
- `docs/install-uninstall-user-stories.md`
- `docs/install-uninstall-test-matrix.md`
- `docs/installer-requirements-traceability.md`
- `config/installer-security-profile.json`
- `docs/customer/installer-technical-security-guide.md`
- `docs/deployment-contract.md`
- `docs/onboarding-discovery-contract.md`
- `docs/upgrade-contract.md`
- `docs/removal-evidence-callback-contract.md`
- `docs/using-the-installer.md`
- `docs/implementation-backlog.md`
- `schemas/customer-install.schema.json`
- `schemas/onboarding-bootstrap.schema.json`
- `schemas/tenant-discovery.schema.json`

The controlled customer publication drafts are under `docs/customer/`. They remain explicitly marked as drafts until the customer-readiness traceability gates are satisfied.

The control plane should first create an onboarding session, then use installer discovery results to pre-fill onboarding forms and generate customer install packages that match the install package schema. Portal mode is strict: API responses must include required session/correlation fields, status responses must include package readiness, and generated package downloads must be JSON that pass local package validation before the UI marks them downloaded. The installer accepts the older alpha package shape for now, but warns when export trust metadata is missing and fails when blocked raw secret containers or package hash mismatches are present.

## Build

This project requires the .NET SDK with Windows desktop support.

```powershell
dotnet build .\PageMaker365.Installer.sln
```

Verified on 2026-07-05 with .NET SDK `8.0.422`.

## CI

GitHub Actions runs on pushes to `main`, pull requests targeting `main`, and manual dispatch.

The workflow runs:

- JSON parsing checks.
- Repository and package hygiene checks for generated handoffs and secret-shaped values.
- PowerShell syntax checks.
- .NET restore and solution build.
- Installer engine/API contract tests and WPF workflow tests.
- Installer module export checks.
- Preflight, deployment contract, what-if, runtime identity, cleanup, Key Vault, and report contracts.
- Release-mode package smoke build.

Successful runs upload a short-retention package artifact named `pagemaker365-installer-ci-package`.

## Run Headless Preflight

```powershell
pwsh .\scripts\install.ps1 -Config .\samples\contoso.customer.install.json
```

Other modes:

```powershell
pwsh .\scripts\install.ps1 -Config .\samples\contoso.customer.install.json -Mode AzureSignIn
pwsh .\scripts\install.ps1 -Config .\samples\contoso.customer.install.json -Mode GraphSignIn
pwsh .\scripts\install.ps1 -Config .\samples\contoso.customer.install.json -Mode WhatIfOnly
pwsh .\scripts\install.ps1 -Config .\samples\contoso.customer.install.json -Mode SmokeTests -DeploymentArtifactPath .\support-bundle\install\azure-deployment.json
pwsh .\scripts\verify.ps1
```

## Package

```powershell
pwsh .\scripts\package.ps1 `
  -Version 0.1.0-dev `
  -OutputPath .\artifacts\installer-package

pwsh .\scripts\test-release-package.ps1 `
  -PackagePath .\artifacts\installer-package `
  -ArchivePath .\artifacts\installer-package.zip `
  -ExpectedVersion 0.1.0-dev
```

The package command produces a deterministic ZIP, a sibling ZIP checksum, an
exact release manifest, per-file SHA-256 checksums, release notes, and an
offline verifier. An unsigned package is marked `UnsignedDevelopment` and is
not a customer release.

Production signing can use a certificate already installed in the current
user certificate store:

```powershell
pwsh .\scripts\package.ps1 `
  -Version 0.1.0 `
  -OutputPath .\artifacts\pagemaker365-installer-0.1.0 `
  -CodeSigningCertificateThumbprint '<certificate-thumbprint>' `
  -ExpectedPublisher '<approved-certificate-subject>' `
  -ExpectedCertificateThumbprint '<certificate-thumbprint>' `
  -RequireCleanSource

pwsh .\scripts\test-release-package.ps1 `
  -PackagePath .\artifacts\pagemaker365-installer-0.1.0 `
  -ArchivePath .\artifacts\pagemaker365-installer-0.1.0.zip `
  -ExpectedVersion 0.1.0 `
  -ExpectedPublisher '<approved-certificate-subject>' `
  -ExpectedCertificateThumbprint '<certificate-thumbprint>' `
  -RequireSignature
```

For PFX input, use `-CodeSigningCertificatePath` and provide its password only
through the environment variable named by
`-CodeSigningCertificatePasswordEnvironmentVariable`. See
`docs/release/distribution-contract.md` and
`docs/customer/installer-distribution-verification.md`.

## First Desktop Flow

1. Launch the WPF app.
2. Choose `Use Setup Workflow`.
3. Choose the PageMaker365 setup file supplied by the customer portal. The installer retrieves and validates the customer package automatically.
4. Sign in to Azure and Microsoft Graph.
5. Run preflight checks.
6. Run deployment preview.
7. Approve and run install.
8. Run validation smoke tests.
9. Generate the final evidence package.

See `docs/using-the-installer.md` for the detailed step-by-step guide and evidence output locations.

## Next Development Step

Deploy the PageMaker365 API and portal application code into the provisioned App Services, implement the deployment-bound `/health` identity contract, and complete the clean install/remove/reinstall staging matrix before a customer release.
