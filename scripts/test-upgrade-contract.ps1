[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$moduleRoot = Join-Path $repoRoot 'modules\PageMaker365.Install'

function Assert-Equal {
    param([object] $Expected, [object] $Actual, [string] $Message)
    if ($Expected -ne $Actual) { throw "$Message Expected '$Expected', actual '$Actual'." }
}

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

Get-ChildItem -Path (Join-Path $moduleRoot 'Private') -Filter '*.ps1' -File |
    ForEach-Object { . $_.FullName }
Get-ChildItem -Path (Join-Path $moduleRoot 'Public') -Filter '*.ps1' -File |
    ForEach-Object { . $_.FullName }

$script:resourceGroupExists = $false
$script:activeDeployment = $false
$script:config = [pscustomobject]@{
    contractVersion = '0.3'
    customer = [pscustomobject]@{
        tenantName = 'Upgrade Test'
        tenantId = '00000000-0000-0000-0000-000000000001'
        installationId = 'inst-upgrade-1'
    }
    azure = [pscustomobject]@{
        subscriptionId = 'sub-1'
        resourceGroupName = 'rg-pm365-upgrade'
        environment = 'sandbox'
        location = 'eastus2'
        resourceNames = [pscustomobject]@{
            keyVaultName = 'kvpm365upgrade01'
            storageAccountName = 'stpm365upgrade01'
            logAnalyticsName = 'log-pm365-upgrade'
            applicationInsightsName = 'ai-pm365-upgrade'
            appServicePlanName = 'asp-pm365-upgrade'
            apiAppName = 'app-pm365-upgrade-api'
            portalAppName = 'app-pm365-upgrade-portal'
            managedIdentityName = 'id-pm365-upgrade'
        }
    }
    app = [pscustomobject]@{ appName = 'pagemaker365-upgrade' }
    controlPlane = [pscustomobject]@{ deploymentExportId = 'export-target-2' }
    deployment = [pscustomobject]@{
        operation = 'install'
        sourceRuntimeVersion = ''
        targetRuntimeVersion = '1.4.3'
        sourceDeploymentExportId = ''
        minimumInstallerVersion = '0.1.0'
        failureRecovery = 'ForwardFix'
        resourceNamePolicy = 'Immutable'
        sharePointDataPolicy = 'Preserve'
    }
}

function Get-PM365Config {
    param([string] $ConfigPath)
    $script:config
}

function Get-AzContext {
    param([System.Management.Automation.ActionPreference] $ErrorAction)
    [pscustomobject]@{
        Tenant = [pscustomobject]@{ Id = '00000000-0000-0000-0000-000000000001' }
        Subscription = [pscustomobject]@{ Id = 'sub-1' }
    }
}

function Get-Module {
    param(
        [string] $Name,
        [switch] $ListAvailable
    )

    if ($Name -eq 'Az.Resources') {
        return [pscustomobject]@{ Name = 'Az.Resources'; Version = [version]'7.0.0' }
    }

    if ($Name -eq 'PageMaker365.Install') {
        return [pscustomobject]@{ Name = 'PageMaker365.Install'; Version = [version]'0.1.0' }
    }
}

function Import-Module {
    param(
        [object] $Name,
        [object] $ErrorAction
    )
}

function Get-AzResourceGroup {
    param([string] $Name, [System.Management.Automation.ActionPreference] $ErrorAction)
    if (-not $script:resourceGroupExists) { return $null }
    [pscustomobject]@{
        ResourceGroupName = $Name
        Tags = @{
            product = 'PageMaker365'
            managedBy = 'PageMaker365'
            appName = 'pagemaker365-upgrade'
            installationId = 'inst-upgrade-1'
            runtimeVersion = '1.4.2'
            deploymentExportId = 'export-source-1'
            resourceNamesHash = Get-PM365ResourceNamesHash -ResourceNames $script:config.azure.resourceNames
        }
    }
}

function Get-AzResourceGroupDeployment {
    param([string] $ResourceGroupName, [System.Management.Automation.ActionPreference] $ErrorAction)
    if ($script:activeDeployment) {
        [pscustomobject]@{ ProvisioningState = 'Running' }
    }
}

$cleanInstall = Test-PM365UpgradeContract -ConfigPath 'test.json'
Assert-Equal 'CleanInstallIntentReady' $cleanInstall.code 'Clean install should require an absent resource group.'

$script:resourceGroupExists = $true
$existingInstall = Test-PM365UpgradeContract -ConfigPath 'test.json'
Assert-Equal 'ExistingInstallationRequiresUpgradePackage' $existingInstall.code 'Clean package should not mutate an existing installation.'

$script:config.deployment.targetRuntimeVersion = '1.4.2'
$script:config.controlPlane.deploymentExportId = 'export-source-1'
$matchingPartialInstall = Test-PM365UpgradeContract -ConfigPath 'test.json'
Assert-Equal 'CleanInstallResumeReady' $matchingPartialInstall.code 'The same clean package should resume its matching partial deployment.'
$script:config.deployment.targetRuntimeVersion = '1.4.3'
$script:config.controlPlane.deploymentExportId = 'export-target-2'

$script:config.deployment.operation = ''
$legacyExisting = Test-PM365UpgradeContract -ConfigPath 'test.json'
Assert-Equal 'UpgradeIntentMissing' $legacyExisting.code 'Legacy package should not mutate an existing installation.'

$script:config.deployment.operation = 'upgrade'
$script:config.deployment.sourceRuntimeVersion = '1.4.2'
$script:config.deployment.targetRuntimeVersion = '1.4.3'
$script:config.deployment.sourceDeploymentExportId = 'export-source-1'
$upgradeReady = Test-PM365UpgradeContract -ConfigPath 'test.json'
Assert-Equal 'UpgradeIntentReady' $upgradeReady.code 'Matching upgrade identity should pass.'

$script:config.deployment.targetRuntimeVersion = '1.6.0'
$unsupportedTransition = Test-PM365UpgradeContract -ConfigPath 'test.json'
Assert-Equal 'UpgradePackageContractInvalid' $unsupportedTransition.code 'Skipped minor upgrade should fail before Azure mutation.'
$script:config.deployment.targetRuntimeVersion = '1.4.3'

$script:config.deployment.sourceRuntimeVersion = '1.3.9'
$versionMismatch = Test-PM365UpgradeContract -ConfigPath 'test.json'
Assert-Equal 'UpgradeSourceIdentityMismatch' $versionMismatch.code 'Wrong source version should fail closed.'
Assert-True ($versionMismatch.details -like '*runtimeVersion*') 'Mismatch evidence should name the failed identity field.'

$script:config.deployment.sourceRuntimeVersion = '1.4.2'
$script:activeDeployment = $true
$activeBlocked = Test-PM365UpgradeContract -ConfigPath 'test.json'
Assert-Equal 'UpgradeDeploymentInProgress' $activeBlocked.code 'Active deployment should block upgrade.'

$script:activeDeployment = $false
$script:resourceGroupExists = $false
$missingSource = Test-PM365UpgradeContract -ConfigPath 'test.json'
Assert-Equal 'UpgradeSourceInstallationMissing' $missingSource.code 'Missing upgrade source should fail closed.'

$script:config.deployment.operation = 'install'
$parameters = New-PM365TemplateParameterObject -Config $script:config
Assert-Equal '1.4.3' $parameters.tags.runtimeVersion 'Deployment tags should record target runtime version.'
Assert-Equal 'export-target-2' $parameters.tags.deploymentExportId 'Deployment tags should record target export identity.'
Assert-Equal 'inst-upgrade-1' $parameters.tags.installationId 'Deployment tags should record installation identity.'
Assert-Equal (Get-PM365ResourceNamesHash -ResourceNames $script:config.azure.resourceNames) $parameters.tags.resourceNamesHash 'Deployment tags should bind immutable resource names.'

Write-Host 'Upgrade contract tests passed.'
