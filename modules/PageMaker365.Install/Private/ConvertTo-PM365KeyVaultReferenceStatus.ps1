function ConvertTo-PM365KeyVaultReferenceStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Response,

        [Parameter(Mandatory)]
        [string[]] $ExpectedAppSettings
    )

    $content = if ($Response.PSObject.Properties['Content'] -and $Response.Content) {
        $Response.Content | ConvertFrom-Json -Depth 20
    } else {
        $Response
    }

    @(
        $content.value |
            ForEach-Object {
                $settingName = [string]$_.name
                if ([string]::IsNullOrWhiteSpace($settingName)) {
                    $settingName = [string]$_.id -replace '^.*/', ''
                }

                [pscustomobject]@{
                    appSettingName = $settingName
                    status = [string]$_.properties.status
                    vaultName = [string]$_.properties.vaultName
                    secretName = [string]$_.properties.secretName
                }
            } |
            Where-Object { $ExpectedAppSettings -contains $_.appSettingName }
    )
}
