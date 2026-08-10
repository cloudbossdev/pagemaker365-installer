function Test-PM365ExactCspSourceDirective {
    [CmdletBinding()]
    param(
        [string] $Policy,
        [string] $Directive,
        [string[]] $ExpectedSources
    )

    $matchingDirectives = @()
    foreach ($rawDirective in @($Policy -split ';')) {
        $tokens = @($rawDirective.Trim() -split '\s+' | Where-Object { $_ })
        if ($tokens.Count -gt 0 -and $tokens[0] -ieq $Directive) {
            $matchingDirectives += ,$tokens
        }
    }
    if ($matchingDirectives.Count -ne 1) {
        return $false
    }

    $actualSources = @($matchingDirectives[0] | Select-Object -Skip 1)
    if ($actualSources.Count -ne $ExpectedSources.Count -or
        @($actualSources | Sort-Object -Unique).Count -ne $actualSources.Count) {
        return $false
    }
    foreach ($expectedSource in $ExpectedSources) {
        if ($actualSources -cnotcontains $expectedSource) {
            return $false
        }
    }
    $true
}

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
    $expectedReleaseId = [string]$config.runtimeArtifacts.releaseId
    $expectedRuntimeVersion = [string]$config.runtimeArtifacts.runtimeVersion
    $expectedTenantId = [string]$config.customer.tenantId
    $expectedPortalClientId = [string]$config.entra.portalClientId
    $expectedApiClientId = [string]$config.entra.apiClientId
    $expectedEnvironment = [string]$config.azure.environment
    $expectedCustomerDisplayName = [string]$config.customer.tenantName
    $expectedCustomerShortName = [string]$config.customer.accountKey
    $expectedFrameOrigin = ([uri][string]$config.sharePoint.siteUrl).GetLeftPart([System.UriPartial]::Authority)
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
            $releaseId = [string]$health.releaseId
            $runtimeVersion = [string]$health.runtimeVersion
            if ($health.ok -ne $true -or
                $product -ne 'PageMaker365' -or
                [string]::IsNullOrWhiteSpace($expectedExportId) -or
                -not [string]::Equals($deploymentExportId, $expectedExportId, [System.StringComparison]::Ordinal) -or
                [string]::IsNullOrWhiteSpace($expectedReleaseId) -or
                -not [string]::Equals($releaseId, $expectedReleaseId, [System.StringComparison]::Ordinal) -or
                [string]::IsNullOrWhiteSpace($expectedRuntimeVersion) -or
                -not [string]::Equals($runtimeVersion, $expectedRuntimeVersion, [System.StringComparison]::Ordinal)) {
                throw 'Runtime health response did not match the PageMaker365 deployment identity.'
            }

            $results += New-PM365Result `
                -Status 'Passed' `
                -Code 'AppHealthReady' `
                -Summary 'PageMaker365 runtime identity was verified.' `
                -Details "$healthUrl returned the expected deployment identity." `
                -Data @{
                    apiUrl = $apiUrl
                    deploymentExportId = $deploymentExportId
                    releaseId = $releaseId
                    runtimeVersion = $runtimeVersion
                }
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
            $contentSecurityPolicy = [string]$response.Headers['Content-Security-Policy']
            $expectedFrameSources = @("'self'", 'https://login.microsoftonline.com', $expectedFrameOrigin)
            $hasExactFrameSources = Test-PM365ExactCspSourceDirective `
                -Policy $contentSecurityPolicy `
                -Directive 'frame-src' `
                -ExpectedSources $expectedFrameSources
            $portalReleaseMarker = Get-PM365PortalReleaseMarker -Content $content
            $hasReleaseMarker = [string]::Equals(
                $portalReleaseMarker,
                $expectedReleaseId,
                [System.StringComparison]::Ordinal)
            if ($content -notmatch '(?i)PageMaker365' -or
                -not $hasReleaseMarker -or
                $content -match '(?i)web app is running and waiting for your content' -or
                -not $hasExactFrameSources) {
                throw "Portal response did not contain the expected PageMaker365 release identity and exact frame-src policy (releaseMarker=$hasReleaseMarker; exactFrameSources=$hasExactFrameSources; expected=$($expectedFrameSources -join ','); policy=$contentSecurityPolicy)."
            }

            $runtimeConfigUrl = "{0}/runtime-config.json" -f $portalUrl.TrimEnd('/')
            $runtimeConfigResponse = Invoke-WebRequest -Uri $runtimeConfigUrl -Method Get -TimeoutSec 20 -ErrorAction Stop
            $runtimeConfigContent = [string]$runtimeConfigResponse.Content
            $runtimeConfig = $runtimeConfigContent | ConvertFrom-Json -ErrorAction Stop
            $expectedRuntimeConfigProperties = @(
                'environment',
                'apiBaseUrl',
                'entraClientId',
                'entraTenantId',
                'entraAuthority',
                'apiScope',
                'productName',
                'productLogoUrl',
                'customerDisplayName',
                'customerShortName',
                'enableWebPartWorkbench'
            )
            $actualRuntimeConfigProperties = @($runtimeConfig.PSObject.Properties.Name)
            $runtimeConfigHasExactProperties = $actualRuntimeConfigProperties.Count -eq $expectedRuntimeConfigProperties.Count -and
                @($expectedRuntimeConfigProperties | Where-Object { $actualRuntimeConfigProperties -cnotcontains $_ }).Count -eq 0
            $runtimeConfigStringPropertiesAreExact = @(
                $expectedRuntimeConfigProperties | Where-Object { $_ -cne 'enableWebPartWorkbench' } | ForEach-Object {
                    $runtimeConfig.PSObject.Properties[$_].Value -is [string]
                }
            ) -notcontains $false
            if (-not $runtimeConfigHasExactProperties -or
                -not $runtimeConfigStringPropertiesAreExact -or
                $runtimeConfig.enableWebPartWorkbench -isnot [bool] -or
                [string]$runtimeConfig.apiBaseUrl -cne $apiUrl -or
                [string]$runtimeConfig.entraClientId -cne $expectedPortalClientId -or
                [string]$runtimeConfig.entraTenantId -cne $expectedTenantId -or
                [string]$runtimeConfig.entraAuthority -cne "https://login.microsoftonline.com/$expectedTenantId" -or
                [string]$runtimeConfig.apiScope -cne "api://$expectedApiClientId/access_as_user" -or
                [string]$runtimeConfig.environment -cne $expectedEnvironment -or
                [string]$runtimeConfig.productName -cne 'PageMaker365' -or
                [string]$runtimeConfig.productLogoUrl -cne '/branding/pagemaker365-logo.png' -or
                [string]$runtimeConfig.customerDisplayName -cne $expectedCustomerDisplayName -or
                [string]$runtimeConfig.customerShortName -cne $expectedCustomerShortName -or
                $runtimeConfig.enableWebPartWorkbench -ne $false -or
                [string]$runtimeConfigResponse.Headers['Cache-Control'] -notmatch '(?i)no-store') {
                throw 'Portal runtime configuration did not match the signed customer deployment contract.'
            }

            $results += New-PM365Result `
                -Status 'Passed' `
                -Code 'PortalAppReady' `
                -Summary 'PageMaker365 portal content was verified.' `
                -Details "$portalUrl returned PageMaker365 application content." `
                -Data @{ portalUrl = $portalUrl; releaseId = $expectedReleaseId; frameOrigin = $expectedFrameOrigin }
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
