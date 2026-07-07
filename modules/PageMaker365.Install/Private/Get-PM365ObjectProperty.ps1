function Get-PM365ObjectProperty {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [object] $InputObject,

        [Parameter(Mandatory)]
        [string[]] $Name
    )

    if ($null -eq $InputObject) {
        return $null
    }

    if ($InputObject -is [System.Collections.IDictionary]) {
        foreach ($candidate in $Name) {
            if ($InputObject.Contains($candidate)) {
                return $InputObject[$candidate]
            }

            foreach ($key in $InputObject.Keys) {
                if ([string]::Equals([string]$key, $candidate, [System.StringComparison]::OrdinalIgnoreCase)) {
                    return $InputObject[$key]
                }
            }
        }
    }

    foreach ($candidate in $Name) {
        foreach ($property in $InputObject.PSObject.Properties) {
            if ([string]::Equals($property.Name, $candidate, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $property.Value
            }
        }
    }

    return $null
}
