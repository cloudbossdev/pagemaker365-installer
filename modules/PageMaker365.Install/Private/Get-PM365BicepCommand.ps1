function Get-PM365BicepCommand {
    [CmdletBinding()]
    param()

    $command = Get-Command bicep -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages\Microsoft.Bicep_Microsoft.Winget.Source_8wekyb3d8bbwe\bicep.exe'),
        (Join-Path $env:USERPROFILE '.azure\bin\bicep.exe'),
        (Join-Path $env:ProgramFiles 'Bicep CLI\bicep.exe')
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    return $null
}

