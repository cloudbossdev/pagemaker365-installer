function Test-PM365SensitiveValue {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [object] $Value
    )

    if ($null -eq $Value -or $Value -isnot [string]) {
        return $false
    }

    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $false
    }

    return (
        $text -match '(?i)(AccountKey|SharedAccessKey|Password|ClientSecret|AccessKey|SecretAccessKey)=([^;\s]+)' -or
        $text -match '(?i)Authorization:\s*Bearer\s+\S+' -or
        $text -match '(?i)(DefaultEndpointsProtocol|Endpoint|Server)=.+;(AccountKey|SharedAccessKey|Password|ClientSecret)=' -or
        $text -match '^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$'
    )
}
