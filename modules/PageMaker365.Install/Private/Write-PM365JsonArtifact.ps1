function Write-PM365JsonArtifact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $OutputPath,

        [Parameter(Mandatory)]
        [object] $InputObject,

        [string] $DefaultFileName = 'pagemaker365-artifact.json'
    )

    if ($WhatIfPreference) {
        return ''
    }

    $targetPath = $OutputPath
    if (
        (Test-Path -LiteralPath $OutputPath -PathType Container) -or
        $OutputPath.EndsWith([string][System.IO.Path]::DirectorySeparatorChar) -or
        $OutputPath.EndsWith([string][System.IO.Path]::AltDirectorySeparatorChar)
    ) {
        $targetPath = Join-Path $OutputPath $DefaultFileName
    }

    $resolvedPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($targetPath)
    $directory = Split-Path -Parent $resolvedPath
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $json = $InputObject | ConvertTo-Json -Depth 50
    Set-Content -LiteralPath $resolvedPath -Value $json -Encoding UTF8
    return $resolvedPath
}
