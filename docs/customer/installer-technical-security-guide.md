# PageMaker365 Installer Technical And Security Guide

Status: controlled draft; not approved for customer publication

Tracking issues: [#8 security contract](https://github.com/cloudbossdev/pagemaker365-installer/issues/8) and [#12 customer technical guide](https://github.com/cloudbossdev/pagemaker365-installer/issues/12)

Contract source: `config/installer-security-profile.json`

## Purpose And Scope

This guide describes the security-relevant behavior implemented by the PageMaker365 Installer. It is intended for customer architecture, identity, security, networking, and operations reviewers.

The current alpha provisions the Azure foundation and implements protected runtime secret provisioning locally, but it does not yet deploy verified production API or portal application content, implement a supported upgrade contract, or ship a production-signed installer. Runtime secret provisioning still requires live sandbox acceptance before customer publication.

## Trust Boundaries And Data Flow

1. The customer receives a PageMaker365 setup file containing an onboarding session ID, a short-lived one-time code, allowed operations, expiration, and trusted PageMaker365 portal/API origins.
2. The desktop installer validates the setup-file contract, expiration, operation policy, HTTPS scheme, and exact trusted host before sending the one-time code.
3. The operator signs in separately to Azure and Microsoft Graph. The Azure context and Graph token must match the customer tenant and package subscription.
4. Read-only tenant, SharePoint, and Azure discovery produces a restricted readiness snapshot. It excludes document content, mailbox content, user files, raw secrets, and broad user exports.
5. The portal returns a customer install package. The installer validates its schema, onboarding binding, tenant, discovery ID, deployment export ID, SHA-256 hash, and Ed25519 signature when signed-package mode is required.
6. Azure What-If produces review evidence before an operator can approve deployment.
7. The installer deploys only the package-named PageMaker365 resource group and resources. Ownership tags and the approved preview are rechecked at destructive boundaries.
8. Sanitized lifecycle evidence is sent to the portal with stable event IDs, sequence numbers, and idempotency keys. Sync failures remain in a local outbox and do not redefine the Azure result.

Implementation: `OnboardingSessionService`, `TrustedPageMaker365EndpointPolicy`, `CustomerConfigService`, `DeploymentApprovalManifestService`, `InstallerEvidenceOutboxState`, and the PowerShell deployment/removal commands.

## Operator Identities And Permissions

### Azure

Azure preview and deployment run at subscription scope because the installer creates the dedicated resource group. The template also creates a `Key Vault Secrets User` role assignment for the deployed user-assigned managed identity.

The signed-in Azure operator must have one of these role sets effective at the target subscription:

| Accepted role set | Why it is required |
| --- | --- |
| `Owner` | Can create the resource group and resources and create the managed-identity role assignment. |
| `Contributor` plus `Role Based Access Control Administrator` | Contributor deploys resources; RBAC Administrator supplies `Microsoft.Authorization/roleAssignments/write`. |
| `Contributor` plus `User Access Administrator` | Contributor deploys resources; User Access Administrator supplies authorization management. |

`Contributor` alone is rejected by preflight. Microsoft documents that Contributor cannot write role assignments, while RBAC Administrator can create them. See [Azure privileged built-in roles](https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles/privileged) and [role-assignment prerequisites](https://learn.microsoft.com/en-us/azure/role-based-access-control/role-assignments-portal).

The preflight uses `Get-AzRoleAssignment` with the target subscription and expands group membership when the installed Az.Resources version supports it. Microsoft defines `-Scope` as returning assignments effective at that scope or above. See [Get-AzRoleAssignment](https://learn.microsoft.com/en-us/powershell/module/az.resources/get-azroleassignment).

### Microsoft Graph And SharePoint

The installer uses OAuth 2.0 device authorization through MSAL. Microsoft describes the device-code protocol and tenant token endpoint in [OAuth 2.0 device authorization grant](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code).

The current installer requests only delegated read permissions:

| Scope | Installer use | Consent |
| --- | --- | --- |
| `User.Read` | Identifies the signed-in operator and reads direct memberships. | User consent is normally available. |
| `Domain.Read.All` | Reads verified and default tenant domains. | Admin consent is required. |
| `RoleManagement.Read.Directory` | Reads directory role definitions and memberships so role display names can be evaluated. | Admin consent is required. |
| `Sites.Read.All` | Resolves the configured SharePoint site and enumerates document libraries. | Delegated permission; tenant policy may still require admin approval. |

The exact requests are four `GET` calls: `/domains`, `/me/memberOf/microsoft.graph.directoryRole`, `/sites/{hostname}:{path}`, and `/sites/{site-id}/drives`. No Graph write call is implemented, and the installer no longer requests `Application.ReadWrite.All`, `AppRoleAssignment.ReadWrite.All`, or `Directory.Read.All`.

Microsoft identifies `Domain.Read.All` as least privileged for listing domains, `User.Read` as least privileged for the signed-in user's direct memberships, and `Sites.Read.All` as least privileged for resolving a site. Role-management permission supplies directory-role details that would otherwise be returned with limited properties. See [list domains](https://learn.microsoft.com/en-us/graph/api/domain-list?view=graph-rest-1.0), [list direct memberships](https://learn.microsoft.com/en-us/graph/api/user-list-memberof?view=graph-rest-1.0), [get a site](https://learn.microsoft.com/en-us/graph/api/site-get?view=graph-rest-1.0), and the [Microsoft Graph permissions reference](https://learn.microsoft.com/en-us/graph/permissions-reference).

`Sites.Selected` is the intended runtime application boundary, not an installer delegated scope. Runtime app registration and site-specific grant provisioning are not implemented and remain tracked by issues #5 and #7.

## Azure Resource Inventory

The package supplies names, region, environment, and target subscription. The installer creates:

| Resource | Security-relevant configuration |
| --- | --- |
| Dedicated resource group | `product=PageMaker365` and `managedBy=PageMaker365` ownership tags. |
| Log Analytics workspace | Backs Application Insights. |
| Application Insights | Workspace-based application telemetry component. |
| Key Vault Standard | Azure RBAC enabled, soft delete enabled for 90 days, purge is never performed by the installer. Public network access is currently enabled. |
| StorageV2 account | Standard LRS, public blob access disabled, HTTPS only, minimum TLS 1.2. |
| User-assigned managed identity | Attached to both App Services. |
| Linux App Service plan | Shared by the API and portal services. |
| API Linux App Service | HTTPS only, minimum TLS 1.2, FTPS disabled, managed identity attached. |
| Portal Linux App Service | HTTPS only, minimum TLS 1.2, FTPS disabled, managed identity attached. |
| Key Vault role assignment | Grants `Key Vault Secrets User` to the managed identity at the vault scope. |

The current App Services are infrastructure shells until production application artifact delivery in #5 is complete. Protected secret provisioning is implemented locally under #7 and awaits a fresh signed staging package plus live runtime verification.

## Network Requirements

All non-local installer endpoints use HTTPS on TCP 443. Local development may use HTTP only for `localhost`, `127.0.0.1`, or `::1`. The installer uses the Windows/.NET networking stack plus MSAL, Az PowerShell, and Microsoft Graph PowerShell; it has no independent proxy bypass or certificate-trust store. Customer proxy inspection must preserve a certificate chain trusted by those components.

| Destination | Purpose |
| --- | --- |
| `login.microsoftonline.com:443` | Device-code and token requests. |
| `microsoft.com:443` | Operator device sign-in page. |
| `graph.microsoft.com:443` | Read-only tenant, role, site, and library requests. |
| `management.azure.com:443` | Azure discovery, What-If, deployment, inventory, validation, and removal through Az PowerShell. |
| `pagemaker365.com:443`, `api.pagemaker365.com:443` | Production portal, onboarding APIs, package download, JWKS, and evidence callbacks. |
| `staging.pagemaker365.com:443`, `api-staging.pagemaker365.com:443` | Staging equivalents used during acceptance testing. |
| Customer `*.sharepoint.com:443` | Customer site URL and browser/runtime target. Graph-based installer discovery itself uses `graph.microsoft.com`. |
| Deployed `*.azurewebsites.net:443` | API and portal health and deployment-identity smoke tests. |

Production and staging PageMaker365 hosts are exact allowlist entries in code. Package download URLs must have the same origin as the active onboarding API. JWKS retrieval additionally requires the fixed `/.well-known/pagemaker365-license-jwks.json` path.

## Package And Setup-File Security

- The setup file is sensitive because it contains a one-time onboarding code. Do not email it in an unprotected thread, commit it, attach it to a support bundle, or paste it into chat.
- Expired setup files and disallowed operations fail closed.
- Portal and API origins must match the trusted PageMaker365 host policy and use HTTPS.
- The downloaded package is bound to the active onboarding session, customer tenant, discovery ID, and deployment export ID.
- SHA-256 integrity is recalculated locally.
- Signed-required packages use Ed25519 verification against a trusted key from the PageMaker365 JWKS endpoint.
- Raw secret containers and secret-looking payload fields are rejected.

Production code signing for the installer executable and distribution wrapper is not yet implemented; see #13.

## Token And Secret Handling

- The Microsoft Graph access token is held in process memory and passed to the PowerShell child process through a process-scoped environment variable. It is not included in resumable session state, logs, evidence, or callbacks.
- Azure authentication is managed by Az.Accounts. The installer records tenant, subscription, and sanitized result metadata, not Azure tokens.
- The one-time onboarding code is sent only to the active trusted onboarding API in request data and headers. Persisted session state retains the setup-file path and session metadata, not the code value.
- Structured logs, support bundles, discovery output, and assistant transcripts pass through redaction. Evidence callbacks accept only lifecycle metadata and sanitized errors.
- Package contract `0.3` declares `DATABASE_URL` and `API_ENTRA_CLIENT_SECRET` as operator-provided values and `API_SESSION_SECRET` as installer-generated. The exact three-setting contract is required, minimum lengths must be between 1 and 4,096 characters, and supplied values must be printable ASCII with a 4,096-character maximum. The installer holds operator values in protected process memory for one attempt, passes values to PowerShell through redirected standard input, and submits them to ARM through a secure Bicep parameter.
- ARM writes the values directly to the customer Key Vault. The API App Service stores only Key Vault references and resolves them through its user-assigned managed identity and `Key Vault Secrets User` role.
- Parent-process secure buffers and password controls are cleared after each attempt and when the window closes. PowerShell releases child-process string references in `finally`; managed runtimes do not guarantee immediate zeroing of immutable strings before process exit.
- Resumable state, command arguments, environment variables, callbacks, reports, and support bundles contain no runtime values. Sanitized evidence contains names, resolution status, `rawValuesIncluded: false`, and `valueStorage: "CustomerKeyVault"` only.
- Live staging proof and a full generated-artifact scan remain required before this behavior is approved for customer publication.

## Local Storage And Retention

| Location | Contents | Current retention behavior |
| --- | --- | --- |
| `%LOCALAPPDATA%\PageMaker365\Installer\sessions\{state-id}\session-state.json` | Resumable state, sanitized results, package metadata, and evidence outbox. | No time-based cleanup. `Forget Saved Session` deletes the selected state directory. |
| `{installer-workspace}\logs\{session-id}` | Operation results and support inputs. | No automatic cleanup; customer-controlled deletion. |
| `{installer-workspace}\support-bundle` | Preview, install, validation, removal, portal-status, outbox, and final-evidence artifacts. | No automatic cleanup; customer-controlled deletion. |

Customers must apply their own endpoint retention policy to the local workspace until PageMaker365 defines and implements an automatic retention policy. Portal-side evidence retention is controlled by the portal service and is not defined by this repository. This is an explicit limitation, not an implied indefinite-retention requirement.

## Evidence, Logging, And Portal Sync

Installer lifecycle events contain stable `eventId`, `eventType`, `installAttemptId`, monotonic `sequence`, session and deployment identifiers, outcome, status, package hash, installer version, and a sanitized message/error. Requests use `Idempotency-Key`.

The installer does not send secrets, tokens, one-time codes, raw files, document content, mailbox content, user files, broad tenant exports, or unsanitized logs. Failed evidence delivery remains queued in the local outbox and does not convert a successful Azure operation into an install failure.

Application Insights is deployed for the customer runtime. The installer itself does not currently send a separate application-telemetry stream to Application Insights.

## Removal And Recovery Boundaries

- Removal uses the original package tenant, subscription, resource group, application name, deployment export, and ownership tags.
- Inventory and preview do not delete resources.
- Ambiguous ownership, unexpected contained resources, active deployments, or context mismatch blocks removal.
- The installer removes only the dedicated PageMaker365 resource group after explicit confirmation.
- SharePoint content and customer-created SharePoint data are not removed.
- Key Vault purge is never performed. Azure soft-delete recovery remains available for the configured 90-day vault retention period.
- A later reinstall uses a new package and new disposable Key Vault name during testing.
- Hardened removal lifecycle callbacks are not implemented; see #9.

## Known Release Blockers

| Capability | Current state | Issue |
| --- | --- | --- |
| API and portal application delivery | Not implemented | #5 |
| Supported upgrade/version policy | Not defined | #6 |
| Runtime secret inventory and protected provisioning | Implemented locally; live staging proof pending | #7 |
| Removal lifecycle callbacks | Not implemented | #9 |
| Clean-workstation and repeated lifecycle acceptance | Not complete | #10 |
| Customer user and technical guide approval | Draft only | #11, #12 |
| Production code signing and distribution | Not implemented | #13 |

## Verification And Review

`scripts/test-security-contract.ps1` verifies the approved read-only Graph scope set, accepted Azure role combinations, required network destinations, protected runtime provisioning path, managed-identity Key Vault references, and the Bicep role-assignment dependency. `scripts/verify.ps1` runs that contract with the repository build and test suite.

Before customer publication this guide still requires engineering review against a released commit, identity/security review, operations review, clean-workstation acceptance, repeated install/remove/reinstall evidence, and production distribution verification.
