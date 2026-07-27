[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$profilePath = Join-Path $repoRoot 'config\installer-security-profile.json'
$profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) {
        throw $Message
    }
}

$expectedScopes = @('User.Read', 'Domain.Read.All', 'RoleManagement.Read.Directory', 'Sites.Read.All')
$profileScopes = @($profile.graph.delegatedScopes | ForEach-Object { [string]$_.name })
Assert-True (($profileScopes -join '|') -eq ($expectedScopes -join '|')) 'The security profile Graph scopes do not match the approved order and set.'
Assert-True (-not $profile.graph.writeScopesRequested) 'The implemented installer profile must not request Graph write scopes.'

$graphAuthenticator = Get-Content -LiteralPath (Join-Path $repoRoot 'src\PageMaker365.Installer.Engine\Services\GraphDeviceCodeAuthenticator.cs') -Raw
$graphScopeHelper = Get-Content -LiteralPath (Join-Path $repoRoot 'modules\PageMaker365.Install\Private\Get-PM365RequiredGraphScopes.ps1') -Raw
foreach ($scope in $expectedScopes) {
    Assert-True ($graphAuthenticator.Contains('"' + $scope + '"')) "C# Graph authentication is missing approved scope $scope."
    Assert-True ($graphScopeHelper.Contains("'$scope'")) "PowerShell Graph authentication is missing approved scope $scope."
}

foreach ($writeScope in @('Application.ReadWrite.All', 'AppRoleAssignment.ReadWrite.All', 'Directory.ReadWrite.All', 'Sites.ReadWrite.All')) {
    Assert-True (-not $graphAuthenticator.Contains($writeScope)) "C# Graph authentication requests prohibited write scope $writeScope."
    Assert-True (-not $graphScopeHelper.Contains($writeScope)) "PowerShell Graph authentication requests prohibited write scope $writeScope."
}

$connectGraph = Get-Content -LiteralPath (Join-Path $repoRoot 'modules\PageMaker365.Install\Public\Connect-PM365Graph.ps1') -Raw
$testEntra = Get-Content -LiteralPath (Join-Path $repoRoot 'modules\PageMaker365.Install\Public\Test-PM365EntraPermissions.ps1') -Raw
$graphDiscovery = Get-Content -LiteralPath (Join-Path $repoRoot 'modules\PageMaker365.Install\Public\Get-PM365GraphDiscovery.ps1') -Raw
foreach ($source in @($connectGraph, $testEntra, $graphDiscovery)) {
    Assert-True ($source.Contains('Get-PM365RequiredGraphScopes')) 'A Graph workflow does not use the canonical PowerShell scope helper.'
    Assert-True (-not $source.Contains('Directory.Read.All')) 'A Graph workflow still contains the deprecated broad Directory.Read.All scope.'
}

. (Join-Path $repoRoot 'modules\PageMaker365.Install\Private\Test-PM365AzureDeploymentRoleSet.ps1')
Assert-True ((Test-PM365AzureDeploymentRoleSet -RoleNames @('Owner')).ready) 'Owner must satisfy the Azure deployment role contract.'
Assert-True ((Test-PM365AzureDeploymentRoleSet -RoleNames @('Contributor', 'Role Based Access Control Administrator')).ready) 'Contributor plus RBAC Administrator must satisfy the Azure deployment role contract.'
Assert-True ((Test-PM365AzureDeploymentRoleSet -RoleNames @('Contributor', 'User Access Administrator')).ready) 'Contributor plus User Access Administrator must satisfy the Azure deployment role contract.'
Assert-True (-not (Test-PM365AzureDeploymentRoleSet -RoleNames @('Contributor')).ready) 'Contributor alone must not satisfy the Azure deployment role contract.'
Assert-True (-not (Test-PM365AzureDeploymentRoleSet -RoleNames @('Role Based Access Control Administrator')).ready) 'RBAC Administrator alone must not satisfy the Azure deployment role contract.'

$roleSets = @($profile.azure.acceptedRoleSets | ForEach-Object { @($_) -join '+' })
Assert-True ($roleSets -contains 'Owner') 'The security profile must allow Owner.'
Assert-True ($roleSets -contains 'Contributor+Role Based Access Control Administrator') 'The security profile is missing the Contributor plus RBAC Administrator role set.'
Assert-True ($roleSets -contains 'Contributor+User Access Administrator') 'The security profile is missing the Contributor plus User Access Administrator role set.'

