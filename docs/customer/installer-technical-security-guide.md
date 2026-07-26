# PageMaker365 Installer Technical And Security Guide

Status: controlled draft; not approved for customer publication

Tracking issue: [#12](https://github.com/cloudbossdev/pagemaker365-installer/issues/12)

## Purpose

This guide will provide customer architecture, identity, security, and operations teams with the verified technical information needed to evaluate and troubleshoot PageMaker365 installation in a customer tenant.

No value marked `TBD` may be represented as a production guarantee. The final guide must cite implementation and test evidence through `docs/installer-requirements-traceability.md`.

## Publication Outline

1. Product scope and shared-responsibility boundaries
2. Control plane, desktop installer, Azure runtime, and Microsoft 365 data flow
3. Supported workstation and distribution requirements
4. Azure resources, regions, naming, tags, and ownership boundaries
5. Azure RBAC permissions and deployment scopes
6. Entra, Microsoft Graph, and SharePoint permissions and consent
7. Network destinations, DNS, ports, TLS, proxy, and firewall requirements
8. Setup-file authorization and package provenance
9. SHA-256 package integrity and Ed25519 signature verification
10. Authentication tokens and local session state
11. Runtime secret provisioning and customer Key Vault
12. Managed identity and application configuration
13. Logs, evidence, callbacks, outbox, redaction, and retention
14. Runtime health and deployment identity validation
15. Upgrade, recovery, uninstall, and retained-resource controls
16. Telemetry and Application Insights
17. Support bundles, correlation IDs, and troubleshooting
18. Versioning, known limitations, and change management

## Verified Baseline

The current repository verifies these controls locally and in CI:

- Customer packages are schema-validated and can require hash and Ed25519 signature verification.
- Portal-downloaded packages are bound to the active onboarding session, tenant, discovery record, and deployment export.
- Raw secret containers are rejected and repository/release-package hygiene checks prevent generated customer artifacts from shipping.
- Azure What-If precedes approved deployment and saves a reviewable artifact.
- Removal fails closed on tenant, subscription, ownership, contained-resource, or active-deployment ambiguity.
- Key Vault purge is not an installer operation.
- Runtime validation rejects Azure default content, unrelated portal content, and mismatched deployment export identity.
- Lifecycle callback failures remain retryable and do not redefine the Azure operation result.

These are implementation-level controls, not proof that the complete customer lifecycle has passed staging acceptance.

## Release-Blocking Technical Specifications

| Specification | Current state | Tracking issue |
| --- | --- | --- |
| Exact Azure least-privilege roles | TBD | [#8](https://github.com/cloudbossdev/pagemaker365-installer/issues/8) |
| Exact Graph/Entra/SharePoint scopes and consent roles | TBD | [#8](https://github.com/cloudbossdev/pagemaker365-installer/issues/8) |
| Complete outbound network allowlist | TBD | [#8](https://github.com/cloudbossdev/pagemaker365-installer/issues/8) |
| Runtime secret inventory and protected provisioning | Not implemented | [#7](https://github.com/cloudbossdev/pagemaker365-installer/issues/7) |
| API and portal application deployment | Not implemented | [#5](https://github.com/cloudbossdev/pagemaker365-installer/issues/5) |
| Supported upgrade/version policy | Not defined | [#6](https://github.com/cloudbossdev/pagemaker365-installer/issues/6) |
| Removal lifecycle callback contract | Not implemented | [#9](https://github.com/cloudbossdev/pagemaker365-installer/issues/9) |
| Production code signing and distribution | Not implemented | [#13](https://github.com/cloudbossdev/pagemaker365-installer/issues/13) |
| Retention periods for logs, evidence, and local sessions | TBD | [#8](https://github.com/cloudbossdev/pagemaker365-installer/issues/8) |

## Review Requirements

Before publication, this guide must receive:

- Engineering review against the released commit.
- Identity/security review of requested permissions and data flows.
- Operations review of diagnostics, evidence, retention, and escalation.
- Live acceptance evidence from the clean install, recovery, removal, and reinstall matrix.
