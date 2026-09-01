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
$fixtureDirectory = Split-Path -Parent $v07FixturePath
$canonicalPackageJson = Get-Content -LiteralPath $v07FixturePath -Raw
$catalogJson = Get-Content -LiteralPath (Join-Path $fixtureDirectory 'runtime-configuration.catalog.json') -Raw
$catalogSchemaJson = Get-Content -LiteralPath (Join-Path $fixtureDirectory 'runtime-configuration.schema.json') -Raw
$publicKeyPem = Get-Content -LiteralPath (Join-Path $fixtureDirectory 'signing-public-key.pem') -Raw
$trustRecord = Get-Content -LiteralPath (Join-Path $fixtureDirectory 'signing-trust.json') -Raw | ConvertFrom-Json
$validationTime = [DateTimeOffset]::new(2026, 8, 31, 12, 0, 0, [TimeSpan]::Zero)
$engineOutput = Join-Path $repoRoot 'tests\PageMaker365.Installer.Engine.Tests\bin\Debug\net8.0'
foreach ($assemblyName in @('BouncyCastle.Cryptography.dll', 'PageMaker365.Installer.Engine.dll')) {
    $assemblyPath = Join-Path $engineOutput $assemblyName
    Assert-True (Test-Path -LiteralPath $assemblyPath) "Runtime-configuration test requires built engine assembly: $assemblyName"
    [void][Reflection.Assembly]::LoadFrom($assemblyPath)
}

$script:applicationProcessStarts = 0
$script:applicationCloudCalls = 0
function Start-Process { $script:applicationProcessStarts++; throw 'Application input generation started a process.' }
function New-AzResourceGroupDeployment { $script:applicationCloudCalls++; throw 'Application input generation invoked Azure.' }

try {
    & (Get-Module PageMaker365.Install) {
        param($Package, $KeyId, $PublicKey, $At, $Catalog, $Schema)
        New-PM365RuntimeConfigurationV2ApplicationInput -CanonicalPackageJson $Package -TrustedSigningKeyId $KeyId -TrustedSigningPublicKeyPem $PublicKey -ValidationTime $At -RuntimeConfigurationCatalogJson $Catalog -RuntimeConfigurationCatalogSchemaJson $Schema
    } $canonicalPackageJson $trustRecord.keyId $publicKeyPem $validationTime $catalogJson $catalogSchemaJson | Out-Null
    throw 'Projection-v2 application input was enabled without explicit approval.'
} catch [System.IO.InvalidDataException] {
    Assert-True ($_.Exception.Message -eq 'runtime_configuration_application_v2_disabled') 'Default-disabled application must return the exact denial.'
}

$applicationInput = & (Get-Module PageMaker365.Install) {
    param($Package, $KeyId, $PublicKey, $At, $Catalog, $Schema)
    New-PM365RuntimeConfigurationV2ApplicationInput -CanonicalPackageJson $Package -TrustedSigningKeyId $KeyId -TrustedSigningPublicKeyPem $PublicKey -ValidationTime $At -RuntimeConfigurationCatalogJson $Catalog -RuntimeConfigurationCatalogSchemaJson $Schema -EnableRuntimeConfigurationProjectionV2
} $canonicalPackageJson $trustRecord.keyId $publicKeyPem $validationTime $catalogJson $catalogSchemaJson
Assert-True ($applicationInput.enableRuntimeConfigurationProjectionV2) 'Application input must carry the explicit enabled gate.'
Assert-True (@($applicationInput.apiRuntimeConfiguration.Keys).Count -eq 31) 'Application input must contain exactly 31 closed API public settings.'
Assert-True (@($applicationInput.portalRuntimeConfiguration.Keys).Count -eq 11) 'Application input must contain exactly 11 closed portal public settings.'
Assert-True (@($applicationInput.apiRuntimeConfigurationVersionedReferences.Keys).Count -eq 2) 'Application input must contain only two already-versioned references.'
Assert-True ($applicationInput.apiRuntimeConfiguration.API_CONNECTOR_EGRESS_REQUIRE_ALLOWLIST -is [bool]) 'Boolean values must remain typed through the PowerShell boundary.'
Assert-True ($applicationInput.apiRuntimeConfiguration.API_LICENSE_VALIDATION_GRACE_HOURS -is [int]) 'Integer values must remain typed through the PowerShell boundary.'
Assert-True ($applicationInput.apiRuntimeConfiguration.API_CORS_ORIGIN -is [string[]]) 'String-list values must remain typed through the PowerShell boundary.'
Assert-True (-not (($applicationInput | ConvertTo-Json -Depth 20) -match 'psr_')) 'Opaque license references must not enter Bicep parameters.'
Assert-True (-not (($applicationInput | ConvertTo-Json -Depth 20) -match 'license-payload|image-cursor-secret')) 'Pending license and cursor destinations must not become premature Bicep app settings.'
Assert-True ($script:applicationProcessStarts -eq 0 -and $script:applicationCloudCalls -eq 0) 'Application input generation must remain offline.'

