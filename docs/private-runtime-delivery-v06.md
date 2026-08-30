# Private runtime delivery — package 0.6 consumer

Status: local, default-disabled protocol consumer. No customer deployment or
runtime-configuration completion is implemented by this contract.

The installer accepts the negotiated pair only through its distinct closed
validator:

- root customer package `contractVersion: "0.6"`;
- capability
  `pagemaker365.customer-install.0.6.protected-acquisition.v1`;
- acquisition contract `pagemaker365.protected-acquisition.v1`; and
- rich runtime manifest `contractVersion: "3.0"`.

The v0.6 package schema is `schemas/customer-install-v0.6.schema.json`. The
canonical synthetic sample and all transport fixtures under
`tests/PageMaker365.Installer.Engine.Tests/Fixtures/private-runtime-delivery-v2`
are byte-identical copies of the accepted PageMaker365 producer fixtures at
commit `d4edd4b16e417ff3d4f519f9d622ac8bb0090712`. `producer.json` records the
copy provenance; `sha256-manifest.json` locks the producer-owned bytes.

## Closed identity and compatibility

Package 0.6 reconstructs the exact canonical manifest 3.0 JSON in producer
field order with UTF-8, LF line endings, and one final newline. The installer
computes SHA-256 over those bytes and constant-time compares it with the signed
`runtimeArtifacts.manifestSha256`. The package signature therefore cannot bind
an internally inconsistent manifest digest.

The v3 validator requires the exact product, repository, provenance schema,
artifact kinds, approved startup commands, safe artifact names, byte sizes,
hashes, source commit, release identifier, Int32-bounded stable semantic
version, and canonical RFC UUID identities. API and portal filenames must be
distinct. Equal content hashes are permitted by manifest v3.

Compatibility remains closed:

| Package | Manifest | Installer boundary |
| --- | --- | --- |
| `0.4` | runtime artifact `1.0` | Existing legacy URL-bearing deployment path only |
| `0.5` | simplified manifest `2.0` | Preserved historical private-acquisition consumer |
| `0.6` | rich manifest `3.0` | New explicit, default-disabled private-acquisition consumer |

Every mixed, missing, unknown, or relabeled pair fails. Package 0.6 does not
fall back to package 0.5 or 0.4, and neither historical validator accepts the
v0.6 shape.

## Acquisition boundary

`PrivateRuntimeDeliveryClient.AcquireV06Async` requires the explicit
`PrivateRuntimeDeliveryOptions.EnablePackageV06` option. The default is false,
and the production/default HTTP constructor remains denied even if a caller
sets the option. Denial occurs before validation, output-directory creation,
or an HTTP request. Contract tests enable it only through the internal,
explicitly injected synthetic local transport.

Once enabled, the existing protected-acquisition controls remain in force:

- exact active onboarding-session binding and approved control-plane API host;
- same-origin relative paths, no redirects, and opaque references only in
  request headers;
- private/no-store ZIP responses, strong SHA-256 ETags, exact full/range
  metadata, bounded streaming, and complete-byte SHA-256 verification;
- safe ZIP structure, exact embedded provenance, artifact kind, release/source
  identity, startup command, and portal release marker; and
- sanitized idempotent receipts with a protected-data-free local outbox.

Successful acquisition returns two verified local ZIP paths. It does not
extract, deploy, publish, execute, or pass those paths to `InstallerEngine`,
PowerShell, Bicep, `Publish-AzWebApp`, or a smoke-test path.

## Explicit remaining gaps

This consumer does not claim customer-install readiness. The separately
verified gaps remain:

- 19 of the 46 mandatory customer-production runtime settings are not mapped;
- the fourth protected reference, `API_LICENSE_SIGNED_PAYLOAD`, is not handled;
- the accepted v0.6 fixture intentionally carries an empty public-settings
  projection; and
- no approved bridge passes privately acquired bytes into deployment.

Those changes require separate assignments and review. This contract makes no
Azure, Entra, Graph, Microsoft 365, SharePoint, DNS, customer, or live request.
