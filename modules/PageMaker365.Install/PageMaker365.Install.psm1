$private = @(Get-ChildItem -Path (Join-Path $PSScriptRoot 'Private') -Filter '*.ps1' -File)
$public = @(Get-ChildItem -Path (Join-Path $PSScriptRoot 'Public') -Filter '*.ps1' -File)

foreach ($file in @($private + $public)) {
    . $file.FullName
}

Export-ModuleMember -Function @(
    'Connect-PM365Azure',
    'Connect-PM365Graph',
    'Get-PM365AzureDiscovery',
    'Invoke-PM365BicepBuild',
    'Invoke-PM365Deployment',
    'Invoke-PM365WhatIf',
    'New-PM365InstallReport',
    'Start-PM365MockInstall',
    'Start-PM365Preflight',
    'Test-PM365SmokeTests',
    'Test-PM365AzureContext',
    'Test-PM365DeploymentContract',
    'Test-PM365EntraPermissions',
    'Test-PM365Prerequisites',
    'Test-PM365SharePointAccess'
)
