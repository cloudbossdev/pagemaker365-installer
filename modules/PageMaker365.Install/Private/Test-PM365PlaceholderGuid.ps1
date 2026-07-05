function Test-PM365PlaceholderGuid {
    [CmdletBinding()]
    param(
        [string] $Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $true
    }

    $normalized = $Value.Trim()
    return $normalized -match '^(0{8}-0{4}-0{4}-0{4}-0{12}|1{8}-1{4}-1{4}-1{4}-1{12})$'
}