try {
    & (Get-Module PageMaker365.Install) {
        param($Package, $KeyId, $PublicKey, $At, $Catalog, $Schema)
        New-PM365RuntimeConfigurationV2ApplicationInput -CanonicalPackageJson ($Package.Replace('"product": "PageMaker365"', '"product": "Tampered"', [StringComparison]::Ordinal)) -TrustedSigningKeyId $KeyId -TrustedSigningPublicKeyPem $PublicKey -ValidationTime $At -RuntimeConfigurationCatalogJson $Catalog -RuntimeConfigurationCatalogSchemaJson $Schema -EnableRuntimeConfigurationProjectionV2
    } $canonicalPackageJson $trustRecord.keyId $publicKeyPem $validationTime $catalogJson $catalogSchemaJson | Out-Null
    throw 'Application input accepted altered package bytes.'
} catch [System.IO.InvalidDataException] {
    Assert-True ($_.Exception.Message -match 'customer_install_v07_|runtime_configuration_') 'Tampered package bytes must fail inside owned validation.'
} catch {
    Assert-True ($_.Exception.ToString() -match 'customer_install_v07_|runtime_configuration_') "Tampered package bytes must fail inside owned validation: $($_.Exception)"
}

$wrongBooleanPackage = $canonicalPackageJson.Replace('"value": true', '"value": "true"', [StringComparison]::Ordinal)
try {
    & (Get-Module PageMaker365.Install) {
        param($Package, $KeyId, $PublicKey, $At, $Catalog, $Schema)
        New-PM365RuntimeConfigurationV2ApplicationInput -CanonicalPackageJson $Package -TrustedSigningKeyId $KeyId -TrustedSigningPublicKeyPem $PublicKey -ValidationTime $At -RuntimeConfigurationCatalogJson $Catalog -RuntimeConfigurationCatalogSchemaJson $Schema -EnableRuntimeConfigurationProjectionV2
    } $wrongBooleanPackage $trustRecord.keyId $publicKeyPem $validationTime $catalogJson $catalogSchemaJson | Out-Null
    throw 'Application input string-converted a boolean value.'
} catch [System.IO.InvalidDataException] {
    Assert-True ($_.Exception.Message.Contains('runtime_configuration_projection_v2_value_type', [StringComparison]::Ordinal)) 'Wrong-type-but-string-convertible values must fail before conversion.'
} catch {
    Assert-True ($_.Exception.ToString().Contains('runtime_configuration_projection_v2_value_type', [StringComparison]::Ordinal)) 'Wrong-type-but-string-convertible values must fail before conversion.'
}

$mainTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'infra\main.bicep') -Raw
$subscriptionTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'infra\subscription.bicep') -Raw
$apiTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'infra\modules\api-app-service.bicep') -Raw
$portalTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'infra\modules\frontend-app-service.bicep') -Raw
foreach ($template in @($mainTemplate, $subscriptionTemplate, $apiTemplate, $portalTemplate)) {
    Assert-True ($template.Contains('param enableRuntimeConfigurationProjectionV2 bool = false')) 'Every Bicep application boundary must default the v2 gate to false.'
    Assert-True (-not $template.Contains('opaqueReference')) 'Bicep templates must not receive the protected license acquisition reference.'
    Assert-True (-not $template.Contains('RuntimeApplicationSetting')) 'Bicep must not expose generic caller-defined name/value arrays.'
}
Assert-True ($apiTemplate.Contains('enableRuntimeConfigurationProjectionV2 ? concat(projectionV2RuntimeAppSettings, projectionV2PlatformAppSettings) : legacyRuntimeAppSettings')) 'API Bicep must preserve the exact legacy branch when disabled.'
Assert-True ($portalTemplate.Contains('enableRuntimeConfigurationProjectionV2 ? concat(projectionV2RuntimeAppSettings, projectionV2PlatformAppSettings) : legacyRuntimeAppSettings')) 'Portal Bicep must preserve the exact legacy branch when disabled.'
Assert-True ($apiTemplate.Contains('type ApiRuntimeConfigurationV2 = {') -and $apiTemplate.Contains('API_CONNECTOR_EGRESS_REQUIRE_ALLOWLIST: bool') -and $apiTemplate.Contains('API_LICENSE_VALIDATION_GRACE_HOURS: int')) 'API Bicep must retain value types until fixed app-setting construction.'
Assert-True ($portalTemplate.Contains('type PortalRuntimeConfigurationV2 = {') -and $portalTemplate.Contains('WEB_FILE_PREVIEW_ALLOWED_FRAME_ORIGINS: string[]')) 'Portal Bicep must use a sealed typed contract.'
$apiProjectionStart = $apiTemplate.IndexOf('var projectionV2RuntimeAppSettings', [StringComparison]::Ordinal)
$apiProjectionEnd = $apiTemplate.IndexOf('var projectionV2PlatformAppSettings', [StringComparison]::Ordinal)
$apiProjection = $apiTemplate.Substring($apiProjectionStart, $apiProjectionEnd - $apiProjectionStart)
$apiProjectionNames = @([regex]::Matches($apiProjection, "\{ name: '([A-Z0-9_]+)'", [System.Text.RegularExpressions.RegexOptions]::CultureInvariant) | ForEach-Object { $_.Groups[1].Value })
$expectedApiProjectionNames = @($applicationInput.apiRuntimeConfiguration.Keys) + @($applicationInput.apiRuntimeConfigurationVersionedReferences.Keys)
Assert-True ($apiProjectionNames.Count -eq 33 -and -not (Compare-Object $expectedApiProjectionNames $apiProjectionNames -SyncWindow 0)) 'API projection must construct the parser-authorized 31+2 settings in exact order.'
$portalProjectionStart = $portalTemplate.IndexOf('var projectionV2RuntimeAppSettings', [StringComparison]::Ordinal)
$portalProjectionEnd = $portalTemplate.IndexOf('var legacyRuntimeAppSettings', [StringComparison]::Ordinal)
$portalProjection = $portalTemplate.Substring($portalProjectionStart, $portalProjectionEnd - $portalProjectionStart)
$portalProjectionNames = @([regex]::Matches($portalProjection, "\{ name: '(WEB_[A-Z0-9_]+)'", [System.Text.RegularExpressions.RegexOptions]::CultureInvariant) | ForEach-Object { $_.Groups[1].Value })
Assert-True ($portalProjectionNames.Count -eq 11 -and -not (Compare-Object @($applicationInput.portalRuntimeConfiguration.Keys) $portalProjectionNames -SyncWindow 0)) 'Portal projection must construct the parser-authorized 11 settings in exact order.'
foreach ($deniedName in @('PORT', 'API_CONNECTOR_ENTITLEMENTS_SYNC_URL', 'API_WEB_PART_ENTITLEMENTS_SYNC_URL', 'API_WEBPART_TEST_ARTIFACTS_ENABLED', 'WEB_ENABLE_WEB_PART_WORKBENCH')) {
    Assert-True ($deniedName -notin $apiProjectionNames -and $deniedName -notin $portalProjectionNames) "Projection Bicep must not represent omitted, conditional, optional, or forbidden setting $deniedName."
}
Assert-True (-not $apiTemplate.Contains('API_WEBPART_TEST_ARTIFACTS_ENABLED')) 'API Bicep must not represent production-forbidden settings.'
Assert-True ([regex]::Matches($portalTemplate, "name: 'WEB_ENABLE_WEB_PART_WORKBENCH'").Count -eq 1) 'Optional Workbench configuration must remain legacy-only.'
$projectionPlatformStart = $portalTemplate.IndexOf('var projectionV2PlatformAppSettings', [StringComparison]::Ordinal)
$portalResourceStart = $portalTemplate.IndexOf('resource frontendApp', [StringComparison]::Ordinal)
$projectionPlatform = $portalTemplate.Substring($projectionPlatformStart, $portalResourceStart - $projectionPlatformStart)
Assert-True (-not $projectionPlatform.Contains("name: 'PM365_RUNTIME_RELEASE_ID'", [StringComparison]::Ordinal)) 'Projection v2 must not invent a portal runtime-release setting absent from the catalog.'

Write-Host 'Runtime configuration reference contract tests passed.'
