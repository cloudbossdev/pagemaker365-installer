function Connect-PM365Graph {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath,

        [string[]] $Scopes = @(
            'Application.ReadWrite.All',
            'AppRoleAssignment.ReadWrite.All',
            'Directory.Read.All',
            'Sites.Read.All'
        ),

        [switch] $UseDeviceCode,

        [ValidateSet('Process', 'CurrentUser')]
        [string] $ContextScope = 'CurrentUser'
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $graphAuth = Get-Module -ListAvailable -Name Microsoft.Graph.Authentication | Select-Object -First 1

    if (-not $graphAuth) {
        return New-PM365Result `
            -Status 'Failed' `
            -Code 'GraphAuthenticationMissing' `
            -Summary 'Microsoft.Graph.Authentication is required for Graph sign-in.' `
            -Details 'Install Microsoft.Graph.Authentication before signing in to Microsoft Graph.' `
            -RetrySafe $true
    }

    Import-Module Microsoft.Graph.Authentication -ErrorAction Stop

    $tenantId = [string]$config.customer.tenantId
    $connectCommand = Get-Command Connect-MgGraph -ErrorAction Stop
    $connectArgs = @{
        Scopes = $Scopes
        ErrorAction = 'Stop'
    }

    if (-not (Test-PM365PlaceholderGuid -Value $tenantId)) {
        $connectArgs.TenantId = $tenantId
    }

    if ($connectCommand.Parameters.ContainsKey('NoWelcome')) {
        $connectArgs.NoWelcome = $true
    }

    if ($connectCommand.Parameters.ContainsKey('ContextScope')) {
        $connectArgs.ContextScope = $ContextScope
    }

    if ($UseDeviceCode) {
        if (-not $connectCommand.Parameters.ContainsKey('UseDeviceCode')) {
            return New-PM365Result `
                -Status 'Failed' `
                -Code 'GraphDeviceCodeUnavailable' `
                -Summary 'Microsoft Graph device-code sign-in is not available.' `
                -Details 'Update Microsoft.Graph.Authentication to a version that supports the UseDeviceCode parameter.' `
                -RetrySafe $true
        }

        $connectArgs.UseDeviceCode = $true
    }

    try {
        Connect-MgGraph @connectArgs | Out-Null
        $context = Get-MgContext -ErrorAction Stop
        $authMode = if ($UseDeviceCode) { 'DeviceCode' } else { 'Interactive' }

        return New-PM365Result `
            -Status 'Passed' `
            -Code 'GraphSignInCompleted' `
            -Summary 'Microsoft Graph sign-in completed.' `
            -Details "Current Graph context is tenant $($context.TenantId)." `
            -Data @{
                tenantId = [string]$context.TenantId
                account = [string]$context.Account
                scopes = ($context.Scopes -join ', ')
                authMode = $authMode
                contextScope = $ContextScope
            }
    } catch {
        return New-PM365Result `
            -Status 'Failed' `
            -Code 'GraphSignInFailed' `
            -Summary 'Microsoft Graph sign-in did not complete.' `
            -Details $_.Exception.Message `
            -RetrySafe $true
    }
}
