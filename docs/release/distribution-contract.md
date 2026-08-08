# PageMaker365 Installer Distribution Contract

Status: implemented for pilot ZIP packaging; production certificate and
clean-workstation acceptance remain open under issue #13.

## Decision

The alpha and customer-pilot distribution is a versioned ZIP. MSI or MSIX can
be reconsidered after install, upgrade, and removal behavior stabilizes. The ZIP
does not register machine-wide components, services, or uninstall records.

Customer packages must contain only the app publish output, approved modules,
infrastructure, rules, AI policy, samples, schemas, allowlisted operator docs,
release evidence, and the offline verification script. Debug symbols and
customer-specific setup, export, status, or handoff artifacts are prohibited.

## Trust Model

The distribution has three independent controls:

1. Authenticode signs `PageMaker365.Installer.exe`, PageMaker365 first-party
   libraries, and every shipped `.ps1`, `.psm1`, and `.psd1` file.
2. A detached CMS signature authenticates `release-manifest.json` with the
   approved signing certificate. The signed manifest, `SHA256SUMS.txt`, and the
   sibling `.zip.sha256` record the exact payload and archive integrity.
3. The official release record supplies the expected publisher and certificate
   thumbprint independently of the ZIP. The verifier requires those external
   values, authenticates the detached manifest signature, checks its own
   Authenticode identity, and rejects a package whose self-declared or actual
   signer differs. Customer instructions independently
   check the verifier's signature before executing code from the ZIP.

The ZIP file itself is not described as Authenticode-signed. A customer release
is valid only when the manifest reports `Signed`, every required signature is
valid and matches both the manifest and the official publisher/thumbprint, and
the archive and file hashes pass. Values read only from inside the ZIP are not
an authenticity trust anchor. `UnsignedDevelopment` is permitted only in
engineering CI and must be rejected by the customer verifier's default mode.

Private keys and PFX passwords must not be passed on the command line, written
to the package, or stored in logs. The package script accepts an installed
certificate thumbprint or imports a PFX whose password is read from a named
environment variable. The imported certificate is removed from the current
user store when packaging finishes. Production CI completes repository
verification before exposing the PFX/password to a step or materializing the
temporary certificate file.

## Version And Source Identity

`Directory.Build.props` defines the development product metadata. Release CI
supplies a SemVer-compatible version to `scripts/package.ps1`. That version is
written to assembly informational metadata, the visible installer header,
release notes, and the manifest. The manifest also records the repository,
source commit, commit timestamp, and dirty-worktree state.

A customer release requires `-RequireCleanSource`. The source commit is the
authoritative link between the package, pull request checks, release notes, and
retained CI evidence.

## Reproducibility

Package file enumeration and ZIP entries are ordinally sorted. ZIP entry times
are fixed, development PDBs are removed, .NET deterministic compilation is
enabled, and generated release evidence contains no wall-clock build time.
Two builds from identical source, version, runtime, configuration, and signing
input must produce the same archive hash.

Authenticode timestamp responses can make signed artifacts differ across
builds. Reproducibility for a signed release therefore means that one approved,
retained build is published and verified from its manifest and checksums; it
does not promise that independently timestamped signatures are byte-identical.

## Required Release Procedure

1. Start from a clean, reviewed commit on `main`. The production signing
   workflow rejects any other selected ref.
2. Run the noninteractive release gate, `scripts/verify.ps1`. Run
   `scripts/verify.ps1 -IncludeLiveCloudChecks` only as a separately authorized
   sandbox acceptance step; it is not part of the default or CI verification
   path.
3. Package with an approved version, certificate, expected publisher, expected
   certificate thumbprint, and `-RequireCleanSource`.
4. Run `scripts/test-package-hygiene.ps1` against the extracted directory.
5. Run `scripts/test-release-package.ps1 -RequireSignature`.
6. Publish the ZIP, sibling checksum, version, publisher, certificate
   thumbprint, source commit, and release notes in one GitHub release record.
   Customers must obtain the expected publisher and thumbprint from this
   record, not from files inside the ZIP.
7. Launch and execute the acceptance matrix on a clean supported Windows 11
   workstation.
8. Retain the exact package, CI run, acceptance evidence, and previous approved
   release for rollback.

## Rollback

Local rollback closes the app, preserves required sanitized evidence, removes
the extracted folder, and extracts the previous approved signed ZIP to a new
folder. It does not modify Azure or SharePoint. Azure rollback uses the guided
owned-resource removal flow; it never treats deleting local files as a cloud
rollback.
