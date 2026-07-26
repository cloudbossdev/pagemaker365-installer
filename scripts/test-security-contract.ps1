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
foreach ($requiredHost in @('login.microsoftonline.com', 'microsoft.com', 'graph.microsoft.com', 'management.azure.com', 'api.pagemaker365.com', 'api-staging.pagemaker365.com')) {
    Assert-True (@($profile.network.destinations.host) -contains $requiredHost) "The network contract is missing $requiredHost."
}

$bicep = Get-Content -LiteralPath (Join-Path $repoRoot 'infra\main.bicep') -Raw
Assert-True ($bicep.Contains("modules/key-vault-role-assignment.bicep")) 'The Azure role contract must account for the Key Vault role assignment deployed by Bicep.'

foreach ($reference in $profile.implementationReferences) {
    Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot ([string]$reference))) "Security profile implementation reference does not exist: $reference"
}

$assistantApi = Get-Content -LiteralPath (Join-Path $repoRoot 'src\PageMaker365.Installer.Engine\Services\AssistantApiClient.cs') -Raw
$assistantTransfer = Get-Content -LiteralPath (Join-Path $repoRoot 'src\PageMaker365.Installer.Engine\Services\AssistantTransferPolicy.cs') -Raw
$assistantActions = Get-Content -LiteralPath (Join-Path $repoRoot 'src\PageMaker365.Installer.Engine\Services\AssistantActionPolicy.cs') -Raw
$assistantContract = Get-Content -LiteralPath (Join-Path $repoRoot 'docs\assistant-api-contract.md') -Raw
$assistantRunbookPath = Join-Path $repoRoot 'docs\testing\assistant-support-handoff.md'

Assert-True ($assistantApi.Contains('ShouldFallback')) 'Assistant API fallback must remain restricted by the local transient-failure policy.'
Assert-True ($assistantTransfer.Contains('RedactedText')) 'Assistant transfer policy must retain the redacted-text content treatment.'
Assert-True ($assistantTransfer.Contains('LocalOnlyBinary')) 'Assistant transfer policy must keep binary attachments local-only.'
Assert-True ($assistantActions.Contains('rerun-preflight') -and $assistantActions.Contains('requiresApproval: true')) 'Privileged assistant actions must retain their local approval floor.'
Assert-True ($assistantContract.Contains('draft, not a final submitted ticket')) 'Assistant contract must retain the draft-only support-ticket boundary.'
Assert-True (Test-Path -LiteralPath $assistantRunbookPath) 'The live assistant support-handoff runbook is missing.'
Assert-True (@($profile.dataBoundaries.prohibited) -contains 'local filesystem paths, original attachment filenames, screenshots, and binary assistant attachments in portal requests') 'The security profile is missing the assistant local-data boundary.'

Write-Host "Security contract verified: $($expectedScopes.Count) read-only Graph scopes, $($roleSets.Count) Azure role sets, $(@($profile.network.destinations).Count) network destinations."
