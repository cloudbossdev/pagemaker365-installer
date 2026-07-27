[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackagePath
)

$ErrorActionPreference = 'Stop'
$resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$files = @(Get-ChildItem -LiteralPath $resolvedPackagePath -Recurse -File)

$forbidden = @(
    $files | Where-Object {
        $_.Name -like '*deployment-export*.json' -or
        $_.Name -like '*.handoff-summary.json' -or
        $_.Name -like '*.installer-status.json' -or
        ($_.Name -like '*.onboarding.bootstrap.json' -and
            $_.FullName -notlike "*\samples\contoso.onboarding.bootstrap.json")
    }
)
if ($forbidden.Count -gt 0) {
    throw "Package contains generated or customer-specific artifacts: $($forbidden.FullName -join ', ')"
}

$unexpectedDocs = @(
    Get-ChildItem -LiteralPath (Join-Path $resolvedPackagePath 'docs') -File |
        Where-Object Name -NotIn @(
            'assistant-api-contract.md',
            'deployment-contract.md',
            'onboarding-discovery-contract.md',
            'portal-install-package-handoff.md',
            'removal-evidence-callback-contract.md',
            'removal-policy.md',
            'runtime-artifact-contract.md',
            'runtime-secret-contract.md',
            'using-the-installer.md',
            'installer-distribution-verification.md'
        )
)
if ($unexpectedDocs.Count -gt 0) {
    throw "Package contains non-allowlisted documentation: $($unexpectedDocs.Name -join ', ')"
}

if (-not (Test-Path -LiteralPath (Join-Path $resolvedPackagePath 'app\PageMaker365.Installer.exe'))) {
    throw 'Package does not contain the installer executable.'
}

$debugFiles = @($files | Where-Object Extension -EQ '.pdb')
if ($debugFiles.Count -gt 0) {
    throw "Package contains development symbol files: $($debugFiles.FullName -join ', ')"
}

@(
    'release-manifest.json',
    'SHA256SUMS.txt',
    'RELEASE-NOTES.md',
    'Verify-PageMaker365Installer.ps1'
) | ForEach-Object {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedPackagePath $_) -PathType Leaf)) {
        throw "Package release evidence is missing: $_"
    }
}

$releaseManifest = Get-Content -LiteralPath (Join-Path $resolvedPackagePath 'release-manifest.json') -Raw | ConvertFrom-Json
if ($releaseManifest.signing.status -eq 'Signed' -and
    -not (Test-Path -LiteralPath (Join-Path $resolvedPackagePath 'release-manifest.json.p7s') -PathType Leaf)) {
    throw 'Signed package release evidence is missing: release-manifest.json.p7s'
}

Write-Host 'Package hygiene checks passed.'
