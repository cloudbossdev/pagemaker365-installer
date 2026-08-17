# Azure Artifact Signing and GitHub OIDC Runbook

Status: source-only release-path design. Nothing in this document creates an
Azure resource, changes a GitHub environment, signs a package, or approves
customer distribution.

## Purpose and boundary

`Internal Signed Release Candidate` is the only installer signing workflow. It
creates an internal draft release candidate from `main`; it does not publish a
customer release. The key stays in Azure Artifact Signing. Do not add an
exportable certificate, a certificate password, or a signing-key file to GitHub
secrets, variables, a runner, or this repository.

The workflow uses the official [Azure Artifact Signing GitHub
Action](https://github.com/Azure/artifact-signing-action) on a supported
Windows runner and [Azure Login with GitHub
OIDC](https://learn.microsoft.com/azure/developer/github/connect-from-azure).

## One-time operator setup (separate authorization required)

An Azure administrator and the GitHub repository administrator must review and
explicitly authorize each of these changes before making them:

1. Create or select one Azure Artifact Signing account and one active **Public
   Trust** code-signing certificate profile in the intended Azure region.
   Complete the required public identity validation first; a Public Trust Test
   or Private Trust profile cannot satisfy the customer-distribution trust
   contract. Record its regional
   `https://<region>.codesigning.azure.net/` endpoint, account, profile,
   publisher subject, and current certificate thumbprint.
2. Create a dedicated Microsoft Entra application or user-assigned managed
   identity for this repository's release workflow. It must have no broad
   subscription role.
3. Add a single GitHub Actions federated credential to that identity:

   ```text
   issuer:   https://token.actions.githubusercontent.com/
   audience: api://AzureADTokenExchange
   subject:  repo:cloudbossdev/pagemaker365-installer:environment:production-signing
   ```

   This exact environment subject prevents tokens from branch, pull-request,
   tag, or other environment jobs from using the identity.
4. Assign only **Artifact Signing Certificate Profile Signer** to that identity
   at the selected certificate-profile scope. Do not assign Owner,
   Contributor, subscription-wide roles, or access to application deployment
   resources.
5. Keep the GitHub `production-signing` environment protected. It must permit
   only `main` and require the project administrator's approval. The workflow
   has `id-token: write` only because OIDC needs it and `contents: write` only
   to create the immutable draft release/tag and upload its evidence.
6. Set these **non-secret** environment variables on `production-signing`:

   | Variable | Value |
   | --- | --- |
   | `PM365_ARTIFACT_SIGNING_ENDPOINT` | Regional Azure Artifact Signing endpoint |
   | `PM365_ARTIFACT_SIGNING_ACCOUNT` | Artifact Signing account name |
   | `PM365_ARTIFACT_SIGNING_PROFILE` | Certificate profile name |
   | `PM365_ARTIFACT_SIGNING_AZURE_CLIENT_ID` | OIDC application or managed-identity client ID |
   | `PM365_ARTIFACT_SIGNING_AZURE_TENANT_ID` | Microsoft Entra tenant ID |
   | `PM365_ARTIFACT_SIGNING_AZURE_SUBSCRIPTION_ID` | Subscription ID containing the signing account |
   | `PM365_ARTIFACT_SIGNING_PUBLISHER` | Exact certificate subject returned by signing |
   | `PM365_ARTIFACT_SIGNING_CERTIFICATE_THUMBPRINT` | Exact 40-hex-character certificate thumbprint |

   None of these values is a private key or secret. The workflow validates their
   shape before Azure authentication and validates the returned signing identity
   again after signing.

## First internal release-candidate proof

After the one-time setup is approved, run the workflow manually from `main`
with `0.1.0-rc.1` (or a newly approved RC version). It rejects a duplicate
release tag before Azure login.

The workflow must finish all of the following before it can create a draft
release record:

1. Verify the repository and prepare a clean unsigned payload.
2. Ask Azure Artifact Signing to sign exactly the installer executable,
   PageMaker365 first-party libraries, and every shipped PowerShell script,
   module, and module manifest.
3. Verify each Authenticode signature, its publisher, certificate thumbprint,
   and timestamp.
4. Ask Azure Artifact Signing to create the documented detached PKCS#7 output
   for `release-manifest.json`, rename that `.p7` output to the package's
   `.p7s` contract name, and verify it with .NET `SignedCms`.
5. Cryptographically validate the detached CMS's RFC 3161 TSA signature and
   message-imprint binding to the manifest signer, then create checksums, the
   deterministic ZIP, release evidence, and a draft GitHub Release.

The Azure action documents `generate-pkcs7: true` and
`pkcs7-options: DetachedSignedData`, but its documentation does not make a
PageMaker365-specific compatibility guarantee for the shipped PowerShell file
extensions or this verifier's detached-CMS layout. This first internal RC is
the compatibility proof. Any unsupported file type, missing timestamp, or CMS
verification difference must fail the workflow before an archive or GitHub
release is created. Do not work around a failure by weakening the verifier or
adding a local certificate.

## Release and acceptance controls

The workflow creates a **draft prerelease** with the ZIP, ZIP SHA-256 sidecar,
manifest, detached CMS signature, payload checksums, and machine-readable
evidence. A draft is durable release evidence, not customer distribution.

Before the draft can be approved or provided to a customer:

1. Record the clean Windows 11 install, removal, and reinstall acceptance
   results against the exact ZIP hash.
2. Resolve issue #13 and all release-critical customer-documentation gates.
3. Obtain explicit project-owner approval to publish or distribute the release.

If the certificate profile renews and the signing thumbprint changes, stop the
workflow, verify the new publisher identity with the signing administrator,
update the protected environment variable under a separate approved change,
and produce a new immutable RC version. Do not replace evidence on an existing
release tag.
