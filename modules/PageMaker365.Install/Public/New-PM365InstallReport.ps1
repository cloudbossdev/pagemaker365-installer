function New-PM365InstallReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath,

        [Parameter(Mandatory)]
        [string] $OutputPath
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $directory = Split-Path -Parent $OutputPath
    if ($directory) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $content = @"
# PageMaker365 Install Report

Generated: $(Get-Date -Format o)

## Customer

- Tenant: $($config.customer.tenantName)
- Primary contact: $($config.customer.primaryContact)

## Azure

- Subscription: $($config.azure.subscriptionId)
- Resource group: $($config.azure.resourceGroupName)
- Location: $($config.azure.location)
- Environment: $($config.azure.environment)

## SharePoint

- Site URL: $($config.sharePoint.siteUrl)
- Default library: $($config.sharePoint.defaultDocumentLibrary)

## Application

- App name: $($config.app.appName)
- Custom domain: $($config.app.customDomain)
- Support email: $($config.app.supportEmail)

## Notes

This report scaffold will include deployment outputs, smoke test results, and support bundle references after those phases are wired into the desktop app.
"@

    Set-Content -LiteralPath $OutputPath -Value $content -Encoding UTF8
    New-PM365Result `
        -Status 'Passed' `
        -Code 'InstallReportCreated' `
        -Summary 'Install report was created.' `
        -Details $OutputPath
}

