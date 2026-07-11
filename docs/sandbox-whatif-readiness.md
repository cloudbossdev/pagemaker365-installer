# Sandbox What-If Readiness

Last updated: 2026-07-10

## Current Status

Phase 2.3 sandbox what-if is unblocked. The target Azure subscription, resource group, live staging package download, installer contract validation, package trust verification, and Azure what-if are ready. The next step is an explicit approval decision for the first real sandbox deployment.

Portal package generation details are documented in `docs/portal-install-package-handoff.md`.

## Readiness Checks

| Check | Status | Notes |
| --- | --- | --- |
| Azure modules installed | Ready | `Az.Accounts` and `Az.Resources` are installed locally. |
| Azure account signed in | Ready | Current signed-in account is `jason@mycloudboss.com`. |
| Sandbox subscription visible | Ready | Subscription `3de10659-9db8-4ab6-ae44-ac4b71b24751` is visible in tenant `edf280e3-9c1b-491c-8a0c-f3bf252761a3`. |
| Sandbox resource group exists | Ready | `rg-pagemaker365-cloudboss-sandbox` exists in `eastus2`. V1 deploys into this pre-existing resource group. |
| Real installer package contract exists locally | Ready | Staging package download succeeded from `GET /api/onboarding/installer/onb_cloudboss_sandbox_b1bf6699da57/install-package` using local bootstrap headers. |
| Package contract validation | Ready | `Test-PM365DeploymentContract` passes. Signature and signature algorithm metadata are present with `trustMode` set to `SignedRequired`. |
| Engine package trust verification | Ready | Installer engine resolved `controlPlane.jwksUrl`, matched key `pagemaker365-staging-20260629`, matched the package hash, and verified the Ed25519 signature without a locally configured public key. |
| Sandbox Azure what-if | Ready with warning | `Invoke-PM365WhatIf` reaches Azure and falls back to unstructured what-if when the structured Az cmdlet throws a local `System.Collections.IEnumerator` error. The fallback result is `AzureWhatIfReady` with no blocked changes. |
| Live staging package normalization | Ready | Live staging package returns the normalized values listed below. |
| Raw deployment export available | Not usable | `docs/cloudboss-sandbox-sandbox-deployment-export-2026-07-07T22-53-19-801Z.json` is a raw deployment export, not the installer package contract. Keep it untracked. |

## Live Staging Package

The fresh live staging package downloaded on 2026-07-10 is valid and normalized:

| Field | Live staging value |
| --- | --- |
| `controlPlane.deploymentExportId` | `ac962aa2-697a-424d-a2b1-7dfbcb817617` |
| `controlPlane.trustMode` | `SignedRequired` |
| `controlPlane.packageHash` | `sha256:ace503c5ab23af68874cc14af3c7f44e8183132bfb13c2a495317613755ca124` |
| `azure.resourceNames.portalAppName` | `app-pm365-cloudboss-portal-sandbox` |
| `azure.resourceNames.managedIdentityName` | `uami-pm365-cloudboss-sandbox` |
| `app.supportEmail` | `support@pagemaker365.com` |
| `sharePoint.defaultDocumentLibrary` | `Documents` |
| `controlPlane.baseUrl` | `https://api-staging.pagemaker365.com` |
| `controlPlane.issuerEnvironment` | `staging` |
| `controlPlane.entitlementSyncUrl` | `https://api-staging.pagemaker365.com/api/runtime/entitlements/sync` |
| `controlPlane.jwksUrl` | `https://api-staging.pagemaker365.com/.well-known/pagemaker365-license-jwks.json` |
| `controlPlane.revocationUrl` | `https://api-staging.pagemaker365.com/api/onboarding/exports/revocation` |

Live staging validation:

- `Test-PM365DeploymentContract`: all checks passed, including `DeploymentPackageTrustMetadataReady`.
- Installer engine trust validation: `Verified`; declared and computed package hashes match; Ed25519 signature verified using the public key resolved from the staging JWKS endpoint.
- `Invoke-PM365WhatIf`: `AzureWhatIfReady` with unstructured fallback warning. What-if shows 9 resources to create, including `Microsoft.Web/sites/app-pm365-cloudboss-portal-sandbox` and `Microsoft.ManagedIdentity/userAssignedIdentities/uami-pm365-cloudboss-sandbox`.
- No blocked changes were reported.

## Available Inputs

The following inputs are available locally for validation:

- Real installer package from the portal endpoint:
  - `GET /api/onboarding/installer/onb_cloudboss_sandbox_b1bf6699da57/install-package`
  - Header `X-PM365-Onboarding-Session`
  - Header `X-PM365-Onboarding-Code`
- Local bootstrap file with the one-time code is in the portal repo `.tmp` folder. Do not print or commit the one-time code.
- Optional portal bearer/API key is not currently set locally.

## Runbook

For deployment preview:

```powershell
Set-AzContext -SubscriptionId '3de10659-9db8-4ab6-ae44-ac4b71b24751' -Tenant 'edf280e3-9c1b-491c-8a0c-f3bf252761a3'
Import-Module .\modules\PageMaker365.Install\PageMaker365.Install.psd1 -Force
Test-PM365DeploymentContract -ConfigPath .\.tmp\cloudboss-sandbox.customer.install.json
Invoke-PM365WhatIf -ConfigPath .\.tmp\cloudboss-sandbox.customer.install.json -OutputPath .\support-bundle\cloudboss-sandbox-whatif.json
```

Before running actual deployment, review the generated artifact for:

- policy denials
- missing provider registrations
- missing permissions
- resource naming collisions
- quota or SKU restrictions
- unexpected deletes or modifications

Actual sandbox deployment should be run as a separate explicit approval step:

```powershell
Invoke-PM365Deployment -ConfigPath .\.tmp\cloudboss-sandbox.customer.install.json -OutputPath .\support-bundle\cloudboss-sandbox-deployment.json
```

## Tooling Behavior

`Invoke-PM365WhatIf` and `Invoke-PM365Deployment` now fail early with:

- `AzureSubscriptionMismatch` when the selected Azure subscription does not match the customer package.
- `AzureResourceGroupMissing` when the configured target resource group does not exist.

This keeps the installer UI aligned with the v1 deployment contract and avoids lower-level Azure deployment errors.
