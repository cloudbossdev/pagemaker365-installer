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

    $appName = [string]$Config.app.appName
    if ([string]::IsNullOrWhiteSpace($appName)) {
        $issues += Add-Issue -Field 'app.appName' -Message 'Application name is required.'
    }

    $environment = [string]$Config.azure.environment
    if ([string]::IsNullOrWhiteSpace($environment)) {
        $issues += Add-Issue -Field 'azure.environment' -Message 'Azure deployment environment is required.'
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
    } elseif ($customerTenantId -notmatch '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$') {
        $issues += Add-Issue -Field 'customer.tenantId' -Value $customerTenantId -Message 'Customer tenant ID must be a GUID.'
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
