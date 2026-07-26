function Initialize-PM365GraphAccessToken {
    [CmdletBinding()]
    param()

    $accessToken = [Environment]::GetEnvironmentVariable('PM365_GRAPH_ACCESS_TOKEN', 'Process')
    if ([string]::IsNullOrWhiteSpace($accessToken)) {
        return $null
    }

    $tokenClaims = ConvertFrom-PM365JwtPayload -AccessToken $accessToken
    $result = [ordered]@{
        connectSucceeded = $false
        error = ''
        tenantId = ''
        account = ''
        scopes = @()
    }

    if ($tokenClaims) {
        $result.tenantId = [string]$tokenClaims.tid
        $result.scopes = @(([string]$tokenClaims.scp -split ' ') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        foreach ($claimName in @('preferred_username', 'upn', 'unique_name')) {
            if ($tokenClaims.PSObject.Properties.Name -contains $claimName -and -not [string]::IsNullOrWhiteSpace([string]$tokenClaims.$claimName)) {
                $result.account = [string]$tokenClaims.$claimName
                break
            }
        }
    }

    try {
        $connectCommand = Get-Command Connect-MgGraph -ErrorAction Stop
        $secureAccessToken = ConvertTo-SecureString $accessToken -AsPlainText -Force
        $connectArgs = @{
            AccessToken = $secureAccessToken
            ErrorAction = 'Stop'
        }

        if ($connectCommand.Parameters.ContainsKey('NoWelcome')) {
            $connectArgs.NoWelcome = $true
        }

        Connect-MgGraph @connectArgs | Out-Null
        $result.connectSucceeded = $true
    } catch {
        $result.error = $_.Exception.Message
    }

    [pscustomobject]$result
}

function ConvertFrom-PM365JwtPayload {
    [CmdletBinding()]
    param(
        [string] $AccessToken
    )

    if ([string]::IsNullOrWhiteSpace($AccessToken)) {
        return $null
    }

    $parts = $AccessToken.Split('.')
    if ($parts.Count -lt 2) {
        return $null
    }

    try {
        $payload = $parts[1].Replace('-', '+').Replace('_', '/')
        switch ($payload.Length % 4) {
            2 { $payload += '==' }
            3 { $payload += '=' }
            0 { }
            default { return $null }
        }

        $json = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload))
        return $json | ConvertFrom-Json -ErrorAction Stop
    } catch {
        return $null
    }
}
