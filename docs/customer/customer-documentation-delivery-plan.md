# PageMaker365 Installer Customer Documentation Delivery Plan

Status: active working plan

GitHub milestone: [Installer Customer Readiness v1](https://github.com/cloudbossdev/pagemaker365-installer/milestone/1)

Tracking issues: [#11 customer user guide](https://github.com/cloudbossdev/pagemaker365-installer/issues/11) and [#12 technical/security guide](https://github.com/cloudbossdev/pagemaker365-installer/issues/12)

## Objective

Publish two customer documents whose instructions and technical claims match the released installer, are traceable to all fifteen canonical user stories, and are supported by automated tests and live lifecycle evidence.

The documents are:

1. A task-focused user guide for authorized customer operators.
2. A technical and security guide for architecture, identity, security, networking, operations, and support review.

This branch establishes the documentation structure, evidence gates, review responsibilities, and publication controls. Merging this delivery plan does not approve either customer guide: both guide files must retain their controlled-draft status until the release and evidence gates below are satisfied. Product behavior changes must be implemented and reviewed on their own issue-linked branches before the guides describe them as supported.

## Audience And Content Boundaries

| Deliverable | Primary audience | Covers | Does not cover |
| --- | --- | --- | --- |
| Customer user guide | Installer operator and customer administrator | Prerequisites, install, warnings and recovery, validation, finish, removal, reinstall, evidence, and escalation | Internal implementation details, raw scripts, unsupported workarounds, or unverified roadmap behavior |
| Technical and security guide | Security reviewer, cloud architect, identity administrator, operations, and support | Architecture, permissions, resources, network flows, package trust, secrets, storage, telemetry, evidence, recovery, removal boundaries, and troubleshooting artifacts | Marketing claims, assumed controls, planned capabilities presented as current, secrets, customer data, or raw tenant output |

`docs/using-the-installer.md` remains an engineering/operator reference and is not a substitute for either customer publication.

## Story Coverage

Every canonical story must be addressed in at least one guide. A story is not publication-ready while its traceability status is `Planned`, `Blocked`, or `Partial` for a release-critical acceptance criterion.

| Story | User guide content | Technical/security content | Publication dependency |
| --- | --- | --- | --- |
| US-01 Start or resume | Start, resume, start new, forget, and non-restored authorization | Local state, retention, and restart security boundaries | #4, #10 |
| US-02 Acquire package | Normal one-file setup flow and controlled local-package fallback | Session binding, hash, signature, trusted origins, expiration, and replay controls | #4, #10 |
| US-03 Discover missing data | Conditional discovery and retry flow | Read-only discovery scope and prohibited data | #4, #10 |
| US-04 Authenticate | Separate Azure and Graph sign-ins, device code, retry, and wrong-context recovery | OAuth flow, tenant binding, delegated scopes, consent, and token handling | #4, #10 |
| US-05 Preflight | Passed, warning, blocker, corrective action, and rerun states | Role sets, quotas, ownership, capacity, Key Vault recovery, and fail-closed controls | #4, #7, #10 |
| US-06 Preview and approve | What-If review, warnings, approval, and typed confirmation | Preview evidence, approval invalidation, and mutation boundary | #4, #10 |
| US-07 Clean install | Visible progress, safe retry, partial failure, and completion boundaries | Deployed resources, runtime artifacts, identity, correlation, and partial-state recovery | #5, #7, #10 |
| US-08 Upgrade | Supported upgrade steps and recovery only after verified | Version compatibility, migrations, rollback boundary, and retained data | #6, #10 |
| US-09 Validate runtime | Smoke tests, warning/failure recovery, and verified URL | Deployment-bound identity checks and false-positive prevention | #5, #7, #10 |
| US-10 Finish and synchronize | Final report, customer URL, portal status, and retryable sync | Evidence schema, idempotency, outbox, sanitization, and retention | #5, #7, #10 |
| US-11 Recover | Cancellation, interruption, resume, retry, and safe cleanup entry | Reconciliation, idempotency, correlation, and destructive-boundary controls | #4, #5, #7, #9, #10 |
| US-12 Troubleshoot | Actionable messages, support bundle, and escalation | Sanitized artifacts, correlation identifiers, redaction, attachment transfer, and support boundaries | #10, #28 |
| US-13 Inventory removal | Inventory, preview, retained items, blockers, and approval | Immutable ownership proof and ambiguity handling | #9, #10 |
| US-14 Execute removal | Confirmation, progress, verification, retained Key Vault, and no SharePoint cleanup | Resource-group deletion, revalidation, no purge, callbacks, and evidence | #9, #10 |
| US-15 Reinstall | Obtain a new setup file/package and repeat the guided install | New export/attempt/Key Vault identity and repeat-cycle evidence | #9, #10 |

## Evidence Gates

A customer-facing instruction or technical claim can move from draft to approved only when all applicable gates pass:

1. The canonical story and scenario IDs are identified.
2. The released UI or technical contract implements the behavior.
3. Deterministic behavior has automated coverage in `scripts/verify.ps1` or a linked test suite.
4. Azure, Graph, SharePoint, portal, removal, or reinstall behavior has current staging evidence where local tests are insufficient.
5. The traceability matrix links the implementation, test, evidence, guide section, and GitHub issue.
6. Screenshots come from the release candidate, contain no customer identifiers or secrets, and show the complete action and state being documented.
7. Engineering reviews procedural accuracy; security/identity reviews access and data-handling claims; operations/support reviews recovery and troubleshooting.
8. CI is green on the guide pull request and on every implementation dependency.

Unknown or incomplete behavior must be labeled as a release blocker in the controlled draft. It must not be converted into an implied customer guarantee.

## Branch And Pull Request Strategy

- `docs/customer-guides-v1` establishes the delivery plan and initial guide controls.
- Each product gap uses a branch named `issue/<number>-<short-description>` from current `main`.
- Product pull requests include implementation, tests, traceability changes, and any immediately affected draft language.
- Product pull requests merge before the documentation branch records the capability as supported.
- The delivery-plan pull request may merge before implementation dependencies so CI enforces the guide controls early; this does not change either guide from controlled draft to approved.
- The final publication pull request starts from current `main` after all implementation, release, and live-evidence dependencies land.
- No direct customer-publication changes are made on `main`.
- The final documentation pull request links #11, #12, the readiness epic #2, the release candidate commit, acceptance evidence, and reviewer approvals.

## Delivery Sequence

| Order | Work | Exit condition |
| --- | --- | --- |
| 1 | Stabilize requirements and technical security baseline | #3 and #8 closed; story catalog and security profile enforced by CI |
| 2 | Close setup, authentication, and preflight negative paths | #4 accepted with automated and staging evidence |
| 3 | Implement protected runtime configuration | #7 accepted; secrets are written to customer Key Vault and never persisted or reported |
| 4 | Deploy and identify the real runtime | #5 accepted; API and portal artifacts pass deployment-bound validation |
| 5 | Define the supported upgrade contract | #6 accepted or explicitly excluded from v1 with approved customer wording |
| 6 | Harden removal reporting | #9 accepted; removal callbacks and retry outbox are verified |
| 7 | Harden assistant and support handoff | #28 accepted; portal retention, attachment transfer, local actions, and draft ownership are verified |
| 8 | Run lifecycle acceptance | #10 accepted on a clean workstation, including repeated install/remove/reinstall |
| 9 | Sign and verify the release candidate | #13 accepted; the distribution is signed and passes release verification |
| 10 | Finalize customer documents | #11 and #12 reference the signed release candidate and pass operator, engineering, security, and operations review |

Work may be drafted in parallel, but publication approval follows this dependency order.

## Review Responsibilities

| Review role | Required decision |
| --- | --- |
| Product owner | Scope, supported workflows, terminology, and customer outcome are correct |
| Installer engineering | Every procedure matches the release candidate and recovery behavior |
| Runtime/API engineering | Deployed application, health, evidence, and portal synchronization claims are correct |
| Identity and security | Roles, Graph scopes, consent, managed identity, Key Vault, trust, redaction, and retention claims are correct |
| Operations/support | Logs, evidence, correlation, retained resources, failure recovery, and escalation are usable |
| Clean test operator | A new operator can complete the documented lifecycle without undocumented assistance |

An individual may hold more than one role, but each decision must be recorded in the guide pull request.

## Documentation Definition Of Done

- All fifteen stories are covered and linked to scenario/evidence records.
- The user guide supports install, recovery, Azure-only removal, and reinstall from a clean workstation.
- The technical guide documents exact permissions, resources, endpoints, storage, secret handling, telemetry, evidence, retention, and removal boundaries.
- UI labels, button names, ordering, warnings, and screenshots match the signed release candidate.
- No placeholder, unsupported roadmap feature, secret, customer identifier, or raw evidence is published.
- Accessibility-relevant instructions do not rely on color alone and screenshots have meaningful alternative text in the publication system.
- The release version and document revision date are explicit.
- #11 and #12 contain approval evidence and are closed only after the documentation pull request merges.

## Immediate Actions

1. Merge the delivery-plan and CI controls while keeping both guide files as controlled drafts until #4 through #10, #13, and #28 are accepted.
2. Integrate issue #7 before issue #5 so runtime deployment consumes the approved Key Vault-backed configuration contract; do not add actual secret values to packages, logs, evidence, or tests.
3. Update the controlled drafts through each implementation pull request, replacing blocker language only with traceable verified behavior.
4. During #10, complete a clean-operator walkthrough using the user guide alone and record the screenshot states needed for publication.
5. After #13 produces the signed release candidate, capture sanitized final screenshots, request the required reviews, and record decisions in the documentation pull request.
