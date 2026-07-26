function Remove-PM365PartialInstall {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath,

        [Parameter(Mandatory)]
        [string] $ConfirmationText,

        [string] $OutputPath = '',

        [bool] $RetainSoftDeletedKeyVault = $true
    )

    if (-not $RetainSoftDeletedKeyVault) {
        throw 'Key Vault purge is intentionally unsupported by partial-install cleanup.'
    }

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $inventory = Get-PM365PartialInstallInventoryData -Config $config
    $resourceGroupName = [string]$inventory.resourceGroupName

    if (-not $inventory.resourceGroupFound) {
        New-PM365Result `
            -Status 'Passed' `
            -Code 'PartialInstallAbsent' `
            -Summary 'No partial PageMaker365 install was found.' `
            -Details "Resource group '$resourceGroupName' does not exist." `
            -Data @{ resourceGroupName = $resourceGroupName; removed = $false }
        return
    }

    if (-not $inventory.safeToRemove) {
        New-PM365Result `
            -Status 'Failed' `
            -Code 'PartialInstallCleanupBlocked' `
            -Summary 'Partial-install cleanup is blocked by ownership or activity checks.' `
            -Details (@($inventory.blockers) -join [Environment]::NewLine) `
            -RetrySafe $false `
            -Data @{ resourceGroupName = $resourceGroupName; removed = $false; blockers = @($inventory.blockers) }
        return
    }

    if (-not [string]::Equals($ConfirmationText.Trim(), $resourceGroupName, [System.StringComparison]::Ordinal)) {
        New-PM365Result `
            -Status 'Failed' `
            -Code 'PartialInstallCleanupConfirmationMismatch' `
            -Summary 'Partial-install cleanup confirmation did not match the target resource group.' `
            -Details "Type '$resourceGroupName' exactly to approve cleanup." `
            -RetrySafe $true `
            -Data @{ resourceGroupName = $resourceGroupName; removed = $false }
        return
    }

    if (-not $PSCmdlet.ShouldProcess($resourceGroupName, 'Delete dedicated PageMaker365 resource group and retain the Key Vault in soft-deleted state')) {
        New-PM365Result `
            -Status 'Skipped' `
            -Code 'PartialInstallCleanupSkipped' `
            -Summary 'Partial-install cleanup was skipped.' `
            -Details 'No Azure resources were removed.' `
            -Data @{ resourceGroupName = $resourceGroupName; removed = $false }
        return
    }

    Remove-AzResourceGroup -Name $resourceGroupName -Force -ErrorAction Stop | Out-Null
    $remaining = Get-AzResourceGroup -Name $resourceGroupName -ErrorAction SilentlyContinue
    if ($remaining) {
        New-PM365Result `
            -Status 'Failed' `
            -Code 'PartialInstallCleanupIncomplete' `
            -Summary 'Azure accepted cleanup, but the resource group still exists.' `
            -Details "Check deletion status for '$resourceGroupName' before retrying." `
            -RetrySafe $true `
            -Data @{ resourceGroupName = $resourceGroupName; removed = $false }
        return
    }

    $cleanupResult = [pscustomobject][ordered]@{
        artifactType = 'PageMaker365.PartialInstallCleanupResult'
        schemaVersion = '0.1'
        completedAt = (Get-Date).ToUniversalTime().ToString('o')
        status = 'Passed'
        resourceGroupName = $resourceGroupName
        resourceGroupRemoved = $true
        removedResourceCount = $inventory.resourceCount
        keyVault = [pscustomobject][ordered]@{
            name = $inventory.keyVault.name
            state = 'SoftDeletedRecoverable'
            purgeExecuted = $false
        }
    }
    $artifactPath = ''
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $artifactPath = Write-PM365JsonArtifact `
            -OutputPath $OutputPath `
            -DefaultFileName 'partial-install-cleanup-result.json' `
            -InputObject $cleanupResult
    }

    New-PM365Result `
        -Status 'Passed' `
        -Code 'PartialInstallCleanupCompleted' `
        -Summary 'The partial PageMaker365 install was removed.' `
        -Details "Deleted dedicated resource group '$resourceGroupName'. Key Vault '$($inventory.keyVault.name)' was not purged and remains recoverable through Azure soft delete." `
        -RetrySafe $false `
        -Data @{ resourceGroupName = $resourceGroupName; removed = $true; removedResourceCount = $inventory.resourceCount; keyVaultName = $inventory.keyVault.name; keyVaultPurged = $false; artifactPath = $artifactPath }
}
