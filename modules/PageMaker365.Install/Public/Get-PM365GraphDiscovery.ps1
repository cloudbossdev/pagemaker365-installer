function Get-PM365GraphDiscovery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath,

        [switch] $SkipSharePoint,

        [string] $MockContextPath = $env:PM365_GRAPH_DISCOVERY_MOCK_PATH
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $findings = @()
    $verifiedDomains = @()
    $availableDocumentLibraries = @()
    $expectedTenantId = [string]$config.customer.tenantId
    if ([string]::IsNullOrWhiteSpace($expectedTenantId)) {
        $expectedTenantId = [string]$config.azure.tenantId
    }

    $siteUrl = [string]$config.sharePoint.siteUrl
    $parsedSiteUri = $null
    $tenantHostname = ''
    if ([Uri]::TryCreate($siteUrl, [UriKind]::Absolute, [ref]$parsedSiteUri)) {
        $tenantHostname = $parsedSiteUri.Host
    }

    $result = [ordered]@{
        contractVersion = '0.1'
        source = 'GraphPowerShell'
        dataPolicy = 'InstallReadinessOnly'
        discoveredAt = (Get-Date).ToUniversalTime().ToString('o')
        accountId = ''
        tenantId = $expectedTenantId
        scopes = @()
        verifiedDomains = @()
        defaultDomain = ''
        tenantHostname = $tenantHostname
        siteUrl = $siteUrl
        siteId = [string]$config.sharePoint.siteId
        siteDisplayName = ''
        siteResolved = $false
        defaultDocumentLibrary = [string]$config.sharePoint.defaultDocumentLibrary
        defaultDocumentLibraryId = ''
        permissionMode = [string]$config.sharePoint.permissionMode
        availableDocumentLibraries = @()
        appRegistrationMode = [string]$config.entra.appRegistrationMode
        consentStatus = 'Unknown'
        entraPermissionMode = [string]$config.entra.permissionMode
        requiredApplicationPermissions = @($config.entra.requiredApplicationPermissions | ForEach-Object { [string]$_ })
        requiredDelegatedScopes = @($config.entra.requiredDelegatedScopes | ForEach-Object { [string]$_ })
        findings = @()
    }

    if ($tenantHostname) {
        $verifiedDomains += $tenantHostname
    }

    if (-not [string]::IsNullOrWhiteSpace($MockContextPath)) {
        if (-not (Test-Path -LiteralPath $MockContextPath)) {
            $findings += [ordered]@{
                severity = 'Warning'
                code = 'GraphMockContextMissing'
                summary = 'Graph mock discovery context was not found.'
                details = $MockContextPath
            }

            $result.verifiedDomains = @($verifiedDomains | Sort-Object -Unique)
            $result.findings = $findings
            return [pscustomobject]$result
        }

        $mock = Get-Content -LiteralPath $MockContextPath -Raw | ConvertFrom-Json
        $result.accountId = [string]$mock.accountId
        $result.tenantId = if ($mock.tenantId) { [string]$mock.tenantId } else { $expectedTenantId }
        $result.scopes = if ($mock.PSObject.Properties.Name -contains 'scopes') {
            @($mock.scopes | ForEach-Object { [string]$_ })
        } else {
            @()
        }
        $result.defaultDomain = [string]$mock.defaultDomain

        if ($mock.PSObject.Properties.Name -contains 'verifiedDomains') {
            $verifiedDomains += @($mock.verifiedDomains | ForEach-Object { [string]$_ })
        }

        if ($result.defaultDomain) {
            $verifiedDomains += $result.defaultDomain
        }

        if ($expectedTenantId -and $result.tenantId -and ($expectedTenantId -ne $result.tenantId)) {
            $findings += [ordered]@{
                severity = 'Failed'
                code = 'GraphTenantMismatch'
                summary = 'The signed-in Microsoft Graph tenant does not match the customer package.'
                details = "Expected tenant $expectedTenantId but current Graph tenant is $($result.tenantId)."
            }
        }

        $defaultDiscoveryScopes = @('Directory.Read.All', 'Sites.Read.All')
        $missingDiscoveryScopes = @($defaultDiscoveryScopes | Where-Object { $_ -notin $result.scopes })
        if ($missingDiscoveryScopes.Count -gt 0) {
            $findings += [ordered]@{
                severity = 'Warning'
                code = 'GraphDiscoveryScopesMissing'
                summary = 'Additional Graph scopes may be required for full discovery.'
                details = 'Missing or unconfirmed scopes: ' + ($missingDiscoveryScopes -join ', ')
            }
        }

        if ($SkipSharePoint) {
            $findings += [ordered]@{
                severity = 'Info'
                code = 'SharePointDiscoverySkipped'
                summary = 'SharePoint discovery is disabled for this onboarding session.'
                details = 'The discovery payload is using package values for SharePoint fields.'
            }
        } elseif (-not [Uri]::TryCreate($siteUrl, [UriKind]::Absolute, [ref]$parsedSiteUri)) {
            $findings += [ordered]@{
                severity = 'Failed'
                code = 'SharePointSiteUrlInvalid'
                summary = 'SharePoint site URL is invalid.'
                details = $siteUrl
            }
        } else {
            $site = $null
            if ($mock.PSObject.Properties.Name -contains 'site') {
                $site = $mock.site
            }

            if ($site) {
                $result.siteId = if ($site.id) { [string]$site.id } else { $result.siteId }
                $result.siteDisplayName = [string]$site.displayName
                $result.siteUrl = if ($site.webUrl) { [string]$site.webUrl } else { $siteUrl }
                $result.tenantHostname = $parsedSiteUri.Host
                $result.siteResolved = $true

                if ($mock.PSObject.Properties.Name -contains 'drives') {
                    $availableDocumentLibraries = @($mock.drives | Where-Object { $_ } | ForEach-Object {
                        [ordered]@{
                            driveId = [string]$_.id
                            name = [string]$_.name
                            webUrl = [string]$_.webUrl
                            driveType = [string]$_.driveType
                        }
                    })
                }

                if ($result.defaultDocumentLibrary) {
                    $matchingDrive = @($availableDocumentLibraries | Where-Object { $_.name -eq $result.defaultDocumentLibrary } | Select-Object -First 1)
                    if ($matchingDrive) {
                        $result.defaultDocumentLibraryId = [string]$matchingDrive.driveId
                    } else {
                        $availableNames = @($availableDocumentLibraries | ForEach-Object { [string]$_.name })
                        $findings += [ordered]@{
                            severity = 'Warning'
                            code = 'SharePointLibraryNotFound'
                            summary = 'Configured SharePoint document library was not found.'
                            details = "Expected '$($result.defaultDocumentLibrary)'. Available libraries: " + ($availableNames -join ', ')
                        }
                    }
                } elseif ($availableDocumentLibraries.Count -gt 0) {
                    $firstLibrary = $availableDocumentLibraries | Select-Object -First 1
                    $result.defaultDocumentLibrary = [string]$firstLibrary.name
                    $result.defaultDocumentLibraryId = [string]$firstLibrary.driveId
                    $findings += [ordered]@{
                        severity = 'Info'
                        code = 'SharePointDefaultLibraryInferred'
                        summary = 'A default SharePoint document library was inferred.'
                        details = "Using '$($result.defaultDocumentLibrary)' from the resolved site."
                    }
                }
            } else {
                $findings += [ordered]@{
                    severity = 'Failed'
                    code = 'SharePointSiteResolveFailed'
                    summary = 'SharePoint site could not be resolved through Microsoft Graph.'
                    details = 'The mock discovery context did not include a site object.'
                }
            }
        }

        $roles = if ($mock.PSObject.Properties.Name -contains 'roles') {
            @($mock.roles | ForEach-Object { [string]$_ })
        } else {
            @()
        }
        $allowedRoles = @('Global Administrator', 'Application Administrator', 'Cloud Application Administrator')
        $matchedRoles = @($roles | Where-Object { $_ -in $allowedRoles })

        if ($matchedRoles.Count -gt 0) {
            $result.consentStatus = 'AdminRoleReady'
            $findings += [ordered]@{
                severity = 'Info'
                code = 'EntraAdminRoleReady'
                summary = 'The signed-in Graph account has an admin role that can approve app consent.'
                details = 'Matched roles: ' + ($matchedRoles -join ', ')
            }
        } else {
            $result.consentStatus = 'NeedsAdminRole'
            $findings += [ordered]@{
                severity = 'Warning'
                code = 'EntraAdminRoleMissing'
                summary = 'The signed-in Graph account may not be able to approve app consent.'
                details = 'Use a Global Administrator, Cloud Application Administrator, or Application Administrator for consent.'
            }
        }

        if ($findings.Count -eq 0) {
            $findings += [ordered]@{
                severity = 'Info'
                code = 'GraphDiscoveryReady'
                summary = 'Graph and SharePoint discovery completed.'
                details = 'Microsoft Graph tenant, domain, Entra, and SharePoint metadata were collected from a test discovery context.'
            }
        }

        $result.verifiedDomains = @($verifiedDomains | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
        $result.availableDocumentLibraries = @($availableDocumentLibraries)
        $result.findings = $findings
        return [pscustomobject]$result
    }

    $graphAuth = Get-Module -ListAvailable -Name Microsoft.Graph.Authentication | Select-Object -First 1
    if (-not $graphAuth) {
        $findings += [ordered]@{
            severity = 'Warning'
            code = 'GraphAuthenticationMissing'
            summary = 'Microsoft.Graph.Authentication is required for Graph discovery.'
            details = 'Install Microsoft.Graph.Authentication, then sign in and rerun discovery.'
        }

        $result.verifiedDomains = @($verifiedDomains | Sort-Object -Unique)
        $result.findings = $findings
        return [pscustomobject]$result
    }

    Import-Module Microsoft.Graph.Authentication -ErrorAction Stop
    $graphRequestCommand = Get-Command Invoke-MgGraphRequest -ErrorAction SilentlyContinue
    if (-not $graphRequestCommand) {
        $findings += [ordered]@{
            severity = 'Warning'
            code = 'GraphRequestCommandMissing'
            summary = 'Invoke-MgGraphRequest is not available.'
            details = 'Update Microsoft.Graph.Authentication before running Graph discovery.'
        }

        $result.verifiedDomains = @($verifiedDomains | Sort-Object -Unique)
        $result.findings = $findings
        return [pscustomobject]$result
    }

    $context = Get-MgContext -ErrorAction SilentlyContinue
    if (-not $context) {
        $findings += [ordered]@{
            severity = 'Warning'
            code = 'GraphNotSignedIn'
            summary = 'Microsoft Graph sign-in is required for live discovery.'
            details = 'Sign in with Microsoft Graph before running tenant, Entra, and SharePoint discovery.'
        }

        $result.verifiedDomains = @($verifiedDomains | Sort-Object -Unique)
        $result.findings = $findings
        return [pscustomobject]$result
    }

    $result.accountId = [string]$context.Account
    $result.tenantId = [string]$context.TenantId
    $result.scopes = @($context.Scopes | ForEach-Object { [string]$_ })

    if ($expectedTenantId -and $result.tenantId -and ($expectedTenantId -ne $result.tenantId)) {
        $findings += [ordered]@{
            severity = 'Failed'
            code = 'GraphTenantMismatch'
            summary = 'The signed-in Microsoft Graph tenant does not match the customer package.'
            details = "Expected tenant $expectedTenantId but current Graph tenant is $($result.tenantId)."
        }
    }

    $defaultDiscoveryScopes = @('Directory.Read.All', 'Sites.Read.All')
    $missingDiscoveryScopes = @($defaultDiscoveryScopes | Where-Object { $_ -notin $result.scopes })
    if ($missingDiscoveryScopes.Count -gt 0) {
        $findings += [ordered]@{
            severity = 'Warning'
            code = 'GraphDiscoveryScopesMissing'
            summary = 'Additional Graph scopes may be required for full discovery.'
            details = 'Missing or unconfirmed scopes: ' + ($missingDiscoveryScopes -join ', ')
        }
    }

    try {
        $domains = Invoke-MgGraphRequest `
            -Method GET `
            -Uri 'https://graph.microsoft.com/v1.0/domains?$select=id,isVerified,isDefault' `
            -ErrorAction Stop
        $verifiedDomains += @($domains.value | Where-Object { $_.isVerified } | ForEach-Object { [string]$_.id })
        $defaultDomain = @($domains.value | Where-Object { $_.isDefault } | Select-Object -First 1)
        if ($defaultDomain) {
            $result.defaultDomain = [string]$defaultDomain.id
        }
    } catch {
        $findings += [ordered]@{
            severity = 'Warning'
            code = 'GraphDomainsUnavailable'
            summary = 'Tenant verified domains could not be read.'
            details = $_.Exception.Message
        }
    }

    if ($SkipSharePoint) {
        $findings += [ordered]@{
            severity = 'Info'
            code = 'SharePointDiscoverySkipped'
            summary = 'SharePoint discovery is disabled for this onboarding session.'
            details = 'The discovery payload is using package values for SharePoint fields.'
        }
    } elseif (-not [Uri]::TryCreate($siteUrl, [UriKind]::Absolute, [ref]$parsedSiteUri)) {
        $findings += [ordered]@{
            severity = 'Failed'
            code = 'SharePointSiteUrlInvalid'
            summary = 'SharePoint site URL is invalid.'
            details = $siteUrl
        }
    } else {
        $serverRelativePath = if ($parsedSiteUri.AbsolutePath -and $parsedSiteUri.AbsolutePath -ne '/') {
            $parsedSiteUri.AbsolutePath.TrimEnd('/')
        } else {
            '/'
        }
        $siteLookup = "$($parsedSiteUri.Host)`:$serverRelativePath"
        $siteUri = "https://graph.microsoft.com/v1.0/sites/$siteLookup"

        try {
            $site = Invoke-MgGraphRequest -Method GET -Uri $siteUri -ErrorAction Stop
            $result.siteId = [string]$site.id
            $result.siteDisplayName = [string]$site.displayName
            $result.siteUrl = [string]$site.webUrl
            $result.tenantHostname = $parsedSiteUri.Host
            $result.siteResolved = $true

            $drivesUri = "https://graph.microsoft.com/v1.0/sites/$($result.siteId)/drives"
            $drives = Invoke-MgGraphRequest -Method GET -Uri $drivesUri -ErrorAction Stop
            $availableDocumentLibraries = @($drives.value | ForEach-Object {
                [ordered]@{
                    driveId = [string]$_.id
                    name = [string]$_.name
                    webUrl = [string]$_.webUrl
                    driveType = [string]$_.driveType
                }
            })

            if ($result.defaultDocumentLibrary) {
                $matchingDrive = @($availableDocumentLibraries | Where-Object { $_.name -eq $result.defaultDocumentLibrary }) | Select-Object -First 1
                if ($matchingDrive) {
                    $result.defaultDocumentLibraryId = [string]$matchingDrive.driveId
                } else {
                    $availableNames = @($availableDocumentLibraries | ForEach-Object { [string]$_.name })
                    $findings += [ordered]@{
                        severity = 'Warning'
                        code = 'SharePointLibraryNotFound'
                        summary = 'Configured SharePoint document library was not found.'
                        details = "Expected '$($result.defaultDocumentLibrary)'. Available libraries: " + ($availableNames -join ', ')
                    }
                }
            } elseif ($availableDocumentLibraries.Count -gt 0) {
                $firstLibrary = $availableDocumentLibraries | Select-Object -First 1
                $result.defaultDocumentLibrary = [string]$firstLibrary.name
                $result.defaultDocumentLibraryId = [string]$firstLibrary.driveId
                $findings += [ordered]@{
                    severity = 'Info'
                    code = 'SharePointDefaultLibraryInferred'
                    summary = 'A default SharePoint document library was inferred.'
                    details = "Using '$($result.defaultDocumentLibrary)' from the resolved site."
                }
            }
        } catch {
            $findings += [ordered]@{
                severity = 'Failed'
                code = 'SharePointSiteResolveFailed'
                summary = 'SharePoint site could not be resolved through Microsoft Graph.'
                details = $_.Exception.Message
            }
        }
    }

    try {
        $roleResponse = Invoke-MgGraphRequest `
            -Method GET `
            -Uri 'https://graph.microsoft.com/v1.0/me/memberOf/microsoft.graph.directoryRole?$select=id,displayName,roleTemplateId' `
            -ErrorAction Stop
        $roles = @($roleResponse.value | ForEach-Object { [string]$_.displayName })
        $allowedRoles = @('Global Administrator', 'Application Administrator', 'Cloud Application Administrator')
        $matchedRoles = @($roles | Where-Object { $_ -in $allowedRoles })

        if ($matchedRoles.Count -gt 0) {
            $result.consentStatus = 'AdminRoleReady'
            $findings += [ordered]@{
                severity = 'Info'
                code = 'EntraAdminRoleReady'
                summary = 'The signed-in Graph account has an admin role that can approve app consent.'
                details = 'Matched roles: ' + ($matchedRoles -join ', ')
            }
        } else {
            $result.consentStatus = 'NeedsAdminRole'
            $findings += [ordered]@{
                severity = 'Warning'
                code = 'EntraAdminRoleMissing'
                summary = 'The signed-in Graph account may not be able to approve app consent.'
                details = 'Use a Global Administrator, Cloud Application Administrator, or Application Administrator for consent.'
            }
        }
    } catch {
        $findings += [ordered]@{
            severity = 'Warning'
            code = 'EntraAdminRoleCheckUnavailable'
            summary = 'Entra admin role verification could not be completed.'
            details = $_.Exception.Message
        }
    }

    if ($findings.Count -eq 0) {
        $findings += [ordered]@{
            severity = 'Info'
            code = 'GraphDiscoveryReady'
            summary = 'Graph and SharePoint discovery completed.'
            details = 'Microsoft Graph tenant, domain, Entra, and SharePoint metadata were collected with read-only requests.'
        }
    }

    $result.verifiedDomains = @($verifiedDomains | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    $result.availableDocumentLibraries = @($availableDocumentLibraries)
    $result.findings = $findings
    [pscustomobject]$result
}
