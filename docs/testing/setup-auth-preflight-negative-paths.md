# Setup, Authentication, And Preflight Negative-Path Runbook

Status: active staging test plan

Tracking issue: [#4](https://github.com/cloudbossdev/pagemaker365-installer/issues/4)

Cross-app UI audit: `docs/testing/long-running-action-state-audit.md`

## Purpose

Use this runbook to collect live evidence for setup-file, authentication, and preflight behavior that cannot be proven by local mocks alone. Run only in a dedicated test tenant and subscription.

## Safety Rules

- Use staging-generated setup files and packages with disposable names.
- Do not commit or paste setup codes, tokens, customer packages, raw logs, or tenant exports.
- Stop before deployment unless the test case explicitly requires preview or install.
- Save only the installer's sanitized evidence and screenshots with codes, account names, tenant IDs, and subscription IDs redacted.
- Record installer commit, package version, deployment export ID, scenario ID, timestamp, and result.

## Setup Authorization

| Scenario | Procedure | Pass condition |
| --- | --- | --- |
| P01 | Select a fresh staging setup file whose package is ready. | One action connects, downloads, validates, and advances to Sign In. |
| P02 | Select a fresh setup file while the portal reports package generation pending. | Progress remains active, polling stops within policy, and the action completes or becomes retryable. |
| P03-expired | Select a setup file whose local expiration has passed. | File is rejected on Package and the operator is told to request a new setup file. |
| P03-used | Reuse a one-time setup authorization already rejected or consumed by staging. | Portal rejection remains on Package, includes a correlation ID, and directs the operator to a new setup file. |
| P12 | Block the staging API with the workstation firewall or an approved test proxy rule. | No sign-in step unlocks; the UI returns to idle and Retry remains available. |

## Azure Authentication

| Scenario | Procedure | Pass condition |
| --- | --- | --- |
| A01 | Sign in to the correct Azure tenant/subscription, then Graph. | Azure alone does not complete Sign In; both contexts are required. |
| A02 | Sign in to Graph first, then the correct Azure context. | Order does not bypass either requirement. |
| A04-tenant | Attempt Azure sign-in with an account from a different tenant. | Sign-in or preflight blocks and identifies the tenant mismatch. |
| A04-subscription | Use the correct tenant but select a different subscription. | Sign-in or preflight blocks and identifies the subscription mismatch. |
| A06-Azure | Cancel the Azure browser/device flow. | The installer reports a sanitized `AzureSignInCanceled` result, returns to idle, stays on Sign In, and permits retry. |

## Microsoft Graph Authentication

| Scenario | Procedure | Pass condition |
| --- | --- | --- |
| A03 | Complete Azure sign-in only. | Graph remains Required and Preflight stays disabled. |
| A05 | Complete Graph device login with an account from another tenant when the authority permits selection. | Sign In remains incomplete and identifies the tenant mismatch. |
| A06-Graph | Cancel the Graph device-code flow. | The installer reports `GraphSignInCanceled`, returns to idle, clears the code, stays on Sign In, and permits retry. |
| A07-code | Allow the Graph device code to expire without authentication. | The installer reports `GraphSignInExpired`, clears the stale code, and requires a new sign-in. |
| A07-token | Allow a short-lived test access token to expire before continuing. | Graph becomes invalid and a new sign-in is required before Preflight. |
| A08 | Use a test account that cannot consent to one or more required scopes. | Missing scopes/admin readiness are explicit; the installer does not grant access automatically. |

## Preflight

| Scenario | Procedure | Pass condition |
| --- | --- | --- |
| F02 | Run on a disposable workstation image without one required module or Bicep. | Missing dependency returns `Failed`, blocks Preview, and identifies remediation. |
| F03 | Use an account intentionally lacking deployment access at the target scope. | Preflight does not represent RBAC as ready; deployment remains blocked under the final permission policy. |
| F04 | Use a Graph session missing final required consent. | `GraphConsentScopesMissing` fails preflight; dependent SharePoint checks do not represent the target as ready. |
| F05 | Use a package that targets a nonexistent or inaccessible SharePoint site/library. | Site and library failures are distinguished, block Preview, and do not read document content or export unrelated library names. |
| F08 | Create the target resource group without PageMaker365 ownership tags. | Preview/install fail closed and do not adopt the resource group. |
| F09-provider | Unregister one required provider in a disposable subscription, or use an approved test fixture that returns `NotRegistered`. | Preflight fails with the provider namespace and does not start preview. |
| F09-sku | Use a subscription/region combination for which Azure omits B1 from the subscription SKU response. | Preflight fails with `AppServiceSkuUnavailable` and directs the operator to a newly approved region or subscription remediation. |
| F09-quota | Exhaust the App Service core quota in a disposable region, or use an approved response fixture with no remaining core count. | Preflight fails with `AppServiceQuotaExhausted`; no deployment starts. |
| F09-capacity | Run only in a region known to return an allocation-time capacity conflict after provider, SKU, and quota checks pass. | Install fails safely and remains retryable. Evidence and UI do not imply that preflight reserved capacity. |
| F10 | Generate a package that reuses a retained soft-deleted Key Vault name. | Preflight blocks before deployment and requests recovery or a new package/name. |
| F12 | Correct one blocker and rerun Preflight in the same session. | Results update without restarting and previously valid sign-ins remain valid only when their tokens are current. |

## Required Evidence

For each run, record:

- Scenario ID and pass/fail result.
- Installer commit and build version.
- Sanitized setup session ID, deployment export ID, tenant/subscription suffixes, and correlation ID.
- Relevant installer evidence path and a redacted screenshot of the resulting UI state.
- Confirmation that no token, setup code, secret, document content, or raw tenant export appears in the evidence.
- Confirmation that the activity indicator remains visible for the awaited operation, duplicate execution is unavailable, and the UI returns to a retryable idle state.
- GitHub issue link for every failure or contract ambiguity.

Live evidence should be summarized in `docs/installer-requirements-traceability.md`; sensitive source artifacts remain outside the repository.
