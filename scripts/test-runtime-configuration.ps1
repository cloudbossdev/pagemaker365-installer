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

$expected = @('DATABASE_URL', 'API_ENTRA_CLIENT_SECRET', 'API_SESSION_SECRET')
$statuses = @(& (Get-Module PageMaker365.Install) {
    param($Response, $Expected)
    ConvertTo-PM365KeyVaultReferenceStatus -Response $Response -ExpectedAppSettings $Expected
} $fixture $expected)

Assert-True ($statuses.Count -eq 2) 'The parser must keep only expected App Service settings returned by Azure.'
Assert-True (($statuses | Where-Object appSettingName -eq 'DATABASE_URL').status -eq 'Resolved') 'Resolved reference status was not parsed.'
Assert-True (($statuses | Where-Object appSettingName -eq 'API_ENTRA_CLIENT_SECRET').status -eq 'AccessToKeyVaultDenied') 'Denied reference status was not parsed.'
Assert-True (-not ($statuses | Where-Object appSettingName -eq 'API_SESSION_SECRET')) 'A reference omitted by Azure must remain missing and block completion.'

$runtimeCommand = Get-Content -LiteralPath (Join-Path $repoRoot 'modules\PageMaker365.Install\Public\Set-PM365RuntimeConfiguration.ps1') -Raw
Assert-True ($runtimeCommand.Contains("status -eq 'Resolved'")) 'Runtime configuration must require every reference to report Resolved.'
Assert-True (-not $runtimeCommand.Contains('resolveStatus')) 'Runtime configuration must use the current App Service properties.status contract.'

Write-Host 'Runtime configuration reference contract tests passed.'
