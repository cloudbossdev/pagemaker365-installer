function ConvertTo-PM365RedactedObject {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [object] $InputObject,

        [int] $Depth = 12
    )

    if ($null -eq $InputObject) {
        return $null
    }

    if ($Depth -lt 0) {
        return '[MaxDepth]'
    }

    if ($InputObject -is [string]) {
        if (Test-PM365SensitiveValue -Value $InputObject) {
            return '[REDACTED]'
        }

        return $InputObject
    }

    if (
        $InputObject -is [bool] -or
        $InputObject -is [byte] -or
        $InputObject -is [int16] -or
        $InputObject -is [int] -or
        $InputObject -is [int64] -or
        $InputObject -is [single] -or
        $InputObject -is [double] -or
        $InputObject -is [decimal] -or
        $InputObject -is [datetime] -or
        $InputObject -is [datetimeoffset] -or
        $InputObject -is [guid]
    ) {
        return $InputObject
    }

    if ($InputObject -is [System.Collections.IDictionary]) {
        $redacted = [ordered]@{}
        $sensitiveEntryValue = $false
        foreach ($key in $InputObject.Keys) {
            $keyText = [string]$key
            if ($keyText -match '^(name|key)$' -and (Test-PM365SensitiveName -Name ([string]$InputObject[$key]))) {
                $sensitiveEntryValue = $true
                break
            }
        }

        foreach ($key in $InputObject.Keys) {
            $keyText = [string]$key
            if (Test-PM365SensitiveName -Name $keyText) {
                $redacted[$keyText] = '[REDACTED]'
            } elseif ($sensitiveEntryValue -and $keyText -match '^(value|content|rawValue)$') {
                $redacted[$keyText] = '[REDACTED]'
            } else {
                $redacted[$keyText] = ConvertTo-PM365RedactedObject -InputObject $InputObject[$key] -Depth ($Depth - 1)
            }
        }

        return $redacted
    }

    if ($InputObject -is [System.Collections.IEnumerable]) {
        $items = @()
        foreach ($item in $InputObject) {
            $items += ConvertTo-PM365RedactedObject -InputObject $item -Depth ($Depth - 1)
        }

        return $items
    }

    $properties = @(
        $InputObject.PSObject.Properties |
            Where-Object {
                $_.MemberType -eq 'NoteProperty' -or
                $_.MemberType -eq 'Property' -or
                $_.MemberType -eq 'AliasProperty' -or
                $_.MemberType -eq 'ScriptProperty'
            }
    )

    if ($properties.Count -eq 0) {
        $text = [string]$InputObject
        if (Test-PM365SensitiveValue -Value $text) {
            return '[REDACTED]'
        }

        return $text
    }

    $result = [ordered]@{}
    $sensitiveObjectValue = $false
    $nameOrKeyValue = Get-PM365ObjectProperty -InputObject $InputObject -Name @('Name', 'name', 'Key', 'key')
    if ($null -ne $nameOrKeyValue -and (Test-PM365SensitiveName -Name ([string]$nameOrKeyValue))) {
        $sensitiveObjectValue = $true
    }

    foreach ($property in $properties) {
        if (Test-PM365SensitiveName -Name $property.Name) {
            $result[$property.Name] = '[REDACTED]'
            continue
        }

        if ($sensitiveObjectValue -and $property.Name -match '^(Value|value|Content|content|RawValue|rawValue)$') {
            $result[$property.Name] = '[REDACTED]'
            continue
        }

        try {
            $value = $property.Value
        } catch {
            $value = $null
        }

        $result[$property.Name] = ConvertTo-PM365RedactedObject -InputObject $value -Depth ($Depth - 1)
    }

    return $result
}
