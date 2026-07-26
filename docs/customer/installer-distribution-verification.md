# PageMaker365 Installer Distribution Verification

Status: controlled draft; not approved for customer publication

Tracking issue: [#13](https://github.com/cloudbossdev/pagemaker365-installer/issues/13)

## Purpose

Use this procedure to verify that a PageMaker365 Installer ZIP is intact,
signed by the publisher recorded in its release manifest, and suitable to run.
Perform these checks before launching the installer on a customer workstation.

## Files Supplied

A release consists of:

- `pagemaker365-installer-<version>.zip` or an equivalently named ZIP.
- A sibling `.zip.sha256` file published through the same release record.
- `release-manifest.json`, `SHA256SUMS.txt`, `RELEASE-NOTES.md`, and
  `Verify-PageMaker365Installer.ps1` inside the ZIP.

The ZIP is an archive, not a machine-wide Windows installer. Authenticode
protects the PageMaker365 executable, libraries, and PowerShell files inside it.
SHA-256 protects the archive and provides an exact inventory of its contents.

## Verify The Archive

From PowerShell, compare the ZIP with the hash printed in its sibling checksum
file:

```powershell
Get-FileHash .\pagemaker365-installer-<version>.zip -Algorithm SHA256
Get-Content .\pagemaker365-installer-<version>.zip.sha256
```

The two SHA-256 values must match. Stop and contact PageMaker365 support if
either file is absent or the values differ.

## Verify The Extracted Package

1. Extract the ZIP to a new local folder.
2. Open PowerShell in that folder.
3. Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy AllSigned `
  -File .\Verify-PageMaker365Installer.ps1
```

The result must report `Verified` and `Signed`. The publisher and certificate
thumbprint must match the values published in the PageMaker365 release record.
Do not use `-AllowUnsignedDevelopment` for a customer installation; that switch
exists only for engineering CI validation.

The verification script checks every manifest path, file length, SHA-256 hash,
required Authenticode signature, publisher, certificate thumbprint, and the
complete extracted-file inventory. Extra, missing, or modified files fail the
check.

## Launch And Local Rollback

Launch `app\PageMaker365.Installer.exe` only after verification passes. The ZIP
does not register services or install machine-wide files. To roll back the local
program version, close the installer, preserve any required support evidence,
delete the extracted program folder, and extract the previously approved signed
release into a new folder.

Deleting the local program files does not remove Azure resources. Use the
installer's guided removal workflow to inventory, preview, approve, and remove
owned PageMaker365 Azure resources. The removal workflow does not delete
SharePoint content and never purges a soft-deleted Key Vault.

## Failure Handling

Do not bypass a hash, inventory, signer, or publisher failure. Record the ZIP
name, expected version, displayed publisher, certificate thumbprint, and the
sanitized verifier error. Do not send setup files, one-time onboarding codes,
tokens, secrets, or customer document content with a support request.
