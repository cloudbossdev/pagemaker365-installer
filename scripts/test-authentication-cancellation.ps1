[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$moduleRoot = Join-Path $repoRoot 'modules\PageMaker365.Install'

function Assert-Equal {
    param([object] $Expected, [object] $Actual, [string] $Message)
    if ($Expected -ne $Actual) { throw "$Message Expected '$Expected', actual '$Actual'." }
}

Get-ChildItem -Path (Join-Path $moduleRoot 'Private') -Filter '*.ps1' -File |
    ForEach-Object { . $_.FullName }
. (Join-Path $moduleRoot 'Public\Connect-PM365Azure.ps1')

function Get-PM365Config {
    param([string] $ConfigPath)
    [pscustomobject]@{
        customer = [pscustomobject]@{ tenantId = '00000000-0000-0000-0000-000000000001' }
        azure = [pscustomobject]@{ subscriptionId = '00000000-0000-0000-0000-000000000002' }
    }
}

function Get-Module {
    param([switch] $ListAvailable, [string] $Name)
    [pscustomobject]@{ Name = $Name; Version = [version]'1.0.0' }
}

function Import-Module {
    param([string] $Name, [System.Management.Automation.ActionPreference] $ErrorAction)
}

function Test-PM365PlaceholderGuid {
    param([string] $Value)
    return $false
}

function Connect-AzAccount {
    param(
        [string] $Tenant,
        [switch] $UseDeviceAuthentication,
        [System.Management.Automation.ActionPreference] $ErrorAction
    )
    throw [OperationCanceledException]::new('The operator canceled authentication.')
}

$result = Connect-PM365Azure -ConfigPath 'test.json'
Assert-Equal 'Failed' $result.status 'Canceled Azure sign-in returned the wrong status.'
Assert-Equal 'AzureSignInCanceled' $result.code 'Canceled Azure sign-in returned the wrong code.'
Assert-Equal $true $result.retrySafe 'Canceled Azure sign-in must remain retryable.'
Assert-Equal 'Retry Azure sign-in and complete the browser authentication flow.' $result.details 'Canceled Azure sign-in exposed an unstable error message.'

Write-Host 'Authentication cancellation tests passed.'
