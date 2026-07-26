function Test-PM365AzureDeploymentRoleSet {
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()]
        [string[]] $RoleNames = @()
    )

    $roles = @($RoleNames | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    $hasOwner = $roles -contains 'Owner'
    $hasContributor = $roles -contains 'Contributor'
    $hasRoleAssignmentAccess =
        $roles -contains 'Role Based Access Control Administrator' -or
        $roles -contains 'User Access Administrator'

    [pscustomobject]@{
        ready = $hasOwner -or ($hasContributor -and $hasRoleAssignmentAccess)
        roles = $roles
        hasResourceDeploymentAccess = $hasOwner -or $hasContributor
        hasRoleAssignmentWriteAccess = $hasOwner -or $hasRoleAssignmentAccess
    }
}
