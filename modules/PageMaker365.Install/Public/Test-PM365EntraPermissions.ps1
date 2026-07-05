function Test-PM365EntraPermissions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $results = @()
    $graphAuth = Get-Module -ListAvailable -Name Microsoft.Graph.Authentication | Select-Object -First 1

    if (-not $graphAuth) {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'GraphAuthenticationMissing' `
            -Summary 'Microsoft.Graph.Authentication is not installed.' `
            -Details 'Graph authentication is required before Entra permission checks can run.' `
            -RetrySafe $true
        return $results
    }

    Import-Module Microsoft.Graph.Authentication -ErrorAction Stop
    $context = Get-MgContext -ErrorAction SilentlyContinue
    if (-not $context) {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'GraphNotSignedIn' `
            -Summary 'Microsoft Graph sign-in is required for Entra permission checks.' `
            -Details 'Sign in with Microsoft Graph before validating app consent/admin readiness.' `
            -RetrySafe $true
        return $results
    }

    $expectedTenantId = [string]$config.customer.tenantId
    if ($expectedTenantId -and $context.TenantId -and ($expectedTenantId -ne $context.TenantId)) {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'GraphTenantMismatch' `
            -Summary 'The Microsoft Graph tenant does not match the customer package.' `
            -Details "Expected tenant $expectedTenantId but current Graph tenant is $($context.TenantId)." `
            -RetrySafe $true
    } else {
        $results += New-PM365Result `
            -Status 'Passed' `
            -Code 'GraphTenantReady' `
            -Summary 'Microsoft Graph tenant context is available.' `
            -Details $context.TenantId
    }

    $requiredScopes = @('Application.ReadWrite.All', 'AppRoleAssignment.ReadWrite.All', 'Directory.Read.All', 'Sites.Read.All')
    $missingScopes = $requiredScopes | Where-Object { $_ -notin $context.Scopes }
    if ($missingScopes.Count -gt 0) {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'GraphConsentScopesMissing' `
            -Summary 'Additional Graph consent scopes may be required.' `
            -Details ("Missing or unconfirmed scopes: " + ($missingScopes -join ', ')) `
            -RetrySafe $true
    } else {
        $results += New-PM365Result `
            -Status 'Passed' `
            -Code 'GraphConsentScopesReady' `
            -Summary 'Required Graph consent scopes are present in the current session.' `
            -Details ($requiredScopes -join ', ')
    }

    try {
        $roleResponse = Invoke-MgGraphRequest `
            -Method GET `
            -Uri 'https://graph.microsoft.com/v1.0/me/memberOf/microsoft.graph.directoryRole?$select=id,displayName,roleTemplateId' `
            -ErrorAction Stop
        $roles = @($roleResponse.value | ForEach-Object { [string]$_.displayName })
        $allowedRoles = @('Global Administrator', 'Application Administrator', 'Cloud Application Administrator')
        $matchedRoles = @($roles | Where-Object { $_ -in $allowedRoles })

        if ($matchedRoles.Count -gt 0) {
            $results += New-PM365Result `
                -Status 'Passed' `
                -Code 'EntraAdminRoleReady' `
                -Summary 'The signed-in Graph account has an admin role that can approve app consent.' `
                -Details ("Matched roles: " + ($matchedRoles -join ', ')) `
                -Data @{
                    roles = ($roles -join ', ')
                }
        } else {
            $results += New-PM365Result `
                -Status 'Warning' `
                -Code 'EntraAdminRoleMissing' `
                -Summary 'The signed-in Graph account may not be able to approve app consent.' `
                -Details 'Use a Global Administrator, Cloud Application Administrator, or Application Administrator for consent.' `
                -RetrySafe $true `
                -Data @{
                    roles = ($roles -join ', ')
                }
        }
    } catch {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'EntraAdminRoleCheckUnavailable' `
            -Summary 'Entra admin role verification could not be completed.' `
            -Details $_.Exception.Message `
            -RetrySafe $true
    }

    $results
}
