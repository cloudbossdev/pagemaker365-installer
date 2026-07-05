# Assistant API Contract

The installer defaults to local mock mode. A production build can switch to the PageMaker365 portal broker API by placing `assistant-api.json` at the repo/package root, `config/assistant-api.json`, or the app output folder.

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
  "localTranscriptPath": "support-bundle/assistant/assistant-20260705-155900"
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

Recommended actions are advisory until the installer explicitly wires approved buttons to them.

## Attachment Upload Endpoint

`POST /api/installer/assistant/attachments`

The desktop app sends `multipart/form-data` with:

- `metadata`: JSON using `AssistantAttachmentUploadRequest`
- `file`: binary attachment stream

The JSON metadata does not include full local paths. The file stream is sourced from the local support-bundle attachment copy.

```json
{
  "contractVersion": "2026-07-05",
  "conversationId": "assistant-20260705-155900",
  "attachmentId": "local-attachment-id",
  "fileName": "error-screenshot.png",
  "contentType": "image/png",
  "sizeBytes": 123456,
  "sha256": "ABC123",
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

The desktop app creates a draft, not a final submitted ticket. The portal should allow staff or the customer to review before submission.

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
  "localTranscriptPath": "support-bundle/assistant/assistant-20260705-155900"
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
