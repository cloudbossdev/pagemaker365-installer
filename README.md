# PageMaker365 Installer

[![CI](https://github.com/cloudbossdev/pagemaker365-installer/actions/workflows/ci.yml/badge.svg)](https://github.com/cloudbossdev/pagemaker365-installer/actions/workflows/ci.yml)

This repository contains the first-build scaffold for the PageMaker365 Windows desktop installer.

The installer is intended to provide a polished customer-facing setup experience while keeping deployment logic deterministic and auditable.

```text
Desktop UI guides the customer.
Installer engine manages state, logs, and step execution.
PowerShell and Bicep perform the real install work.
AI explains failures and suggests safe next actions.
```

## Current Status

This is a Milestone 1 scaffold.

Implemented:

- WPF desktop app shell.
- Reusable installer engine project.
- Customer install config model.
- Sample customer package.
- Desktop Azure and Microsoft Graph sign-in command wiring.
- Deployment contract documentation and package schema.
- Deployment contract preflight check.
- Onboarding bootstrap session contract.
- Tenant discovery result contract.
- Mock PageMaker365 onboarding API client.
- Mock tenant discovery payload generation.
- Redacted local tenant discovery export.
- Read-only Azure, Microsoft Graph, and SharePoint discovery commands.
- Discovery command contract tests with mockable Azure and Graph contexts.
- Local active-session resume state.
- PowerShell-backed local prerequisite checks.
- Azure context validation.
- Azure RBAC validation scaffold.
- Microsoft Graph / Entra scope and admin-role readiness checks.
- SharePoint URL, site, and document-library readiness checks.
- Bicep template build check.
- What-if/deployment command scaffolding.
- Smoke-test command scaffolding.
- Install report generation.
- Structured session logging.
- Redaction service.
- Support bundle stub.
- PowerShell module skeleton.
- Headless support script.
- Package/signing script scaffold.
- Placeholder Bicep entry point.
- Known error and remediation rule files.
- AI diagnostic instruction files.

Not implemented yet:

- Production app registration and consent contract validation.
- Full Azure deployment against the final production infrastructure template.
- Production Bicep resource modules.
- Real app configuration.
- Live AI call.
- Production PageMaker365 API sync.
- Installer packaging/signing.

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

- `docs/deployment-contract.md`
- `docs/onboarding-discovery-contract.md`
- `docs/using-the-installer.md`
- `docs/implementation-backlog.md`
- `schemas/customer-install.schema.json`
- `schemas/onboarding-bootstrap.schema.json`
- `schemas/tenant-discovery.schema.json`

The control plane should first create an onboarding session, then use installer discovery results to pre-fill onboarding forms and generate customer install packages that match the install package schema. The installer accepts the older alpha package shape for now, but `Test-PM365DeploymentContract` warns when launch fields are missing and fails when blocked raw secret containers are present.

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
- PowerShell syntax checks.
- .NET restore and solution build.
- Installer module export checks.
- Preflight, deployment contract, what-if guard, smoke test scaffold, and report generation.
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
pwsh .\scripts\install.ps1 -Config .\samples\contoso.customer.install.json -Mode SmokeTests
pwsh .\scripts\verify.ps1
```

## Package

```powershell
pwsh .\scripts\package.ps1
```

Optional signing:

```powershell
pwsh .\scripts\package.ps1 -CodeSigningCertificatePath C:\certs\pagemaker365.pfx
```

## First Desktop Flow

1. Launch the WPF app.
2. Choose `Use Setup Workflow`.
3. Load the customer package.
4. Sign in to Azure and Microsoft Graph.
5. Run preflight checks.
6. Run deployment preview.
7. Approve and run install.
8. Run validation smoke tests.
9. Generate the final evidence package.

See `docs/using-the-installer.md` for the detailed step-by-step guide and evidence output locations.

## Next Development Step

Wire the production PageMaker365 API endpoints documented in `docs/onboarding-discovery-contract.md`, then align the control-plane deployment export with `schemas/customer-install.schema.json`.
