function Test-PM365SensitiveName {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [string] $Name
    )

    if ([string]::IsNullOrWhiteSpace($Name)) {
        return $false
    }

    return $Name -match '(?i)(password|passwd|pwd|secret|token|credential|connectionstring|connection_string|instrumentationkey|instrumentation_key|accountkey|account_key|sharedaccesskey|shared_access_key|clientsecret|client_secret|privatekey|private_key|apikey|api_key|accesskey|access_key|sas|authorization|bearer)'
}
