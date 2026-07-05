# Redaction Policy

All logs and diagnostic payloads must be redacted before being shown to the AI assistant or exported in a support bundle.

Always remove:

- Access tokens
- Refresh tokens
- Passwords
- Client secrets
- Connection strings
- Account keys
- Shared access signatures
- Full preview URLs with embedded access tokens
- Raw customer files

Mask where useful:

- Tenant IDs
- Subscription IDs
- Site IDs
- App registration IDs

Do not redact values that support troubleshooting and are safe to share:

- Customer display name
- Resource group name
- Azure region
- Environment name
- SharePoint site URL
- Failed step code
- Known error code

