function Test-PM365SharePointAccess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $results = @()
    $siteUrl = [string]$config.sharePoint.siteUrl
    $parsedSiteUri = $null

    if (-not [Uri]::TryCreate($siteUrl, [UriKind]::Absolute, [ref]$parsedSiteUri)) {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'SharePointSiteUrlInvalid' `
            -Summary 'SharePoint site URL is invalid.' `
            -Details $siteUrl `
            -RetrySafe $false
        return $results
    }

    $results += New-PM365Result `
        -Status 'Passed' `
        -Code 'SharePointSiteUrlReady' `
        -Summary 'SharePoint site URL is well formed.' `
        -Details $siteUrl

    $graphSites = Get-Module -ListAvailable -Name Microsoft.Graph.Sites | Select-Object -First 1
    if (-not $graphSites) {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'GraphSitesModuleMissing' `
            -Summary 'Microsoft.Graph.Sites is not installed.' `
            -Details 'Install Microsoft.Graph.Sites to validate SharePoint site and library access through Graph.' `
            -RetrySafe $true
        return $results
    }

    Import-Module Microsoft.Graph.Authentication -ErrorAction SilentlyContinue
    $graphContextCommand = Get-Command Get-MgContext -ErrorAction SilentlyContinue
    if (-not $graphContextCommand) {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'GraphAuthenticationMissing' `
            -Summary 'Microsoft.Graph.Authentication is required for SharePoint access checks.' `
            -Details 'Install Microsoft.Graph.Authentication before resolving SharePoint sites through Graph.' `
            -RetrySafe $true
        return $results
    }

    $graphContext = Get-MgContext -ErrorAction SilentlyContinue
    $tokenContext = Initialize-PM365GraphAccessToken
    if ($tokenContext -and -not $tokenContext.connectSucceeded) {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'GraphAccessTokenConnectionFailedForSharePoint' `
            -Summary 'The installer could not initialize the app-provided Microsoft Graph token for SharePoint checks.' `
            -Details $tokenContext.error `
            -RetrySafe $true
        return $results
    }

    if (-not $graphContext -and -not $tokenContext) {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'GraphNotSignedInForSharePoint' `
            -Summary 'Microsoft Graph sign-in is required for SharePoint access checks.' `
            -Details 'Sign in with Sites.Read.All before resolving the SharePoint site.' `
            -RetrySafe $true
        return $results
    }

    try {
        Import-Module Microsoft.Graph.Sites -ErrorAction Stop
    } catch {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'GraphSitesModuleLoadFailed' `
            -Summary 'Microsoft.Graph.Sites could not be loaded.' `
            -Details $_.Exception.Message `
            -RetrySafe $true
        return $results
    }

    $hostName = $parsedSiteUri.Host
    $serverRelativePath = if ($parsedSiteUri.AbsolutePath -and $parsedSiteUri.AbsolutePath -ne '/') {
        $parsedSiteUri.AbsolutePath.TrimEnd('/')
    } else {
        '/'
    }
    $siteLookup = "$hostName`:$serverRelativePath"
    $siteUri = "https://graph.microsoft.com/v1.0/sites/$siteLookup"

    try {
        $site = Invoke-MgGraphRequest -Method GET -Uri $siteUri -ErrorAction Stop
    } catch {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'SharePointSiteResolveFailed' `
            -Summary 'SharePoint site could not be resolved through Microsoft Graph.' `
            -Details $_.Exception.Message `
            -RetrySafe $true
        return $results
    }

    $siteId = [string]$site.id
    $results += New-PM365Result `
        -Status 'Passed' `
        -Code 'SharePointSiteResolved' `
        -Summary 'SharePoint site was resolved through Microsoft Graph.' `
        -Details $site.webUrl `
        -Data @{
            siteId = $siteId
            displayName = [string]$site.displayName
            lookup = $siteLookup
        }

    $libraryName = [string]$config.sharePoint.defaultDocumentLibrary
    if ([string]::IsNullOrWhiteSpace($libraryName)) {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'SharePointLibraryNotConfigured' `
            -Summary 'No default SharePoint document library was configured.' `
            -Details 'Regenerate the customer package with sharePoint.defaultDocumentLibrary.' `
            -RetrySafe $false
        return $results
    }

    $drivesUri = "https://graph.microsoft.com/v1.0/sites/$siteId/drives"
    try {
        $drives = Invoke-MgGraphRequest -Method GET -Uri $drivesUri -ErrorAction Stop
    } catch {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'SharePointLibraryAccessFailed' `
            -Summary 'The configured SharePoint document library could not be verified.' `
            -Details $_.Exception.Message `
            -RetrySafe $true
        return $results
    }

    $matchingDrive = @($drives.value | Where-Object { $_.name -eq $libraryName }) | Select-Object -First 1
    if ($matchingDrive) {
        $results += New-PM365Result `
            -Status 'Passed' `
            -Code 'SharePointLibraryReady' `
            -Summary 'Configured SharePoint document library is accessible.' `
            -Details $matchingDrive.webUrl `
            -Data @{
                driveId = [string]$matchingDrive.id
                libraryName = $libraryName
            }
    } else {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'SharePointLibraryNotFound' `
            -Summary 'Configured SharePoint document library was not found.' `
            -Details "Expected document library '$libraryName' was not returned for the configured site." `
            -RetrySafe $true
    }

    $results
}
