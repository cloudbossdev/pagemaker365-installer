# Assistant And Support-Handoff Staging Runbook

Status: active staging test plan

Tracking issue: [#28](https://github.com/cloudbossdev/pagemaker365-installer/issues/28)

Stories and scenarios: US-12; T03-T06

## Purpose

Use this runbook to prove the assistant portal integration and customer-approved support handoff against staging. Automated tests prove the local safety policy; these runs prove that the portal implements the same contract, retains only approved data, and returns exact correlated receipts.

## Preconditions

- Use a package and installer build created from the commit under test.
- Use a non-production customer, environment, and installer session.
- Configure the assistant API only with an approved PageMaker365 staging origin.
- Do not paste or attach tokens, setup codes, secrets, document content, mailbox content, or customer files.
- Prepare one synthetic text log containing secret-like values and local paths, plus one synthetic image with no customer data.
- Record the installer commit, package version, customer/environment, session ID, timestamp, and portal correlation IDs.

## Test Cases

| ID | Procedure | Pass condition |
| --- | --- | --- |
| T05-action | Return a known privileged action with a changed label and `requiresApproval: false`, plus an unknown and duplicate action. | Only the locally registered known action renders; its local label and approval requirement are retained. |
| T05-auth | Configure a valid staging request, then make the API return HTTP 401 or 403. | The error is shown without a response body, secret, or local path; the request does not fall back to a local success. |
| T05-transient | Make the API return HTTP 429 or 5xx, or temporarily block the endpoint. | When fallback is enabled, the result is explicitly identified as local fallback and no portal success is claimed. |
| T05-origin | Attempt to configure an unapproved or cross-origin assistant endpoint. | Configuration is rejected before an authorization header or payload can be sent. |
| T06-default | Open the assistant and create a support ticket draft without enabling attachment transfer. | Transfer is off; no attachment bytes or local attachment metadata are sent. |
| T06-text | Import the synthetic text log, enable redacted text transfer, and create a draft. | The transferred copy uses an opaque filename; size and SHA-256 match the redacted copy; secret-like values and local paths are absent. |
| T06-binary | Import the synthetic image, enable redacted text transfer, and create a draft. | The image remains local-only and is absent from message, upload, and ticket payloads. |
| T06-receipt | Alter one staging response so its conversation, attachment, contract, status, or correlation identity does not match. | The installer rejects the response and does not display a successful upload or ticket draft. |
| T06-draft | Complete an approved handoff with the staging contract. | Portal status is exactly `Drafted`; the operator can review it, and the installer does not submit a final ticket. |
| T03-bundle | Create a support bundle after the conversation and draft. | The bundle contains the sanitized conversation and approved outbox evidence, but no original attachments, screenshots, binary files, tokens, secrets, or absolute local paths. |

## Portal Review

After the successful draft, a portal operator must verify:

- The portal record belongs to the expected customer, environment, conversation, and installer session.
- Only the approved sanitized message, selected diagnostics, opaque attachment metadata, and redacted text copy were retained.
- The original filename, local path, original raw log, synthetic image, authentication data, and response error body were not retained.
- The portal retention period, deletion authority, audit trail, and final ticket-submission owner are documented and approved.
- Duplicate or retried requests do not create misleading extra records.

## Required Evidence

Record a sanitized result for every test case with:

- Pass/fail, installer commit, package version, and timestamp.
- Sanitized session, conversation, attachment, draft, and correlation identifiers.
- Redacted screenshots of the installer state and portal draft.
- The transferred redacted file's size and SHA-256, without committing the file.
- Confirmation that the prohibited data listed above is absent from installer evidence, network captures, and the portal record.
- A GitHub issue link for every failure, ambiguity, or retention-policy gap.

Summarize the completed run in `docs/installer-requirements-traceability.md`. Keep captures and staging records in the approved evidence system, not in this repository.
