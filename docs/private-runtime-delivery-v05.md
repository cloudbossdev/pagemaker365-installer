# Private runtime delivery — installer consumer

`customer-install` `0.5` is the only installer package accepted by the
private runtime-acquisition consumer. It is separate from customer package
`0.4` and the validation-only `initial-install` v1 handoff. Neither legacy
path can invoke this consumer.

The installer first verifies the strict signed package: the canonical payload
hash and Ed25519 signature, customer/installation/environment/deployment and
onboarding bindings, expiry, manifest `2.0` identity, artifact identities,
fixed relative API paths, and non-secret runtime projection. A package with a
public URL, blob locator, SAS, storage field, token field, unknown authority,
or an altered signature is rejected before transport.

## Control-plane exchange

All requests use the active installer onboarding-session authentication. The
control-plane origin comes from that trusted session; it is never supplied by
the package and cannot be a `downloads` hostname.

`POST /api/onboarding/installer/runtime-delivery-sessions` has a single
authority: the exact canonical signed `customer-install` `0.5` package. Its
body therefore has this shape (where `package` is an object, not a JSON
string):

```json
{
  "package": {
    "contractVersion": "0.5",
    "...": "all remaining canonical signed package fields"
  }
}
```

The successful response is:

```json
{
  "ok": true,
  "created": true,
  "deliverySession": {
    "contractVersion": "pagemaker365.runtime-delivery-session.v1",
    "deliverySessionId": "rds_<opaque>",
    "expiresAt": "2030-01-01T00:00:00.000Z",
    "artifactKinds": ["api", "portal"],
    "status": "active"
  }
}
```

The two references are already bound in the signed package. They are sent
only as `X-PM365-Runtime-Delivery-Ref` headers on `GET
/api/onboarding/installer/runtime-artifacts/api` and `/portal`, together with
`X-PM365-Runtime-Delivery-Session`. They must never appear in a path, query
string, redirect, receipt, outbox, or log.

Artifact responses must be same-origin `200` or exact `206` ZIP streams with
the signed byte length, a strong SHA-256 ETag (`"sha256:<sha256>"`),
`Cache-Control: private, no-store`, and no `Location`. The installer can
resume a stable partial file with an exact range, verifies the final SHA-256,
archive safety, embedded provenance, and startup command, then atomically
moves it to a verified local path. This consumer does not run PowerShell
deployment or make any tenant write.

The production installer boundary is
`InstallerEngine.AcquirePrivateRuntimeAsync`. Its result supplies the two
verified local paths (and no remote location) to a future, separately approved
0.5 deployment handoff. The existing wizard's `0.4` PowerShell deployment
route is intentionally not a fallback or consumer for these artifacts.

## Receipt

After both artifacts verify, or after a delivery failure that occurs after a
session is issued, the installer posts a sanitized, idempotent receipt to
`POST /api/onboarding/installer/runtime-delivery-receipts`. It carries only
package/release/session correlation, safe artifact verification counters, and
a bounded safe error. The expected acknowledgement is:

```json
{
  "ok": true,
  "created": true,
  "receipt": {
    "deliverySessionId": "rds_<opaque>",
    "packageHash": "sha256:<64 lowercase hex>",
    "releaseId": "<approved release>",
    "eventId": "<installer event>",
    "occurredAt": "2030-01-01T00:00:00.000Z",
    "installerVersion": "<installer version>",
    "outcome": "completed",
    "artifacts": { "api": {}, "portal": {} },
    "safeResult": { "code": "runtime_artifacts_verified", "state": "completed" },
    "createdAt": "2030-01-01T00:00:00.000Z"
  }
}
```

If acknowledgement cannot be obtained, the safe receipt is atomically staged
under the local `runtime-acquisition/receipt-outbox` directory. It contains no
onboarding code, bearer token, delivery reference, storage locator, ZIP bytes,
or customer discovery facts.
