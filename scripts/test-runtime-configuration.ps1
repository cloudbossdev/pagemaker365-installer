[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $repoRoot 'modules\PageMaker365.Install\PageMaker365.Install.psd1'
Import-Module $modulePath -Force

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) {
        throw $Message
    }
}

$fixture = [pscustomobject]@{
    Content = @'
{
  "value": [
    {
      "id": "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Web/sites/api/config/configreferences/appsettings/DATABASE_URL",
      "properties": {
        "status": "Resolved",
        "vaultName": "kv-pm365-test",
        "secretName": "DATABASE-URL"
      }
    },
    {
      "id": "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Web/sites/api/config/configreferences/appsettings/API_ENTRA_CLIENT_SECRET",
      "properties": {
        "status": "AccessToKeyVaultDenied",
        "vaultName": "kv-pm365-test",
        "secretName": "API-ENTRA-CLIENT-SECRET"
      }
    },
    {
      "id": "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Web/sites/api/config/configreferences/appsettings/UNRELATED_SETTING",
      "properties": {
        "status": "Resolved",
        "vaultName": "kv-other",
        "secretName": "OTHER"
      }
    }
  ]
}
'@
}

$expected = @('DATABASE_URL', 'API_ENTRA_CLIENT_SECRET', 'API_IMAGE_ASSET_CURSOR_SECRET')
$statuses = @(& (Get-Module PageMaker365.Install) {
    param($Response, $Expected)
    ConvertTo-PM365KeyVaultReferenceStatus -Response $Response -ExpectedAppSettings $Expected
} $fixture $expected)

Assert-True ($statuses.Count -eq 2) 'The parser must keep only expected App Service settings returned by Azure.'
Assert-True (($statuses | Where-Object appSettingName -eq 'DATABASE_URL').status -eq 'Resolved') 'Resolved reference status was not parsed.'
Assert-True (($statuses | Where-Object appSettingName -eq 'API_ENTRA_CLIENT_SECRET').status -eq 'AccessToKeyVaultDenied') 'Denied reference status was not parsed.'
Assert-True (-not ($statuses | Where-Object appSettingName -eq 'API_IMAGE_ASSET_CURSOR_SECRET')) 'A reference omitted by Azure must remain missing and block completion.'

$runtimeCommand = Get-Content -LiteralPath (Join-Path $repoRoot 'modules\PageMaker365.Install\Public\Set-PM365RuntimeConfiguration.ps1') -Raw
Assert-True ($runtimeCommand.Contains("status -eq 'Resolved'")) 'Runtime configuration must require every reference to report Resolved.'
Assert-True (-not $runtimeCommand.Contains('resolveStatus')) 'Runtime configuration must use the current App Service properties.status contract.'
Assert-True ($runtimeCommand.Contains('$declaredSecrets.Count -ne $expectedAppSettings.Count')) 'Runtime configuration must reject contracts with extra or missing secret definitions.'
Assert-True ($runtimeCommand.Contains('$value.Length -gt 4096')) 'Runtime configuration must reject oversized standard-input values.'
Assert-True ($runtimeCommand.Contains('rawValuesIncluded = $false')) 'Runtime evidence must explicitly exclude raw secret values.'
Assert-True ($runtimeCommand.Contains("valueStorage = 'CustomerKeyVault'")) 'Runtime evidence must identify customer Key Vault as the persistence boundary.'
Assert-True (-not $runtimeCommand.Contains('valuesPersisted')) 'Runtime evidence must not claim that persisted Key Vault values are non-persistent.'
Assert-True ($runtimeCommand.Contains('Get-PM365BoundConfig')) 'Runtime configuration must bind mutation to the exact cryptographically validated package payload.'
Assert-True ($runtimeCommand.Contains('Get-PM365TemplateParameterValidationIssue')) 'Runtime configuration must rerun the complete blocking deployment contract before mutation.'
Assert-True ($runtimeCommand.Contains('$keyVaultSecretName = [string]$inputSecret.keyVaultSecretName')) 'Runtime configuration must accept the signed package-defined Key Vault secret name from protected metadata.'
Assert-True ($runtimeCommand.Contains('keyVaultSecretName = $keyVaultSecretName')) 'Runtime configuration must provision each protected value under its signed package-defined Key Vault secret name.'

