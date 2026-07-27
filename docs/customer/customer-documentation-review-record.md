# Customer Documentation Review Record

Status: template; no approval recorded

Use one completed copy of this record for the customer user guide and one for the technical/security guide. Store the completed record with the release evidence and link it from the final documentation pull request.

## Release Identity

| Field | Value |
| --- | --- |
| Document | User guide / Technical and security guide |
| Document revision |  |
| Installer release version |  |
| Installer source commit |  |
| Signed archive SHA-256 |  |
| Official publisher |  |
| Certificate thumbprint |  |
| Portal/API versions |  |
| Lifecycle acceptance run ID |  |
| Traceability revision |  |

## Claim Review

| Claim area | Evidence reviewed | Decision | Reviewer | UTC date | Conditions or issue |
| --- | --- | --- | --- | --- | --- |
| Distribution and launch |  | Approve / Reject |  |  |  |
| Setup file and package trust |  | Approve / Reject |  |  |  |
| Azure roles and resources |  | Approve / Reject |  |  |  |
| Graph/SharePoint permissions and access |  | Approve / Reject |  |  |  |
| Network and proxy behavior |  | Approve / Reject |  |  |  |
| Runtime secret handling |  | Approve / Reject |  |  |  |
| Install and validation |  | Approve / Reject |  |  |  |
| Upgrade support/exclusion |  | Approve / Reject |  |  |  |
| Removal, retention, and reinstall |  | Approve / Reject |  |  |  |
| Evidence, callbacks, and portal state |  | Approve / Reject |  |  |  |
| Troubleshooting and support handoff |  | Approve / Reject |  |  |  |
| Local storage and deletion |  | Approve / Reject |  |  |  |

`Approve` means the claim matches the named release and its evidence. It does not approve a future version. `Reject` or a condition that affects customer behavior keeps the document in controlled-draft status.

## Required Decisions

| Role | Named reviewer | Decision | UTC date | Evidence/comment link |
| --- | --- | --- | --- | --- |
| Product owner |  | Approve / Reject |  |  |
| Installer engineering |  | Approve / Reject |  |  |
| Runtime/API engineering |  | Approve / Reject |  |  |
| Identity and security |  | Approve / Reject |  |  |
| Operations/support |  | Approve / Reject |  |  |
| Clean test operator |  | Guide usable / Guide not usable |  |  |

## Publication Decision

| Field | Value |
| --- | --- |
| All required roles approved | Yes / No |
| All publication dependencies closed or explicitly excluded | Yes / No |
| Lifecycle JSON passes `-RequireApproval` for the exact release | Yes / No; result/evidence link |
| Screenshots are release-matched and sanitized | Yes / No |
| No TBD/roadmap statement is presented as a guarantee | Yes / No |
| Knowledge-base publication approved | Yes / No |
| Approving product owner and UTC date |  |

Do not remove the controlled-draft warning from either guide until every required decision is affirmative and the final documentation pull request links this completed record.
