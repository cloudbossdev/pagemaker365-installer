function Get-PM365DefaultRuntimeConfigurationTemplateFile {
    [CmdletBinding()]
    param()

    $moduleRoot = Split-Path -Parent $PSScriptRoot
    $repoRoot = Split-Path -Parent (Split-Path -Parent $moduleRoot)
    Join-Path $repoRoot 'infra\runtime-configuration.bicep'
}
