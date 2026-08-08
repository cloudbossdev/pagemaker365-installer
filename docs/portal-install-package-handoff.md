# Portal Install Package Handoff

Last updated: 2026-07-26

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
- Runtime secret contract: `D:\projects\pagemaker365-installer\docs\runtime-secret-contract.md`

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
- `runtimeArtifacts`
- `controlPlane`
- `secrets`
- `features`

Contract `0.4` requires every section above. `smokeTests` is optional and does
not block package validation. Missing Entra, runtime artifact, control-plane, or
protected-secret metadata blocks the package before Azure mutation, matching the
JSON schema and `RuntimeContractValidator`.

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
| `azure.environment` | `staging` (the hosted runtime contract uses `dev`, `staging`, or `production`) |
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

## Runtime Artifact Contract

The portal must select an approved immutable customer-runtime release and copy
its manifest values into the signed package as `runtimeArtifacts`. The required
shape and security rules are defined in `runtime-artifact-contract.md`.

Required fields:

- `runtimeArtifacts.contractVersion`: `1.0`
- `runtimeArtifacts.releaseId`: immutable release identifier
- `runtimeArtifacts.runtimeVersion`: stable `major.minor.patch` version
- `runtimeArtifacts.sourceCommit`: exact 40-character lowercase spo-ui source commit
- `runtimeArtifacts.api` and `runtimeArtifacts.portal`
- for each artifact: simple ZIP `fileName`, positive bounded `sizeBytes`, approved HTTPS `downloadUrl`, 64
  character lowercase `sha256`, and the contract-fixed `startupCommand`

The release values come from the runtime release manifest and are covered by
the customer package hash and signature. The portal must not create hashes from
operator-entered URLs, place download credentials or SAS tokens in the package,
or use ephemeral CI artifact URLs. Package readiness remains blocked until both
artifacts are present and the runtime version matches the approved deployment
target.

The runtime launcher contract is bound to
`cloudbossdev/spo-ui@1a4aa8519456d1c59022b7f962331389c18e9f9e`.
The signed package must provide non-empty canonical GUID values for
`entra.portalClientId` and `entra.apiClientId`, a hosted `azure.environment`
of `dev`, `staging`, or `production`, a trimmed 1-128 character
`customer.tenantName`, and a trimmed 1-64 character `customer.accountKey`.
The installer derives the API audience and portal scope from the API client ID,
the default App Service origins from the signed resource names, and the exact
File Preview origin from `sharePoint.siteUrl`; these derived values do not need
new signed fields.

Both selected runtime ZIPs must contain `.pm365/provenance.json`. It is a closed
object using producer schema `pagemaker365.runtime-provenance.v1`; its exact,
case-sensitive product, `artifactKind`, release, version, source repository,
source commit, dependency-lock digest, and startup command must satisfy the
producer schema and signed package identity. The portal ZIP must also contain
`index.html`, `auth-redirect.html`, `.pm365/start-portal-runtime.mjs`, and
`.pm365/generate-web-runtime-config.mjs`, and must not contain the obsolete
root `staticwebapp.config.json`.

## Secrets Contract

The portal must generate contract version `0.4`. `secrets.runtimeSecrets` must contain exactly `DATABASE_URL`, `API_ENTRA_CLIENT_SECRET`, and `API_IMAGE_ASSET_CURSOR_SECRET` using the metadata shape in `samples/contoso.customer.install.json` and `docs/runtime-secret-contract.md`.

`DATABASE_URL` and `API_ENTRA_CLIENT_SECRET` use source `operator` with minimum lengths of at least 12 and 16 respectively. `API_IMAGE_ASSET_CURSOR_SECRET` uses source `installerGenerated` with a minimum length of at least 32; the installer currently generates at least 64. All three use owner `customer`, target `api`, and `required: true`. `secrets.keyVaultName` must equal `azure.resourceNames.keyVaultName`.

The legacy `requiredSecretNames` and `promptForSecrets` fields may be present only for compatibility; they do not replace `runtimeSecrets`. Any package without the complete `0.4` runtime contract is rejected before Azure mutation.

Blocked containers:

- `secrets.values`
- `secrets.connectionStrings`
- `secrets.passwords`
- `secrets.tokens`
- `secrets.clientSecrets`
- `secrets.apiKeys`

Each runtime secret entry is metadata only and must not contain `value`, `defaultValue`, or any encoded secret material.

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
Invoke-PM365WhatIf -ConfigPath <path-to-cloudboss-customer-install-json> -ExpectedPackagePayloadSha256 $validatedPayloadSha256 -OutputPath .\support-bundle\cloudboss-sandbox-whatif.json
```

`$validatedPayloadSha256` must come from the successful C# package signature
validation result for the exact UTF-8 payload. Do not calculate it from the
current file or copy a self-declared package hash. Direct `SignedRequired`
preview, deployment, and runtime-configuration calls without that trusted
binding fail closed before any Azure call. The standard installer GUI carries
this binding automatically.

The target resource group `rg-pagemaker365-cloudboss-sandbox` already exists in `eastus2`.

## Done Criteria For The Portal Agent

The portal side is ready for installer validation when:

- The portal can produce a CloudBoss `customer-install` package matching the schema.
- The package endpoint returns the package from `GET /api/onboarding/installer/{sessionId}/install-package`.
- The endpoint requires and validates `X-PM365-Onboarding-Session` and `X-PM365-Onboarding-Code`.
- The package contains real CloudBoss tenant/subscription/resource values.
- The package does not contain raw secrets.
- The package declares `contractVersion: "0.4"` and the exact required runtime secret metadata.
- The package includes the approved immutable `runtimeArtifacts` release manifest values.
- The package includes `controlPlane.deploymentExportId`.
- The package binds to the active onboarding session and discovery payload.
- The package passes `Test-PM365DeploymentContract`.
- The installer can use the package to run `Invoke-PM365WhatIf`.
