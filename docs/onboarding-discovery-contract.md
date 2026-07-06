# Onboarding Discovery Contract

Status: draft for first implementation.

Last updated: 2026-07-06.

## Purpose

The installer should become both a guided deployment client and a read-only tenant discovery client.

The PageMaker365 customer portal remains the system of record. The desktop installer connects to an onboarding session created by the portal, discovers install-readiness values from the customer tenant, and sends those values back so the portal can pre-fill onboarding forms and generate the final signed install package after review.

## High-Level Flow

1. Customer starts onboarding in the PageMaker365 portal.
2. Portal creates an onboarding session.
3. Portal gives the customer a one-time onboarding code or bootstrap JSON file.
4. Customer opens the PageMaker365 Installer.
5. Installer loads the bootstrap session and connects to the PageMaker365 API.
6. Installer signs in to Azure, Microsoft Graph, and SharePoint as needed.
7. Installer runs read-only discovery.
8. Installer sends the discovery payload to the PageMaker365 API.
9. Portal pre-fills onboarding forms and shows values for customer/internal review.
10. Portal generates a signed customer install package.
11. Installer loads or downloads the install package.
12. Installer runs preflight, what-if, install, validation, and evidence generation.

## Contracts

### Bootstrap Session

Schema: `schemas/onboarding-bootstrap.schema.json`

Sample: `samples/contoso.onboarding.bootstrap.json`

The bootstrap session is a short-lived handoff from the portal to the installer. It identifies the onboarding record and authorizes only narrow discovery and status sync operations.

Important fields:

- `sessionId`: portal onboarding session ID.
- `customerName`: customer display name for the install session.
- `expectedTenantId`: optional expected tenant guard.
- `portalBaseUrl`: human portal URL.
- `apiBaseUrl`: API base URL for sync.
- `oneTimeCode`: short-lived onboarding code. Treat as sensitive.
- `allowedOperations`: operations the installer may perform for this session.
- `discoveryPolicy`: allowed discovery providers, required fields, and excluded data classes.

### Tenant Discovery Result

Schema: `schemas/tenant-discovery.schema.json`

Sample: `samples/contoso.tenant.discovery.json`

The tenant discovery payload is the installer-to-portal result. It should contain only install-readiness metadata.

Allowed examples:

- Tenant ID and tenant display name.
- Verified domains needed for install readiness.
- Azure subscription IDs and target resource group names.
- Recommended Azure location.
- SharePoint tenant hostname, target site URL, site ID, and default library.
- Entra app registration and consent readiness.
- Warnings and blockers discovered during read-only checks.

Excluded examples:

- Document content.
- Mailbox content.
- User files.
- Raw secrets.
- Broad user profile exports.
- Marketing analytics data.

### Azure Discovery

The first live discovery provider is Azure discovery. It is read-only and runs through the `Get-PM365AzureDiscovery` PowerShell command.

Collected fields:

- Signed-in Azure account ID.
- Signed-in tenant ID.
- Current subscription ID, display name, and state.
- Accessible subscription IDs, display names, and states.
- Recommended Azure location from the package or existing target resource group.
- Target resource group name.
- Whether the target resource group already exists.
- Tenant/subscription mismatch findings.

Required local tooling:

- PowerShell 7.
- `Az.Accounts` for Azure context and subscription discovery.
- `Az.Resources` for resource group existence checks.

Failure behavior:

- If Azure PowerShell modules are missing, discovery keeps bootstrap/package Azure values and adds warning findings.
- If Azure is not signed in, discovery keeps bootstrap/package Azure values and adds `AzureNotSignedIn`.
- If the signed-in tenant or subscription differs from the generated package, discovery adds failed findings but still returns the metadata it can read.
- The installer must not use Azure discovery to deploy, mutate, create, delete, or grant permissions.

### Graph And SharePoint Discovery

The second live discovery provider is Graph discovery. It is read-only and runs through the `Get-PM365GraphDiscovery` PowerShell command.

Collected fields:

- Signed-in Microsoft Graph account ID.
- Signed-in Graph tenant ID.
- Granted Graph scopes in the current session.
- Verified tenant domains when readable.
- SharePoint tenant hostname.
- Target SharePoint site URL, site ID, and display name.
- Whether the target SharePoint site resolved through Graph.
- Default document library name and drive ID when discoverable.
- Available SharePoint document libraries for the target site.
- Entra app registration mode, permission mode, required permissions, and admin consent readiness.
- Tenant mismatch, missing scope, site resolution, library, and admin role findings.

