function Get-PM365DefaultTemplateFile {
    [CmdletBinding()]
    param()

    $moduleRoot = Split-Path -Parent $PSScriptRoot
    $repoRoot = Split-Path -Parent (Split-Path -Parent $moduleRoot)
    Join-Path $repoRoot 'infra\main.bicep'
}

