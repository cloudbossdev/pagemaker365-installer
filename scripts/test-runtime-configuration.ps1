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

Write-Host 'Runtime configuration reference contract tests passed.'
