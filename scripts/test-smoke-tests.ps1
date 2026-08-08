[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$moduleRoot = Join-Path $repoRoot 'modules\PageMaker365.Install'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "pm365-smoke-tests\$([guid]::NewGuid().ToString('N'))"

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param([object] $Expected, [object] $Actual, [string] $Message)
    if ($Expected -ne $Actual) { throw "$Message Expected '$Expected', actual '$Actual'." }
}

New-Item -Path $tempRoot -ItemType Directory -Force | Out-Null

try {
    Get-ChildItem -Path (Join-Path $moduleRoot 'Private') -Filter '*.ps1' -File |
        ForEach-Object { . $_.FullName }
    Get-ChildItem -Path (Join-Path $moduleRoot 'Public') -Filter '*.ps1' -File |
        ForEach-Object { . $_.FullName }

    $script:healthExportId = 'export-001'
    $script:healthReleaseId = 'pm365-runtime-1.0.0+test'
    $script:healthRuntimeVersion = '1.0.0'
    $script:portalContent = '<html><head><title>PageMaker365</title><meta name="pm365-release-id" content="pm365-runtime-1.0.0+test"></head></html>'
    $script:portalCsp = "default-src 'self'; frame-src 'self' https://login.microsoftonline.com https://example.sharepoint.com"
    $script:customerDisplayName = 'Example Customer'
    $script:customerShortName = 'example'
    $script:runtimeConfigAdditional = @{}
    $script:runtimeConfigRemove = @()

    function Get-PM365Config {
        param([string] $ConfigPath)
        [pscustomobject]@{
            controlPlane = [pscustomobject]@{ deploymentExportId = 'export-001' }
            runtimeArtifacts = [pscustomobject]@{
                releaseId = 'pm365-runtime-1.0.0+test'
                runtimeVersion = '1.0.0'
            }
            azure = [pscustomobject]@{ resourceGroupName = 'rg-pm365-test'; environment = 'staging' }
            customer = [pscustomobject]@{
                accountKey = $script:customerShortName
                tenantName = $script:customerDisplayName
                tenantId = '11111111-1111-4111-8111-111111111111'
            }
            entra = [pscustomobject]@{
                portalClientId = '22222222-2222-4222-8222-222222222222'
                apiClientId = '33333333-3333-4333-8333-333333333333'
            }
            sharePoint = [pscustomobject]@{ siteUrl = 'https://example.sharepoint.com/sites/intranet' }
        }
    }

    function Invoke-WebRequest {
        param(
            [string] $Uri,
            [string] $Method,
            [int] $TimeoutSec,
            [System.Management.Automation.ActionPreference] $ErrorAction
        )

        if ($Uri -like '*/health') {
            return [pscustomobject]@{
                StatusCode = 200
                Content = (@{
                    ok = $true
                    product = 'PageMaker365'
                    deploymentExportId = $script:healthExportId
                    releaseId = $script:healthReleaseId
                    runtimeVersion = $script:healthRuntimeVersion
                } | ConvertTo-Json -Compress)
            }
        }

        if ($Uri -like '*/runtime-config.json') {
            $runtimeConfig = [ordered]@{
                environment = 'staging'
                apiBaseUrl = 'https://api-test.azurewebsites.net'
                entraClientId = '22222222-2222-4222-8222-222222222222'
                entraTenantId = '11111111-1111-4111-8111-111111111111'
                entraAuthority = 'https://login.microsoftonline.com/11111111-1111-4111-8111-111111111111'
                apiScope = 'api://33333333-3333-4333-8333-333333333333/access_as_user'
                productName = 'PageMaker365'
                productLogoUrl = '/branding/pagemaker365-logo.png'
                customerDisplayName = $script:customerDisplayName
                customerShortName = $script:customerShortName
                enableWebPartWorkbench = $false
            }
            foreach ($name in $script:runtimeConfigAdditional.Keys) {
                $runtimeConfig[$name] = $script:runtimeConfigAdditional[$name]
            }
            foreach ($name in $script:runtimeConfigRemove) {
                $runtimeConfig.Remove($name)
            }
            return [pscustomobject]@{
                StatusCode = 200
                Headers = @{ 'Cache-Control' = 'private, no-store' }
                Content = ($runtimeConfig | ConvertTo-Json -Compress)
            }
        }

        [pscustomobject]@{
            StatusCode = 200
            Headers = @{ 'Content-Security-Policy' = $script:portalCsp }
            Content = $script:portalContent
        }
    }

    function Test-PM365SharePointAccess {
        param([string] $ConfigPath)
        New-PM365Result `
            -Status 'Passed' `
            -Code 'SharePointSiteReady' `
            -Summary 'SharePoint test stub passed.' `
            -Details 'No live SharePoint request was made.'
    }

    $artifactPath = Join-Path $tempRoot 'deployment.json'
    @{
        artifactType = 'PageMaker365.AzureDeployment'
        schemaVersion = '0.1'
        status = 'Passed'
        azure = @{ resourceGroupName = 'rg-pm365-test' }
        outputs = @{
            apiUrl = @{ type = 'String'; value = 'https://api-test.azurewebsites.net' }
            portalUrl = @{ type = 'String'; value = 'https://portal-test.azurewebsites.net' }
        }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $artifactPath -Encoding utf8

    $passed = @(Test-PM365SmokeTests -ConfigPath 'test.json' -DeploymentArtifactPath $artifactPath)
    Assert-True ($passed.code -contains 'AppHealthReady') 'Matching runtime identity did not pass.'
    Assert-True ($passed.code -contains 'PortalAppReady') "PageMaker365 portal content did not pass. $($passed | ConvertTo-Json -Depth 8 -Compress)"

    $script:customerDisplayName = 'DatabaseUrl Token Services'
    $script:customerShortName = 'cursorSecret-company'
    $tokenLikeCustomer = @(Test-PM365SmokeTests -ConfigPath 'test.json' -DeploymentArtifactPath $artifactPath)
    Assert-True ($tokenLikeCustomer.code -contains 'PortalAppReady') 'Legitimate token-like customer text was falsely treated as secret material.'
    $script:runtimeConfigAdditional = @{ password = 'not-a-secret-but-not-public-contract' }
    $extraRuntimeField = @(Test-PM365SmokeTests -ConfigPath 'test.json' -DeploymentArtifactPath $artifactPath)
    Assert-True ($extraRuntimeField.code -contains 'PortalAppFailed') 'An extra portal runtime configuration field was accepted.'
    $script:runtimeConfigAdditional = @{}
    $script:runtimeConfigRemove = @('productLogoUrl')
    $missingRuntimeField = @(Test-PM365SmokeTests -ConfigPath 'test.json' -DeploymentArtifactPath $artifactPath)
    Assert-True ($missingRuntimeField.code -contains 'PortalAppFailed') 'A portal runtime configuration missing a required field was accepted.'
    $script:runtimeConfigRemove = @()
    $script:runtimeConfigAdditional = @{ apiBaseUrl = 42 }
    $wrongTypeRuntimeField = @(Test-PM365SmokeTests -ConfigPath 'test.json' -DeploymentArtifactPath $artifactPath)
    Assert-True ($wrongTypeRuntimeField.code -contains 'PortalAppFailed') 'A portal runtime configuration with a wrong field type was accepted.'
    $script:runtimeConfigAdditional = @{}
    $script:customerDisplayName = 'Example Customer'
    $script:customerShortName = 'example'

    foreach ($invalidCsp in @(
        "default-src 'self'; frame-src 'self' https://login.microsoftonline.com https://example.sharepoint.com https://attacker.example",
        "default-src https://example.sharepoint.com; frame-src 'self' https://login.microsoftonline.com",
        "frame-src 'self' https://login.microsoftonline.com https://example.sharepoint.com; frame-src 'self' https://login.microsoftonline.com https://example.sharepoint.com"
    )) {
        $script:portalCsp = $invalidCsp
        $invalidCspResult = @(Test-PM365SmokeTests -ConfigPath 'test.json' -DeploymentArtifactPath $artifactPath)
        Assert-True ($invalidCspResult.code -contains 'PortalAppFailed') 'An inexact frame-src origin set was accepted.'
    }
    $script:portalCsp = "default-src 'self'; frame-src 'self' https://login.microsoftonline.com https://example.sharepoint.com"

    $script:healthExportId = 'wrong-export'
    $identityMismatch = @(Test-PM365SmokeTests -ConfigPath 'test.json' -DeploymentArtifactPath $artifactPath)
    Assert-True ($identityMismatch.code -contains 'AppHealthFailed') 'Mismatched deployment identity was accepted.'

    $script:healthExportId = 'Export-001'
    $caseMismatch = @(Test-PM365SmokeTests -ConfigPath 'test.json' -DeploymentArtifactPath $artifactPath)
    Assert-True ($caseMismatch.code -contains 'AppHealthFailed') 'Case-mismatched deployment identity was accepted.'

    $script:healthExportId = 'export-001'
    $script:healthReleaseId = 'wrong-release'
    $releaseMismatch = @(Test-PM365SmokeTests -ConfigPath 'test.json' -DeploymentArtifactPath $artifactPath)
    Assert-True ($releaseMismatch.code -contains 'AppHealthFailed') 'Mismatched runtime release identity was accepted.'

    $script:healthReleaseId = 'pm365-runtime-1.0.0+test'
    $script:portalContent = '<html><head><title>PageMaker365</title><meta name="pm365-release-id" content="PM365-runtime-1.0.0+test"></head></html>'
    $portalReleaseCaseMismatch = @(Test-PM365SmokeTests -ConfigPath 'test.json' -DeploymentArtifactPath $artifactPath)
    Assert-True ($portalReleaseCaseMismatch.code -contains 'PortalAppFailed') 'A case-mismatched portal release marker was accepted.'

    $script:portalContent = '<html><title>Your web app is running and waiting for your content</title></html>'
    $defaultPage = @(Test-PM365SmokeTests -ConfigPath 'test.json' -DeploymentArtifactPath $artifactPath)
    Assert-True ($defaultPage.code -contains 'PortalAppFailed') 'Azure default portal content was accepted.'

    $missingEvidence = @(Test-PM365SmokeTests -ConfigPath 'test.json')
    Assert-Equal 'RuntimeDeploymentEvidenceMissing' $missingEvidence[0].code 'Missing deployment evidence returned the wrong code.'

    Write-Host 'Runtime smoke-test contract tests passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
