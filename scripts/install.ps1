[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Config,

    [ValidateSet('Headless', 'AzureSignIn', 'GraphSignIn', 'WhatIfOnly', 'SmokeTests')]
    [string] $Mode = 'Headless',

    [string] $OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $repoRoot 'modules\PageMaker365.Install\PageMaker365.Install.psd1'

Import-Module $modulePath -Force
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'support-bundle'
}

$result = switch ($Mode) {
    'Headless' { Start-PM365Preflight -ConfigPath $Config }
    'AzureSignIn' { Connect-PM365Azure -ConfigPath $Config }
    'GraphSignIn' { Connect-PM365Graph -ConfigPath $Config }
    'WhatIfOnly' {
        $previewDirectory = Join-Path $OutputRoot 'preview'
        $previewArtifactPath = Join-Path $previewDirectory 'azure-whatif.json'
        Invoke-PM365WhatIf -ConfigPath $Config -OutputPath $previewArtifactPath
    }
    'SmokeTests' { Test-PM365SmokeTests -ConfigPath $Config }
}

[pscustomobject]@{
    mode = $Mode
    config = $Config
    result = $result
} | ConvertTo-Json -Depth 12
