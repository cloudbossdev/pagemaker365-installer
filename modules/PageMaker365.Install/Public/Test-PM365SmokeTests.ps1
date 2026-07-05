function Test-PM365SmokeTests {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $results = @()
    $appUrl = [string]$config.app.customDomain

    if ([string]::IsNullOrWhiteSpace($appUrl)) {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'AppUrlMissing' `
            -Summary 'App URL is not configured in the customer package.' `
            -Details 'Smoke tests that require the deployed app will run after the deployment contract defines the app URL.' `
            -RetrySafe $true
    } else {
        if ($appUrl -notmatch '^https?://') {
            $appUrl = "https://$appUrl"
        }

        try {
            $response = Invoke-WebRequest -Uri $appUrl -Method Head -TimeoutSec 15 -ErrorAction Stop
            $results += New-PM365Result `
                -Status 'Passed' `
                -Code 'AppHealthReady' `
                -Summary 'Application endpoint responded.' `
                -Details "$appUrl returned HTTP $($response.StatusCode)."
        } catch {
            $results += New-PM365Result `
                -Status 'Failed' `
                -Code 'AppHealthFailed' `
                -Summary 'Application endpoint did not respond successfully.' `
                -Details $_.Exception.Message `
                -RetrySafe $true
        }
    }

    $results += Test-PM365SharePointAccess -ConfigPath $ConfigPath
    $results
}

