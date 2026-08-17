# PageMaker365 Installer Distribution Contract

Status: pilot ZIP packaging is implemented. The source-only Azure Artifact
Signing + GitHub OIDC release path is implemented, but Azure resource setup,
the first internal release-candidate run, and clean-workstation acceptance
remain open under issue #13.

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

1. Azure Artifact Signing Authenticode-signs
   `PageMaker365.Installer.exe`, PageMaker365 first-party libraries, and every
   shipped `.ps1`, `.psm1`, and `.psd1` file. Every required file must have the
   approved publisher, certificate thumbprint, and a trusted timestamp.
2. Azure Artifact Signing creates a detached CMS signature for
   `release-manifest.json`. The verifier requires its approved signer and
   cryptographically validates the RFC 3161 timestamp token and its binding to
   that signer. The signed manifest, `SHA256SUMS.txt`, and the sibling
   `.zip.sha256` record the exact payload and archive integrity.
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

The signing key remains managed by Azure Artifact Signing. Exportable
certificates, private-key files, and certificate-password variables are
prohibited in this repository, GitHub Actions, and release documentation.
The workflow obtains a short-lived GitHub OIDC token only after version,
source, release-immutability, and non-secret identity checks pass. The Azure
identity receives only the Artifact Signing Certificate Profile Signer role on
the selected certificate profile.

## Version And Source Identity

`Directory.Build.props` defines the development product metadata. Release CI
supplies a SemVer-compatible version to `scripts/package.ps1`. That version is
written to assembly informational metadata, the visible installer header,
release notes, and the manifest. The manifest also records the repository,
source commit, commit timestamp, and dirty-worktree state.

A customer release candidate requires `-RequireCleanSource`. The source commit
is the authoritative link between the package, pull request checks, release
notes, and retained GitHub release evidence.

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
3. Run the protected `Internal Signed Release Candidate` workflow with an
   unused strict SemVer RC version. It uses Azure Artifact Signing through
   GitHub OIDC and rejects a request before Azure authentication if the version,
   source ref, release identity variables, or immutability check is invalid.
4. The workflow signs the exact required file inventory and creates a detached
   PKCS#7/CMS manifest signature using `DetachedSignedData`.
5. The workflow verifies package hygiene, signature validity, timestamps,
   publisher, certificate thumbprint, detached manifest signature, file hashes,
   and archive checksum.
6. The workflow creates one durable draft GitHub release record containing the
   ZIP, sibling checksum, manifest, detached signature, `SHA256SUMS.txt`, and
   machine-readable evidence. It does not publish a customer release.
7. Launch and execute the acceptance matrix on a clean supported Windows 11
   workstation, then obtain explicit release approval before changing the draft
   or distributing the package.
8. Retain the exact package, GitHub release evidence, acceptance evidence, and
   previous approved release for rollback.

## Rollback

Local rollback closes the app, preserves required sanitized evidence, removes
the extracted folder, and extracts the previous approved signed ZIP to a new
folder. It does not modify Azure or SharePoint. Azure rollback uses the guided
owned-resource removal flow; it never treats deleting local files as a cloud
rollback.
