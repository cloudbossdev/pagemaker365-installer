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
Get-ChildItem -Path (Join-Path $moduleRoot 'Public') -Filter '*.ps1' -File |
    ForEach-Object { . $_.FullName }

$script:restStatusCode = 200
$script:restContent = '{"properties":{"scheduledPurgeDate":"2026-10-09T06:04:09Z","recoveryId":"/subscriptions/sub-1/providers/Microsoft.KeyVault/locations/eastus2/deletedVaults/kv-pm365-test"}}'

function Get-PM365Config {
    param([string] $ConfigPath)
    [pscustomobject]@{
        azure = [pscustomobject]@{
            subscriptionId = 'sub-1'
            location = 'eastus2'
            resourceNames = [pscustomobject]@{ keyVaultName = 'kv-pm365-test' }
        }
    }
}

function Get-AzContext {
    param([System.Management.Automation.ActionPreference] $ErrorAction)
    [pscustomobject]@{ Subscription = [pscustomobject]@{ Id = 'sub-1' } }
}

function Invoke-AzRestMethod {
    param(
        [string] $Method,
        [string] $Path,
        [System.Management.Automation.ActionPreference] $ErrorAction
    )
    [pscustomobject]@{ StatusCode = $script:restStatusCode; Content = $script:restContent }
}

$retained = Test-PM365KeyVaultRecoveryState -ConfigPath 'test.json'
Assert-Equal 'Failed' $retained.status 'Soft-deleted vault should block preflight.'
Assert-Equal 'KeyVaultRecoveryRequired' $retained.code 'Soft-deleted vault returned the wrong code.'
Assert-Equal $false $retained.data.purgeRecommended 'Preflight should not recommend purge.'

$script:restStatusCode = 404
$script:restContent = ''
$available = Test-PM365KeyVaultRecoveryState -ConfigPath 'test.json'
Assert-Equal 'Passed' $available.status 'Available vault name should pass preflight.'
Assert-Equal 'KeyVaultNameReady' $available.code 'Available vault name returned the wrong code.'

$script:restStatusCode = 500
$unavailable = Test-PM365KeyVaultRecoveryState -ConfigPath 'test.json'
Assert-Equal 'Failed' $unavailable.status 'Unavailable Key Vault recovery state should block preflight.'
Assert-Equal 'KeyVaultRecoveryCheckUnavailable' $unavailable.code 'REST failure returned the wrong code.'

Write-Host 'Key Vault recovery preflight tests passed.'
