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
        Sample = 'samples\contoso.tenant.discovery.json'
        Schema = 'schemas\tenant-discovery.schema.json'
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

Write-Host 'Schema validation completed.'
