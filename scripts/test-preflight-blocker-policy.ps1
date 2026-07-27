[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$moduleRoot = Join-Path $repoRoot 'modules\PageMaker365.Install'

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

Get-ChildItem -Path (Join-Path $moduleRoot 'Private') -Filter '*.ps1' -File |
    ForEach-Object { . $_.FullName }
@(
    'Test-PM365Prerequisites.ps1',
    'Test-PM365AzureContext.ps1',
    'Test-PM365EntraPermissions.ps1',
    'Test-PM365SharePointAccess.ps1'
) | ForEach-Object { . (Join-Path $moduleRoot "Public\$_") }

$script:availableModules = @()
$script:graphContext = $null
$script:tokenContext = $null
$script:siteMode = 'Ready'
$script:libraryMode = 'Ready'
$script:libraryName = 'Documents'
$script:azureContext = $null
$script:moduleLoadFailures = @()

function Get-PM365Config {
    param([string] $ConfigPath)
    [pscustomobject]@{
        customer = [pscustomobject]@{ tenantId = '00000000-0000-0000-0000-000000000001' }
        azure = [pscustomobject]@{
            subscriptionId = '00000000-0000-0000-0000-000000000002'
            resourceGroupName = 'rg-pm365-test'
            location = 'eastus2'
        }
        sharePoint = [pscustomobject]@{
            siteUrl = 'https://contoso.sharepoint.com/sites/pagemaker365'
            defaultDocumentLibrary = $script:libraryName
        }
    }
}

function Get-PM365BicepCommand { return $null }

function Get-Module {
    param(
        [switch] $ListAvailable,
        [string] $Name
    )
    if ($Name -in $script:availableModules) {
        return [pscustomobject]@{ Name = $Name; Version = [version]'1.0.0' }
    }
    return $null
}

function Import-Module {
    param(
        [string] $Name,
        [System.Management.Automation.ActionPreference] $ErrorAction
    )
    if ($Name -in $script:moduleLoadFailures) {
        throw "Simulated module load failure: $Name"
    }
}

function Get-Command {
    param(
        [string] $Name,
        [System.Management.Automation.ActionPreference] $ErrorAction
    )
    if ($Name -eq 'Get-MgContext') {
        return [pscustomobject]@{ Name = $Name }
    }
    return $null
}

function Get-AzContext {
    param([System.Management.Automation.ActionPreference] $ErrorAction)
    return $script:azureContext
}

function Get-AzResourceGroup {
    param(
        [string] $Name,
        [System.Management.Automation.ActionPreference] $ErrorAction
    )
    return $null
}

function Get-MgContext {
    param([System.Management.Automation.ActionPreference] $ErrorAction)
    return $script:graphContext
}

function Initialize-PM365GraphAccessToken { return $script:tokenContext }

function Invoke-MgGraphRequest {
    param(
        [string] $Method,
        [string] $Uri,
        [System.Management.Automation.ActionPreference] $ErrorAction
    )

    if ($Uri -like '*memberOf*') {
        return [pscustomobject]@{
            value = @([pscustomobject]@{ displayName = 'Global Administrator' })
        }
    }

    if ($Uri -like '*/drives') {
        if ($script:libraryMode -eq 'Denied') {
            throw 'HTTP 403 Forbidden while reading configured site drives.'
        }
        if ($script:libraryMode -eq 'Missing') {
            return [pscustomobject]@{
                value = @([pscustomobject]@{ id = 'drive-other'; name = 'Other Library'; webUrl = 'https://example.invalid/other' })
            }
        }
        return [pscustomobject]@{
            value = @([pscustomobject]@{ id = 'drive-documents'; name = 'Documents'; webUrl = 'https://contoso.sharepoint.com/sites/pagemaker365/Documents' })
        }
    }

    if ($script:siteMode -eq 'Denied') {
        throw 'HTTP 403 Forbidden while resolving configured site.'
    }
    return [pscustomobject]@{
        id = 'contoso.sharepoint.com,site-collection,site-id'
        displayName = 'PageMaker365'
        webUrl = 'https://contoso.sharepoint.com/sites/pagemaker365'
    }
}

$prerequisites = @(Test-PM365Prerequisites)
Assert-True (($prerequisites | Where-Object code -eq 'AzAccountsMissing').status -eq 'Failed') 'Missing Az.Accounts did not block preflight.'
Assert-True (($prerequisites | Where-Object code -eq 'BicepMissing').status -eq 'Failed') 'Missing Bicep did not block preflight.'

$script:availableModules = @('Az.Accounts')
$azureNotSignedIn = @(Test-PM365AzureContext -ConfigPath 'test.json')
Assert-True (($azureNotSignedIn | Where-Object code -eq 'AzureNotSignedIn').status -eq 'Failed') 'Missing Azure context did not block preflight.'

$script:moduleLoadFailures = @('Az.Accounts')
$azureModuleLoadFailure = @(Test-PM365AzureContext -ConfigPath 'test.json')
Assert-True (($azureModuleLoadFailure | Where-Object code -eq 'AzAccountsLoadFailed').status -eq 'Failed') 'Az.Accounts import failure escaped preflight handling.'
$script:moduleLoadFailures = @()

$script:azureContext = [pscustomobject]@{
    Tenant = [pscustomobject]@{ Id = '00000000-0000-0000-0000-000000000001' }
    Subscription = [pscustomobject]@{ Id = '00000000-0000-0000-0000-000000000002' }
}
$azureResourcesMissing = @(Test-PM365AzureContext -ConfigPath 'test.json')
Assert-True (($azureResourcesMissing | Where-Object code -eq 'AzResourcesMissing').status -eq 'Failed') 'Missing Az.Resources did not block preflight.'

$script:availableModules = @('Az.Accounts', 'Az.Resources')
$rbacUnavailable = @(Test-PM365AzureContext -ConfigPath 'test.json')
Assert-True (($rbacUnavailable | Where-Object code -eq 'AzureRbacCheckUnavailable').status -eq 'Failed') 'Unverifiable Azure RBAC did not block preflight.'

$script:availableModules = @('Microsoft.Graph.Authentication', 'Microsoft.Graph.Sites')
$script:tokenContext = [pscustomobject]@{
    connectSucceeded = $true
    tenantId = '00000000-0000-0000-0000-000000000001'
    scopes = @('User.Read', 'Domain.Read.All', 'RoleManagement.Read.Directory')
}
$missingConsent = @(Test-PM365EntraPermissions -ConfigPath 'test.json')
Assert-True (($missingConsent | Where-Object code -eq 'GraphConsentScopesMissing').status -eq 'Failed') 'Missing Sites.Read.All consent did not block preflight.'

$script:availableModules = @()
$missingGraphAuthentication = @(Test-PM365EntraPermissions -ConfigPath 'test.json')
Assert-True (($missingGraphAuthentication | Where-Object code -eq 'GraphAuthenticationMissing').status -eq 'Failed') 'Missing Graph authentication module did not block preflight.'
$script:availableModules = @('Microsoft.Graph.Authentication', 'Microsoft.Graph.Sites')

$script:tokenContext.scopes = @('User.Read', 'Domain.Read.All', 'RoleManagement.Read.Directory', 'Sites.Read.All')
$script:libraryMode = 'Missing'
$missingLibrary = @(Test-PM365SharePointAccess -ConfigPath 'test.json')
$missingLibraryResult = $missingLibrary | Where-Object code -eq 'SharePointLibraryNotFound'
Assert-True ($missingLibraryResult.status -eq 'Failed') 'Missing configured SharePoint library did not block preflight.'
Assert-True ($missingLibraryResult.details -notlike '*Other Library*') 'SharePoint failure exported unrelated library names.'

$script:libraryMode = 'Denied'
$deniedLibrary = @(Test-PM365SharePointAccess -ConfigPath 'test.json')
Assert-True (($deniedLibrary | Where-Object code -eq 'SharePointLibraryAccessFailed').status -eq 'Failed') 'Denied SharePoint library enumeration returned the wrong result.'
Assert-True ($deniedLibrary.code -notcontains 'SharePointSiteResolveFailed') 'Library denial was mislabeled as a site-resolution failure.'

$script:libraryMode = 'Ready'
$script:siteMode = 'Denied'
$deniedSite = @(Test-PM365SharePointAccess -ConfigPath 'test.json')
Assert-True (($deniedSite | Where-Object code -eq 'SharePointSiteResolveFailed').status -eq 'Failed') 'Denied SharePoint site resolution did not block preflight.'

$script:siteMode = 'Ready'
$script:libraryName = ''
$missingLibraryContract = @(Test-PM365SharePointAccess -ConfigPath 'test.json')
Assert-True (($missingLibraryContract | Where-Object code -eq 'SharePointLibraryNotConfigured').status -eq 'Failed') 'Missing package library name did not block preflight.'

$script:availableModules = @('Microsoft.Graph.Authentication')
$missingGraphSitesModule = @(Test-PM365SharePointAccess -ConfigPath 'test.json')
Assert-True (($missingGraphSitesModule | Where-Object code -eq 'GraphSitesModuleMissing').status -eq 'Failed') 'Missing Graph sites module did not block preflight.'

$script:availableModules = @('Microsoft.Graph.Authentication', 'Microsoft.Graph.Sites')
$script:moduleLoadFailures = @('Microsoft.Graph.Sites')
$graphSitesLoadFailure = @(Test-PM365SharePointAccess -ConfigPath 'test.json')
Assert-True (($graphSitesLoadFailure | Where-Object code -eq 'GraphSitesModuleLoadFailed').status -eq 'Failed') 'Graph sites import failure escaped preflight handling.'

Write-Host 'Preflight blocker policy tests passed.'
