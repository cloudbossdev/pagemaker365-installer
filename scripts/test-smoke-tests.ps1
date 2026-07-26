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
    $script:portalContent = '<html><title>PageMaker365</title></html>'

    function Get-PM365Config {
        param([string] $ConfigPath)
        [pscustomobject]@{
            controlPlane = [pscustomobject]@{ deploymentExportId = 'export-001' }
            azure = [pscustomobject]@{ resourceGroupName = 'rg-pm365-test' }
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
                } | ConvertTo-Json -Compress)
            }
        }

        [pscustomobject]@{ StatusCode = 200; Content = $script:portalContent }
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
    Assert-True ($passed.code -contains 'PortalAppReady') 'PageMaker365 portal content did not pass.'

    $script:healthExportId = 'wrong-export'
    $identityMismatch = @(Test-PM365SmokeTests -ConfigPath 'test.json' -DeploymentArtifactPath $artifactPath)
    Assert-True ($identityMismatch.code -contains 'AppHealthFailed') 'Mismatched deployment identity was accepted.'

    $script:healthExportId = 'export-001'
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
