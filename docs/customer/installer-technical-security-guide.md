# PageMaker365 Installer Technical And Security Guide

Status: controlled draft; not approved for customer publication

Tracking issues: [#8 security contract](https://github.com/cloudbossdev/pagemaker365-installer/issues/8) and [#12 customer technical guide](https://github.com/cloudbossdev/pagemaker365-installer/issues/12)

Contract source: `config/installer-security-profile.json`

Document revision: 2026-07-27

Release version: not assigned

## Purpose And Scope

This guide describes the security-relevant behavior implemented by the PageMaker365 Installer. It is intended for customer architecture, identity, security, networking, and operations reviewers.

The current alpha provisions the Azure foundation and implements protected runtime-secret, upgrade, and removal contracts locally. It does not yet deploy verified production API or portal application content, complete portal/staging acceptance for those lifecycle contracts, or have a production certificate and clean-workstation proof for its signed distribution. Those live acceptance gaps remain release blockers.

## Trust Boundaries And Data Flow

1. The customer receives a PageMaker365 setup file containing an onboarding session ID, a short-lived one-time code, allowed operations, expiration, and trusted PageMaker365 portal/API origins.
2. The desktop installer validates the setup-file contract, expiration, operation policy, HTTPS scheme, and exact trusted host before sending the one-time code.
3. The operator signs in separately to Azure and Microsoft Graph. The Azure context and Graph token must match the customer tenant and package subscription.
4. Read-only tenant, SharePoint, and Azure discovery produces a restricted readiness snapshot. It excludes document content, mailbox content, user files, raw secrets, and broad user exports.
5. The portal returns a customer install package. The installer validates its schema, onboarding binding, tenant, discovery ID, deployment export ID, SHA-256 hash, and Ed25519 signature when signed-package mode is required.
6. Azure What-If produces review evidence before an operator can approve deployment.
7. The installer deploys only the package-named PageMaker365 resource group and resources. Ownership tags and the approved preview are rechecked at destructive boundaries.
8. Sanitized lifecycle evidence is sent to the portal with stable event IDs, sequence numbers, and idempotency keys. Sync failures remain in a local outbox and do not redefine the Azure result.

Implementation: `OnboardingSessionService`, `TrustedPageMaker365EndpointPolicy`, `CustomerConfigService`, `DeploymentApprovalManifestService`, the install/removal evidence outboxes, and the PowerShell deployment/removal commands.

## Lifecycle And Mutation Controls

| Boundary | Read-only preparation | Required authorization | Fail-closed condition |
| --- | --- | --- | --- |
| Package activation | Setup/session validation, readiness, download, schema, provenance, hash, and signature | Short-lived onboarding session permits the exact operation | Expired/reused code, untrusted origin, mismatched session/tenant/discovery/export, invalid hash/signature, or prohibited package content |
| Preflight | Tooling, context, RBAC, provider/SKU/quota, Key Vault recovery, Graph scopes, and SharePoint metadata | Independently valid Azure and Graph contexts | Any mandatory result is absent, unverifiable, or failed |
| Deployment preview | Subscription-scope Azure What-If and redacted evidence | Validated package and successful preflight | Foreign ownership, unstructured/unsafe result under policy, changed package, or target mismatch |
| Install | Revalidation of package, preview, What-If artifact, approval state, confirmation, and target ownership | Explicit approval plus exact resource-group text | Input hash changed, authorization is stale, ownership is ambiguous, or protected runtime values do not meet the signed contract |
| Runtime completion | Deployment-bound API identity, portal content, Key Vault reference resolution, and SharePoint access | Successful current deployment attempt | HTTP success without expected product/export identity, default content, old custom-domain content, or inaccessible target |
| Removal | Inventory, ownership tags, contained-resource policy, active-deployment check, and retained-resource preview | Explicit removal approval plus exact resource-group text | Wrong context, ambiguous/foreign ownership, unexpected resource, active deployment, or Key Vault purge request |

Application restart clears authorization-bearing state. Tokens, runtime values, deployment/removal approval checkboxes, and typed confirmations are not persisted. A saved session can restore sanitized context and evidence references, but every later mutation must pass its current boundary again.

Upgrade behavior is not a production guarantee in this version. The draft installer-side version and recovery contract is tracked by issue #6; it remains excluded from customer support until signed package generation, API receipt handling, and live staging acceptance complete.

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

Preflight fails closed when required Az/Bicep tooling is absent, Azure context or deployment RBAC cannot be verified, the configured Key Vault recovery state cannot be checked, a required delegated Graph scope is missing, or the package-configured SharePoint site or document library cannot be resolved. A failed check must pass on rerun before Preview unlocks. Warnings are reserved for advisory or nonauthoritative signals and remain in evidence; they do not silently represent a required boundary as ready.

Preflight also uses read-only Azure Resource Manager operations to verify registration of the resource providers used by the Bicep deployment, confirm that App Service B1 appears in the subscription SKU inventory for the package region, and read App Service core usage and limits for that region. Unregistered providers, an unavailable B1 SKU, or less than one remaining core block deployment. Microsoft documents these interfaces in [Get-AzResourceProvider](https://learn.microsoft.com/en-us/powershell/module/az.resources/get-azresourceprovider), [App Service List SKUs](https://learn.microsoft.com/en-us/rest/api/appservice/list-skus/list-skus?view=rest-appservice-2025-05-01), and [App Service usages in a location](https://learn.microsoft.com/en-us/rest/api/appservice/get-usages-in-location/list?view=rest-appservice-2025-05-01).

These checks do not create or reserve App Service capacity. Azure can still reject the plan during asynchronous regional allocation even when provider, SKU, and quota checks pass. That condition is handled as a sanitized, retryable deployment failure and must not be represented as a successful install.

### Microsoft Graph And SharePoint

The installer uses OAuth 2.0 device authorization through MSAL. Microsoft describes the device-code protocol and tenant token endpoint in [OAuth 2.0 device authorization grant](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code).

Canceled and expired device-code attempts are recorded as sanitized, retryable failures. The desktop clears the stale user code, retains no access token, keeps Preflight locked, and requires a new sign-in attempt. Azure browser cancellation follows the same retryable failure policy.

The current installer requests only delegated read permissions:

| Scope | Installer use | Consent |
| --- | --- | --- |
| `User.Read` | Identifies the signed-in operator and reads direct memberships. | User consent is normally available. |
| `Domain.Read.All` | Reads verified and default tenant domains. | Admin consent is required. |
| `RoleManagement.Read.Directory` | Reads directory role definitions and memberships so role display names can be evaluated. | Admin consent is required. |
| `Sites.Read.All` | Resolves the configured SharePoint site and enumerates document libraries. | Delegated permission; tenant policy may still require admin approval. |

The exact requests are four `GET` calls: `/domains`, `/me/memberOf/microsoft.graph.directoryRole`, `/sites/{hostname}:{path}`, and `/sites/{site-id}/drives`. No Graph write call is implemented, and the installer no longer requests `Application.ReadWrite.All`, `AppRoleAssignment.ReadWrite.All`, or `Directory.Read.All`.

SharePoint preflight reads site and drive metadata only. It does not read list items, files, or document content. A missing library response does not export the names of unrelated libraries into installer evidence.

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
| `management.azure.com:443` | Azure discovery, provider/SKU/quota readiness, What-If, deployment, inventory, validation, and removal through Az PowerShell. |
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
- Package download retries are limited to four attempts for HTTP 408, 429, and 5xx. Each attempt uses a fresh same-origin request; `Retry-After` is capped at 30 seconds, cancellation remains effective, and 401/403 or package validation failures do not retry.
- SHA-256 integrity is recalculated locally.
- Signed-required packages use Ed25519 verification against a trusted key from the PageMaker365 JWKS endpoint.
- Raw secret containers and secret-looking payload fields are rejected.

The pilot distribution is a deterministic versioned ZIP. The executable, PageMaker365 first-party libraries, and shipped PowerShell files support Authenticode signing. The manifest and SHA-256 files record the exact payload and ZIP integrity. Unsigned CI packages are labeled `UnsignedDevelopment` and are rejected by the customer verifier by default. Production certificate configuration and clean-workstation verification remain open under #13.

## Cryptographic Trust Layers

The installer uses separate trust layers. Passing one does not substitute for another.

| Layer | Mechanism | Trust source | Failure behavior |
| --- | --- | --- | --- |
| Distribution archive | SHA-256 archive checksum | Checksum delivered with the approved release | Extraction/launch is stopped when the archive differs. |
| Release inventory | Detached CMS signature over the exact `release-manifest.json` bytes | Official publisher and certificate thumbprint supplied outside the ZIP | Missing/extra files, hash differences, signature failure, or signer mismatch stop launch. |
| Shipped code/scripts | Authenticode signing where required by the release contract | Same externally approved publisher/thumbprint policy | Required unsigned or wrongly signed files fail verification. |
| Customer install package | Canonical JSON SHA-256 plus Ed25519 signature | Trusted PageMaker365 JWKS key ID and fixed trusted endpoint policy | The package is not activated and later workflow gates remain locked. |
| Portal callback | TLS plus stable event identity, sequence, and `Idempotency-Key`; exact response receipt validation | Active trusted onboarding API/session | Delivery remains queued; Azure/local result is not reclassified. |

The verifier must receive the expected official publisher and certificate thumbprint from the release record or another approved external channel. A value declared inside the package being verified is not an independent trust anchor.

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

## Assistant And Support Handoff Security

- Portal assistant endpoints must remain on the exact trusted PageMaker365 production or staging HTTPS origins. Root-relative endpoint configuration cannot redirect the bearer credential to another host.
- Message and ticket payloads contain sanitized operator text and selected diagnostic fields. Local transcript, package, discovery, and attachment paths are empty; API error bodies are not copied into transcripts.
- Attachment transfer is disabled by default. Explicit opt-in permits only redacted `.txt`, `.log`, `.json`, and `.md` copies. The installer recalculates size and SHA-256 and sends an opaque filename.
- Screenshots and other binary attachments remain local-only. Failed and local-only attachments are omitted from remote ticket requests rather than represented by metadata.
- Portal message, attachment, and ticket responses must match the submitted contract identity. Ticket status must be `Drafted`; the installer never submits a final support ticket.
- Only transient network, timeout, HTTP 408/429, or HTTP 5xx failures may use configured local-mock fallback. Authorization, validation, contract, and cancellation failures remain failures.
- Recommended actions are intersected with a local registry. Local labels and approval requirements override portal values; unknown and duplicate actions are discarded. No local action performs install, removal, Azure mutation, consent, or tenant writes.
- Local assistant data remains under the customer-controlled support-bundle folder. PageMaker365 portal retention and final ticket-submission policy remain a release gate under issue #28.

## Local Storage And Retention

| Location | Contents | Current retention behavior |
| --- | --- | --- |
| `%LOCALAPPDATA%\PageMaker365\Installer\sessions\{state-id}\session-state.json` | Resumable state, sanitized results, package metadata, and evidence outbox. | No time-based cleanup. `Forget Saved Session` deletes the selected state directory. |
| `{installer-workspace}\logs\{session-id}` | Operation results and support inputs. | No automatic cleanup; customer-controlled deletion. |
| `{installer-workspace}\support-bundle` | Preview, install, validation, removal, portal-status, outbox, and final-evidence artifacts. | No automatic cleanup; customer-controlled deletion. |

Customers must apply their own endpoint retention policy to the local workspace until PageMaker365 defines and implements an automatic retention policy. Portal-side evidence retention is controlled by the portal service and is not defined by this repository. This is an explicit limitation, not an implied indefinite-retention requirement.

## Evidence, Logging, And Portal Sync

Installer lifecycle events contain stable `eventId`, `eventType`, `installAttemptId`, monotonic `sequence`, session and deployment identifiers, outcome, status, package hash, installer version, and a sanitized message/error. Requests use an exact attempt/sequence/event `Idempotency-Key`. Upgrade events use a distinct ordered state machine and require an exact `Accepted` contract 0.3 receipt; HTTP success alone does not dequeue an event.

The installer does not send secrets, tokens, one-time codes, raw files, document content, mailbox content, user files, broad tenant exports, or unsanitized logs. Failed evidence delivery remains queued in the local outbox and does not convert a successful Azure operation into an install failure.

Application Insights is deployed for the customer runtime. The installer itself does not currently send a separate application-telemetry stream to Application Insights.

## Upgrade Boundaries

- Signed packages declare `install` or `upgrade`, target runtime version, minimum installer version, and fixed preservation policies.
- Upgrade packages additionally declare the exact source runtime version and source deployment export.
- Patch and immediately adjacent minor transitions within one major version are supported; downgrade, skipped-minor, major, and malformed transitions fail before mutation.
- Preflight and the mutation boundary compare package identity with Azure `appName`, `installationId`, `runtimeVersion`, and `deploymentExportId` tags.
- Azure What-If and explicit approval remain mandatory for upgrades. Mutation is
  bound to hashes of the canonical package, preview receipt, and What-If artifact.
- Resource names are immutable, SharePoint customer content is preserved, and secret values remain in customer Key Vault.
- Recovery is forward-fix only. Exact target-state recovery requires authorization
  persisted by the original saved session before mutation and bound to its package
  hash and target export. Fresh sessions and changed identities fail closed.
- Upgrade evidence enforces event order, terminal state, redaction, monotonic
  sequence, exact idempotency identity, and exact portal receipt identity.
- The installer does not automatically downgrade or treat an older package as rollback authorization.
- A clean-install package may reconcile an existing group only when all package ownership, installation, target version/export, and resource-name identity tags match exactly; otherwise it fails closed.

Implementation: `UpgradeContractService`, `Test-PM365UpgradeContract`, package schema deployment intent, and versioned Azure tags. Portal generation/callback integration and live staging evidence remain open under #6.
## Lifecycle Event Families

Install events use one stable install attempt and monotonic sequence:

1. `package_validated` or the pre-mutation terminal package-validation failure event.
2. `install_started` with lifecycle status `provisioning`.
3. `azure_deployment_completed`.
4. `runtime_configured` only after protected configuration is written and the required App Service Key Vault references report resolved.
5. `smoke_tests_completed`.
6. `install_completed` with status `completed`, or `install_failed` with status `failed` and a sanitized error.

Removal uses a separate `ra_` attempt and removal-only state machine:

1. `removal_started`.
2. `removal_inventory_completed`, which may repeat for an active inventory refresh while sequence advances.
3. `removal_execution_completed`.
4. `removal_validation_completed`.
5. `removal_completed`, `removal_blocked`, or `removal_failed` as the terminal outcome.

The removal payload may include sanitized removed/retained/skipped/blocked/failed counts and approved disposition categories. It must not include raw Azure inventory exports. Install and removal outboxes are persisted independently so their attempts and event ordering cannot be confused.

## Removal And Recovery Boundaries

- Removal uses the original package tenant, subscription, resource group, application name, deployment export, and ownership tags.
- Inventory and preview do not delete resources.
- Ambiguous ownership, unexpected contained resources, active deployments, or context mismatch blocks removal.
- The installer removes only the dedicated PageMaker365 resource group after explicit confirmation.
- SharePoint content and customer-created SharePoint data are not removed.
- Key Vault purge is never performed. When inventory proves that the package-named vault exists before successful resource-group deletion, final evidence records it as soft-deleted and recoverable for the configured 90-day retention period. A missing or already-absent resource group does not produce an unverified vault-retention claim.
- A later reinstall uses a new package and new disposable Key Vault name during testing.
- Authorized removal callbacks use a distinct `ra_` attempt, ordered removal-only event types, sanitized disposition counts, identity-derived idempotency keys, exact `Accepted` receipt validation, and a persisted outbox. Portal v0.3 acceptance and staging proof remain open under #9.

## Troubleshooting And Correlation

Use the narrowest identifier that follows the failing boundary. Do not paste raw logs or protected values into a ticket.

| Symptom or boundary | Primary identifier | Supporting artifact | Safe escalation content |
| --- | --- | --- | --- |
| Setup connect/readiness/download | Onboarding session ID and API correlation ID | `support-bundle\onboarding\{sessionId}\portal-sync-receipt.json` | UTC time, readiness/error code, package version, sanitized endpoint host |
| Package validation | Deployment export ID and package hash | Local package-trust result; do not attach setup file | Trust status, signing key ID, expected customer/environment alias |
| Azure sign-in/preflight | Azure tenant/subscription aliases and check code | Preflight evidence | Missing role/scope/check code and target scope; no token |
| Graph/SharePoint preflight | Graph tenant alias and check code | Preflight/validation evidence | Missing delegated scope or configured site/library result; no unrelated library list |
| What-If | Azure What-If deployment/correlation ID | `support-bundle\preview\deployment-preview.json` and redacted What-If artifact | Counts, warning code, target resource group |
| Install/runtime configuration | Azure deployment name/correlation and installer attempt ID | `support-bundle\install\deployment-install.json`, Azure deployment artifact, runtime-configuration artifact | Failed phase, sanitized error code, Key Vault reference resolution state; no values |
| Runtime validation | Deployment export ID and validation attempt | `support-bundle\validate\deployment-validation.json` | Expected/observed product and export identity, HTTP status, endpoint host |
| Portal evidence sync | Event ID, attempt ID, sequence, idempotency identity, API correlation ID | Persisted outbox and portal status receipt | Event type/status/outcome and retry count; no raw callback body if it contains restricted identifiers |
| Removal | Removal attempt ID, Azure resource-group operation/correlation | Removal inventory/execution/validation evidence | Ownership result, terminal disposition counts, retained categories |

Before transfer, review the support bundle manifest and selected artifacts. The approved handoff must state who owns the copy, transfer destination, retention period, and deletion responsibility. The assistant/support workflow cannot execute privileged actions, modify a signed package, or bypass normal approval gates.

## Customer Security Review Checklist

- Confirm the exact release version, publisher, certificate thumbprint, and approved distribution channel.
- Approve the Azure subscription and accepted role set assigned to the operator.
- Approve the four delegated Graph scopes and customer consent process.
- Approve HTTPS destinations, proxy inspection behavior, and customer SharePoint/deployed-app endpoints.
- Review Azure resource types, public-network settings, regions, tags, managed identity, Key Vault RBAC, and 90-day soft-delete/no-purge policy.
- Review package/setup-file handling and the separate distribution/package trust anchors.
- Review the exact runtime secret metadata contract and confirm raw values never enter packages, state, arguments, environment variables, callbacks, or support bundles.
- Approve local workspace/session retention and secure deletion responsibilities.
- Approve portal-side evidence schema, idempotency, retention, and support-handoff policy outside this repository.
- Confirm upgrade is excluded until issue #6 is accepted and that removal is Azure-only with no SharePoint mutation.
- Require completed clean-workstation and lifecycle evidence from `docs/testing/customer-lifecycle-acceptance-runbook.md` before production authorization.
- Require the sanitized lifecycle JSON to pass `scripts/validate-customer-lifecycle-result.ps1 -RequireApproval` for the exact release commit before approving publication.

## Known Release Blockers

| Capability | Current state | Issue |
| --- | --- | --- |
| API and portal application delivery | Installer contract implemented in draft PR #22; immutable producers and staging proof pending | #5 |
| Supported upgrade/version policy | Installer contract implemented in draft PR #21; control-plane support and staging proof pending | #6 |
| Runtime secret inventory and protected provisioning | Implemented locally; live staging proof pending | #7 |
| Removal lifecycle callbacks | Installer implemented; portal/API acceptance and staging proof pending | #9 |
| Clean-workstation and repeated lifecycle acceptance | Not complete | #10 |
| Customer user and technical guide approval | Draft only | #11, #12 |
| Production code signing and distribution | Signing and verification implemented; certificate-backed release and clean-workstation proof pending | #13 |
| Assistant portal retention and approved support handoff | Installer boundary implemented; live portal review pending | #28 |

## Verification And Review

`scripts/test-security-contract.ps1` verifies the approved read-only Graph scope set, accepted Azure role combinations, required network destinations, protected runtime provisioning path, managed-identity Key Vault references, and the Bicep role-assignment dependency. `scripts/verify.ps1` runs that contract with the repository build and test suite.

Before customer publication this guide still requires engineering review against a released commit, identity/security review, operations review, clean-workstation acceptance, repeated install/remove/reinstall evidence, and production distribution verification.