Assert-True ($profile.network.transport -eq 'HTTPS') 'The security profile must require HTTPS.'
Assert-True ($profile.network.defaultPort -eq 443) 'The security profile must require default port 443.'
foreach ($requiredHost in @('login.microsoftonline.com', 'microsoft.com', 'graph.microsoft.com', 'management.azure.com', 'api.pagemaker365.com', 'api-staging.pagemaker365.com', 'downloads.pagemaker365.com', 'downloads-staging.pagemaker365.com')) {
    Assert-True (@($profile.network.destinations.host) -contains $requiredHost) "The network contract is missing $requiredHost."
}

$bicep = Get-Content -LiteralPath (Join-Path $repoRoot 'infra\main.bicep') -Raw
Assert-True ($bicep.Contains("modules/key-vault-role-assignment.bicep")) 'The Azure role contract must account for the Key Vault role assignment deployed by Bicep.'

$runtimeTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'infra\runtime-configuration.bicep') -Raw
$apiTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'infra\modules\api-app-service.bicep') -Raw
$installerEngine = Get-Content -LiteralPath (Join-Path $repoRoot 'src\PageMaker365.Installer.Engine\Services\InstallerEngine.cs') -Raw
$runtimeCommand = Get-Content -LiteralPath (Join-Path $repoRoot 'modules\PageMaker365.Install\Public\Set-PM365RuntimeConfiguration.ps1') -Raw
$stateModel = Get-Content -LiteralPath (Join-Path $repoRoot 'src\PageMaker365.Installer.Engine\Models\PersistedInstallerState.cs') -Raw
$runtimeArtifactCommand = Get-Content -LiteralPath (Join-Path $repoRoot 'modules\PageMaker365.Install\Private\Publish-PM365RuntimeArtifacts.ps1') -Raw

Assert-True ($runtimeTemplate.Contains('@secure()')) 'Runtime secret values must enter ARM through a secure Bicep parameter.'
Assert-True ($runtimeTemplate.Contains('Microsoft.KeyVault/vaults/secrets')) 'Runtime secrets must be provisioned directly as customer Key Vault resources.'
Assert-True ($apiTemplate.Contains('keyVaultReferenceIdentity')) 'The API App Service must explicitly use the customer managed identity for Key Vault references.'
Assert-True ($apiTemplate.Contains('@Microsoft.KeyVault(VaultName=')) 'Runtime App Service settings must use Key Vault references instead of raw values.'
Assert-True ($installerEngine.Contains('standardInputWriter:')) 'Protected values must be passed to the child process through redirected standard input.'
Assert-True ($runtimeCommand.Contains('[Console]::In.ReadLine()')) 'The runtime configuration command must read protected input from standard input.'
Assert-True ($runtimeCommand.Contains('rawValuesIncluded = $false')) 'Runtime configuration evidence must explicitly exclude raw values.'
Assert-True ($runtimeCommand.Contains("valueStorage = 'CustomerKeyVault'")) 'Runtime configuration evidence must identify the customer Key Vault storage boundary.'
Assert-True (-not $stateModel.Contains('RuntimeSecretMaterial')) 'Resumable installer state must not contain protected runtime material.'
Assert-True ($runtimeArtifactCommand.Contains('Publish-AzWebApp')) 'Verified runtime ZIP files must be published with the authenticated Az.Websites module.'
Assert-True ($runtimeArtifactCommand.Contains('Get-FileHash')) 'Runtime artifact SHA-256 must be recomputed before Azure publish.'
Assert-True ($runtimeArtifactCommand.Contains('AllowAutoRedirect = $false')) 'Runtime artifact downloads must not follow an untrusted redirect.'
Assert-True ($runtimeArtifactCommand.Contains('MaximumBytes 268435456')) 'Runtime artifact downloads must enforce a bounded response size.'

