# PageMaker365 Installer

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
- `docs/implementation-backlog.md`
- `schemas/customer-install.schema.json`

The control plane should generate customer install packages that match the schema. The installer accepts the older alpha package shape for now, but `Test-PM365DeploymentContract` warns when launch fields are missing and fails when blocked raw secret containers are present.

## Build

This project requires the .NET SDK with Windows desktop support.

```powershell
dotnet build .\PageMaker365.Installer.sln
```

Verified on 2026-07-05 with .NET SDK `8.0.422`.

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
2. Click `Load Sample`.
3. Click `Sign In Azure`.
4. Click `Sign In Graph`.
5. Click `Run Preflight`.
6. Review PowerShell-backed local, Azure, Entra, and SharePoint readiness checks.
7. Use `Explain Issue`, `Generate Admin Message`, or `Create Support Bundle`.

## Next Development Step

Align the PageMaker365 control-plane deployment export with `schemas/customer-install.schema.json`, then replace the placeholder Bicep template with the real customer runtime Azure resources.
