# Fixture lifecycle runner

The installer exposes a test-only, noninteractive lifecycle command for the
shared harness:

```powershell
$env:PM365_ENABLE_FIXTURE_LIFECYCLE_RUNNER = '1'
dotnet run --project .\tests\PageMaker365.Installer.Engine.Tests\PageMaker365.Installer.Engine.Tests.csproj -- `
  --fixture-lifecycle-runner <fixture-runner.json> <sanitized-result.json>
```

It is disabled by default. The command calls the public installer-engine paths
that the WPF client uses for preflight, preview, deployment, runtime
configuration, validation, removal inventory, removal, and removal validation.
Its PowerShell boundary is an in-memory fixture: it cannot launch PowerShell,
authenticate to Azure/Microsoft 365, make network calls, or mutate a tenant.

## Mandatory safety gate

The runner rejects the request before reading the bootstrap or creating an
engine session unless every condition holds:

- `PM365_ENABLE_FIXTURE_LIFECYCLE_RUNNER=1`;
- `fixtureOnly` and `testOnlyEnabled` are `true`;
- `allowCloudMutation` is `false`;
- `environment` is exactly `disposable-sandbox`;
- the selected subscription exactly appears in `allowedSubscriptionIds`;
- the resource group starts with `rg-pm365-harness-`;
- `confirmation` is exactly `RUN-FIXTURE-LIFECYCLE:<runId>`.

There is no switch that changes this command into a cloud runner. Production
and customer environments are rejected rather than merely requiring a warning.

## Fixture request

The request schema is
`pagemaker365.installer-lifecycle-runner.fixture.v1`. It contains safe IDs and
paths only; never put setup codes, tokens, package bodies, settings, or Azure
credentials in it.

```json
{
  "contractVersion": "pagemaker365.installer-lifecycle-runner.fixture.v1",
  "fixtureOnly": true,
  "testOnlyEnabled": true,
  "allowCloudMutation": false,
  "runId": "fixture-run-001",
  "environment": "disposable-sandbox",
  "subscriptionId": "fixture-subscription-001",
  "allowedSubscriptionIds": ["fixture-subscription-001"],
  "resourceGroupName": "rg-pm365-harness-fixture-001",
  "confirmation": "RUN-FIXTURE-LIFECYCLE:fixture-run-001",
  "outputRoot": "D:\\fixture-output",
  "runtimeBootstrapPath": "D:\\fixture-input\\runtime-bootstrap.json",
  "verifiedPackagePayloadSha256": "<64 lowercase hex characters>",
  "customerId": "fixture-customer-001",
  "tenantId": "fixture-tenant-001",
  "installationId": "fixture-install-001",
  "environmentId": "fixture-environment-001",
  "deploymentExportId": "fixture-export-001",
  "runtimeReleaseId": "fixture-runtime-001",
  "onboardingSessionId": "fixture-onboarding-001",
  "scenario": "failure-recovery-reinstall-uninstall",
  "inducePortalOutageOnce": true
}
```

Supported scenarios are `install-uninstall` and
`failure-recovery-reinstall-uninstall`. The latter induces a fixture deployment
failure, starts a fresh ordered install-evidence attempt for recovery/reinstall,
then exercises governed removal and a callback replay.

## Opaque runtime-bootstrap boundary

The runner reads a sealed `pagemaker365.runtime-bootstrap.v1` fixture envelope
with exactly these outer fields:

```text
contractVersion, packagePayloadSha256, payloadSha256,
customerId, tenantId, installationId, environmentId,
deploymentExportId, runtimeReleaseId, idempotencyKey, payloadBase64
```

`payloadBase64` is decoded only to verify its SHA-256 and is immediately cleared
from memory. The installer does not parse its contents or make decisions about
workspace, SharePoint, page, navigation, branding, or entitlement semantics.
All outer IDs must match the verified-package/install binding supplied to the
runner.

This is a fixture adapter, not a claim that the cross-repository canonical
schema is complete. The orchestration contract still lists canonical schemas
and fixtures as pending. Therefore the result always declares
`runtimeDeliveryStatus: "blocked"` with
`RUNTIME_DELIVERY_CONTRACT_PENDING`; it does not connect v0.6 artifacts to a
deployment, configuration, health, or production-signing path.

## Result and outbox evidence

The command writes a new, sanitized JSON result. It contains only stage/status,
safe correlation or attempt references, receipt status, blocker/recovery codes,
and outbox count. It excludes runtime-bootstrap bytes, tokens, secrets,
credentials, raw package settings, customer content, and Azure command output.

The local `fixture-lifecycle-evidence-state.json` persists ordered event
references and retry state. A one-time induced portal outage preserves the
pending event, then replays it in order. A result is never marked `passed` with
pending evidence.

This proves deterministic installer orchestration and recovery mechanics. It is
not a live acceptance run. Live artifact acquisition/deployment and runtime
bootstrap apply remain gated on the accepted shared runtime contract and the
separate disposable-tenant authorization gates.
