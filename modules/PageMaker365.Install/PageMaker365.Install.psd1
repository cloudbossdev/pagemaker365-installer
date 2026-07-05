@{
    RootModule = 'PageMaker365.Install.psm1'
    ModuleVersion = '0.1.0'
    GUID = '2a5298e7-7d10-48e5-9d72-f551893c2b71'
    Author = 'PageMaker365'
    CompanyName = 'PageMaker365'
    Copyright = '(c) PageMaker365. All rights reserved.'
    Description = 'PowerShell installer module for PageMaker365 customer deployments.'
    PowerShellVersion = '7.0'
    CompatiblePSEditions = @('Core')
    FunctionsToExport = @(
        'Connect-PM365Azure',
        'Connect-PM365Graph',
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
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
    PrivateData = @{
        PSData = @{
            Tags = @('PageMaker365', 'Installer', 'Azure', 'SharePoint')
            ProjectUri = 'https://pagemaker365.com'
        }
    }
}
