function Test-PM365SmokeTests {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath,

        [string] $DeploymentArtifactPath = ''
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $results = @()
    $expectedExportId = [string]$config.controlPlane.deploymentExportId
    $apiUrl = ''
    $portalUrl = ''

    if ([string]::IsNullOrWhiteSpace($DeploymentArtifactPath) -or -not (Test-Path -LiteralPath $DeploymentArtifactPath)) {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'RuntimeDeploymentEvidenceMissing' `
            -Summary 'Runtime validation requires the Azure deployment artifact.' `
            -Details 'Run Install again so the installer can capture the deployed API and portal URLs before validation.' `
            -RetrySafe $true
    } else {
        try {
            $deploymentArtifact = Get-Content -LiteralPath $DeploymentArtifactPath -Raw | ConvertFrom-Json -ErrorAction Stop
            $artifactResourceGroup = [string]$deploymentArtifact.azure.resourceGroupName
            $expectedResourceGroup = [string]$config.azure.resourceGroupName
            if ($deploymentArtifact.status -ne 'Passed' -or
                -not [string]::Equals($artifactResourceGroup, $expectedResourceGroup, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw 'Deployment artifact status or resource-group identity did not match the customer package.'
            }

            $apiUrl = [string]$deploymentArtifact.outputs.apiUrl.value
            $portalUrl = [string]$deploymentArtifact.outputs.portalUrl.value
            if ([string]::IsNullOrWhiteSpace($apiUrl) -or [string]::IsNullOrWhiteSpace($portalUrl)) {
                throw 'Deployment artifact did not contain the deployed API and portal URLs.'
            }
        } catch {
            $results += New-PM365Result `
                -Status 'Failed' `
                -Code 'RuntimeDeploymentEvidenceInvalid' `
                -Summary 'Azure deployment evidence could not establish the runtime targets.' `
                -Details $_.Exception.Message `
                -RetrySafe $true
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($apiUrl)) {
        $healthUrl = "{0}/health" -f $apiUrl.TrimEnd('/')
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -Method Get -TimeoutSec 20 -ErrorAction Stop
            $health = $response.Content | ConvertFrom-Json -ErrorAction Stop
            $product = [string]$health.product
            $deploymentExportId = [string]$health.deploymentExportId
            if ($health.ok -ne $true -or
                $product -ne 'PageMaker365' -or
                [string]::IsNullOrWhiteSpace($expectedExportId) -or
                -not [string]::Equals($deploymentExportId, $expectedExportId, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw 'Runtime health response did not match the PageMaker365 deployment identity.'
            }

            $results += New-PM365Result `
                -Status 'Passed' `
                -Code 'AppHealthReady' `
                -Summary 'PageMaker365 runtime identity was verified.' `
                -Details "$healthUrl returned the expected deployment identity." `
                -Data @{ apiUrl = $apiUrl; deploymentExportId = $deploymentExportId }
        } catch {
            $results += New-PM365Result `
                -Status 'Failed' `
                -Code 'AppHealthFailed' `
                -Summary 'PageMaker365 runtime identity could not be verified.' `
                -Details "$healthUrl did not return the expected product and deployment identity. $($_.Exception.Message)" `
                -RetrySafe $true
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($portalUrl)) {
        try {
            $response = Invoke-WebRequest -Uri $portalUrl -Method Get -TimeoutSec 20 -ErrorAction Stop
            $content = [string]$response.Content
            if ($content -notmatch '(?i)PageMaker365' -or
                $content -match '(?i)web app is running and waiting for your content') {
                throw 'Portal response did not contain the PageMaker365 application marker.'
            }

            $results += New-PM365Result `
                -Status 'Passed' `
                -Code 'PortalAppReady' `
                -Summary 'PageMaker365 portal content was verified.' `
                -Details "$portalUrl returned PageMaker365 application content." `
                -Data @{ portalUrl = $portalUrl }
        } catch {
            $results += New-PM365Result `
                -Status 'Failed' `
                -Code 'PortalAppFailed' `
                -Summary 'PageMaker365 portal content could not be verified.' `
                -Details "$portalUrl did not return deployed PageMaker365 content. $($_.Exception.Message)" `
                -RetrySafe $true
        }
    }

    $results += Test-PM365SharePointAccess -ConfigPath $ConfigPath
    $results
}