$firstInputRead = $runtimeCommand.IndexOf('[Console]::In.ReadLine()', [StringComparison]::Ordinal)
foreach ($prerequisite in @(
    'Test-Path -LiteralPath $TemplateFile',
    "Import-Module Az.Accounts -ErrorAction Stop",
    "Import-Module Az.Resources -ErrorAction Stop",
    'Get-AzContext -ErrorAction Stop'
)) {
    $prerequisiteIndex = $runtimeCommand.IndexOf($prerequisite, [StringComparison]::Ordinal)
    Assert-True ($prerequisiteIndex -ge 0) "Runtime configuration is missing prerequisite check: $prerequisite"
    Assert-True ($prerequisiteIndex -lt $firstInputRead) "Runtime configuration must complete prerequisite check before reading protected input: $prerequisite"
}

$script:runtimeSecretMutations = 0
function New-AzResourceGroupDeployment {
    $script:runtimeSecretMutations++
    throw 'An invalid runtime package reached secret mutation.'
}

$sampleConfigPath = Join-Path $repoRoot 'samples\contoso.customer.install.json'
$tamperedConfigPath = Join-Path ([System.IO.Path]::GetTempPath()) "pm365-runtime-tamper-$([guid]::NewGuid().ToString('N')).json"
try {
    Copy-Item -LiteralPath $sampleConfigPath -Destination $tamperedConfigPath
    $approvedPayloadSha256 = (Get-FileHash -LiteralPath $tamperedConfigPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $tamperedConfig = Get-Content -LiteralPath $tamperedConfigPath -Raw | ConvertFrom-Json
    $tamperedConfig.azure.resourceGroupName = 'rg-pm365-redirected'
    $tamperedConfig.azure.resourceNames.keyVaultName = 'kv-pm365-redirected'
    $tamperedConfig.azure.resourceNames.apiAppName = 'api-pm365-redirected'
    $tamperedConfig | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $tamperedConfigPath -Encoding utf8

    try {
        Set-PM365RuntimeConfiguration `
            -ConfigPath $tamperedConfigPath `
            -ExpectedPackagePayloadSha256 $approvedPayloadSha256 | Out-Null
        throw 'Runtime configuration accepted target changes made after package approval.'
    } catch [System.IO.InvalidDataException] {
        Assert-True ($script:runtimeSecretMutations -eq 0) 'Post-approval target tampering must fail before runtime secret mutation.'
    }

    $invalidContract = Get-Content -LiteralPath $sampleConfigPath -Raw | ConvertFrom-Json
    $invalidContract.entra.apiClientId = $invalidContract.entra.portalClientId
    $invalidContract | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $tamperedConfigPath -Encoding utf8
    $invalidPayloadSha256 = (Get-FileHash -LiteralPath $tamperedConfigPath -Algorithm SHA256).Hash.ToLowerInvariant()
    try {
        Set-PM365RuntimeConfiguration `
            -ConfigPath $tamperedConfigPath `
            -ExpectedPackagePayloadSha256 $invalidPayloadSha256 | Out-Null
        throw 'Runtime configuration accepted a package that fails the full deployment contract.'
    } catch [System.IO.InvalidDataException] {
        Assert-True ($script:runtimeSecretMutations -eq 0) 'Invalid runtime contract must fail before runtime secret mutation.'
    }
} finally {
    Remove-Item -LiteralPath $tamperedConfigPath -Force -ErrorAction SilentlyContinue
}

$v07FixturePath = Join-Path $repoRoot 'tests\PageMaker365.Installer.Engine.Tests\Fixtures\private-runtime-delivery-v3\customer-install-0.7.json'
$v07Package = Get-Content -LiteralPath $v07FixturePath -Raw | ConvertFrom-Json -Depth 40
$projection = $v07Package.runtimeConfiguration

function ConvertTo-ApplicationSetting {
    param([object] $Setting)
    $value = if ($Setting.value -is [System.Array]) {
        @($Setting.value) -join ','
    } elseif ($Setting.value -is [bool]) {
        ([string]$Setting.value).ToLowerInvariant()
    } else {
        [string]$Setting.value
    }
    [ordered]@{ name = [string]$Setting.name; value = $value }
}

$apiPublic = @($projection.publicSettings | Where-Object targetApp -eq 'api' | ForEach-Object { ConvertTo-ApplicationSetting $_ })
$portalPublic = @($projection.publicSettings | Where-Object targetApp -eq 'portal' | ForEach-Object { ConvertTo-ApplicationSetting $_ })
$vaultOrigin = [string]($projection.publicSettings | Where-Object name -eq 'API_AZURE_KEY_VAULT_URL').value
$protectedReferences = @(
    $projection.protectedSettings | ForEach-Object {
        $uri = "${vaultOrigin}/secrets/$([string]$_.reference.secretName)"
        if ([string]$_.mode -ceq 'customer-azure-key-vault-reference') {
            $uri += "/$([string]$_.reference.secretVersion)"
        }
        [ordered]@{
            name = [string]$_.name
            mode = [string]$_.mode
            keyVaultReference = "@Microsoft.KeyVault(SecretUri=$uri)"
        }
    }
)
$license = $projection.protectedSettings | Where-Object name -eq 'API_LICENSE_SIGNED_PAYLOAD'
$cursor = $projection.protectedSettings | Where-Object name -eq 'API_IMAGE_ASSET_CURSOR_SECRET'
$rollbackTargets = @(
    $apiPublic | ForEach-Object { "api:$($_.name)" }
    $portalPublic | ForEach-Object { "portal:$($_.name)" }
    $protectedReferences | ForEach-Object { "api:$($_.name)" }
)
$applicationPlan = [ordered]@{
    contractVersion = 'pagemaker365.runtime-configuration-application.v2'
    packageHash = [string]$v07Package.controlPlane.packageHash
    projectionSha256 = [string]$projection.projectionSha256
    binding = [ordered]@{
        customerId = [string]$projection.binding.customerId
        installationId = [string]$projection.binding.installationId
        environmentId = [string]$projection.binding.environmentId
        tenantId = [string]$projection.binding.tenantId
        azureSubscriptionId = [string]$projection.binding.azureSubscriptionId
        deploymentExportId = [string]$projection.binding.deploymentExportId
        runtimeReleaseId = [string]$projection.binding.runtimeReleaseId
        runtimeVersion = [string]$projection.binding.runtimeVersion
        manifestSha256 = [string]$projection.binding.manifestSha256
    }
    apiPublicSettings = $apiPublic
    portalPublicSettings = $portalPublic
    apiProtectedSettingReferences = $protectedReferences
    licenseAcquisition = [ordered]@{
        contractVersion = [string]$license.reference.contractVersion
        opaqueReference = [string]$license.reference.opaqueReference
        vaultResourceId = [string]$license.reference.vaultResourceId
        secretName = [string]$license.reference.secretName
    }
    cursorGeneration = [ordered]@{
        generationAlgorithm = [string]$cursor.reference.generationAlgorithm
        minimumEntropyBytes = [int]$cursor.reference.minimumEntropyBytes
        vaultResourceId = [string]$cursor.reference.vaultResourceId
        secretName = [string]$cursor.reference.secretName
    }
    rollback = [ordered]@{
        strategy = 'restore-previous-app-setting-state'
        targetQualifiedSettings = $rollbackTargets
        containsValues = $false
    }
}
$planJson = ($applicationPlan | ConvertTo-Json -Depth 40 -Compress) + "`n"
$planHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData(
    [System.Text.UTF8Encoding]::new($false, $true).GetBytes($planJson))).ToLowerInvariant()

