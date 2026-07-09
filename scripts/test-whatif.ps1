[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$moduleRoot = Join-Path $repoRoot 'modules\PageMaker365.Install'
$configPath = Join-Path $repoRoot 'samples\contoso.customer.install.json'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "pm365-whatif-tests\$([guid]::NewGuid().ToString('N'))"

function Assert-True {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Equal {
    param(
        [object] $Expected,
        [object] $Actual,
        [string] $Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', actual '$Actual'."
    }
}

New-Item -Path $tempRoot -ItemType Directory -Force | Out-Null

try {
    Get-ChildItem -Path (Join-Path $moduleRoot 'Private') -Filter '*.ps1' -File |
        ForEach-Object { . $_.FullName }
    Get-ChildItem -Path (Join-Path $moduleRoot 'Public') -Filter '*.ps1' -File |
        ForEach-Object { . $_.FullName }

    $script:structuredCallCount = 0
    $script:unstructuredCallCount = 0
    $script:lastDeploymentArguments = $null

    function Invoke-PM365BicepBuild {
        param([string] $TemplateFile)

        New-PM365Result `
            -Status 'Passed' `
            -Code 'BicepBuildReady' `
            -Summary 'Mock Bicep build passed.' `
            -Details $TemplateFile
    }

    function Get-Command {
        param(
            [string] $Name,
            [System.Management.Automation.ActionPreference] $ErrorAction
        )

        if ($Name -eq 'Get-AzResourceGroupDeploymentWhatIfResult') {
            return [pscustomobject]@{ Name = $Name }
        }

        if ($Name -eq 'bicep') {
            return $null
        }

        Microsoft.PowerShell.Core\Get-Command -Name $Name -ErrorAction $ErrorAction
    }

    function Get-Module {
        param(
            [string] $Name,
            [switch] $ListAvailable
        )

        if ($ListAvailable -and ($Name -eq 'Az.Accounts' -or $Name -eq 'Az.Resources')) {
            return [pscustomobject]@{
                Name = $Name
                Version = [version]'0.0.0'
            }
        }

        if ($Name) {
            return Microsoft.PowerShell.Core\Get-Module -Name $Name
        }

        Microsoft.PowerShell.Core\Get-Module
    }

    function Import-Module {
        param(
            [string] $Name,
            [switch] $Force,
            [System.Management.Automation.ActionPreference] $ErrorAction
        )

        if ($Name -eq 'Az.Accounts' -or $Name -eq 'Az.Resources') {
            return
        }

        Microsoft.PowerShell.Core\Import-Module -Name $Name -Force:$Force -ErrorAction $ErrorAction
    }

    function Get-AzContext {
        [pscustomobject]@{
            Tenant = [pscustomobject]@{
                Id = '00000000-0000-0000-0000-000000000000'
            }
            Subscription = [pscustomobject]@{
                Id = '11111111-1111-1111-1111-111111111111'
                Name = 'Contoso Production'
            }
        }
    }

    function Get-AzResourceGroup {
        param(
            [string] $Name,
            [System.Management.Automation.ActionPreference] $ErrorAction
        )

        [pscustomobject]@{
            ResourceGroupName = $Name
            Location = 'eastus'
        }
    }

    function Get-AzResourceGroupDeploymentWhatIfResult {
        param(
            [string] $ResourceGroupName,
            [string] $TemplateFile,
            [hashtable] $TemplateParameterObject,
            [System.Management.Automation.ActionPreference] $ErrorAction
        )

        $script:structuredCallCount++
        throw 'Structured what-if mock failure.'
    }

    function New-AzResourceGroupDeployment {
        param(
            [string] $ResourceGroupName,
            [string] $TemplateFile,
            [hashtable] $TemplateParameterObject,
            [System.Management.Automation.ActionPreference] $ErrorAction,
            [switch] $WhatIf
        )

        $script:unstructuredCallCount++
        $script:lastDeploymentArguments = @{
            ResourceGroupName = $ResourceGroupName
            TemplateFile = $TemplateFile
            TemplateParameterObject = $TemplateParameterObject
            WhatIf = [bool]$WhatIf
        }

        @(
            'What if:'
            'Resource changes: 9 to create.'
        )
    }

    $artifactPath = Join-Path $tempRoot 'azure-whatif.json'
    $result = Invoke-PM365WhatIf -ConfigPath $configPath -OutputPath $artifactPath

    Assert-Equal 'Warning' $result.status 'Structured failure fallback should return a warning.'
    Assert-Equal 'AzureWhatIfReady' $result.code 'Structured failure fallback should preserve the ready result code.'
    Assert-Equal 1 $script:structuredCallCount 'Structured what-if should be called once.'
    Assert-Equal 1 $script:unstructuredCallCount 'Unstructured fallback should be called once.'
    Assert-True (Test-Path -LiteralPath $artifactPath) 'What-if fallback artifact was not written.'
    Assert-Equal 'rg-pagemaker365-contoso-prod' $script:lastDeploymentArguments.ResourceGroupName 'Fallback used the wrong resource group.'
    Assert-True $script:lastDeploymentArguments.WhatIf 'Fallback did not pass -WhatIf.'
    Assert-Equal 'pagemaker365-contoso' $script:lastDeploymentArguments.TemplateParameterObject.appName 'Fallback used the wrong template parameters.'

    $artifact = Get-Content -LiteralPath $artifactPath -Raw | ConvertFrom-Json
    Assert-Equal 'Warning' $artifact.status 'Fallback artifact should be warning status.'
    Assert-Equal 'New-AzResourceGroupDeployment -WhatIf' $artifact.method 'Fallback artifact should identify the unstructured method.'
    Assert-Equal 'StructuredWhatIfFailedFallbackUsed' $artifact.error.code 'Fallback artifact should identify structured failure fallback.'
    Assert-True ($artifact.error.message -like '*Structured what-if mock failure*') 'Fallback artifact should preserve the structured failure message.'
    Assert-True (($artifact.output -join "`n") -like '*Resource changes: 9 to create*') 'Fallback artifact should preserve unstructured output.'
    Assert-Equal 1 $artifact.risk.unknownCount 'Fallback risk should mark one unknown count.'
    Assert-Equal 1 $artifact.risk.warningCount 'Fallback risk should mark one warning count.'

    Write-Host 'What-if fallback tests passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
