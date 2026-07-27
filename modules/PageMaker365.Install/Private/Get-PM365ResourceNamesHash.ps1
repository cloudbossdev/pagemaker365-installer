function Get-PM365ResourceNamesHash {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [object] $ResourceNames
    )

    if ($null -eq $ResourceNames) {
        return ''
    }

    if ($ResourceNames -is [System.Collections.IDictionary]) {
        $entries = @(
            $ResourceNames.Keys |
                ForEach-Object {
                    [pscustomobject]@{ Name = [string]$_; Value = $ResourceNames[$_] }
                } |
                Sort-Object -Property Name |
                ForEach-Object {
                    '{0}={1}' -f $_.Name.ToLowerInvariant(), ([string]$_.Value).Trim().ToLowerInvariant()
                }
        )
    } else {
        $entries = @(
            $ResourceNames.PSObject.Properties |
                Sort-Object -Property Name |
                ForEach-Object {
                    '{0}={1}' -f $_.Name.ToLowerInvariant(), ([string]$_.Value).Trim().ToLowerInvariant()
                }
        )
    }
    if ($entries.Count -eq 0) {
        return ''
    }

    $payload = [System.Text.Encoding]::UTF8.GetBytes(($entries -join "`n"))
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha256.ComputeHash($payload)
    } finally {
        $sha256.Dispose()
    }
    'sha256:' + ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
}
