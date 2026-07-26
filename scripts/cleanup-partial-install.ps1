[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string] $Config,

    [string] $ConfirmationText = '',

    [string] $OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $repoRoot 'modules\PageMaker365.Install\PageMaker365.Install.psd1'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'support-bundle\cleanup'
}

Import-Module $modulePath -Force
if ([string]::IsNullOrWhiteSpace($ConfirmationText)) {
    Get-PM365PartialInstallInventory `
        -ConfigPath $Config `
        -OutputPath (Join-Path $OutputRoot 'partial-install-cleanup-preview.json')
    return
}

Remove-PM365PartialInstall `
    -ConfigPath $Config `
    -ConfirmationText $ConfirmationText `
    -OutputPath (Join-Path $OutputRoot 'partial-install-cleanup-result.json') `
    -Confirm:$false `
    -WhatIf:$WhatIfPreference
