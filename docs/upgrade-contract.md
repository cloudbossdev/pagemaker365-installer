# PageMaker365 Upgrade Contract

Status: installer contract implemented; portal package generation, upgrade callback
acceptance, and live staging proof pending under issue #6.

## Purpose

An upgrade is not a clean install against an existing resource group. It is a
separate, package-authorized operation bound to the runtime version and deployment
export already recorded in Azure. Unsupported or ambiguous transitions fail before
What-If or deployment.

## Package Contract

Production packages include a `deployment` object:

```json
{
  "operation": "upgrade",
  "sourceRuntimeVersion": "1.4.2",
  "targetRuntimeVersion": "1.5.0",
  "sourceDeploymentExportId": "<immutable-source-export>",
  "minimumInstallerVersion": "0.1.0",
  "failureRecovery": "ForwardFix",
  "resourceNamePolicy": "Immutable",
  "sharePointDataPolicy": "Preserve"
}
```

`operation` is `install` or `upgrade`. A clean-install package omits both source
fields and can proceed when the target resource group is absent. The same immutable
package may also reconcile its own deployment only when every ownership,
installation, target version, target export, and resource-name tag matches. It
cannot adopt a different existing group. A signed package without deployment intent
is rejected. An unsigned legacy package can be used only as a clean install against
an absent resource group.

All version fields use stable `major.minor.patch` semantic versions. The package is
rejected when `minimumInstallerVersion` is newer than the running installer.

## Supported Transitions

The v1 installer supports:

- a higher patch version within the same major/minor version;
- the immediately adjacent minor version within the same major version.

It rejects no-op transitions, downgrades, skipped minor versions, major-version
changes, malformed versions, and missing source identity. A later policy can add a
major-version migration only with its own explicit data and rollback contract.

## Azure Source Reconciliation

Successful PageMaker365 deployments record these ownership and version tags on the
dedicated resource group and managed resources:

- `product=PageMaker365`
- `managedBy=PageMaker365`
- `appName`
- `installationId`
- `runtimeVersion`
- `deploymentExportId`
- `packageContractVersion`
- `resourceNamesHash`

Before preview and again immediately before deployment, the installer requires an
upgrade package to match `appName`, `installationId`, `sourceRuntimeVersion`, and
`sourceDeploymentExportId`. A deterministic `resourceNamesHash` must also match the
package resource-name map. Missing or mismatched identity, an absent source group,
or an active Azure deployment blocks mutation. The installer never infers a source
version from URLs, resource names, or portal status.

## Preview And Approval

Azure What-If remains mandatory. It must show the exact create, modify, deploy,
delete, ignore, unknown, and blocked counts for the target package. Approval is
bound to that preview and package. `resourceNamePolicy=Immutable` means an upgrade
cannot rename or replace package-owned resources through a new package identity.

The deployment writes the target runtime version and target deployment export only
after Azure accepts the approved deployment. Smoke tests then verify the runtime
against the target deployment export.

## Customer Data And Secrets

`sharePointDataPolicy=Preserve` is mandatory. Upgrade does not delete or rewrite
SharePoint sites, libraries, pages, files, or other customer-created content.
Infrastructure changes remain limited to the dedicated PageMaker365 Azure resource
group. Runtime secret values remain in the customer Key Vault and are never placed
in the package, evidence callbacks, logs, or support bundles.

## Failure And Recovery

The v1 policy is `ForwardFix`:

1. A failure before Azure mutation is corrected and previewed again.
2. A failure during Azure deployment is reconciled against live deployment state.
3. The same immutable upgrade package may be retried only after source/target state
   still matches the package contract.
4. If the original saved session already contains successful deployment evidence,
   the operator resumes that session at validation. A fresh upgrade attempt still
   requires the live Azure source identity to match and fails closed when Azure
   already reports the target identity; it never replays a blind deployment.
5. Automatic downgrade and automatic restoration of an older template are not
   supported.

The previous package and evidence are retained for diagnosis, but possession of an
older package is not rollback authorization. A true rollback requires a separately
approved package and compatibility policy.

## Upgrade Evidence Lifecycle

Upgrade uses a distinct `ua_<opaque-id>` attempt. Package rejection before mutation
emits terminal `upgrade_package_validation_failed`. A valid attempt then uses these
ordered event types:

1. `upgrade_package_validated`
2. `upgrade_started`
3. `upgrade_deployment_completed`
4. `upgrade_runtime_configured`
5. `upgrade_validation_completed`
6. `upgrade_completed`

`upgrade_failed` is terminal only after `upgrade_started`. Every event includes
`lifecycle=upgrade`, `operation=upgrade`, source and target runtime versions,
deployment export, stable event/attempt identity, sequence, and idempotency key.
Portal sync failure remains in the installer outbox and never changes the Azure
result. Install and upgrade state machines must not share ordering or terminal
state.

Installations created before the version and ownership tags in this contract are
not automatically upgrade eligible. They require a clean removal/reinstall or a
future, separately approved adoption workflow; the installer does not infer or
backfill source identity.

The portal/API must add the upgrade events and lifecycle state before a staging
upgrade can be accepted. Customer surfaces should show Upgrade Preparing,
Upgrading, Validating Upgrade, Upgrade Complete, Upgrade Failed, and Sync Pending.

## Live Acceptance Gate

Issue #6 remains open until staging proves:

- a supported patch transition;
- an adjacent minor transition;
- rejected downgrade, skipped-minor, major, wrong-source, and stale-export cases;
- preview approval and target identity validation;
- interrupted deployment reconciliation and same-package forward-fix retry;
- SharePoint content and Key Vault secret preservation;
- ordered portal callbacks, duplicate handling, offline outbox retry, and terminal
  state;
- customer evidence showing source, target, outcome, and no prohibited data.
