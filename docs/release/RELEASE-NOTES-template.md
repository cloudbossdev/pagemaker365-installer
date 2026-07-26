# PageMaker365 Installer {{VERSION}}

Source commit: `{{SOURCE_COMMIT}}`

## Release Classification

This package is a pilot ZIP distribution. It is customer-releasable only when
`release-manifest.json` reports `Signed` and the included verifier completes
with the publisher and certificate thumbprint from the official release record,
without `-AllowUnsignedDevelopment`.

## Included Workflows

- Guided package acquisition and verification.
- Azure and Microsoft Graph sign-in.
- Preflight, Azure What-If, approval, deployment, and smoke tests.
- Evidence generation and portal synchronization.
- Owned-resource inventory, removal preview, approval, and cleanup.

## Known Limitations

- Runtime application delivery, upgrade policy, production secret handling,
  repeated lifecycle acceptance, and final customer documentation remain
  release gates tracked in the repository.
- The ZIP does not install machine-wide components. Extract it to a new folder
  before launch.

## Verification And Rollback

Follow `docs/installer-distribution-verification.md` before launch.
Closing the installer and deleting the extracted program folder removes the
local application files. It does not reverse Azure changes; use the guided
removal workflow for deployed PageMaker365 resources.
