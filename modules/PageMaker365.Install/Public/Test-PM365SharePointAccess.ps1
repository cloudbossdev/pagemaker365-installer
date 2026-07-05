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
            -Status 'Warning' `
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
            -Status 'Warning' `
            -Code 'GraphAuthenticationMissing' `
            -Summary 'Microsoft.Graph.Authentication is required for SharePoint access checks.' `
            -Details 'Install Microsoft.Graph.Authentication before resolving SharePoint sites through Graph.' `
            -RetrySafe $true
        return $results
    }

    $graphContext = Get-MgContext -ErrorAction SilentlyContinue
    if (-not $graphContext) {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'GraphNotSignedInForSharePoint' `
            -Summary 'Microsoft Graph sign-in is required for SharePoint access checks.' `
            -Details 'Sign in with Sites.Read.All or Sites.Selected permission before resolving the SharePoint site.' `
            -RetrySafe $true
        return $results
    }

    Import-Module Microsoft.Graph.Sites -ErrorAction Stop

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
                -Status 'Skipped' `
                -Code 'SharePointLibraryNotConfigured' `
                -Summary 'No default SharePoint document library was configured.' `
                -Details 'Add sharePoint.defaultDocumentLibrary to the customer package when library validation is required.'
        } else {
            $drivesUri = "https://graph.microsoft.com/v1.0/sites/$siteId/drives"
            $drives = Invoke-MgGraphRequest -Method GET -Uri $drivesUri -ErrorAction Stop
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
                $availableLibraries = @($drives.value | ForEach-Object { [string]$_.name })
                $results += New-PM365Result `
                    -Status 'Warning' `
                    -Code 'SharePointLibraryNotFound' `
                    -Summary 'Configured SharePoint document library was not found.' `
                    -Details ("Expected '$libraryName'. Available libraries: " + ($availableLibraries -join ', ')) `
                    -RetrySafe $true
            }
        }
    } catch {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'SharePointSiteResolveFailed' `
            -Summary 'SharePoint site could not be resolved through Microsoft Graph.' `
            -Details $_.Exception.Message `
            -RetrySafe $true
    }

    $results
}
