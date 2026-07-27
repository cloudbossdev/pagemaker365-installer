# PageMaker365 Removal Policy

Status: approved Azure-only v1 policy for implementation and sandbox testing.

## Policy Principles

1. Inventory before deletion.
2. Fail closed when ownership or activity is ambiguous.
3. Require explicit local approval for every destructive run.
4. Delete only the dedicated PageMaker365 resource group proven by the customer package and resource tags.
5. Never delete SharePoint content or perform SharePoint cleanup.
6. Never purge Key Vault through the installer.
7. Use a newly generated package and new Key Vault name for a later reinstall.
8. Make cleanup idempotent and safe to resume or rerun.
9. Preserve sanitized evidence of removed, retained, skipped, blocked, and failed items.

## Ownership Evidence

Automatic removal requires all of the following:

- The signed or hash-verified package identifies the active Azure subscription and resource group.
- The signed-in Azure context matches the package tenant and subscription.
- The resource group carries the expected PageMaker365 ownership tags.
- Every resource in the group carries the expected package application tag.
- No Azure deployment is active in the resource group.

Missing or conflicting evidence blocks removal. The operator cannot override an ownership blocker from the installer UI.

## Removal Categories

### Remove After Approval

- The dedicated PageMaker365 Azure resource group and resources contained in it when all ownership checks pass.

### Retain

- The package-named Key Vault and its recoverability metadata when inventory established that the vault existed before approved deletion.
- Customer SharePoint sites, libraries, documents, lists, and other content.
- Local and control-plane audit/evidence records.
- Any shared or ambiguous Azure resource.

### Not Managed In V1

- Entra app registrations and enterprise applications.
- Microsoft Graph consent and tenant-wide grants.
- Customer DNS and certificates outside the dedicated resource group.
- Customer portal account deletion or commercial offboarding.

## Approval

Removal requires:

- A current inventory and preview from the same package and resource group.
- An inventory result marked safe to remove.
- A checked destructive-action approval.
- The exact resource-group name typed by the operator.
- Standard PowerShell `ShouldProcess` behavior for command-line execution.

Approval is not restored after the installer restarts.

## Reinstall Policy

The control plane must generate a new deployment export and a package with a new Key Vault name. Testing names are disposable identifiers and must not contain customer secrets or business data. The installer must not purge the old vault to make its name reusable.

## Control-Plane Reporting

The v1 installer writes local removal inventory, execution, validation, report, manifest, and bundle artifacts. It records Key Vault presence before deletion and does not infer a retained vault when the resource group was already absent or the vault was never created. It also implements the distinct v0.3 removal lifecycle and persisted retry outbox defined in `removal-evidence-callback-contract.md`. Callbacks require the existing `RemovalStatusSync` authorization operation and never reuse install event types or install state. Portal/control-plane acceptance, UI state, API negative tests, and staging proof remain pending under issue #9; delivery failure or a mismatched/non-accepted receipt leaves the original event queued and never changes the Azure removal result.
