function Invoke-PM365BicepBuild {
    [CmdletBinding()]
    param(
        [string] $TemplateFile = (Get-PM365DefaultTemplateFile)
    )

    $bicep = Get-PM365BicepCommand
    if (-not $bicep) {
        New-PM365Result `
            -Status 'Failed' `
            -Code 'BicepMissing' `
            -Summary 'Bicep is not available.' `
            -Details 'Install Bicep before validating deployment templates.' `
            -RetrySafe $true
        return
    }

    if (-not (Test-Path -LiteralPath $TemplateFile)) {
        New-PM365Result `
            -Status 'Failed' `
            -Code 'BicepTemplateMissing' `
            -Summary 'Bicep template was not found.' `
            -Details $TemplateFile `
            -RetrySafe $false
        return
    }

    $outputFile = Join-Path ([System.IO.Path]::GetTempPath()) ("pm365-main-{0}.json" -f ([guid]::NewGuid()))
    $output = & $bicep build $TemplateFile --outfile $outputFile 2>&1
    if ($LASTEXITCODE -ne 0) {
        New-PM365Result `
            -Status 'Failed' `
            -Code 'BicepBuildFailed' `
            -Summary 'Bicep template build failed.' `
            -Details ($output -join [Environment]::NewLine) `
            -RetrySafe $true
        return
    }

    Remove-Item -LiteralPath $outputFile -Force -ErrorAction SilentlyContinue
    New-PM365Result `
        -Status 'Passed' `
        -Code 'BicepBuildReady' `
        -Summary 'Bicep template builds successfully.' `
        -Details $TemplateFile
}

