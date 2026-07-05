function Start-PM365Preflight {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath
    )

    $results = @()
    $results += Test-PM365Prerequisites
    $results += Test-PM365DeploymentContract -ConfigPath $ConfigPath
    $results += Test-PM365AzureContext -ConfigPath $ConfigPath
    $results += Test-PM365EntraPermissions -ConfigPath $ConfigPath
    $results += Test-PM365SharePointAccess -ConfigPath $ConfigPath
    $results
}
