function Get-PM365PortalReleaseMarker {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Content
    )

    $options = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
        [System.Text.RegularExpressions.RegexOptions]::Singleline
    foreach ($pattern in @(
        '<meta\s+[^>]*\bname\s*=\s*["'']pm365-release-id["''][^>]*\bcontent\s*=\s*["''](?<release>[^"'']*)["'']',
        '<meta\s+[^>]*\bcontent\s*=\s*["''](?<release>[^"'']*)["''][^>]*\bname\s*=\s*["'']pm365-release-id["'']'
    )) {
        $match = [regex]::Match($Content, $pattern, $options)
        if ($match.Success) {
            return [string]$match.Groups['release'].Value
        }
    }

    $null
}
