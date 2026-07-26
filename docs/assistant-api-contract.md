# Assistant API Contract

The installer defaults to local mock mode. A production build can switch to the PageMaker365 portal broker API by placing `assistant-api.json` at the repo/package root, `config/assistant-api.json`, or the app output folder. Portal mode accepts only HTTPS production/staging PageMaker365 origins or HTTP(S) localhost development origins. Every endpoint path must remain on that configured origin.

## Configuration

```json
{
  "mode": "Portal",
  "portalApiBaseUrl": "https://pagemaker365.com",
  "messageEndpointPath": "/api/installer/assistant/messages",
  "attachmentEndpointPath": "/api/installer/assistant/attachments",
  "supportTicketEndpointPath": "/api/installer/support-tickets",
  "apiKeyEnvironmentVariable": "PM365_ASSISTANT_API_KEY",
  "timeoutSeconds": 30,
  "maxAttachmentBytes": 10485760,
  "fallbackToMockOnFailure": true
}
```

Environment overrides:

- `PM365_ASSISTANT_MODE`
- `PM365_ASSISTANT_API_BASE_URL`
- `PM365_ASSISTANT_ENDPOINT_PATH`
- `PM365_ASSISTANT_ATTACHMENT_ENDPOINT_PATH`
- `PM365_ASSISTANT_SUPPORT_TICKET_ENDPOINT_PATH`
- `PM365_ASSISTANT_API_KEY_ENV`
- `PM365_ASSISTANT_TIMEOUT_SECONDS`
- `PM365_ASSISTANT_MAX_ATTACHMENT_BYTES`
- `PM365_ASSISTANT_FALLBACK_TO_MOCK`

## Message Endpoint

`POST /api/installer/assistant/messages`

The desktop app sends metadata and local attachment manifests. Binary attachment upload is a later slice.

## Request Shape

```json
{
  "contractVersion": "2026-07-05",
  "conversationId": "assistant-20260705-155900",
  "includeDiagnostics": true,
  "diagnosticContext": {},
  "userMessage": {},
  "conversationHistory": [],
  "localTranscriptPath": ""
}
```

## Response Shape

```json
{
  "contractVersion": "2026-07-05",
  "conversationId": "assistant-20260705-155900",
  "correlationId": "server-correlation-id",
  "source": "PortalApi",
  "usedFallback": false,
  "respondedAt": "2026-07-05T21:00:00Z",
  "message": {},
  "recommendedActions": []
}
```

Responses must echo contract version and conversation identity and include a correlation ID and an `Assistant` message. HTTP success with a mismatched response is rejected. Server message text is redacted again before display or persistence.

## Recommended Actions

The message response can include advisory action IDs. The desktop app ignores server labels, descriptions, categories, enabled-state escalation, and approval flags. A local registry supplies all displayed metadata and approval requirements. Unknown, duplicate, disabled, or malformed recommendations are dropped.

Supported installer action IDs:

- `create-support-bundle`: create a local redacted support bundle from the current installer session.
- `create-support-ticket-draft`: create a reviewable support ticket draft from the assistant conversation and explicitly approved attachments. Local approval is mandatory.
- `draft-admin-message`: generate an administrator-facing message in the installer guidance state.
- `rerun-preflight`: rerun preflight after explicit user approval. The portal cannot lower this requirement.
- `open-portal-outbox`: open the local mock portal handoff folder.
- `copy-escalation-summary`: copy a redacted issue summary to the clipboard.

Example:

```json
{
  "actionId": "rerun-preflight",
  "label": "Rerun preflight",
  "description": "Retry the preflight check after the blocker has been resolved.",
  "category": "Installer",
  "requiresApproval": true,
  "enabled": true
}
```

No install, upgrade, uninstall, cleanup, Azure mutation, consent grant, or tenant write action exists in the local registry. A server response cannot introduce one.

## Attachment Upload Endpoint

`POST /api/installer/assistant/attachments`

