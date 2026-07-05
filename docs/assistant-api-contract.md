# Assistant API Contract

The installer defaults to local mock mode. A production build can switch to the PageMaker365 portal broker API by placing `assistant-api.json` at the repo/package root, `config/assistant-api.json`, or the app output folder.

## Configuration

```json
{
  "mode": "Portal",
  "portalApiBaseUrl": "https://pagemaker365.com",
  "messageEndpointPath": "/api/installer/assistant/messages",
  "apiKeyEnvironmentVariable": "PM365_ASSISTANT_API_KEY",
  "timeoutSeconds": 30,
  "fallbackToMockOnFailure": true
}
```

Environment overrides:

- `PM365_ASSISTANT_MODE`
- `PM365_ASSISTANT_API_BASE_URL`
- `PM365_ASSISTANT_ENDPOINT_PATH`
- `PM365_ASSISTANT_API_KEY_ENV`
- `PM365_ASSISTANT_TIMEOUT_SECONDS`
- `PM365_ASSISTANT_FALLBACK_TO_MOCK`

## Endpoint

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
