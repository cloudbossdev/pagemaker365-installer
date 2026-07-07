function ConvertTo-PM365WhatIfChange {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Change
    )

    $changeType = [string](Get-PM365ObjectProperty -InputObject $Change -Name @('ChangeType', 'changeType'))
    if ([string]::IsNullOrWhiteSpace($changeType)) {
        $changeType = 'Unknown'
    }

    $resourceId = [string](Get-PM365ObjectProperty -InputObject $Change -Name @('ResourceId', 'resourceId', 'Id', 'id'))
    $resourceType = [string](Get-PM365ObjectProperty -InputObject $Change -Name @('ResourceType', 'resourceType', 'Type', 'type'))
    $resourceName = [string](Get-PM365ObjectProperty -InputObject $Change -Name @('ResourceName', 'resourceName', 'Name', 'name'))

    if ([string]::IsNullOrWhiteSpace($resourceName) -and -not [string]::IsNullOrWhiteSpace($resourceId)) {
        $resourceName = ($resourceId -split '/')[-1]
    }

    $before = Get-PM365ObjectProperty -InputObject $Change -Name @('Before', 'before')
    $after = Get-PM365ObjectProperty -InputObject $Change -Name @('After', 'after')
    $delta = Get-PM365ObjectProperty -InputObject $Change -Name @('Delta', 'delta', 'PropertyChanges', 'propertyChanges')
    $unsupportedReason = Get-PM365ObjectProperty -InputObject $Change -Name @('UnsupportedReason', 'unsupportedReason')
    $isSecretResource = (
        $resourceType -match '(?i)Microsoft\.KeyVault/vaults/secrets' -or
        $resourceId -match '(?i)/providers/Microsoft\.KeyVault/vaults/[^/]+/secrets/'
    )

    [pscustomobject][ordered]@{
        changeType = $changeType
        resourceId = $resourceId
        resourceType = $resourceType
        resourceName = $resourceName
        unsupportedReason = if ($null -eq $unsupportedReason) { $null } else { [string]$unsupportedReason }
        before = if ($isSecretResource) { '[REDACTED]' } else { ConvertTo-PM365RedactedObject -InputObject $before -Depth 12 }
        after = if ($isSecretResource) { '[REDACTED]' } else { ConvertTo-PM365RedactedObject -InputObject $after -Depth 12 }
        delta = if ($isSecretResource) { '[REDACTED]' } else { ConvertTo-PM365RedactedObject -InputObject $delta -Depth 12 }
    }
}
