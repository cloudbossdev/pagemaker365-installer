# Portal Install Package Handoff

Last updated: 2026-07-09

## Purpose

This handoff tells the PageMaker365 portal/customer onboarding agent exactly what the installer needs from the portal before Phase 2.3 sandbox what-if can run.

The installer is ready to consume a generated package, validate it, convert it into Bicep parameters, and run Azure what-if. The remaining integration proof is for the portal to return the normalized `customer-install` package contract from the install-package endpoint.

## Source Of Truth

Use these files in the installer repo as the contract source:

- Schema: `D:\projects\pagemaker365-installer\schemas\customer-install.schema.json`
- Example shape only: `D:\projects\pagemaker365-installer\samples\contoso.customer.install.json`
- Portal API flow: `D:\projects\pagemaker365-installer\docs\onboarding-discovery-contract.md`
- Deployment trust rules: `D:\projects\pagemaker365-installer\docs\deployment-contract.md`
- Current sandbox readiness: `D:\projects\pagemaker365-installer\docs\sandbox-whatif-readiness.md`

Do not use `D:\projects\pagemaker365-installer\docs\cloudboss-sandbox-sandbox-deployment-export-2026-07-07T22-53-19-801Z.json` as the installer package. That file is a raw deployment export. It can be used as source data by the portal, but the endpoint response must be transformed into the `customer-install` contract.

## Endpoint To Implement

```http
GET /api/onboarding/installer/{sessionId}/install-package
```

Required request headers:

- `X-PM365-Onboarding-Session`
- `X-PM365-Onboarding-Code`

Optional request header:

- `Authorization: Bearer <token>` when the portal API requires `PM365_ONBOARDING_API_KEY`.

Response requirements:

- HTTP 200 when the package is ready.
- JSON content type, such as `application/json` or `application/*+json`.
- Body must be the normalized `customer-install` package.
- Do not return a wrapper object unless the installer contract is changed to support one.
- Do not return raw secrets.
- Do not return the raw deployment export.

## Required Package Sections

The response must include these top-level sections:

- `contractVersion`
- `customer`
- `azure`
- `sharePoint`
- `app`
- `entra`
- `controlPlane`
- `secrets`
- `features`
- `smokeTests`

The schema currently requires `customer`, `azure`, `sharePoint`, `app`, and `features`, but the portal should include all sections above for launch readiness.

## CloudBoss Sandbox Values

Use these values for the first real CloudBoss sandbox package unless the portal record has newer values:

| Field | Value |
| --- | --- |
| `customer.accountKey` | `cloudboss` |
| `customer.tenantName` | `CloudBoss` |
| `customer.tenantId` | `edf280e3-9c1b-491c-8a0c-f3bf252761a3` |
| `azure.tenantId` | `edf280e3-9c1b-491c-8a0c-f3bf252761a3` |
| `azure.subscriptionId` | `3de10659-9db8-4ab6-ae44-ac4b71b24751` |
| `azure.location` | `eastus2` |
| `azure.resourceGroupName` | `rg-pagemaker365-cloudboss-sandbox` |
| `azure.environment` | `sandbox` |
| `sharePoint.siteUrl` | `https://bosscloud.sharepoint.com/sites/cloudboss` |
| `sharePoint.defaultDocumentLibrary` | `Documents` unless the portal has a better value |
| `sharePoint.permissionMode` | `SitesSelected` |
| `app.appName` | `pagemaker365-cloudboss` |
| `app.runtimeBaseUrl` | `https://intranet.mycloudboss.com` |
| `app.apiBaseUrl` | `https://intranet.mycloudboss.com/api` |
| `app.customDomain` | `intranet.mycloudboss.com` |
| `app.supportEmail` | `support@pagemaker365.com` |

## Azure Resource Names

The installer v1 hosting model is Linux App Service. The package must provide these resource names under `azure.resourceNames`:

| Contract field | CloudBoss value / generation rule |
| --- | --- |
| `keyVaultName` | `kv-pm365-cb-sandbox` |
| `storageAccountName` | `stpm365cbsbox001` |
| `logAnalyticsName` | `log-pm365-cloudboss-sandbox` |
| `applicationInsightsName` | `appi-pm365-cloudboss-sandbox` |
| `appServicePlanName` | `asp-pm365-cloudboss-sandbox` |
| `apiAppName` | `app-pm365-cloudboss-api-sandbox` |
| `portalAppName` | Generate a Linux App Service name, recommended `app-pm365-cloudboss-portal-sandbox` |
| `managedIdentityName` | Generate a user-assigned identity name, recommended `id-pm365-cloudboss-sandbox` |

The raw export may contain `staticWebAppName`. Do not map that directly unless the portal intentionally chooses to reuse the name for the frontend Linux App Service. The current installer Bicep deploys a frontend App Service, not an Azure Static Web App.

## Resource Name Validation

The installer validates deployment parameters before what-if or deploy:

- `app.appName`, `azure.environment`, and `azure.location` are required.
- `azure.location` must be the Azure location name, such as `eastus2`, not a display name like `East US 2`.
- `customer.tenantId` must be a GUID.
- `azure.resourceGroupName` must be 1-90 characters, use Azure-safe resource group characters, and must not end with a period.
- `keyVaultName` must be 3-24 characters, start with a letter, end with a letter or number, contain only letters, numbers, or hyphens, and must not contain `--`.
- `storageAccountName` must be 3-24 lowercase letters or numbers only.
- `logAnalyticsName` must be 4-63 characters, start and end with a letter or number, and contain only letters, numbers, or hyphens.
- `applicationInsightsName` must be 1-260 characters, start and end with a letter or number, and contain only letters, numbers, hyphens, underscores, or periods.
- `appServicePlanName` must be 1-40 characters, start and end with a letter or number, and contain only letters, numbers, or hyphens.
- `apiAppName` and `portalAppName` must be 2-60 characters, start and end with a letter or number, and contain only letters, numbers, or hyphens.
- `managedIdentityName` must be 3-128 characters, start and end with a letter or number, and contain only letters, numbers, hyphens, or underscores.

## Control Plane Provenance

For portal-downloaded packages, the installer validates package provenance before saving the package as active installer state.

Required or strongly recommended fields in `controlPlane`:

- `baseUrl`
- `deploymentExportId`
- `exportedAt`
- `expiresAt`
- `issuer`
- `issuerEnvironment`
- `onboardingSessionId`
- `discoveryId` when discovery context exists
- `schemaId`
- `environmentId`
- `licenseActivationId`
- `entitlementSyncUrl`
- `publicKeyId`
- `packageHash`
- `packageHashAlgorithm`: `SHA-256`
- `canonicalization`: `json-c14n-v1`
- `signature`
- `signatureAlgorithm`
- `trustMode`: `UnsignedAllowed` for alpha or `SignedRequired` for strict packages
- `jwksUrl`
- `revocationUrl`
- `correlationId`

Blocking provenance rules:

- `controlPlane.onboardingSessionId` must match the active installer bootstrap `sessionId`.
- `customer.tenantId` must match bootstrap `expectedTenantId` when that bootstrap value is present.
- `azure.tenantId`, when present, must match the same expected tenant ID.
- `controlPlane.discoveryId` must match the active discovery payload when discovery context exists.
- `controlPlane.deploymentExportId` is required for generated portal packages.
- If `controlPlane.packageHash` is present, it must match the installer-computed canonical package hash.
- If `controlPlane.trustMode` is `SignedRequired`, missing hash/signature/key metadata is a failure.

Alpha packages may use `trustMode: "UnsignedAllowed"`. Missing hash/signature metadata produces warnings, but a declared hash mismatch always blocks.

## Secrets Contract

The package may include secret names and prompts, but must not include raw secret values.

Allowed examples:

- `secrets.keyVaultName`
- `secrets.requiredSecretNames`
- `secrets.promptForSecrets`

Blocked containers:

- `secrets.values`
- `secrets.connectionStrings`
- `secrets.passwords`
- `secrets.tokens`
- `secrets.clientSecrets`
- `secrets.apiKeys`

## Minimal Validation Commands

After the portal agent generates a CloudBoss package JSON file, copy or download it locally and run:

```powershell
Set-Location D:\projects\pagemaker365-installer
Import-Module .\modules\PageMaker365.Install\PageMaker365.Install.psd1 -Force
Test-PM365DeploymentContract -ConfigPath <path-to-cloudboss-customer-install-json>
```

Expected result:

- `DeploymentContractReadable`: `Passed`
- `DeploymentPackageSecretSafe`: `Passed`
- `DeploymentParametersReady`: `Passed`
- `DeploymentContractReady`: `Passed` or only non-blocking trust warnings during alpha

Then run what-if:

```powershell
Set-AzContext -SubscriptionId '3de10659-9db8-4ab6-ae44-ac4b71b24751' -Tenant 'edf280e3-9c1b-491c-8a0c-f3bf252761a3'
Invoke-PM365WhatIf -ConfigPath <path-to-cloudboss-customer-install-json> -OutputPath .\support-bundle\cloudboss-sandbox-whatif.json
```

The target resource group `rg-pagemaker365-cloudboss-sandbox` already exists in `eastus2`.

## Done Criteria For The Portal Agent

The portal side is ready for installer validation when:

- The portal can produce a CloudBoss `customer-install` package matching the schema.
- The package endpoint returns the package from `GET /api/onboarding/installer/{sessionId}/install-package`.
- The endpoint requires and validates `X-PM365-Onboarding-Session` and `X-PM365-Onboarding-Code`.
- The package contains real CloudBoss tenant/subscription/resource values.
- The package does not contain raw secrets.
- The package includes `controlPlane.deploymentExportId`.
- The package binds to the active onboarding session and discovery payload.
- The package passes `Test-PM365DeploymentContract`.
- The installer can use the package to run `Invoke-PM365WhatIf`.