Required local tooling:

- PowerShell 7.
- `Microsoft.Graph.Authentication` for Graph sign-in, Graph context, and `Invoke-MgGraphRequest`.

Failure behavior:

- If Microsoft Graph PowerShell is missing, discovery keeps package Graph/SharePoint values and adds `GraphAuthenticationMissing`.
- If Graph is not signed in, discovery keeps package Graph/SharePoint values and adds `GraphNotSignedIn`.
- If the signed-in Graph tenant differs from the generated package, discovery adds `GraphTenantMismatch`.
- If SharePoint discovery is disabled by the bootstrap policy, Graph discovery still runs tenant and Entra checks but returns package SharePoint values.
- If the SharePoint site or library cannot be resolved, discovery adds findings but still returns the metadata it can read.
- The installer must not use Graph discovery to create app registrations, grant consent, modify SharePoint, read documents, read mailbox content, or export user profiles.

## API Shape

The production API should expose endpoints equivalent to:

```http
POST /api/onboarding/installer/connect
POST /api/onboarding/installer/discovery
POST /api/onboarding/installer/status
GET  /api/onboarding/installer/{sessionId}/install-package
```

The desktop app defaults to `Mock` mode, which does not make network calls. It lets the app prove the UI, model, redaction, and local persistence flow before the portal API exists.

Portal mode is configured with `onboarding-api.json` in the repo/package root, `config/onboarding-api.json`, or beside the app executable. A sample is available at `samples/onboarding-api.portal.example.json`.

Supported environment overrides:

- `PM365_ONBOARDING_MODE`: `Mock` or `Portal`.
- `PM365_ONBOARDING_API_BASE_URL`: default API base URL if the bootstrap session does not supply one.
- `PM365_ONBOARDING_CONNECT_ENDPOINT_PATH`: connect endpoint path.
- `PM365_ONBOARDING_DISCOVERY_ENDPOINT_PATH`: discovery sync endpoint path.
- `PM365_ONBOARDING_STATUS_ENDPOINT_PATH`: package-readiness endpoint path.
- `PM365_ONBOARDING_PACKAGE_ENDPOINT_TEMPLATE`: package download endpoint template. Use `{sessionId}` as the token.
- `PM365_ONBOARDING_API_KEY_ENV`: name of the environment variable that contains the bearer token.
- `PM365_ONBOARDING_TIMEOUT_SECONDS`: HTTP timeout.
- `PM365_ONBOARDING_FALLBACK_TO_MOCK`: whether portal API failures should fall back to local mock behavior.

When portal mode is enabled, the installer attaches these headers:

- `Authorization: Bearer <token>` when the configured API key environment variable has a value.
- `X-PM365-Onboarding-Session`: onboarding session ID.
- `X-PM365-Onboarding-Code`: one-time onboarding code from the bootstrap session.

The bootstrap session `apiBaseUrl` takes precedence over `PM365_ONBOARDING_API_BASE_URL`, so the portal can generate customer/session-specific API routing.

### Connect Request

`POST /api/onboarding/installer/connect`

```json
{
  "contractVersion": "0.1",
  "sessionId": "onb_contoso_sandbox_001",
  "oneTimeCode": "PM365-CONTOSO-DEMO",
  "requestedBy": "admin@contoso.com",
  "customerName": "Contoso Intranet"
}
```

Response: `OnboardingSessionConnection`.

### Discovery Submit Request

`POST /api/onboarding/installer/discovery`

```json
{
  "contractVersion": "0.1",
  "sessionId": "onb_contoso_sandbox_001",
  "oneTimeCode": "PM365-CONTOSO-DEMO",
  "discovery": {
    "contractVersion": "0.1",
    "discoveryId": "disc_contoso_sandbox_001",
    "onboardingSessionId": "onb_contoso_sandbox_001",
    "dataPolicy": "InstallReadinessOnly"
  }
}
```

Response: `OnboardingDiscoverySubmission`.

The `discovery` object is the full `TenantDiscoveryResult` payload. It must remain install-readiness metadata only.

### Onboarding Status / Package Readiness

Sample: `samples/contoso.onboarding.status.json`

