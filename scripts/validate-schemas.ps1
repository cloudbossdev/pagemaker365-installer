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

Write-Host 'Schema validation completed.'
