[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $repoRoot 'modules\PageMaker365.Install\PageMaker365.Install.psd1'
$configPath = Join-Path $repoRoot 'samples\contoso.customer.install.json'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "pm365-discovery-tests\$([guid]::NewGuid().ToString('N'))"

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
    Import-Module $modulePath -Force

    $azureMockPath = Join-Path $tempRoot 'azure-ready.json'
    @'
{
  "accountId": "admin@contoso.com",
  "tenantId": "00000000-0000-0000-0000-000000000000",
  "selectedSubscriptionId": "11111111-1111-1111-1111-111111111111",
  "selectedSubscriptionName": "Contoso Production",
  "selectedSubscriptionState": "Enabled",
  "recommendedLocation": "eastus",
  "targetResourceGroupName": "rg-pagemaker365-contoso-prod",
  "resourceGroupExists": true,
  "subscriptions": [
    {
      "subscriptionId": "11111111-1111-1111-1111-111111111111",
      "displayName": "Contoso Production",
      "state": "Enabled"
    }
  ]
}
'@ | Set-Content -LiteralPath $azureMockPath -Encoding utf8

    $graphMockPath = Join-Path $tempRoot 'graph-ready.json'
    @'
{
  "accountId": "admin@contoso.com",
  "tenantId": "00000000-0000-0000-0000-000000000000",
  "scopes": [
    "Directory.Read.All",
    "Sites.Read.All",
    "User.Read"
  ],
  "verifiedDomains": [
    "contoso.com",
    "contoso.sharepoint.com"
  ],
  "defaultDomain": "contoso.com",
  "site": {
    "id": "contoso.sharepoint.com,site-collection,site-id",
    "displayName": "Contoso Intranet",
    "webUrl": "https://contoso.sharepoint.com/sites/intranet"
  },
  "drives": [
    {
      "id": "drive-docs",
      "name": "Documents",
      "webUrl": "https://contoso.sharepoint.com/sites/intranet/Shared%20Documents",
      "driveType": "documentLibrary"
    }
  ],
  "roles": [
    "Global Administrator"
  ]
}
'@ | Set-Content -LiteralPath $graphMockPath -Encoding utf8

    $azure = Get-PM365AzureDiscovery -ConfigPath $configPath -MockContextPath $azureMockPath
    Assert-Equal '00000000-0000-0000-0000-000000000000' $azure.tenantId 'Azure tenant did not match the mock context.'
    Assert-Equal '11111111-1111-1111-1111-111111111111' $azure.selectedSubscriptionId 'Azure subscription did not match the mock context.'
    Assert-Equal 'Enabled' $azure.selectedSubscriptionState 'Azure subscription state was not preserved.'
    Assert-True $azure.resourceGroupExists 'Azure resource group existence was not preserved.'
    Assert-True (@($azure.findings | Where-Object { $_.code -eq 'AzureDiscoveryReady' }).Count -eq 1) 'Azure discovery ready finding was not returned.'

    $graph = Get-PM365GraphDiscovery -ConfigPath $configPath -MockContextPath $graphMockPath
    Assert-Equal '00000000-0000-0000-0000-000000000000' $graph.tenantId 'Graph tenant did not match the mock context.'
    Assert-Equal 'contoso.com' $graph.defaultDomain 'Graph default domain was not preserved.'
    Assert-True ($graph.verifiedDomains -contains 'contoso.sharepoint.com') 'Graph verified domains did not include the SharePoint hostname.'
    Assert-True $graph.siteResolved 'SharePoint site was not marked resolved.'
    Assert-Equal 'drive-docs' $graph.defaultDocumentLibraryId 'Default document library drive was not resolved.'
    Assert-Equal 'AdminRoleReady' $graph.consentStatus 'Admin consent readiness was not preserved.'
    Assert-True (@($graph.findings | Where-Object { $_.code -eq 'EntraAdminRoleReady' }).Count -eq 1) 'Entra admin-role finding was not returned.'

    $serializedDiscovery = @{ azure = $azure; graph = $graph } | ConvertTo-Json -Depth 24
    foreach ($excludedDataType in @('documentContent', 'mailboxContent', 'userFiles', 'rawSecrets')) {
        Assert-True (-not $serializedDiscovery.Contains($excludedDataType)) "Discovery output included excluded data type '$excludedDataType'."
    }

    $azureMismatchPath = Join-Path $tempRoot 'azure-mismatch.json'
    @'
{
  "accountId": "admin@wrong.example",
  "tenantId": "99999999-9999-9999-9999-999999999999",
  "selectedSubscriptionId": "11111111-1111-1111-1111-111111111111",
  "selectedSubscriptionName": "Contoso Production",
  "selectedSubscriptionState": "Enabled",
  "resourceGroupExists": true
}
'@ | Set-Content -LiteralPath $azureMismatchPath -Encoding utf8

    $azureMismatch = Get-PM365AzureDiscovery -ConfigPath $configPath -MockContextPath $azureMismatchPath
    Assert-True (@($azureMismatch.findings | Where-Object { $_.code -eq 'AzureTenantMismatch' -and $_.severity -eq 'Failed' }).Count -eq 1) 'Azure tenant mismatch was not reported as failed.'

    $graphMissingScopesPath = Join-Path $tempRoot 'graph-missing-scopes.json'
    @'
{
  "accountId": "admin@contoso.com",
  "tenantId": "00000000-0000-0000-0000-000000000000",
  "scopes": [
    "User.Read"
  ],
  "verifiedDomains": [
    "contoso.com"
  ],
  "defaultDomain": "contoso.com",
  "site": {
    "id": "contoso.sharepoint.com,site-collection,site-id",
    "displayName": "Contoso Intranet",
    "webUrl": "https://contoso.sharepoint.com/sites/intranet"
  },
  "drives": [
    {
      "id": "drive-docs",
      "name": "Documents",
      "webUrl": "https://contoso.sharepoint.com/sites/intranet/Shared%20Documents",
      "driveType": "documentLibrary"
    }
  ],
  "roles": [
    "Global Administrator"
  ]
}
'@ | Set-Content -LiteralPath $graphMissingScopesPath -Encoding utf8

    $graphMissingScopes = Get-PM365GraphDiscovery -ConfigPath $configPath -MockContextPath $graphMissingScopesPath
    Assert-True (@($graphMissingScopes.findings | Where-Object { $_.code -eq 'GraphDiscoveryScopesMissing' -and $_.severity -eq 'Warning' }).Count -eq 1) 'Graph missing-scope warning was not returned.'

    Write-Host 'Discovery command contract tests passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
