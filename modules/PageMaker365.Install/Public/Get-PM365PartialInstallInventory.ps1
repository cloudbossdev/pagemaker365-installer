function Get-PM365PartialInstallInventory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath,

        [string] $OutputPath = ''
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $inventory = Get-PM365PartialInstallInventoryData -Config $config
    $artifactPath = ''
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $artifactPath = Write-PM365JsonArtifact `
            -OutputPath $OutputPath `
            -DefaultFileName 'partial-install-cleanup-preview.json' `
            -InputObject $inventory
    }

    $data = @{
        artifactPath = $artifactPath
        resourceGroupName = $inventory.resourceGroupName
        resourceGroupFound = $inventory.resourceGroupFound
        resourceCount = $inventory.resourceCount
        safeToRemove = $inventory.safeToRemove
        blockers = @($inventory.blockers)
        keyVaultName = $inventory.keyVault.name
        keyVaultFound = [bool]$inventory.keyVault.found
        keyVaultDisposition = $inventory.keyVault.disposition
    }

    if (-not $inventory.resourceGroupFound) {
        New-PM365Result `
            -Status 'Passed' `
            -Code 'PartialInstallAbsent' `
            -Summary 'No partial PageMaker365 install was found.' `
            -Details "Resource group '$($inventory.resourceGroupName)' does not exist." `
            -Data $data
        return
    }

    if (-not $inventory.safeToRemove) {
        New-PM365Result `
            -Status 'Failed' `
            -Code 'PartialInstallCleanupBlocked' `
            -Summary 'Partial-install cleanup is blocked by ownership or activity checks.' `
            -Details (@($inventory.blockers) -join [Environment]::NewLine) `
            -RetrySafe $false `
            -Data $data
        return
    }

    New-PM365Result `
        -Status 'Passed' `
        -Code 'PartialInstallCleanupReady' `
        -Summary 'The partial PageMaker365 install is ready for approved cleanup.' `
        -Details $(if ($inventory.keyVault.found) {
            "$($inventory.resourceCount) PageMaker365-owned resource(s) will be removed with resource group '$($inventory.resourceGroupName)'. Key Vault '$($inventory.keyVault.name)' will remain recoverable through soft delete."
        } else {
            "$($inventory.resourceCount) PageMaker365-owned resource(s) will be removed with resource group '$($inventory.resourceGroupName)'. The package-named Key Vault is not present in the resource group, so no vault-retention claim will be made."
        }) `
        -RetrySafe $false `
        -Data $data
}