$script:applicationProcessStarts = 0
$script:applicationCloudCalls = 0
function Start-Process { $script:applicationProcessStarts++; throw 'Application input generation started a process.' }
function New-AzResourceGroupDeployment { $script:applicationCloudCalls++; throw 'Application input generation invoked Azure.' }

try {
    & (Get-Module PageMaker365.Install) {
        param($Json, $Hash)
        New-PM365RuntimeConfigurationV2ApplicationInput -PlanJson $Json -ExpectedPlanSha256 $Hash
    } $planJson $planHash | Out-Null
    throw 'Projection-v2 application input was enabled without explicit approval.'
} catch [System.IO.InvalidDataException] {
    Assert-True ($_.Exception.Message -eq 'runtime_configuration_application_v2_disabled') 'Default-disabled application must return the exact denial.'
}

$applicationInput = & (Get-Module PageMaker365.Install) {
    param($Json, $Hash)
    New-PM365RuntimeConfigurationV2ApplicationInput `
        -PlanJson $Json `
        -ExpectedPlanSha256 $Hash `
        -EnableRuntimeConfigurationProjectionV2
} $planJson $planHash
Assert-True ($applicationInput.enableRuntimeConfigurationProjectionV2) 'Application input must carry the explicit enabled gate.'
Assert-True (@($applicationInput.apiRuntimeConfigurationPublicSettings).Count -eq 31) 'Application input must contain exactly 31 API public settings.'
Assert-True (@($applicationInput.portalRuntimeConfigurationPublicSettings).Count -eq 11) 'Application input must contain exactly 11 portal public settings.'
Assert-True (@($applicationInput.apiRuntimeConfigurationProtectedSettingReferences).Count -eq 4) 'Application input must contain exactly four protected Key Vault references.'
Assert-True (-not (($applicationInput | ConvertTo-Json -Depth 20) -match 'psr_')) 'Opaque license references must not enter Bicep parameters.'
Assert-True ($script:applicationProcessStarts -eq 0 -and $script:applicationCloudCalls -eq 0) 'Application input generation must remain offline.'

try {
    & (Get-Module PageMaker365.Install) {
        param($Json, $Hash)
        New-PM365RuntimeConfigurationV2ApplicationInput -PlanJson ($Json.Replace('Customer', 'Tampered', [StringComparison]::Ordinal)) -ExpectedPlanSha256 $Hash -EnableRuntimeConfigurationProjectionV2
    } $planJson $planHash | Out-Null
    throw 'Application input accepted bytes that do not match the trusted plan digest.'
} catch [System.IO.InvalidDataException] {
    Assert-True ($_.Exception.Message -eq 'runtime_configuration_application_v2_plan_hash') 'Tampered application bytes must fail at the plan hash.'
}

$expandedPlan = $planJson | ConvertFrom-Json -Depth 40
$expandedPlan | Add-Member -NotePropertyName unknownDeploymentParameter -NotePropertyValue 'denied'
$expandedJson = ($expandedPlan | ConvertTo-Json -Depth 40 -Compress) + "`n"
$expandedHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData(
    [System.Text.UTF8Encoding]::new($false, $true).GetBytes($expandedJson))).ToLowerInvariant()