Attachment transfer is off by default. After explicit operator opt-in, the desktop app sends `multipart/form-data` with:

- `metadata`: JSON using `AssistantAttachmentUploadRequest`
- `file`: binary attachment stream

Only `.txt`, `.log`, `.json`, and `.md` are eligible. The installer creates a redacted local copy, recalculates its size and SHA-256 hash, and replaces the original filename with `attachment-<opaque-id>.<extension>`. Original paths and filenames are never sent. Screenshots and other binary attachments remain local-only and are omitted from ticket requests.

```json
{
  "contractVersion": "2026-07-05",
  "conversationId": "assistant-20260705-155900",
  "attachmentId": "local-attachment-id",
  "fileName": "attachment-local-attach.log",
  "contentType": "text/plain",
  "sizeBytes": 123456,
  "sha256": "<sha256-of-redacted-copy>",
  "contentTreatment": "RedactedText",
  "diagnosticContext": {}
}
```

Response:

```json
{
  "contractVersion": "2026-07-05",
  "conversationId": "assistant-20260705-155900",
  "attachmentId": "local-attachment-id",
  "uploadedAttachmentId": "portal-attachment-id",
  "correlationId": "server-correlation-id",
  "source": "PortalApi",
  "usedFallback": false,
  "status": "Uploaded",
  "message": "Uploaded"
}
```

## Support Ticket Draft Endpoint

`POST /api/installer/support-tickets`

The desktop app creates a draft, not a final submitted ticket. It accepts only an exact `Drafted` response for the submitted conversation. A `Submitted` or mismatched HTTP-success response is rejected and cannot be represented as a successful draft.

```json
{
  "contractVersion": "2026-07-05",
  "conversationId": "assistant-20260705-155900",
  "includeDiagnostics": true,
  "diagnosticContext": {},
  "subject": "PageMaker365 installer assistance - Contoso - 4. Preflight",
  "description": "Latest issue summary",
  "conversationHistory": [],
  "uploadedAttachments": [],
  "localTranscriptPath": ""
}
```

Response:

```json
{
  "contractVersion": "2026-07-05",
  "conversationId": "assistant-20260705-155900",
  "ticketDraftId": "portal-ticket-draft-id",
  "portalRecordUrl": "https://pagemaker365.com/admin/support/tickets/portal-ticket-draft-id",
  "correlationId": "server-correlation-id",
  "source": "PortalApi",
  "usedFallback": false,
  "status": "Drafted",
  "message": "Draft created",
  "createdAt": "2026-07-05T21:00:00Z",
  "uploadedAttachments": []
}
```

## Mock Mode

In mock mode, the installer writes a local portal handoff package under:

`support-bundle/assistant/{conversationId}/portal-outbox/`

That folder includes uploaded attachment copies, upload manifests, and `support-ticket-draft.json`. The normal support bundle includes this folder.

## Failure And Fallback Policy

- Authentication/authorization failures, invalid requests, contract mismatches, and explicit cancellation never fall back to mock success.
- Network failures, client timeouts, HTTP 408/429, and HTTP 5xx may fall back only when configured.
- Fallback responses are labeled `LocalMockFallback`; they create local outbox artifacts and do not claim portal delivery.
- API error bodies are not copied into the UI or transcript. Only sanitized status and correlation metadata are surfaced.

## Data And Retention Boundary

Portal requests may contain the operator-authored sanitized message, selected sanitized diagnostic fields, sanitized conversation history, opaque attachment metadata, and explicitly approved redacted text attachments. They must not contain tokens, one-time codes, secrets, connection strings, local paths, original filenames, raw logs, screenshots, binary files, document content, mailbox content, or broad tenant exports.

Local assistant transcripts and attachments remain under `support-bundle/assistant/<conversation-id>/` until the customer deletes them. Portal retention and final support-ticket submission remain portal responsibilities and require separate operational approval under issue #28.
