function New-PM365Result {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Passed', 'Warning', 'Failed', 'Skipped')]
        [string] $Status,

        [Parameter(Mandatory)]
        [string] $Code,

        [Parameter(Mandatory)]
        [string] $Summary,

        [string] $Details = '',

        [bool] $RetrySafe = $true,

        [hashtable] $Data = @{}
    )

    [pscustomobject]@{
        status = $Status
        code = $Code
        summary = $Summary
        details = $Details
        retrySafe = $RetrySafe
        data = $Data
        timestamp = (Get-Date).ToUniversalTime().ToString('o')
    }
}

