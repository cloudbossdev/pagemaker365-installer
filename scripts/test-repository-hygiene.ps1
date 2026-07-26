[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repoRoot
try {
    $files = @(
        git ls-files
        git ls-files --others --exclude-standard
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique

    $allowedBootstrap = 'samples/contoso.onboarding.bootstrap.json'
    foreach ($file in $files) {
        $normalized = $file.Replace('\', '/')
        $name = [System.IO.Path]::GetFileName($normalized)
        if (($name -like '*.onboarding.bootstrap.json' -and $normalized -ne $allowedBootstrap) -or
            $name -like '*.handoff-summary.json' -or
            $name -like '*deployment-export*.json' -or
            $name -like '*.installer-status.json') {
            throw "Generated or customer-specific installer artifact must not be committed: $normalized"
        }

        if (-not (Test-Path -LiteralPath $normalized -PathType Leaf)) {
            continue
        }

        if ([System.IO.Path]::GetExtension($normalized) -in @('.png', '.jpg', '.jpeg', '.gif', '.ico', '.zip', '.dll', '.exe')) {
            continue
        }

        $content = Get-Content -LiteralPath $normalized -Raw -ErrorAction SilentlyContinue
        if ($null -eq $content) {
            continue
        }

        if ($content -match '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----' -or
            $content -match '(?i)\b(?:github_pat|gh[pousr])_[A-Za-z0-9_]{20,}\b' -or
            $content -match '(?i)\bAccountKey=[A-Za-z0-9+/=]{20,}') {
            throw "Secret-shaped credential material was found in $normalized."
        }

        if ([System.IO.Path]::GetExtension($normalized) -ne '.json' -or $normalized -eq $allowedBootstrap) {
            continue
        }

        try {
            $json = $content | ConvertFrom-Json -ErrorAction Stop
        } catch {
            continue
        }

        function Test-SensitiveJsonValue {
            param([object] $Node, [string] $JsonPath = '')

            if ($null -eq $Node) { return }
            if ($Node -is [pscustomobject]) {
                foreach ($property in $Node.PSObject.Properties) {
                    $nextPath = if ($JsonPath) { "$JsonPath.$($property.Name)" } else { $property.Name }
                    if ($property.Name -match '^(?i:oneTimeCode|clientSecret|clientSecretValue|accessToken|refreshToken|password|privateKey|apiKey|connectionString)$' -and
                        $property.Value -is [string] -and
                        -not [string]::IsNullOrWhiteSpace([string]$property.Value) -and
                        [string]$property.Value -notmatch '(?i)sample|example|placeholder|redact|not.?set|change.?me') {
                        throw "Secret-shaped JSON value found at $nextPath in $normalized."
                    }

                    Test-SensitiveJsonValue -Node $property.Value -JsonPath $nextPath
                }
                return
            }

            if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
                $index = 0
                foreach ($item in $Node) {
                    Test-SensitiveJsonValue -Node $item -JsonPath "$JsonPath[$index]"
                    $index++
                }
            }
        }

        Test-SensitiveJsonValue -Node $json
    }

    Write-Host 'Repository hygiene checks passed.'
}
finally {
    Pop-Location
}
