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
            'removal-policy.md',
            'using-the-installer.md'
        )
)
if ($unexpectedDocs.Count -gt 0) {
    throw "Package contains non-allowlisted documentation: $($unexpectedDocs.Name -join ', ')"
}

if (-not (Test-Path -LiteralPath (Join-Path $resolvedPackagePath 'app\PageMaker365.Installer.exe'))) {
    throw 'Package does not contain the installer executable.'
}

Write-Host 'Package hygiene checks passed.'
