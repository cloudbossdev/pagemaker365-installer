# Install And Uninstall User Stories

## Actors

- Customer administrator: owns the Azure subscription and Microsoft 365 tenant.
- Installer operator: runs the guided desktop workflow and records approvals.
- PageMaker365 control plane: issues customer-bound packages and records lifecycle evidence.
- PageMaker365 support: receives sanitized evidence when customer-approved support is required.

## Install Or Update

As an authorized installer operator, I want to deploy or update PageMaker365 through a guided and reviewable workflow so that customer administrators do not need to run raw scripts and every customer-environment change is validated, approved, and evidenced.

Acceptance flow:

1. Choose the control-plane setup file and let the installer retrieve the immutable customer install package.
2. Verify package hash, trust policy, tenant, onboarding session, and deployment export.
3. Sign in to the package Azure tenant/subscription and Microsoft Graph tenant.
4. Run package, workstation, Azure, Entra, Graph, SharePoint, and recovery-state preflight checks.
5. Run Azure What-If and save a reviewable preview artifact.
6. Require approval and exact resource-group confirmation.
7. Deploy the approved resources and save deployment evidence.
8. Verify the deployment-bound API identity, portal content, and SharePoint access. An unrelated custom domain or Azure default page must fail validation.
9. Generate final evidence and report completion to the control plane.
10. Preserve incomplete portal callbacks in a local retry outbox without changing the Azure result.

The installer must be resumable, must not persist access tokens or secrets, and must require sign-in or approval again when those authorizations cannot be safely restored.

## Azure-Only Uninstall V1

As an authorized customer administrator, I want the dedicated PageMaker365 Azure deployment removed through an inventory-first workflow so that PageMaker365-owned Azure resources are deleted without modifying SharePoint content, shared customer resources, or ambiguous resources.

Acceptance flow:

1. Load the original customer package and bind removal to its tenant, subscription, resource group, application tag, and deployment export.
2. Sign in to the package Azure tenant and subscription.
3. Inventory the dedicated resource group without deleting anything.
4. Prove ownership using the signed package and PageMaker365 resource tags.
5. Block removal when a deployment is active, ownership is ambiguous, the subscription differs, or any contained resource is not owned by the package.
6. Save and display an immutable removal preview.
7. Require explicit approval and exact resource-group confirmation.
8. Delete the approved dedicated resource group.
9. Never purge Key Vault. Record the old vault as soft-deleted and recoverable.
10. Validate that the resource group is absent and save a final removal report.

SharePoint is not a removal target. PageMaker365 uses the customer's existing SharePoint content as a backend; uninstalling the Azure runtime does not alter that content or normal SharePoint behavior.

## Reinstall After Uninstall

As a test operator, I want to install, uninstall, and install again using a newly generated package so that repeated lifecycle testing proves package generation, deployment, cleanup, and portal state behavior without depending on recovery or purge of the old Key Vault.

For each reinstall:

- Generate a new immutable deployment export.
- Generate a new package with a different Key Vault name.
- Treat the previous soft-deleted vault as retained evidence, not as a deployment dependency.
- Use disposable testing names that satisfy Azure naming requirements and do not carry customer meaning.
- Record the relationship between the previous removal attempt and the new install attempt in test evidence.

## Out Of Scope For Uninstall V1

- Deleting or changing SharePoint content, libraries, sites, permissions, or customer data.
- Purging or automatically recovering a soft-deleted Key Vault.
- Deleting shared Azure resources or resources with ambiguous ownership.
- General Entra application, service-principal, or tenant-consent cleanup until immutable ownership IDs are recorded during install.
- Broad Microsoft 365 or tenant discovery.
