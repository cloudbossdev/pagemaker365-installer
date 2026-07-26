# PageMaker365 Runtime Artifact Contract

Status: proposed cross-repository contract; installer implementation in progress

Tracking issue: [installer #5](https://github.com/cloudbossdev/pagemaker365-installer/issues/5)

## Purpose

The installer provisions customer-owned Azure resources and then deploys the
approved PageMaker365 customer runtime into the API and portal App Services.
This contract prevents an infrastructure-only deployment from being reported
as a successful application install.

The runtime release is generic product code. Customer identifiers, credentials,
connection strings, tokens, document content, and tenant exports must not be
embedded in either runtime artifact.

## Repository Ownership

| Repository | Responsibility |
| --- | --- |
| `cloudbossdev/spo-ui` | Build ready-to-run API and portal ZIP files, publish an immutable release, and expose deployment identity at runtime. |
| `cloudbossdev/pagemaker365` | Select an approved runtime release and place its immutable metadata in the signed customer install package. |
| `cloudbossdev/pagemaker365-installer` | Validate the signed metadata, download from an approved endpoint, verify every digest, deploy both ZIP files, and require runtime identity checks before completion. |

The PageMaker365 customer portal and control-plane API are not customer runtime
artifacts and must never be deployed into a customer subscription.

## Signed Package Shape

Contract `0.3` adds a required `runtimeArtifacts` object:

```json
{
  "runtimeArtifacts": {
    "contractVersion": "1.0",
    "releaseId": "pm365-runtime-1.4.3+abc1234",
    "runtimeVersion": "1.4.3",
    "api": {
      "fileName": "pagemaker365-api-1.4.3.zip",
      "downloadUrl": "https://downloads.pagemaker365.com/runtime/1.4.3/pagemaker365-api-1.4.3.zip",
      "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "startupCommand": "node dist/index.js"
    },
    "portal": {
      "fileName": "pagemaker365-portal-1.4.3.zip",
      "downloadUrl": "https://downloads.pagemaker365.com/runtime/1.4.3/pagemaker365-portal-1.4.3.zip",
      "sha256": "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
      "startupCommand": "pm2 serve /home/site/wwwroot --no-daemon --spa"
    }
  }
}
```

Rules:

- `contractVersion` is exactly `1.0`.
- `releaseId` is immutable and unique to one pair of artifacts.
- `runtimeVersion` is stable semantic versioning and must match the deployment
  target version when the package declares upgrade intent.
- Both artifacts are ready to run. Azure must not restore dependencies or build
  source code during deployment.
- `fileName` is a display and evidence value, not a local path.
- `downloadUrl` uses HTTPS, has no user information or fragment, and is hosted
  by the PageMaker365 release-download allowlist.
- `sha256` is exactly 64 lowercase hexadecimal characters and covers the bytes
  returned by `downloadUrl`.
- Startup commands are fixed by this contract. The installer rejects arbitrary
  commands rather than executing package-defined commands.
- The signed customer package authenticates the artifact references. The
  release files remain generic and contain no customer secret.

## Runtime Release Outputs

The runtime pipeline publishes:

1. A ready-to-run API ZIP containing `dist`, production dependencies, and a
   minimal `package.json` whose start script runs `node dist/index.js`.
2. A portal ZIP whose root contains the production Vite output, including
   `index.html` and the PageMaker365 deployment marker.
3. A release manifest containing contract version, release ID, runtime version,
   source commit, file names, sizes, SHA-256 digests, and public download URLs.

The manifest and ZIP files are retained for every supported upgrade and
rollback window. Ephemeral CI artifacts are not a production release channel.

## Installer Deployment Flow

1. Validate the complete signed customer package before Azure mutation.
2. Provision or reconcile the package-owned Azure resources.
3. Download each artifact to an installer-owned temporary directory.
4. Enforce the trusted HTTPS endpoint policy and a bounded response size.
5. Compute SHA-256 while downloading and fail closed on any mismatch.
6. Import `Az.Websites` and deploy the verified ZIP files with
   `Publish-AzWebApp` to the package-named App Services.
7. Configure ready-to-run deployment settings and the fixed startup command.
8. Delete temporary artifact bytes after the attempt.
9. Persist only sanitized artifact identity and deployment results.
10. Run API and portal identity smoke tests. Any identity failure blocks
    `smoke_tests_completed` success and final install completion.

Resource provisioning, API artifact deployment, and portal artifact deployment
are separate evidence stages. A retry may reuse correctly owned resources, but
must revalidate the package and artifact digests.

## Deployment Identity

The installer supplies these non-secret API App Service settings from the signed
package:

| Setting | Value |
| --- | --- |
| `PM365_PRODUCT` | `PageMaker365` |
| `PM365_DEPLOYMENT_EXPORT_ID` | `controlPlane.deploymentExportId` |
| `PM365_RUNTIME_RELEASE_ID` | `runtimeArtifacts.releaseId` |
| `PM365_RUNTIME_VERSION` | `runtimeArtifacts.runtimeVersion` |

`GET /health` returns HTTP 200 with these top-level fields:

```json
{
  "ok": true,
  "product": "PageMaker365",
  "deploymentExportId": "1ecc023c-4708-49d4-80d9-06f7d2dba9ea",
  "releaseId": "pm365-runtime-1.4.3+abc1234",
  "runtimeVersion": "1.4.3"
}
```

The endpoint may include additional non-secret health metadata. It must not
return secrets, connection strings, tokens, tenant exports, or internal errors.

The portal root response must include both:

- visible or document metadata identifying `PageMaker365`
- `meta[name="pm365-release-id"]` whose content equals the signed `releaseId`

This distinguishes the intended portal from the Azure default page and from an
older PageMaker365 deployment.

## Evidence

Runtime deployment evidence records only:

- release ID and runtime version
- artifact kind, file name, declared and computed SHA-256
- byte count and trusted source host
- target resource group and App Service names
- Azure publish outcome and sanitized error code/message
- API and portal verified URLs
- identity-check result

It must not contain download authorization, query strings, local temporary
paths, package bytes, raw logs, environment values, or secrets. Portal callback
failure remains an outbox condition and never changes the Azure deployment
outcome.

## Failure And Recovery

- Download, digest, ZIP deployment, startup, or identity failure blocks install
  completion and emits a sanitized install or upgrade failure.
- Successfully provisioned resources remain package-owned and can be reconciled
  by a retry with the same immutable package.
- A different release requires a newly signed package. Editing artifact
  references locally invalidates package trust.
- Removal follows the existing ownership and Key Vault retention contract; it
  does not delete SharePoint content.

## Acceptance Gates

- Runtime CI produces both ready-to-run ZIP files and a durable release manifest.
- Runtime tests prove the health and portal release-identity contracts.
- Portal package generation includes the exact approved manifest values before
  signing.
- Installer tests reject untrusted URLs, malformed digests, hash mismatches,
  unsupported commands, oversized downloads, and partial artifact deployment.
- A clean staging install deploys both artifacts, verifies both identities,
  displays the verified portal URL, and records completed lifecycle evidence.
- A stale-release and a tampered-artifact staging test both fail closed.

## References

- [Deploy files to Azure App Service](https://learn.microsoft.com/azure/app-service/deploy-zip)
- [Publish-AzWebApp](https://learn.microsoft.com/powershell/module/az.websites/publish-azwebapp)
