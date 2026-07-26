# Installer Requirements Traceability

Status: active working record

This document prevents drift between requirements, implementation, tests, live evidence, and customer documentation. A status of `Implemented` is not equivalent to `Verified`.

## Status Vocabulary

- `Verified`: implementation, applicable automated tests, and required live evidence exist.
- `Implemented`: code exists and local tests pass, but required live evidence is incomplete.
- `Partial`: only part of the acceptance criteria is implemented or tested.
- `Planned`: no complete implementation exists.
- `Blocked`: an external contract or prerequisite prevents meaningful verification.

## Story Traceability

| Story | Primary workflow surface | Scenario groups | Automated evidence | Live evidence | Customer documentation | Status | Issue |
| --- | --- | --- | --- | --- | --- | --- | --- |
| US-01 Start or resume | Welcome | L05, S01-S05 | App workflow tests cover saved-session load and resume | Resume across every destructive boundary is pending | User guide draft | Implemented | [#3](https://github.com/cloudbossdev/pagemaker365-installer/issues/3) |
| US-02 Acquire package | Package | P01-P09 | Engine/API and app package tests | Fresh staging setup-file acquisition has been exercised; expiry/reuse matrix pending | User and technical drafts | Partial | [#4](https://github.com/cloudbossdev/pagemaker365-installer/issues/4) |
| US-03 Discover missing data | Package recovery | P10-P13 | Azure/Graph discovery and portal app tests | Missing-field recovery requires repeat staging evidence | User and technical drafts | Implemented | [#4](https://github.com/cloudbossdev/pagemaker365-installer/issues/4) |
| US-04 Authenticate | Sign In | A01-A09 | Engine Graph tests and sign-in gating app tests | Wrong-context, cancellation, and expiry suite pending | User and technical drafts | Partial | [#4](https://github.com/cloudbossdev/pagemaker365-installer/issues/4) |
| US-05 Preflight | Preflight | F01-F12 | PowerShell preflight, Azure role-set, Key Vault recovery, and contract tests | Full permission/quota matrix pending | User and technical drafts | Partial | [#4](https://github.com/cloudbossdev/pagemaker365-installer/issues/4) |
| US-06 Preview and approve | Preview, Install gate | L01, D01-D05 | What-if and deployment approval tests | Warning/no-change/capacity variants pending | User guide draft | Implemented | [#10](https://github.com/cloudbossdev/pagemaker365-installer/issues/10) |
| US-07 Clean install | Install | L01, L04, D06-D10 | Deployment, runtime-secret contract, protected-input, Key Vault reference, state-leakage, and artifact tests | Azure resources deployed previously; fresh `0.3` package runtime provisioning proof pending | User and technical drafts | Implemented | [#5](https://github.com/cloudbossdev/pagemaker365-installer/issues/5), [#7](https://github.com/cloudbossdev/pagemaker365-installer/issues/7) |
| US-08 Upgrade | Install/update | U01-U08 | None sufficient | Not run | Explicitly marked unsupported/TBD | Planned | [#6](https://github.com/cloudbossdev/pagemaker365-installer/issues/6) |
| US-09 Validate runtime | Validate | L07-L09, V01-V07 | Runtime smoke contract tests | Cannot pass until runtime applications deploy | User and technical drafts | Blocked | [#5](https://github.com/cloudbossdev/pagemaker365-installer/issues/5) |
| US-10 Finish and synchronize | Finish, Current Session | L06, E01-E08 | Evidence callback/outbox tests | Successful terminal staging callback after verified runtime is pending | User and technical drafts | Partial | [#5](https://github.com/cloudbossdev/pagemaker365-installer/issues/5) |
| US-11 Recover failure | All long-running steps | L04-L06, S01-S05, D08-D10 | Timeout, cancellation, resume, cleanup, and outbox tests | Full interruption matrix pending | User guide draft | Partial | [#4](https://github.com/cloudbossdev/pagemaker365-installer/issues/4) |
| US-12 Troubleshoot and support | Guidance, support bundle | T01-T06 | Redaction/repository/package checks cover part of contract | Support handoff review pending | User and technical drafts | Partial | [#12](https://github.com/cloudbossdev/pagemaker365-installer/issues/12) |
| US-13 Inventory uninstall | Removal inventory/preview | R01-R08 | Partial cleanup safety tests | UI workflow and staging inventory evidence pending | User and technical drafts | Implemented | [#10](https://github.com/cloudbossdev/pagemaker365-installer/issues/10) |
| US-14 Execute uninstall | Removal approval/validation | R09-R13, E09-E14 | Partial cleanup and Key Vault retention tests | Complete staging removal and portal sync pending | User and technical drafts | Partial | [#9](https://github.com/cloudbossdev/pagemaker365-installer/issues/9) |
| US-15 Reinstall | New setup session | L02-L03, R14-R15 | Key Vault recovery and package provenance tests | Three consecutive lifecycle runs pending | User guide draft | Partial | [#10](https://github.com/cloudbossdev/pagemaker365-installer/issues/10) |

## Release-Blocking Decisions

| Decision | Required evidence | Issue |
| --- | --- | --- |
| Exact Azure roles and scope | Implemented contract accepts Owner, or Contributor plus RBAC Administrator/User Access Administrator, at subscription scope; live negative-role evidence remains | [#8](https://github.com/cloudbossdev/pagemaker365-installer/issues/8) |
| Exact Graph/Entra/SharePoint permissions | Implemented read-only delegated scope contract is User.Read, Domain.Read.All, RoleManagement.Read.Directory, and Sites.Read.All; live consent variants remain | [#8](https://github.com/cloudbossdev/pagemaker365-installer/issues/8) |
| Runtime artifact delivery | Reproducible API/portal deployment and deployment-bound health evidence | [#5](https://github.com/cloudbossdev/pagemaker365-installer/issues/5) |
| Runtime secret inventory and source | `0.3` contract and local protected provisioning tests implemented; fresh signed staging package and live reference-resolution evidence remain | [#7](https://github.com/cloudbossdev/pagemaker365-installer/issues/7) |
| Upgrade compatibility | Version policy, preview semantics, recovery, and live upgrade evidence | [#6](https://github.com/cloudbossdev/pagemaker365-installer/issues/6) |
| Network allowlist | Machine-readable endpoint inventory and trusted PageMaker365 host enforcement implemented; enterprise proxy acceptance remains | [#8](https://github.com/cloudbossdev/pagemaker365-installer/issues/8) |
| Removal portal lifecycle | Hardened event contract, outbox, API tests, and staging proof | [#9](https://github.com/cloudbossdev/pagemaker365-installer/issues/9) |
| Customer distribution | Code-signing chain, clean-workstation launch, hashes, and release evidence | [#13](https://github.com/cloudbossdev/pagemaker365-installer/issues/13) |

## Pull Request Requirement

Every implementation pull request in this milestone must identify:

- Story IDs changed.
- Scenario IDs added or executed.
- Security controls affected.
- Automated commands run.
- Live evidence location when required.
- Customer documents updated or an explanation of why no customer-facing behavior changed.
