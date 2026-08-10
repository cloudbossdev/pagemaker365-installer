[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $repoRoot 'modules\PageMaker365.Install\PageMaker365.Install.psd1'
$module = Import-Module $modulePath -Force -PassThru

Import-Module Az.Resources -ErrorAction Stop
$jsonObject = [Newtonsoft.Json.Linq.JObject]::Parse('{"product":"PageMaker365","environment":"sandbox"}')
$sensitiveValue = [Newtonsoft.Json.Linq.JValue]::CreateString('secret-value')
$outputs = [ordered]@{
    tags = [pscustomobject]@{
        Type = 'Object'
        Value = $jsonObject
    }
    clientSecret = [pscustomobject]@{
        Type = 'String'
        Value = $sensitiveValue
    }
}

$safeOutputs = & $module {
    param($value)
    Get-PM365SafeDeploymentOutputs -Outputs $value
} $outputs

if ($safeOutputs.outputCount -ne 2) {
    throw "Expected two deployment outputs, got $($safeOutputs.outputCount)."
}

if ($safeOutputs.includedOutputCount -ne 1 -or $safeOutputs.redactedOutputCount -ne 1) {
    throw 'Deployment output redaction counts were incorrect.'
}

if ($safeOutputs.outputs.tags.value.product -ne 'PageMaker365') {
    throw 'Newtonsoft JObject deployment output was not converted to a JSON-safe object.'
}

if ($safeOutputs.outputs.Contains('clientSecret')) {
    throw 'Sensitive deployment output was not removed.'
}

Get-ChildItem -Path (Join-Path $repoRoot 'modules\PageMaker365.Install\Private') -Filter '*.ps1' -File |
    ForEach-Object { . $_.FullName }
Get-ChildItem -Path (Join-Path $repoRoot 'modules\PageMaker365.Install\Public') -Filter '*.ps1' -File |
    ForEach-Object { . $_.FullName }

$script:azureDeploymentMutations = 0
function Invoke-PM365BicepBuild {
    New-PM365Result `
        -Status 'Passed' `
        -Code 'BicepBuildReady' `
        -Summary 'Mock Bicep build passed.' `
        -Details 'No live build was required.'
}
function Get-PM365BicepCommand { $null }
function New-AzSubscriptionDeployment {
    $script:azureDeploymentMutations++
    throw 'An invalid package reached Azure mutation.'
}

$sampleConfigPath = Join-Path $repoRoot 'samples\contoso.customer.install.json'
$baseConfig = Get-Content -LiteralPath $sampleConfigPath -Raw | ConvertFrom-Json
$alternateNamesConfig = $baseConfig | ConvertTo-Json -Depth 30 | ConvertFrom-Json
$alternateNamesConfig.secrets.runtimeSecrets[0].keyVaultSecretName = 'Customer-Database-Connection'
$alternateNamesConfig.secrets.runtimeSecrets[1].keyVaultSecretName = 'Customer-Entra-Credential'
$alternateNamesConfig.secrets.runtimeSecrets[2].keyVaultSecretName = 'Customer-Image-Cursor-Key'
$alternateNameIssues = @(Get-PM365TemplateParameterValidationIssue -Config $alternateNamesConfig)
if ($alternateNameIssues.Count -ne 0) {
    throw "Safe package-defined Key Vault secret names were rejected: $($alternateNameIssues | ConvertTo-Json -Compress)"
}
$alternateNameParameters = New-PM365TemplateParameterObject -Config $alternateNamesConfig
$alternateReferences = @($alternateNameParameters.runtimeSecretReferences)
foreach ($expectedReference in @(
    @{ AppSettingName = 'DATABASE_URL'; KeyVaultSecretName = 'Customer-Database-Connection' },
    @{ AppSettingName = 'API_ENTRA_CLIENT_SECRET'; KeyVaultSecretName = 'Customer-Entra-Credential' },
    @{ AppSettingName = 'API_IMAGE_ASSET_CURSOR_SECRET'; KeyVaultSecretName = 'Customer-Image-Cursor-Key' }
)) {
    $matchingReferences = @($alternateReferences | Where-Object {
        [string]$_.appSettingName -ceq $expectedReference.AppSettingName -and
        [string]$_.keyVaultSecretName -ceq $expectedReference.KeyVaultSecretName
    })
    if ($matchingReferences.Count -ne 1) {
        throw "Deployment parameters did not preserve signed Key Vault secret name $($expectedReference.KeyVaultSecretName)."
    }
}

try {
    Invoke-PM365Deployment -ConfigPath $sampleConfigPath -Confirm:$false | Out-Null
    throw 'Direct deployment accepted a signed package without a trusted exact-payload binding.'
} catch [System.IO.InvalidDataException] {
    if ($script:azureDeploymentMutations -ne 0) {
        throw 'Missing trusted package binding reached Azure deployment mutation.'
    }
}

$validationCases = @(
    @{ Name = 'top-level contract version'; Mutate = { param($c) $c.contractVersion = '0.3' } },
    @{ Name = 'missing runtime secret'; Mutate = { param($c) $c.secrets.runtimeSecrets = @($c.secrets.runtimeSecrets | Select-Object -First 2) } },
    @{ Name = 'duplicate runtime secret'; Mutate = { param($c) $c.secrets.runtimeSecrets = @($c.secrets.runtimeSecrets[0], $c.secrets.runtimeSecrets[1], $c.secrets.runtimeSecrets[1]) } },
    @{ Name = 'legacy API session secret'; Mutate = { param($c) $c.secrets.runtimeSecrets[2].appSettingName = 'API_SESSION_SECRET' } },
    @{ Name = 'additional runtime secret field'; Mutate = { param($c) $c.secrets.runtimeSecrets[0] | Add-Member -NotePropertyName rawValue -NotePropertyValue 'blocked' } },
    @{ Name = 'missing runtime secret field'; Mutate = { param($c) $c.secrets.runtimeSecrets[0].PSObject.Properties.Remove('purpose') } },
    @{ Name = 'wrong runtime secret string type'; Mutate = { param($c) $c.secrets.runtimeSecrets[0].label = 42 } },
    @{ Name = 'unsafe package-defined Key Vault secret name'; Mutate = { param($c) $c.secrets.runtimeSecrets[0].keyVaultSecretName = 'unsafe/secret' } },
    @{ Name = 'duplicate package-defined Key Vault secret name'; Mutate = { param($c) $c.secrets.runtimeSecrets[1].keyVaultSecretName = $c.secrets.runtimeSecrets[0].keyVaultSecretName.ToLowerInvariant() } },
    @{ Name = 'wrong runtime secret source'; Mutate = { param($c) $c.secrets.runtimeSecrets[0].source = 'installerGenerated' } },
    @{ Name = 'wrong runtime required type'; Mutate = { param($c) $c.secrets.runtimeSecrets[0].required = 'true' } },
    @{ Name = 'undersized database minimum'; Mutate = { param($c) $c.secrets.runtimeSecrets[0].minimumLength = 11 } },
    @{ Name = 'oversized runtime minimum'; Mutate = { param($c) $c.secrets.runtimeSecrets[2].minimumLength = 4097 } },
    @{ Name = 'identical client IDs'; Mutate = { param($c) $c.entra.apiClientId = $c.entra.portalClientId } },
    @{ Name = 'unsafe customer display name'; Mutate = { param($c) $c.customer.tenantName = "Unsafe$([char]0x202e)Name" } },
    @{ Name = 'unsafe customer short name'; Mutate = { param($c) $c.customer.accountKey = "unsafe$([char]0x2028)key" } },
    @{ Name = 'runtime contract version'; Mutate = { param($c) $c.runtimeArtifacts.contractVersion = '2.0' } },
    @{ Name = 'runtime source commit'; Mutate = { param($c) $c.runtimeArtifacts.sourceCommit = 'A' * 40 } },
    @{ Name = 'runtime release'; Mutate = { param($c) $c.runtimeArtifacts.releaseId = 'unsafe/release' } },
    @{ Name = 'runtime version'; Mutate = { param($c) $c.runtimeArtifacts.runtimeVersion = '01.0.0' } },
    @{ Name = 'API file name'; Mutate = { param($c) $c.runtimeArtifacts.api.fileName = '..\api.zip' } },
    @{ Name = 'API size'; Mutate = { param($c) $c.runtimeArtifacts.api.sizeBytes = 0 } },
    @{ Name = 'API hash'; Mutate = { param($c) $c.runtimeArtifacts.api.sha256 = 'A' * 64 } },
    @{ Name = 'API URL credentials'; Mutate = { param($c) $c.runtimeArtifacts.api.downloadUrl = 'https://user@downloads.pagemaker365.com/runtime/1.0.0/pagemaker365-api-1.0.0.zip' } },
    @{ Name = 'local production artifact URL'; Mutate = { param($c) $c.runtimeArtifacts.api.downloadUrl = 'http://localhost:5443/runtime/1.0.0/pagemaker365-api-1.0.0.zip' } },
    @{ Name = 'artifact release directory mismatch'; Mutate = { param($c) $c.runtimeArtifacts.portal.downloadUrl = 'https://downloads.pagemaker365.com/runtime/other/pagemaker365-portal-1.0.0.zip' } },
    @{ Name = 'malicious portal startup'; Mutate = { param($c) $c.runtimeArtifacts.portal.startupCommand = 'node attacker.js' } }
)

foreach ($case in $validationCases) {
    $candidate = $baseConfig | ConvertTo-Json -Depth 30 | ConvertFrom-Json
    & $case.Mutate $candidate
    $candidatePath = Join-Path ([System.IO.Path]::GetTempPath()) "pm365-invalid-deployment-$([guid]::NewGuid().ToString('N')).json"
    try {
        $candidate | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $candidatePath -Encoding utf8
        $expectedPayloadSha256 = (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $result = Invoke-PM365Deployment `
            -ConfigPath $candidatePath `
            -ExpectedPackagePayloadSha256 $expectedPayloadSha256 `
            -Confirm:$false
        if ($result.code -ne 'DeploymentParameterValidationFailed') {
            throw "Invalid $($case.Name) returned $($result.code) instead of DeploymentParameterValidationFailed."
        }
        if ($script:azureDeploymentMutations -ne 0) {
            throw "Invalid $($case.Name) reached New-AzSubscriptionDeployment."
        }
    } finally {
        Remove-Item -LiteralPath $candidatePath -Force -ErrorAction SilentlyContinue
    }
}

$tamperedPath = Join-Path ([System.IO.Path]::GetTempPath()) "pm365-tampered-deployment-$([guid]::NewGuid().ToString('N')).json"
try {
    Copy-Item -LiteralPath $sampleConfigPath -Destination $tamperedPath
    $approvedPayloadSha256 = (Get-FileHash -LiteralPath $tamperedPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $boundConfig = Get-PM365BoundConfig `
        -ConfigPath $tamperedPath `
        -ExpectedPackagePayloadSha256 $approvedPayloadSha256
    if ([string]$boundConfig.contractVersion -cne '0.4') {
        throw 'An unchanged exact package payload did not pass its trusted binding.'
    }

    $tamperedConfig = Get-Content -LiteralPath $tamperedPath -Raw | ConvertFrom-Json
    $tamperedConfig.azure.resourceGroupName = 'rg-pm365-attacker'
    $tamperedConfig | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $tamperedPath -Encoding utf8
    try {
        Invoke-PM365Deployment `
            -ConfigPath $tamperedPath `
            -ExpectedPackagePayloadSha256 $approvedPayloadSha256 `
            -Confirm:$false | Out-Null
        throw 'Deployment accepted a package that changed after cryptographic validation.'
    } catch [System.IO.InvalidDataException] {
        if ($script:azureDeploymentMutations -ne 0) {
            throw 'A package changed after validation reached Azure deployment mutation.'
        }
    }
} finally {
    Remove-Item -LiteralPath $tamperedPath -Force -ErrorAction SilentlyContinue
}

Write-Host 'Deployment artifact contract tests passed.'
