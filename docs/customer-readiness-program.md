# Installer Customer Readiness Program

Status: active

GitHub milestone: [Installer Customer Readiness v1](https://github.com/cloudbossdev/pagemaker365-installer/milestone/1)

Umbrella issue: [#2 Epic: Installer customer readiness v1](https://github.com/cloudbossdev/pagemaker365-installer/issues/2)

## Objective

Deliver a customer-ready PageMaker365 Installer whose documented user journeys, technical security claims, implementation, automated tests, and live lifecycle evidence agree.

The program produces two customer deliverables:

1. A knowledge-base user guide for authorized customer operators.
2. A technical and security guide for architecture review, tenant security approval, operations, and troubleshooting.

## Sources Of Truth

- `docs/install-uninstall-user-stories.md` is the canonical user-story catalog.
- `docs/install-uninstall-test-matrix.md` is the canonical lifecycle scenario catalog.
- `docs/installer-requirements-traceability.md` maps stories and scenarios to implementation, tests, evidence, documentation, and GitHub work.
- `docs/customer/installer-user-guide.md` is the controlled customer user-guide draft.
- `docs/customer/installer-technical-security-guide.md` is the controlled customer technical/security-guide draft.
- `config/installer-security-profile.json` is the machine-readable implemented security baseline enforced by CI.

Product behavior, customer documentation, and technical claims must not be treated as complete when the traceability record shows missing or indirect evidence.

## Workstreams

| Workstream | Outcome | GitHub issue |
| --- | --- | --- |
| Requirements | Fifteen stable user stories and complete scenario traceability | [#3](https://github.com/cloudbossdev/pagemaker365-installer/issues/3) |
| Negative paths | Setup, authentication, permission, and preflight failures are safe and understandable | [#4](https://github.com/cloudbossdev/pagemaker365-installer/issues/4) |
| Runtime delivery | API and portal code deploy and pass deployment-bound identity checks | [#5](https://github.com/cloudbossdev/pagemaker365-installer/issues/5) |
| Upgrade | Supported version transitions and recovery behavior are explicit | [#6](https://github.com/cloudbossdev/pagemaker365-installer/issues/6) |
| Secrets | Runtime values are provisioned through customer Key Vault without disclosure | [#7](https://github.com/cloudbossdev/pagemaker365-installer/issues/7) |
| Security contract | Permissions, resources, networking, storage, data flows, and retention are verified | [#8](https://github.com/cloudbossdev/pagemaker365-installer/issues/8) |
| Removal reporting | Hardened removal lifecycle callbacks and outbox behavior are implemented | [#9](https://github.com/cloudbossdev/pagemaker365-installer/issues/9) |
| Acceptance testing | Clean workstation and repeated lifecycle scenarios pass in staging | [#10](https://github.com/cloudbossdev/pagemaker365-installer/issues/10) |
| User guide | Customer operator guide is validated against the released UI | [#11](https://github.com/cloudbossdev/pagemaker365-installer/issues/11) |
| Technical guide | Customer technical/security guide contains only supported claims | [#12](https://github.com/cloudbossdev/pagemaker365-installer/issues/12) |
| Distribution | Signed installer artifacts and release verification are available | [#13](https://github.com/cloudbossdev/pagemaker365-installer/issues/13) |

## Delivery Sequence

1. Approve the story catalog and scenario vocabulary.
2. Complete traceability and identify every missing implementation, test, evidence, or documentation link.
3. Resolve release-blocking technical decisions before publishing security claims.
4. Implement each product gap through a small issue-linked branch and pull request.
5. Keep automated tests and traceability current in the same pull request as behavior changes.
6. Run clean install, recovery, removal, and repeated reinstall tests against staging.
7. Complete the two customer guides from verified behavior and current screenshots.
8. Produce a signed release candidate and repeat acceptance testing on a clean supported workstation.

## User Experience Invariants

Every supported workflow must satisfy these rules:

- Present one clear primary action for the current step.
- Do not require raw PowerShell during the standard customer path.
- Show progress for every long-running action and prevent duplicate execution.
- Explain warnings and blockers with a concrete next action.
- Require both Azure and Microsoft Graph authentication when the selected workflow needs both.
- Never restore access tokens, secrets, or destructive approval after restart.
- Make cancellation, retry, resume, and idempotent rerun behavior explicit.
- Never report deployment success from HTTP status alone or from unrelated existing content.
- Display the verified customer URL after successful runtime validation.
- Never delete SharePoint content, purge Key Vault, or remove an ambiguously owned resource.

## Definition Of Done

A story is complete only when all applicable items are proven:

- User-visible happy, warning, failure, cancellation, retry, resume, and rerun states are defined.
- Security boundaries, required permissions, data handling, and prohibited behavior are documented.
- Implementation matches the acceptance criteria.
- Automated tests cover deterministic behavior and negative paths.
- A live test covers behavior that depends on Azure, Microsoft Graph, SharePoint, or the portal.
- Evidence is sanitized and identifies the correct session, package, tenant, subscription, deployment, and attempt.
- Customer and technical documentation are updated.
- CI passes and the pull request links the relevant story, scenarios, and GitHub issue.

## Release Gates

Customer release is blocked until:

- Issues #4 through #10 and #13 are closed or explicitly removed from v1 scope with an approved rationale.
- All v1 stories show verified automated and live evidence.
- The user and technical guides pass operator and security review.
- The release candidate is signed and passes the clean-workstation lifecycle suite.
