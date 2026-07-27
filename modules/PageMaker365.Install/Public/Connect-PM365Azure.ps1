function Connect-PM365Azure {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath,

        [switch] $UseDeviceCode
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $azAccounts = Get-Module -ListAvailable -Name Az.Accounts | Select-Object -First 1

    if (-not $azAccounts) {
        return New-PM365Result `
            -Status 'Failed' `
            -Code 'AzAccountsMissing' `
            -Summary 'Az.Accounts is required for Azure sign-in.' `
            -Details 'Install the Az.Accounts module before signing in.' `
            -RetrySafe $true
    }

    $tenantId = [string]$config.customer.tenantId
    $subscriptionId = [string]$config.azure.subscriptionId
    $connectArgs = @{
        ErrorAction = 'Stop'
    }

    if (-not (Test-PM365PlaceholderGuid -Value $tenantId)) {
        $connectArgs.Tenant = $tenantId
    }

    if ($UseDeviceCode) {
        $connectArgs.UseDeviceAuthentication = $true
    }

    try {
        Import-Module Az.Accounts -ErrorAction Stop
        Connect-AzAccount @connectArgs | Out-Null

        if (-not (Test-PM365PlaceholderGuid -Value $subscriptionId)) {
            $setContextArgs = @{
                SubscriptionId = $subscriptionId
                ErrorAction = 'Stop'
            }

            if (-not (Test-PM365PlaceholderGuid -Value $tenantId)) {
                $setContextArgs.Tenant = $tenantId
            }

            Set-AzContext @setContextArgs | Out-Null
        }

        $context = Get-AzContext -ErrorAction Stop
        return New-PM365Result `
            -Status 'Passed' `
            -Code 'AzureSignInCompleted' `
            -Summary 'Azure sign-in completed.' `
            -Details "Current Azure context is tenant $($context.Tenant.Id), subscription $($context.Subscription.Id)." `
            -Data @{
                tenantId = [string]$context.Tenant.Id
                subscriptionId = [string]$context.Subscription.Id
                account = [string]$context.Account.Id
            }
    } catch {
        $wasCanceled = $_.Exception -is [OperationCanceledException] -or
            $_.Exception.Message -match '(?i)cancel(?:ed|led|lation)?'
        $resultCode = if ($wasCanceled) { 'AzureSignInCanceled' } else { 'AzureSignInFailed' }
        $summary = if ($wasCanceled) { 'Azure sign-in was canceled.' } else { 'Azure sign-in did not complete.' }
        $details = if ($wasCanceled) {
            'Retry Azure sign-in and complete the browser authentication flow.'
        } else {
            $_.Exception.Message
        }
        return New-PM365Result `
            -Status 'Failed' `
            -Code $resultCode `
            -Summary $summary `
            -Details $details `
            -RetrySafe $true
    }
}
