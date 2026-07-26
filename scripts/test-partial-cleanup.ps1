[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$moduleRoot = Join-Path $repoRoot 'modules\PageMaker365.Install'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "pm365-cleanup-tests\$([guid]::NewGuid().ToString('N'))"

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param([object] $Expected, [object] $Actual, [string] $Message)
    if ($Expected -ne $Actual) { throw "$Message Expected '$Expected', actual '$Actual'." }
}

New-Item -Path $tempRoot -ItemType Directory -Force | Out-Null

try {
    Get-ChildItem -Path (Join-Path $moduleRoot 'Private') -Filter '*.ps1' -File |
        ForEach-Object { . $_.FullName }
    Get-ChildItem -Path (Join-Path $moduleRoot 'Public') -Filter '*.ps1' -File |
        ForEach-Object { . $_.FullName }

    $script:resourceGroupExists = $true
    $script:removeCallCount = 0
    $script:activeDeployment = $false
    $script:contextTenantId = 'tenant-1'
    $script:resources = @(
        [pscustomobject]@{
            Name = 'pm365-storage'
            ResourceType = 'Microsoft.Storage/storageAccounts'
            Location = 'eastus2'
            ResourceId = '/subscriptions/sub-1/resourceGroups/rg-pm365-test/providers/Microsoft.Storage/storageAccounts/pm365-storage'
            Tags = @{ product = 'PageMaker365'; appName = 'pagemaker365-test'; customerNote = 'do-not-export-resource-tag' }
        },
        [pscustomobject]@{
            Name = 'pm365-identity'
            ResourceType = 'Microsoft.ManagedIdentity/userAssignedIdentities'
            Location = 'eastus2'
            ResourceId = '/subscriptions/sub-1/resourceGroups/rg-pm365-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/pm365-identity'
            Tags = @{ product = 'PageMaker365'; appName = 'pagemaker365-test' }
        },
        [pscustomobject]@{
            Name = 'kv-pm365-test'
            ResourceType = 'Microsoft.KeyVault/vaults'
            Location = 'eastus2'
            ResourceId = '/subscriptions/sub-1/resourceGroups/rg-pm365-test/providers/Microsoft.KeyVault/vaults/kv-pm365-test'
            Tags = @{ product = 'PageMaker365'; appName = 'pagemaker365-test' }
        }
    )

    function Get-PM365Config {
        [CmdletBinding()]
        param([string] $ConfigPath)
        [pscustomobject]@{
            customer = [pscustomobject]@{ tenantId = 'tenant-1' }
            app = [pscustomobject]@{ appName = 'pagemaker365-test' }
            azure = [pscustomobject]@{
                tenantId = 'tenant-1'
                subscriptionId = 'sub-1'
                resourceGroupName = 'rg-pm365-test'
                resourceNames = [pscustomobject]@{ keyVaultName = 'kv-pm365-test' }
            }
        }
    }

    function Get-AzContext {
        param([System.Management.Automation.ActionPreference] $ErrorAction)
        [pscustomobject]@{
            Tenant = [pscustomobject]@{ Id = $script:contextTenantId }
            Subscription = [pscustomobject]@{ Id = 'sub-1' }
        }
    }

    function Get-AzResourceGroup {
        param([string] $Name, [System.Management.Automation.ActionPreference] $ErrorAction)
        if (-not $script:resourceGroupExists) { return $null }
        [pscustomobject]@{
            ResourceGroupName = $Name
            Location = 'eastus2'
            Tags = @{ product = 'PageMaker365'; managedBy = 'PageMaker365'; customerNote = 'do-not-export-group-tag' }
        }
    }

    function Get-AzResource {
        param([string] $ResourceGroupName, [switch] $ExpandProperties, [System.Management.Automation.ActionPreference] $ErrorAction)
        $script:resources
    }

    function Get-AzResourceGroupDeployment {
        param([string] $ResourceGroupName, [System.Management.Automation.ActionPreference] $ErrorAction)
        if ($script:activeDeployment) {
            [pscustomobject]@{ DeploymentName = 'active-deployment'; ProvisioningState = 'Running'; Timestamp = Get-Date; CorrelationId = 'corr-active' }
        }
    }

    function Remove-AzResourceGroup {
        param([string] $Name, [switch] $Force, [System.Management.Automation.ActionPreference] $ErrorAction)
        $script:removeCallCount++
        $script:resourceGroupExists = $false
        $true
    }

    $previewPath = Join-Path $tempRoot 'preview.json'
    $preview = Get-PM365PartialInstallInventory -ConfigPath 'test.json' -OutputPath $previewPath
    Assert-Equal 'Passed' $preview.status 'Owned partial install preview should pass.'
    Assert-Equal 'PartialInstallCleanupReady' $preview.code 'Preview returned the wrong code.'
    Assert-Equal 3 $preview.data.resourceCount 'Preview returned the wrong resource count.'
    Assert-True $preview.data.safeToRemove 'Preview should mark owned resources safe to remove.'
    Assert-True $preview.data.keyVaultFound 'Preview should identify the package-named Key Vault before removal.'
    Assert-Equal 'RetainSoftDeletedRecoverable' $preview.data.keyVaultDisposition 'Preview returned the wrong Key Vault disposition.'
    Assert-True (Test-Path -LiteralPath $previewPath) 'Preview artifact was not written.'
    $previewJson = Get-Content -LiteralPath $previewPath -Raw
    Assert-True ($previewJson -notlike '*do-not-export*') 'Preview artifact included non-ownership Azure tags.'

    $resourcesWithVault = @($script:resources)
    $script:resources = @($script:resources | Where-Object ResourceType -ne 'Microsoft.KeyVault/vaults')
    $missingVaultPreview = Get-PM365PartialInstallInventory -ConfigPath 'test.json'
    Assert-Equal 'PartialInstallCleanupReady' $missingVaultPreview.code 'A never-created Key Vault should not block removal of owned resources.'
    Assert-True (-not $missingVaultPreview.data.keyVaultFound) 'Inventory incorrectly reported a missing Key Vault as present.'
    Assert-Equal 'NotPresent' $missingVaultPreview.data.keyVaultDisposition 'Missing Key Vault disposition was not explicit.'
    $script:resources = $resourcesWithVault

    $mismatch = Remove-PM365PartialInstall -ConfigPath 'test.json' -ConfirmationText 'wrong-name' -Confirm:$false
    Assert-Equal 'PartialInstallCleanupConfirmationMismatch' $mismatch.code 'Mismatched confirmation should fail closed.'
    Assert-Equal 0 $script:removeCallCount 'Mismatched confirmation invoked deletion.'

    $whatIf = Remove-PM365PartialInstall -ConfigPath 'test.json' -ConfirmationText 'rg-pm365-test' -WhatIf -Confirm:$false
    Assert-Equal 'PartialInstallCleanupSkipped' $whatIf.code 'WhatIf cleanup should be skipped.'
    Assert-Equal 0 $script:removeCallCount 'WhatIf cleanup invoked deletion.'

    $cleanupPath = Join-Path $tempRoot 'cleanup.json'
    $cleanup = Remove-PM365PartialInstall -ConfigPath 'test.json' -ConfirmationText 'rg-pm365-test' -OutputPath $cleanupPath -Confirm:$false
    Assert-Equal 'PartialInstallCleanupCompleted' $cleanup.code 'Approved cleanup returned the wrong code.'
    Assert-Equal 1 $script:removeCallCount 'Approved cleanup should invoke deletion once.'
    Assert-True $cleanup.data.removed 'Approved cleanup should report resource removal.'
    Assert-True (-not $cleanup.data.keyVaultPurged) 'Partial cleanup must never report Key Vault purge.'
    Assert-True $cleanup.data.keyVaultFound 'Cleanup should preserve the pre-removal Key Vault presence result.'
    Assert-Equal 'SoftDeletedRecoverable' $cleanup.data.keyVaultDisposition 'Cleanup returned an unverified Key Vault disposition.'
    Assert-True (Test-Path -LiteralPath $cleanupPath) 'Cleanup result artifact was not written.'

    $script:resourceGroupExists = $true
    $script:removeCallCount = 0
    $script:resources[0].Tags.product = 'CustomerOwned'
    $blocked = Get-PM365PartialInstallInventory -ConfigPath 'test.json'
    Assert-Equal 'PartialInstallCleanupBlocked' $blocked.code 'Unowned resource should block cleanup.'
    Assert-Equal 0 $script:removeCallCount 'Ownership blocker invoked deletion.'

    $script:resources[0].Tags.product = 'PageMaker365'
    $script:activeDeployment = $true
    $activeBlocked = Get-PM365PartialInstallInventory -ConfigPath 'test.json'
    Assert-Equal 'PartialInstallCleanupBlocked' $activeBlocked.code 'Active deployment should block cleanup.'

    $script:activeDeployment = $false
    $script:contextTenantId = 'tenant-other'
    $tenantBlocked = Get-PM365PartialInstallInventory -ConfigPath 'test.json'
    Assert-Equal 'PartialInstallCleanupBlocked' $tenantBlocked.code 'Wrong Azure tenant should block cleanup.'
    Assert-True (($tenantBlocked.data.blockers -join ' ') -like '*does not match package tenant*') 'Tenant mismatch blocker was not reported.'
    $script:contextTenantId = 'tenant-1'

    $purgeBlocked = $false
    try {
        Remove-PM365PartialInstall -ConfigPath 'test.json' -ConfirmationText 'rg-pm365-test' -RetainSoftDeletedKeyVault:$false -Confirm:$false | Out-Null
    } catch {
        $purgeBlocked = $_.Exception.Message -like '*purge is intentionally unsupported*'
    }
    Assert-True $purgeBlocked 'Partial cleanup accepted a Key Vault purge request.'

    Write-Host 'Partial-install cleanup tests passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
