[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = 'C:\Program Files\dotnet'
if (Test-Path -LiteralPath $dotnetPath) {
    $env:Path = "$dotnetPath;$env:Path"
}

Write-Host 'Checking JSON files...'
Get-ChildItem -Path $repoRoot -Recurse -File -Include *.json |
    Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\|\\logs\\|\\support-bundle\\' } |
    ForEach-Object {
        Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json | Out-Null
    }

Write-Host 'Validating sample schemas...'
& (Join-Path $repoRoot 'scripts\validate-schemas.ps1')

Write-Host 'Checking onboarding discovery samples...'
$bootstrapPath = Join-Path $repoRoot 'samples\contoso.onboarding.bootstrap.json'
$discoveryPath = Join-Path $repoRoot 'samples\contoso.tenant.discovery.json'
$bootstrap = Get-Content -LiteralPath $bootstrapPath -Raw | ConvertFrom-Json
$discovery = Get-Content -LiteralPath $discoveryPath -Raw | ConvertFrom-Json

@(
    @{ value = $bootstrap.sessionId; name = 'bootstrap.sessionId' },
    @{ value = $bootstrap.portalBaseUrl; name = 'bootstrap.portalBaseUrl' },
    @{ value = $bootstrap.apiBaseUrl; name = 'bootstrap.apiBaseUrl' },
    @{ value = $bootstrap.oneTimeCode; name = 'bootstrap.oneTimeCode' },
    @{ value = $discovery.discoveryId; name = 'discovery.discoveryId' },
    @{ value = $discovery.onboardingSessionId; name = 'discovery.onboardingSessionId' },
    @{ value = $discovery.dataPolicy; name = 'discovery.dataPolicy' }
) | ForEach-Object {
    if ([string]::IsNullOrWhiteSpace([string]$_.value)) {
        throw "Required onboarding discovery sample field is missing: $($_.name)"
    }
}

if ($discovery.dataPolicy -ne 'InstallReadinessOnly') {
    throw 'Tenant discovery sample must use the InstallReadinessOnly data policy.'
}

@('documentContent', 'mailboxContent', 'userFiles', 'rawSecrets') | ForEach-Object {
    if ($bootstrap.discoveryPolicy.excludedDataTypes -notcontains $_) {
        throw "Bootstrap discovery policy must exclude $_."
    }
}

Write-Host 'Checking PowerShell syntax...'
$scriptRoots = @(
    (Join-Path $repoRoot 'modules')
    (Join-Path $repoRoot 'scripts')
)
$scriptRoots |
    ForEach-Object { Get-ChildItem -Path $_ -Recurse -File -Include *.ps1,*.psm1 } |
    ForEach-Object {
        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$errors) | Out-Null
        if ($errors.Count -gt 0) {
            throw "PowerShell parse error in $($_.FullName): $($errors[0].Message)"
        }
    }

Write-Host 'Restoring solution...'
dotnet restore (Join-Path $repoRoot 'PageMaker365.Installer.sln')
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

Write-Host 'Building solution...'
dotnet build (Join-Path $repoRoot 'PageMaker365.Installer.sln') --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

Write-Host 'Running onboarding API contract tests...'
$contractTestProject = Join-Path $repoRoot 'tests\PageMaker365.Installer.Engine.Tests\PageMaker365.Installer.Engine.Tests.csproj'
dotnet run --project $contractTestProject --no-build
if ($LASTEXITCODE -ne 0) {
    throw "Onboarding API contract tests failed with exit code $LASTEXITCODE."
}

$modulePath = Join-Path $repoRoot 'modules\PageMaker365.Install\PageMaker365.Install.psd1'
$configPath = Join-Path $repoRoot 'samples\contoso.customer.install.json'
$reportPath = Join-Path $repoRoot 'support-bundle\verify-install-report.md'
Import-Module $modulePath -Force

Write-Host 'Checking exported commands...'
@(
    'Connect-PM365Azure',
    'Connect-PM365Graph',
    'Get-PM365AzureDiscovery',
    'Get-PM365GraphDiscovery',
    'Start-PM365Preflight',
    'Test-PM365DeploymentContract',
    'Invoke-PM365WhatIf',
    'Invoke-PM365Deployment',
    'Test-PM365SmokeTests'
) | ForEach-Object {
    Get-Command $_ -Module PageMaker365.Install -ErrorAction Stop | Out-Null
}

Write-Host 'Running Azure discovery...'
Get-PM365AzureDiscovery -ConfigPath $configPath | ConvertTo-Json -Depth 12 | Out-Null

Write-Host 'Running Graph discovery...'
Get-PM365GraphDiscovery -ConfigPath $configPath | ConvertTo-Json -Depth 16 | Out-Null

Write-Host 'Testing discovery command contracts...'
& (Join-Path $repoRoot 'scripts\test-discovery.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "Discovery command contract tests failed with exit code $LASTEXITCODE."
}

Write-Host 'Running preflight...'
Start-PM365Preflight -ConfigPath $configPath | ConvertTo-Json -Depth 12 | Out-Null

Write-Host 'Checking deployment contract...'
Test-PM365DeploymentContract -ConfigPath $configPath | ConvertTo-Json -Depth 12 | Out-Null

Write-Host 'Checking deployment parameter validation...'
$invalidConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$invalidConfig.azure.resourceNames.storageAccountName = 'Invalid-Storage-Name'
$invalidConfigPath = Join-Path ([System.IO.Path]::GetTempPath()) ("pm365-invalid-config-{0}.json" -f ([guid]::NewGuid()))
try {
    $invalidConfig | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $invalidConfigPath -Encoding utf8
    $invalidContractResults = @(Test-PM365DeploymentContract -ConfigPath $invalidConfigPath)
    if ($invalidContractResults.code -notcontains 'DeploymentParametersInvalid') {
        throw 'Invalid storage account name was not rejected by deployment contract validation.'
    }
} finally {
    Remove-Item -LiteralPath $invalidConfigPath -Force -ErrorAction SilentlyContinue
}

Write-Host 'Building Bicep template...'
Invoke-PM365BicepBuild | ConvertTo-Json -Depth 12 | Out-Null

Write-Host 'Checking what-if guard...'
$whatIfArtifactPath = Join-Path $repoRoot 'support-bundle\verify-azure-whatif.json'
if (Test-Path -LiteralPath $whatIfArtifactPath) {
    Remove-Item -LiteralPath $whatIfArtifactPath -Force
}

Invoke-PM365WhatIf -ConfigPath $configPath -OutputPath $whatIfArtifactPath | ConvertTo-Json -Depth 12 | Out-Null
if (-not (Test-Path -LiteralPath $whatIfArtifactPath)) {
    throw "What-if artifact was not written: $whatIfArtifactPath"
}

Get-Content -LiteralPath $whatIfArtifactPath -Raw | ConvertFrom-Json | Out-Null

Write-Host 'Running smoke test scaffold...'
Test-PM365SmokeTests -ConfigPath $configPath | ConvertTo-Json -Depth 12 | Out-Null

Write-Host 'Generating report...'
New-PM365InstallReport -ConfigPath $configPath -OutputPath $reportPath | ConvertTo-Json -Depth 12 | Out-Null

Write-Host 'Verification completed.'
