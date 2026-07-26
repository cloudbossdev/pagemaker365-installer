[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $Runtime = 'win-x64',

    [string] $OutputPath = '',

    [string] $CodeSigningCertificatePath = '',

    [string] $TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = 'C:\Program Files\dotnet'
if (Test-Path -LiteralPath $dotnetPath) {
    $env:Path = "$dotnetPath;$env:Path"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot 'artifacts\installer-package'
}

$resolvedOutputParent = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $resolvedOutputParent)) {
    New-Item -ItemType Directory -Path $resolvedOutputParent | Out-Null
}

$resolvedRepoRoot = (Resolve-Path -LiteralPath $repoRoot).Path
$resolvedOutputParent = (Resolve-Path -LiteralPath $resolvedOutputParent).Path
if (-not $resolvedOutputParent.StartsWith($resolvedRepoRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Package output must stay inside the repository. Requested path: $OutputPath"
}

if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputPath | Out-Null

$publishPath = Join-Path $OutputPath 'app'
$appProject = Join-Path $repoRoot 'src\PageMaker365.Installer.App\PageMaker365.Installer.App.csproj'
dotnet publish $appProject `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained false `
    --output $publishPath

@('modules', 'infra', 'rules', 'ai', 'samples', 'schemas') | ForEach-Object {
    Copy-Item `
        -LiteralPath (Join-Path $repoRoot $_) `
        -Destination (Join-Path $OutputPath $_) `
        -Recurse `
        -Force
}

$packageDocs = @(
    'assistant-api-contract.md',
    'deployment-contract.md',
    'onboarding-discovery-contract.md',
    'portal-install-package-handoff.md',
    'removal-policy.md',
    'upgrade-contract.md',
    'using-the-installer.md'
)
$packageDocsPath = Join-Path $OutputPath 'docs'
New-Item -ItemType Directory -Path $packageDocsPath -Force | Out-Null
$packageDocs | ForEach-Object {
    $sourcePath = Join-Path $repoRoot "docs\$_"
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Required package documentation is missing: $sourcePath"
    }

    Copy-Item -LiteralPath $sourcePath -Destination $packageDocsPath -Force
}

Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $OutputPath -Force

$exePath = Join-Path $publishPath 'PageMaker365.Installer.exe'
if (-not [string]::IsNullOrWhiteSpace($CodeSigningCertificatePath)) {
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signtool) {
        throw 'signtool.exe was not found. Install the Windows SDK or omit CodeSigningCertificatePath.'
    }

    & $signtool.Source sign `
        /fd SHA256 `
        /f $CodeSigningCertificatePath `
        /tr $TimestampServer `
        /td SHA256 `
        $exePath
}

[pscustomobject]@{
    outputPath = $OutputPath
    appPath = $publishPath
    signed = -not [string]::IsNullOrWhiteSpace($CodeSigningCertificatePath)
}
