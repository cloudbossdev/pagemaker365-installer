[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-RelativeRepoPath {
    param(
        [string] $Path
    )

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    return [System.IO.Path]::GetRelativePath($repoRoot, $resolvedPath)
}

function Invoke-SchemaValidation {
    param(
        [string] $SamplePath,
        [string] $SchemaPath
    )

    $sampleRelativePath = Get-RelativeRepoPath $SamplePath
    $schemaRelativePath = Get-RelativeRepoPath $SchemaPath

    Write-Host "Validating $sampleRelativePath against $schemaRelativePath..."

    try {
        $isValid = Test-Json -LiteralPath $SamplePath -SchemaFile $SchemaPath
    }
    catch {
        throw "Schema validation failed for '$sampleRelativePath' against '$schemaRelativePath': $($_.Exception.Message)"
    }

    if (-not $isValid) {
        throw "Schema validation failed for '$sampleRelativePath' against '$schemaRelativePath'."
    }
}

$validations = @(
    @{
        Sample = 'samples\contoso.customer.install.json'
        Schema = 'schemas\customer-install.schema.json'
    },
    @{
        Sample = 'samples\contoso.onboarding.bootstrap.json'
        Schema = 'schemas\onboarding-bootstrap.schema.json'
    },
    @{
        Sample = 'samples\contoso.onboarding.status.json'
        Schema = 'schemas\onboarding-status.schema.json'
    },
    @{
        Sample = 'samples\contoso.tenant.discovery.json'
        Schema = 'schemas\tenant-discovery.schema.json'
    },
    @{
        Sample = 'docs\testing\results\customer-lifecycle-result.template.json'
        Schema = 'docs\testing\schemas\customer-lifecycle-result.schema.json'
    },
    @{
        Sample = 'tests\PageMaker365.Installer.Engine.Tests\Fixtures\private-runtime-delivery-v2\customer-install-0.6.json'
        Schema = 'schemas\customer-install-v0.6.schema.json'
    }
)

foreach ($validation in $validations) {
    $samplePath = Join-Path $repoRoot $validation.Sample
    $schemaPath = Join-Path $repoRoot $validation.Schema

    if (-not (Test-Path -LiteralPath $samplePath)) {
        throw "Sample file not found: $($validation.Sample)"
    }

    if (-not (Test-Path -LiteralPath $schemaPath)) {
        throw "Schema file not found: $($validation.Schema)"
    }

    Invoke-SchemaValidation -SamplePath $samplePath -SchemaPath $schemaPath
}

$customerSchemaPath = Join-Path $repoRoot 'schemas\customer-install.schema.json'
$customerSample = Get-Content -LiteralPath (Join-Path $repoRoot 'samples\contoso.customer.install.json') -Raw | ConvertFrom-Json
foreach ($negativeCase in @(
    @{ Name = 'customer line separator'; Mutate = { param($c) $c.customer.tenantName = "Unsafe$([char]0x2028)Name" } },
    @{ Name = 'customer bidi override'; Mutate = { param($c) $c.customer.accountKey = "unsafe$([char]0x202e)key" } },
    @{ Name = 'missing runtime source commit'; Mutate = { param($c) $c.runtimeArtifacts.PSObject.Properties.Remove('sourceCommit') } },
    @{ Name = 'invalid runtime artifact size'; Mutate = { param($c) $c.runtimeArtifacts.api.sizeBytes = 0 } }
)) {
    $candidate = $customerSample | ConvertTo-Json -Depth 30 | ConvertFrom-Json
    & $negativeCase.Mutate $candidate
    $candidateJson = $candidate | ConvertTo-Json -Depth 30
    if (Test-Json -Json $candidateJson -SchemaFile $customerSchemaPath -ErrorAction SilentlyContinue) {
        throw "Customer package schema accepted $($negativeCase.Name)."
    }
}

$producerFileNameCandidate = $customerSample | ConvertTo-Json -Depth 30 | ConvertFrom-Json
$producerFileNameCandidate.runtimeArtifacts.api.fileName = 'pagemaker365-api-pm365-runtime-1.0.0+sample.zip'
if (-not (Test-Json `
    -Json ($producerFileNameCandidate | ConvertTo-Json -Depth 30) `
    -SchemaFile $customerSchemaPath `
    -ErrorAction SilentlyContinue)) {
    throw 'Customer package schema rejected a producer-compatible plus-sign artifact file name.'
}

$v06SchemaPath = Join-Path $repoRoot 'schemas\customer-install-v0.6.schema.json'
$v06Sample = Get-Content -LiteralPath (Join-Path $repoRoot 'tests\PageMaker365.Installer.Engine.Tests\Fixtures\private-runtime-delivery-v2\customer-install-0.6.json') -Raw | ConvertFrom-Json
foreach ($negativeCase in @(
    @{ Name = 'mixed package version'; Mutate = { param($c) $c.contractVersion = '0.5' } },
    @{ Name = 'mixed manifest version'; Mutate = { param($c) $c.runtimeArtifacts.manifestContractVersion = '2.0' } },
    @{ Name = 'missing manifest product'; Mutate = { param($c) $c.runtimeArtifacts.PSObject.Properties.Remove('product') } },
    @{ Name = 'non-RFC UUID version and variant'; Mutate = { param($c) $c.customer.customerId = '11111111-1111-0111-0111-111111111111' } },
    @{ Name = 'runtime version above Int32'; Mutate = { param($c) $c.runtimeArtifacts.runtimeVersion = '2147483648.0.0' } },
    @{ Name = 'unsafe artifact filename'; Mutate = { param($c) $c.runtimeArtifacts.api.fileName = '../api.zip' } },
    @{ Name = 'unknown root field'; Mutate = { param($c) $c | Add-Member -NotePropertyName unknownField -NotePropertyValue $true } }
)) {
    $candidate = $v06Sample | ConvertTo-Json -Depth 30 | ConvertFrom-Json
    & $negativeCase.Mutate $candidate
    if (Test-Json -Json ($candidate | ConvertTo-Json -Depth 30) -SchemaFile $v06SchemaPath -ErrorAction SilentlyContinue) {
        throw "Customer package 0.6 schema accepted $($negativeCase.Name)."
    }
}

$v07FixtureRoot = Join-Path $repoRoot 'tests\PageMaker365.Installer.Engine.Tests\Fixtures\private-runtime-delivery-v3'
$v07PackagePath = Join-Path $v07FixtureRoot 'customer-install-0.7.json'
$v07ProjectionPath = Join-Path $v07FixtureRoot 'runtime-configuration-projection-v2.json'
$v07PackageSchemaPath = Join-Path $repoRoot 'schemas\customer-install-v0.7.schema.json'
$v07ProjectionSchemaPath = Join-Path $repoRoot 'schemas\runtime-configuration-projection-v2.schema.json'

Invoke-SchemaValidation -SamplePath $v07ProjectionPath -SchemaPath $v07ProjectionSchemaPath

# PowerShell's JSON-schema resolver does not resolve the package schema's
# closed projection-v2 reference from a sibling file. Compose the two accepted
# schema objects in memory; no generated schema or fixture is written.
$v07PackageSchema = Get-Content -LiteralPath $v07PackageSchemaPath -Raw | ConvertFrom-Json
$v07ProjectionSchema = Get-Content -LiteralPath $v07ProjectionSchemaPath -Raw | ConvertFrom-Json
$v07PackageSchema.properties.runtimeConfiguration = $v07ProjectionSchema
$v07CombinedSchema = $v07PackageSchema | ConvertTo-Json -Depth 100
$v07Package = Get-Content -LiteralPath $v07PackagePath -Raw | ConvertFrom-Json
if (-not (Test-Json -LiteralPath $v07PackagePath -Schema $v07CombinedSchema -ErrorAction SilentlyContinue)) {
    throw 'Accepted customer package 0.7 fixture failed its closed package/projection schemas.'
}

foreach ($negativeCase in @(
    @{ Name = 'mixed package version'; Mutate = { param($c) $c.contractVersion = '0.6' } },
    @{ Name = 'mixed manifest version'; Mutate = { param($c) $c.runtimeArtifacts.manifestContractVersion = '2.0' } },
    @{ Name = 'mixed projection version'; Mutate = { param($c) $c.runtimeConfiguration.schemaVersion = 'pagemaker365.runtime-configuration-projection.v1' } },
    @{ Name = 'enabled connector profile'; Mutate = { param($c) $c.runtimeConfiguration.featureProfile.connectorSynchronization = $true } },
    @{ Name = 'missing public setting'; Mutate = { param($c) $c.runtimeConfiguration.publicSettings = @($c.runtimeConfiguration.publicSettings | Select-Object -Skip 1) } },
    @{ Name = 'raw protected value'; Mutate = { param($c) $c.runtimeConfiguration.protectedSettings[0].reference | Add-Member -NotePropertyName value -NotePropertyValue 'forbidden' } },
    @{ Name = 'unknown root field'; Mutate = { param($c) $c | Add-Member -NotePropertyName unknownField -NotePropertyValue $true } }
)) {
    $candidate = $v07Package | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    & $negativeCase.Mutate $candidate
    if (Test-Json -Json ($candidate | ConvertTo-Json -Depth 100) -Schema $v07CombinedSchema -ErrorAction SilentlyContinue) {
        throw "Customer package 0.7 schemas accepted $($negativeCase.Name)."
    }
}

Write-Host 'Schema validation completed.'
