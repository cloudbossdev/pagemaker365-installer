function Get-PM365TemplateParameterValidationIssue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Config
    )

    $issues = @()

    function Add-Issue {
        param(
            [Parameter(Mandatory)]
            [string] $Field,

            [Parameter(Mandatory)]
            [string] $Message,

            [string] $Value = ''
        )

        [pscustomobject][ordered]@{
            field = $Field
            value = $Value
            message = $Message
        }
    }

    function Test-Name {
        param(
            [Parameter(Mandatory)]
            [string] $Field,

            [AllowNull()]
            [object] $Value,

            [Parameter(Mandatory)]
            [int] $MinimumLength,

            [Parameter(Mandatory)]
            [int] $MaximumLength,

            [Parameter(Mandatory)]
            [string] $Pattern,

            [Parameter(Mandatory)]
            [string] $PatternDescription,

            [string[]] $AdditionalBlockedPatterns = @()
        )

        $name = [string]$Value
        if ([string]::IsNullOrWhiteSpace($name)) {
            return Add-Issue -Field $Field -Message 'Required resource name is missing.'
        }

        if ($name.Length -lt $MinimumLength -or $name.Length -gt $MaximumLength) {
            return Add-Issue `
                -Field $Field `
                -Value $name `
                -Message ("Name must be between {0} and {1} characters." -f $MinimumLength, $MaximumLength)
        }

        if ($name -notmatch $Pattern) {
            return Add-Issue -Field $Field -Value $name -Message $PatternDescription
        }

        foreach ($blockedPattern in $AdditionalBlockedPatterns) {
            if ($name -match $blockedPattern) {
                return Add-Issue -Field $Field -Value $name -Message 'Name contains a blocked character sequence.'
            }
        }

        $null
    }

    function Test-RuntimeArtifact {
        param(
            [Parameter(Mandatory)]
            [object] $Artifact,

            [Parameter(Mandatory)]
            [string] $Path,

            [Parameter(Mandatory)]
            [string] $ExpectedStartupCommand,

            [Parameter(Mandatory)]
            [string] $DeploymentEnvironment
        )

        $artifactIssues = @()
        $fileName = [string]$Artifact.fileName
        if ($fileName -cnotmatch '^[A-Za-z0-9._+-]+\.zip$' -or
            [System.IO.Path]::GetFileName($fileName) -cne $fileName) {
            $artifactIssues += Add-Issue -Field "$Path.fileName" -Message 'Runtime artifact file name must be a simple portable ZIP file name.'
        }

        $rawSize = $Artifact.sizeBytes
        $sizeIsInteger = $rawSize -is [byte] -or $rawSize -is [sbyte] -or
            $rawSize -is [int16] -or $rawSize -is [uint16] -or
            $rawSize -is [int32] -or $rawSize -is [uint32] -or
            $rawSize -is [int64]
        if (-not $sizeIsInteger -or [long]$rawSize -lt 1 -or [long]$rawSize -gt 268435456) {
            $artifactIssues += Add-Issue -Field "$Path.sizeBytes" -Message 'Runtime artifact size must be an integer from 1 byte through 256 MiB.'
        }

        $sha256 = [string]$Artifact.sha256
        if ($sha256 -cnotmatch '^[0-9a-f]{64}$') {
            $artifactIssues += Add-Issue -Field "$Path.sha256" -Message 'Runtime artifact SHA-256 must be exactly 64 lowercase hexadecimal characters.'
        }

        if ([string]$Artifact.startupCommand -cne $ExpectedStartupCommand) {
            $artifactIssues += Add-Issue -Field "$Path.startupCommand" -Message 'Runtime artifact startup command does not match the fixed contract.'
        }

        $downloadUrl = [string]$Artifact.downloadUrl
        $downloadUri = $null
        $validUri = [uri]::TryCreate($downloadUrl, [System.UriKind]::Absolute, [ref]$downloadUri)
        $isLocal = $validUri -and $downloadUri.Host -in @('localhost', '127.0.0.1', '::1')
        $allowLocal = $DeploymentEnvironment -ceq 'dev' -and
            [Environment]::GetEnvironmentVariable('PM365_ALLOW_LOCAL_RUNTIME_ARTIFACTS', 'Process') -ceq 'true'
        $allowedReleaseHost = $validUri -and $downloadUri.Host -in @(
            'downloads.pagemaker365.com',
            'downloads-staging.pagemaker365.com'
        )
        $downloadFileName = if ($validUri -and $downloadUri.Segments.Count -gt 0) {
            [uri]::UnescapeDataString($downloadUri.Segments[-1])
        } else {
            ''
        }
        if ($downloadUrl -cne $downloadUrl.Trim() -or
            -not $validUri -or
            (-not $allowedReleaseHost -and -not ($isLocal -and $allowLocal)) -or
            ($downloadUri.Scheme -cne 'https' -and -not ($isLocal -and $allowLocal -and $downloadUri.Scheme -ceq 'http')) -or
            (-not $isLocal -and -not $downloadUri.IsDefaultPort) -or
            -not [string]::IsNullOrWhiteSpace($downloadUri.UserInfo) -or
            -not [string]::IsNullOrEmpty($downloadUri.Query) -or
            -not [string]::IsNullOrEmpty($downloadUri.Fragment) -or
            $downloadUri.AbsolutePath.Contains('//') -or
            $downloadFileName -cne $fileName) {
            $artifactIssues += Add-Issue -Field "$Path.downloadUrl" -Message 'Runtime artifact URL must be the exact file on an approved default-port HTTPS release host without credentials, query, or fragment.'
        }

        $artifactIssues
    }

    if ($Config.contractVersion -isnot [string] -or [string]$Config.contractVersion -cne '0.4') {
        $issues += Add-Issue -Field 'contractVersion' -Message 'Customer install contract version must be exactly 0.4.'
    }

    $runtimeSecretExpectedProperties = @(
        'keyVaultSecretName',
        'appSettingName',
        'label',
        'purpose',
        'source',
        'owner',
        'targetApp',
        'required',
        'minimumLength'
    )
    $runtimeSecretContracts = @{
        DATABASE_URL = @{
            Source = 'operator'
            MinimumLength = 12
        }
        API_ENTRA_CLIENT_SECRET = @{
            Source = 'operator'
            MinimumLength = 16
        }
        API_IMAGE_ASSET_CURSOR_SECRET = @{
            Source = 'installerGenerated'
            MinimumLength = 32
        }
    }
    $runtimeSecrets = @($Config.secrets.runtimeSecrets)
    $declaredRuntimeSettings = @()
    $declaredVaultSecretNames = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    if ($runtimeSecrets.Count -ne 3) {
        $issues += Add-Issue -Field 'secrets.runtimeSecrets' -Message 'Exactly three supported runtime secret definitions are required.'
    }
    foreach ($runtimeSecret in $runtimeSecrets) {
        if ($null -eq $runtimeSecret -or $runtimeSecret -isnot [pscustomobject]) {
            $issues += Add-Issue -Field 'secrets.runtimeSecrets' -Message 'Each runtime secret definition must be a JSON object.'
            continue
        }

        $actualProperties = @($runtimeSecret.PSObject.Properties.Name)
        $missingProperties = @($runtimeSecretExpectedProperties | Where-Object { $actualProperties -cnotcontains $_ })
        $unexpectedProperties = @($actualProperties | Where-Object { $runtimeSecretExpectedProperties -cnotcontains $_ })
        if ($missingProperties.Count -gt 0 -or $unexpectedProperties.Count -gt 0 -or
            $actualProperties.Count -ne $runtimeSecretExpectedProperties.Count) {
            $issues += Add-Issue -Field 'secrets.runtimeSecrets' -Message 'Runtime secret definitions must use only the exact approved metadata fields.'
            continue
        }

        $appSettingName = if ($runtimeSecret.appSettingName -is [string]) {
            [string]$runtimeSecret.appSettingName
        } else {
            ''
        }
        $declaredRuntimeSettings += $appSettingName
        $expectedContract = $runtimeSecretContracts[$appSettingName]
        $minimumLength = $runtimeSecret.minimumLength
        $minimumIsInteger = $minimumLength -is [byte] -or $minimumLength -is [sbyte] -or
            $minimumLength -is [int16] -or $minimumLength -is [uint16] -or
            $minimumLength -is [int32] -or $minimumLength -is [uint32] -or
            $minimumLength -is [int64]
        $stringFieldsValid = @(
            'keyVaultSecretName',
            'appSettingName',
            'label',
            'purpose',
            'source',
            'owner',
            'targetApp'
        ) | ForEach-Object { $runtimeSecret.$_ -is [string] }
        $keyVaultSecretName = if ($runtimeSecret.keyVaultSecretName -is [string]) {
            [string]$runtimeSecret.keyVaultSecretName
        } else {
            ''
        }
        $keyVaultSecretNameIsValid = $keyVaultSecretName -cmatch '^[A-Za-z0-9-]{1,127}$'
        $keyVaultSecretNameIsUnique = $keyVaultSecretNameIsValid -and
            $declaredVaultSecretNames.Add($keyVaultSecretName)

        if ($null -eq $expectedContract -or
            $stringFieldsValid -contains $false -or
            [string]::IsNullOrWhiteSpace([string]$runtimeSecret.label) -or
            [string]::IsNullOrWhiteSpace([string]$runtimeSecret.purpose) -or
            -not $keyVaultSecretNameIsValid -or
            -not $keyVaultSecretNameIsUnique -or
            [string]$runtimeSecret.source -cne [string]$expectedContract.Source -or
            [string]$runtimeSecret.owner -cne 'customer' -or
            [string]$runtimeSecret.targetApp -cne 'api' -or
            $runtimeSecret.required -isnot [bool] -or
            $runtimeSecret.required -ne $true -or
            -not $minimumIsInteger -or
            [long]$minimumLength -lt [long]$expectedContract.MinimumLength -or
            [long]$minimumLength -gt 4096) {
            $issues += Add-Issue -Field "secrets.runtimeSecrets[$appSettingName]" -Message 'Runtime secret metadata does not match the exact supported contract.'
        }
    }

    foreach ($expectedSetting in $runtimeSecretContracts.Keys) {
        if (@($declaredRuntimeSettings | Where-Object { $_ -ceq $expectedSetting }).Count -ne 1) {
            $issues += Add-Issue -Field 'secrets.runtimeSecrets' -Message "Runtime setting $expectedSetting must appear exactly once."
        }
    }

    $appName = [string]$Config.app.appName
    if ([string]::IsNullOrWhiteSpace($appName)) {
        $issues += Add-Issue -Field 'app.appName' -Message 'Application name is required.'
    }

    $environment = [string]$Config.azure.environment
    if ([string]::IsNullOrWhiteSpace($environment)) {
        $issues += Add-Issue -Field 'azure.environment' -Message 'Azure deployment environment is required.'
    } elseif ($environment -cnotin @('dev', 'staging', 'production')) {
        $issues += Add-Issue -Field 'azure.environment' -Value $environment -Message 'Environment must be dev, staging, or production.'
    }

    $location = [string]$Config.azure.location
    if ([string]::IsNullOrWhiteSpace($location)) {
        $issues += Add-Issue -Field 'azure.location' -Message 'Azure deployment location is required.'
    } elseif ($location -notmatch '^[A-Za-z0-9]+$') {
        $issues += Add-Issue `
            -Field 'azure.location' `
            -Value $location `
            -Message 'Azure deployment location must use the Azure location name, such as eastus, not the display name.'
    }

    $customerTenantId = [string]$Config.customer.tenantId
    if ([string]::IsNullOrWhiteSpace($customerTenantId)) {
        $issues += Add-Issue -Field 'customer.tenantId' -Message 'Customer tenant ID is required.'
    } elseif ($customerTenantId -notmatch '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$' -or
        $customerTenantId -ceq '00000000-0000-0000-0000-000000000000') {
        $issues += Add-Issue -Field 'customer.tenantId' -Value $customerTenantId -Message 'Customer tenant ID must be a non-empty canonical GUID.'
    }

    foreach ($identityField in @(
        @{ Field = 'entra.portalClientId'; Value = [string]$Config.entra.portalClientId },
        @{ Field = 'entra.apiClientId'; Value = [string]$Config.entra.apiClientId }
    )) {
        if ($identityField.Value -notmatch '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$' -or
            $identityField.Value -ceq '00000000-0000-0000-0000-000000000000') {
            $issues += Add-Issue -Field $identityField.Field -Value $identityField.Value -Message 'Application client ID must be a non-empty canonical GUID.'
        }
    }
    if ([string]$Config.entra.portalClientId -and
        [string]$Config.entra.apiClientId -and
        [string]$Config.entra.portalClientId -ieq [string]$Config.entra.apiClientId) {
        $issues += Add-Issue -Field 'entra' -Message 'Portal and API client IDs must identify distinct applications.'
    }

    foreach ($displayField in @(
        @{ Field = 'customer.tenantName'; Value = [string]$Config.customer.tenantName; MaximumLength = 128 },
        @{ Field = 'customer.accountKey'; Value = [string]$Config.customer.accountKey; MaximumLength = 64 }
    )) {
        if ([string]::IsNullOrEmpty($displayField.Value) -or
            $displayField.Value.Length -gt $displayField.MaximumLength -or
            $displayField.Value -cne $displayField.Value.Trim() -or
            $displayField.Value -match '[\x00-\x1F\x7F-\x9F\u2028\u2029\u202A-\u202E\u2066-\u2069]') {
            $issues += Add-Issue -Field $displayField.Field -Message "Value must be 1-$($displayField.MaximumLength) trimmed characters without controls, line separators, or bidi overrides."
        }
    }

    $sharePointSiteUrl = [string]$Config.sharePoint.siteUrl
    $sharePointUri = $null
    if (-not [uri]::TryCreate($sharePointSiteUrl, [System.UriKind]::Absolute, [ref]$sharePointUri) -or
        $sharePointUri.Scheme -cne 'https' -or
        -not $sharePointUri.IsDefaultPort -or
        -not [string]::IsNullOrWhiteSpace($sharePointUri.UserInfo) -or
        -not $sharePointUri.Host.EndsWith('.sharepoint.com', [System.StringComparison]::OrdinalIgnoreCase)) {
        $issues += Add-Issue -Field 'sharePoint.siteUrl' -Message 'SharePoint site URL must use the customer exact default-port HTTPS SharePoint host.'
    }

    $runtimeReleaseId = [string]$Config.runtimeArtifacts.releaseId
    if ([string]$Config.runtimeArtifacts.contractVersion -cne '1.0') {
        $issues += Add-Issue -Field 'runtimeArtifacts.contractVersion' -Message 'Runtime artifact contract version must be exactly 1.0.'
    }
    if ($runtimeReleaseId -cnotmatch '^[A-Za-z0-9._+-]{1,128}$') {
        $issues += Add-Issue -Field 'runtimeArtifacts.releaseId' -Message 'Runtime release ID is required.'
    }

    $runtimeVersion = [string]$Config.runtimeArtifacts.runtimeVersion
    $runtimeVersionParts = @($runtimeVersion -split '\.')
    $runtimeVersionValid = $runtimeVersionParts.Count -eq 3
    if ($runtimeVersionValid) {
        foreach ($part in $runtimeVersionParts) {
            $parsedPart = 0
            if ($part -cnotmatch '^(0|[1-9][0-9]*)$' -or -not [int]::TryParse($part, [ref]$parsedPart)) {
                $runtimeVersionValid = $false
                break
            }
        }
    }
    if (-not $runtimeVersionValid) {
        $issues += Add-Issue -Field 'runtimeArtifacts.runtimeVersion' -Message 'Runtime version must be stable major.minor.patch with 32-bit integer components.'
    }

    $runtimeSourceCommit = [string]$Config.runtimeArtifacts.sourceCommit
    if ($runtimeSourceCommit -cnotmatch '^[0-9a-f]{40}$') {
        $issues += Add-Issue -Field 'runtimeArtifacts.sourceCommit' -Message 'Runtime source commit must be exactly 40 lowercase hexadecimal characters.'
    }

    $issues += Test-RuntimeArtifact `
        -Artifact $Config.runtimeArtifacts.api `
        -Path 'runtimeArtifacts.api' `
        -ExpectedStartupCommand 'node dist/index.js' `
        -DeploymentEnvironment $environment
    $issues += Test-RuntimeArtifact `
        -Artifact $Config.runtimeArtifacts.portal `
        -Path 'runtimeArtifacts.portal' `
        -ExpectedStartupCommand 'node .pm365/start-portal-runtime.mjs' `
        -DeploymentEnvironment $environment

    $apiDownloadUri = $null
    $portalDownloadUri = $null
    if ([uri]::TryCreate([string]$Config.runtimeArtifacts.api.downloadUrl, [System.UriKind]::Absolute, [ref]$apiDownloadUri) -and
        [uri]::TryCreate([string]$Config.runtimeArtifacts.portal.downloadUrl, [System.UriKind]::Absolute, [ref]$portalDownloadUri) -and
        [uri]::new($apiDownloadUri, '.').AbsoluteUri -cne [uri]::new($portalDownloadUri, '.').AbsoluteUri) {
        $issues += Add-Issue -Field 'runtimeArtifacts' -Message 'API and portal artifacts must use the exact same approved release directory.'
    }

    $deploymentExportId = [string]$Config.controlPlane.deploymentExportId
    if ([string]::IsNullOrEmpty($deploymentExportId) -or
        $deploymentExportId.Length -gt 256 -or
        $deploymentExportId -cne $deploymentExportId.Trim() -or
        $deploymentExportId -match '[\x00-\x1F\x7F-\x9F]') {
        $issues += Add-Issue -Field 'controlPlane.deploymentExportId' -Message 'Deployment export ID must be 1-256 trimmed characters without C0/C1 controls.'
    }

    $resourceGroupName = [string]$Config.azure.resourceGroupName
    if ([string]::IsNullOrWhiteSpace($resourceGroupName)) {
        $issues += Add-Issue -Field 'azure.resourceGroupName' -Message 'Target resource group name is required.'
    } elseif ($resourceGroupName.Length -gt 90 -or $resourceGroupName -notmatch '^[A-Za-z0-9_\-\.\(\)]+$' -or $resourceGroupName.EndsWith('.')) {
        $issues += Add-Issue `
            -Field 'azure.resourceGroupName' `
            -Value $resourceGroupName `
            -Message 'Resource group name must be 1-90 characters, use only letters, numbers, underscores, hyphens, periods, or parentheses, and must not end with a period.'
    }

    $resourceNames = $Config.azure.resourceNames
    if (-not $resourceNames) {
        $issues += Add-Issue -Field 'azure.resourceNames' -Message 'Azure resource names are required for deployment.'
        return $issues
    }

    $rules = @(
        @{
            Field = 'azure.resourceNames.keyVaultName'
            Value = $resourceNames.keyVaultName
            MinimumLength = 3
            MaximumLength = 24
            Pattern = '^[A-Za-z][A-Za-z0-9-]*[A-Za-z0-9]$'
            PatternDescription = 'Key Vault name must start with a letter, end with a letter or number, and contain only letters, numbers, or hyphens.'
            AdditionalBlockedPatterns = @('--')
        },
        @{
            Field = 'azure.resourceNames.storageAccountName'
            Value = $resourceNames.storageAccountName
            MinimumLength = 3
            MaximumLength = 24
            Pattern = '^[a-z0-9]+$'
            PatternDescription = 'Storage account name must contain only lowercase letters and numbers.'
        },
        @{
            Field = 'azure.resourceNames.logAnalyticsName'
            Value = $resourceNames.logAnalyticsName
            MinimumLength = 4
            MaximumLength = 63
            Pattern = '^[A-Za-z0-9][A-Za-z0-9-]*[A-Za-z0-9]$'
            PatternDescription = 'Log Analytics workspace name must start and end with a letter or number and contain only letters, numbers, or hyphens.'
        },
        @{
            Field = 'azure.resourceNames.applicationInsightsName'
            Value = $resourceNames.applicationInsightsName
            MinimumLength = 1
            MaximumLength = 260
            Pattern = '^[A-Za-z0-9][A-Za-z0-9\-_\.]*[A-Za-z0-9]$'
            PatternDescription = 'Application Insights name must start and end with a letter or number and contain only letters, numbers, hyphens, underscores, or periods.'
        },
        @{
            Field = 'azure.resourceNames.appServicePlanName'
            Value = $resourceNames.appServicePlanName
            MinimumLength = 1
            MaximumLength = 40
            Pattern = '^[A-Za-z0-9][A-Za-z0-9-]*[A-Za-z0-9]$'
            PatternDescription = 'App Service plan name must start and end with a letter or number and contain only letters, numbers, or hyphens.'
        },
        @{
            Field = 'azure.resourceNames.apiAppName'
            Value = $resourceNames.apiAppName
            MinimumLength = 2
            MaximumLength = 60
            Pattern = '^[A-Za-z0-9][A-Za-z0-9-]*[A-Za-z0-9]$'
            PatternDescription = 'App Service name must start and end with a letter or number and contain only letters, numbers, or hyphens.'
        },
        @{
            Field = 'azure.resourceNames.portalAppName'
            Value = $resourceNames.portalAppName
            MinimumLength = 2
            MaximumLength = 60
            Pattern = '^[A-Za-z0-9][A-Za-z0-9-]*[A-Za-z0-9]$'
            PatternDescription = 'Frontend App Service name must start and end with a letter or number and contain only letters, numbers, or hyphens.'
        },
        @{
            Field = 'azure.resourceNames.managedIdentityName'
            Value = $resourceNames.managedIdentityName
            MinimumLength = 3
            MaximumLength = 128
            Pattern = '^[A-Za-z0-9][A-Za-z0-9_-]*[A-Za-z0-9]$'
            PatternDescription = 'Managed identity name must start and end with a letter or number and contain only letters, numbers, hyphens, or underscores.'
        }
    )

    foreach ($rule in $rules) {
        $issue = Test-Name @rule
        if ($issue) {
            $issues += $issue
        }
    }

    $issues
}