The status endpoint should tell the installer whether the portal has enough customer/onboarding data to generate the final install package.

Important fields:

- `sessionId`: portal onboarding session ID.
- `status`: high-level onboarding state, such as `NeedsCustomerInput`, `Ready`, or `Downloaded`.
- `missingFields`: required fields still needed before package generation.
- `packageReadiness.status`: package state, such as `NotChecked`, `NeedsCustomerInput`, `Ready`, or `Downloaded`.
- `packageReadiness.packageVersion`: generated package contract/version identifier.
- `packageReadiness.packageDownloadUrl`: API endpoint the installer can use to retrieve the generated package.
- `correlationId`: server-side trace ID for audit/support.

Request:

```json
{
  "contractVersion": "0.1",
  "sessionId": "onb_contoso_sandbox_001",
  "oneTimeCode": "PM365-CONTOSO-DEMO",
  "discovery": {
    "contractVersion": "0.1",
    "discoveryId": "disc_contoso_sandbox_001",
    "dataPolicy": "InstallReadinessOnly"
  },
  "loadedPackage": {
    "tenantId": "00000000-0000-0000-0000-000000000000",
    "tenantName": "Contoso Intranet",
    "azureSubscriptionId": "11111111-1111-1111-1111-111111111111",
    "azureLocation": "eastus",
    "resourceGroupName": "rg-pagemaker365-contoso-prod",
    "sharePointSiteUrl": "https://contoso.sharepoint.com/sites/intranet",
    "sharePointTenantHostname": "contoso.sharepoint.com",
    "primaryContact": "admin@contoso.com",
    "environmentId": "",
    "packageHash": ""
  }
}
```

The installer sends only a sanitized `loadedPackage` context for readiness checks. It must not send raw secrets, full customer package content, or generated credentials.

Example response:

```json
{
  "contractVersion": "0.1",
  "sessionId": "onb_contoso_sandbox_001",
  "customerName": "Contoso Intranet",
  "status": "Ready",
  "portalRecordUrl": "https://pagemaker365.com/admin/onboarding/onb_contoso_sandbox_001",
  "correlationId": "server-correlation-id",
  "message": "Portal has enough onboarding data to generate the install package.",
  "lastSyncAt": "2026-07-05T22:00:00Z",
  "missingFields": [],
  "packageReadiness": {
    "status": "Ready",
    "packageVersion": "0.2",
    "packageDownloadUrl": "https://pagemaker365.com/api/onboarding/installer/onb_contoso_sandbox_001/install-package",
    "localPackagePath": "",
    "readyAt": "2026-07-05T22:00:00Z",
    "message": "Generated package is ready for installer download."
  }
}
```

The desktop app currently implements this in mock mode by writing:

`support-bundle/onboarding/{sessionId}/portal-status.mock.json`

When the package is ready, mock download copies:

`samples/contoso.customer.install.json`

to:

`support-bundle/onboarding/{sessionId}/generated-package/{sessionId}.customer.install.json`

In portal mode, status snapshots are written locally to:

`support-bundle/onboarding/{sessionId}/portal-status.json`

Generated packages are downloaded from `packageReadiness.packageDownloadUrl` when present, otherwise from the configured package endpoint template.

## Security Rules

- Discovery is read-only.
- The installer must not submit raw secrets.
- Local discovery exports must be redacted by default.
- AI guidance must not run commands or submit data on its own.
- Portal sync must require an active onboarding session.
- Final install packages must be generated by the portal/control plane after review, not hand-written in the installer.

## Current Implementation

Implemented in this repo:

- `OnboardingBootstrapSession` model.
- `TenantDiscoveryResult` model.
- Bootstrap session validation.
- Mock PageMaker365 API connect and discovery submission.
- Mock discovery generation from the bootstrap session and loaded install package.
- Redacted local discovery JSON export.
- Mock package-readiness status.
- Mock generated package download and load into the installer.
- Portal API client scaffold for connect, discovery sync, package readiness, and package download.
- Onboarding API configuration and environment override support.
- Read-only Azure discovery command and engine integration.
- Read-only Graph and SharePoint discovery command and engine integration.
- Onboarding API, Azure discovery, and Graph discovery contract harness.

Not implemented yet:

- Live PageMaker365 portal API endpoint implementation and validation.
- Portal-side onboarding form population.
- Signed final install package generation and signature validation.