try {
    & (Get-Module PageMaker365.Install) {
        param($Json, $Hash)
        New-PM365RuntimeConfigurationV2ApplicationInput -PlanJson $Json -ExpectedPlanSha256 $Hash -EnableRuntimeConfigurationProjectionV2
    } $expandedJson $expandedHash | Out-Null
    throw 'Application input accepted an unknown deployment parameter.'
} catch [System.IO.InvalidDataException] {
    Assert-True ($_.Exception.Message -eq 'runtime_configuration_application_v2_plan_shape') 'Unknown deployment parameters must fail closed.'
}

$mainTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'infra\main.bicep') -Raw
$subscriptionTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'infra\subscription.bicep') -Raw
$apiTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'infra\modules\api-app-service.bicep') -Raw
$portalTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'infra\modules\frontend-app-service.bicep') -Raw
foreach ($template in @($mainTemplate, $subscriptionTemplate, $apiTemplate, $portalTemplate)) {
    Assert-True ($template.Contains('param enableRuntimeConfigurationProjectionV2 bool = false')) 'Every Bicep application boundary must default the v2 gate to false.'
    Assert-True (-not $template.Contains('opaqueReference')) 'Bicep templates must not receive the protected license acquisition reference.'
}
Assert-True ($apiTemplate.Contains('enableRuntimeConfigurationProjectionV2 ? concat(projectionV2RuntimeAppSettings, projectionV2PlatformAppSettings) : legacyRuntimeAppSettings')) 'API Bicep must preserve the exact legacy branch when disabled.'
Assert-True ($portalTemplate.Contains('enableRuntimeConfigurationProjectionV2 ? concat(projectionV2RuntimeAppSettings, projectionV2PlatformAppSettings) : legacyRuntimeAppSettings')) 'Portal Bicep must preserve the exact legacy branch when disabled.'
Assert-True (-not $apiTemplate.Contains('API_WEBPART_TEST_ARTIFACTS_ENABLED')) 'API Bicep must not represent production-forbidden settings.'
Assert-True ([regex]::Matches($portalTemplate, "name: 'WEB_ENABLE_WEB_PART_WORKBENCH'").Count -eq 1) 'Optional Workbench configuration must remain legacy-only.'
$projectionPlatformStart = $portalTemplate.IndexOf('var projectionV2PlatformAppSettings', [StringComparison]::Ordinal)
$portalResourceStart = $portalTemplate.IndexOf('resource frontendApp', [StringComparison]::Ordinal)
$projectionPlatform = $portalTemplate.Substring($projectionPlatformStart, $portalResourceStart - $projectionPlatformStart)
Assert-True (-not $projectionPlatform.Contains("name: 'PM365_RUNTIME_RELEASE_ID'", [StringComparison]::Ordinal)) 'Projection v2 must not invent a portal runtime-release setting absent from the catalog.'

Write-Host 'Runtime configuration reference contract tests passed.'