$samplePackage = Get-Content -LiteralPath (Join-Path $repoRoot 'samples\contoso.customer.install.json') -Raw | ConvertFrom-Json
$runtimeSecretNames = @($samplePackage.secrets.runtimeSecrets | ForEach-Object { [string]$_.appSettingName })
foreach ($requiredSetting in @('DATABASE_URL', 'API_ENTRA_CLIENT_SECRET', 'API_SESSION_SECRET')) {
    Assert-True ($runtimeSecretNames -contains $requiredSetting) "The sample signed runtime contract is missing $requiredSetting."
}
foreach ($secretDefinition in @($samplePackage.secrets.runtimeSecrets)) {
    Assert-True ($null -eq $secretDefinition.PSObject.Properties['value']) "Runtime secret metadata must not contain a raw value for $($secretDefinition.appSettingName)."
}

$modulePath = Join-Path $repoRoot 'modules\PageMaker365.Install\PageMaker365.Install.psd1'
Import-Module $modulePath -Force
$invalidPackagePath = Join-Path ([IO.Path]::GetTempPath()) ("pm365-runtime-contract-{0}.json" -f [guid]::NewGuid())
try {
    $invalidPackage = Get-Content -LiteralPath (Join-Path $repoRoot 'samples\contoso.customer.install.json') -Raw | ConvertFrom-Json
    $invalidPackage.contractVersion = '0.2'
    $invalidPackage | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $invalidPackagePath -Encoding utf8
    $invalidVersionResults = @(Test-PM365DeploymentContract -ConfigPath $invalidPackagePath)
    Assert-True ($invalidVersionResults.code -contains 'DeploymentContractVersionUnsupported') 'Direct PowerShell deployment must reject pre-0.3 packages.'

    $invalidPackage.contractVersion = '0.3'
    $invalidPackage.secrets.runtimeSecrets = @($invalidPackage.secrets.runtimeSecrets | Select-Object -First 2)
    $invalidPackage | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $invalidPackagePath -Encoding utf8
    $invalidSecretResults = @(Test-PM365DeploymentContract -ConfigPath $invalidPackagePath)
    Assert-True ($invalidSecretResults.code -contains 'DeploymentSecretsContractInvalid') 'Direct PowerShell deployment must reject incomplete runtime secret metadata.'

    $invalidPackage = Get-Content -LiteralPath (Join-Path $repoRoot 'samples\contoso.customer.install.json') -Raw | ConvertFrom-Json
    $unexpectedSecret = $invalidPackage.secrets.runtimeSecrets[0].PSObject.Copy()
    $unexpectedSecret.keyVaultSecretName = 'UNSUPPORTED-SECRET'
    $unexpectedSecret.appSettingName = 'UNSUPPORTED_SECRET'
    $invalidPackage.secrets.runtimeSecrets = @($invalidPackage.secrets.runtimeSecrets) + @($unexpectedSecret)
    $invalidPackage | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $invalidPackagePath -Encoding utf8
    $unexpectedSecretResults = @(Test-PM365DeploymentContract -ConfigPath $invalidPackagePath)
    Assert-True ($unexpectedSecretResults.code -contains 'DeploymentSecretsContractInvalid') 'Direct PowerShell deployment must reject unexpected runtime secret metadata.'

    $invalidPackage = Get-Content -LiteralPath (Join-Path $repoRoot 'samples\contoso.customer.install.json') -Raw | ConvertFrom-Json
    $invalidPackage.secrets.runtimeSecrets[0].minimumLength = 4097
    $invalidPackage | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $invalidPackagePath -Encoding utf8
    $oversizedSecretResults = @(Test-PM365DeploymentContract -ConfigPath $invalidPackagePath)
    Assert-True ($oversizedSecretResults.code -contains 'DeploymentSecretsContractInvalid') 'Direct PowerShell deployment must reject runtime secret minimum lengths over 4096.'
} finally {
    Remove-Item -LiteralPath $invalidPackagePath -Force -ErrorAction SilentlyContinue
}

foreach ($reference in $profile.implementationReferences) {
    Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot ([string]$reference))) "Security profile implementation reference does not exist: $reference"
}

Write-Host "Security contract verified: $($expectedScopes.Count) read-only Graph scopes, $($roleSets.Count) Azure role sets, $(@($profile.network.destinations).Count) network destinations, and protected runtime provisioning."
