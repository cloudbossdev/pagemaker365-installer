function Test-PM365DeploymentContract {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $results = @()
    $results += New-PM365Result `
        -Status 'Passed' `
        -Code 'DeploymentContractReadable' `
        -Summary 'Customer install package can be read.' `
        -Details $ConfigPath

    $secrets = $config.PSObject.Properties['secrets'].Value
    if ($secrets) {
        $blockedSecretProperties = @('values', 'connectionStrings', 'passwords', 'tokens', 'clientSecrets', 'apiKeys')
        $presentBlockedProperties = @(
            $blockedSecretProperties |
                Where-Object { $secrets.PSObject.Properties.Name -contains $_ }
        )

        if ($presentBlockedProperties.Count -gt 0) {
            $results += New-PM365Result `
                -Status 'Failed' `
                -Code 'DeploymentPackageContainsRawSecrets' `
                -Summary 'Customer install package appears to contain raw secret values.' `
                -Details ("Remove these properties from the package: " + ($presentBlockedProperties -join ', ')) `
                -RetrySafe $false
        } else {
            $results += New-PM365Result `
                -Status 'Passed' `
                -Code 'DeploymentPackageSecretSafe' `
                -Summary 'No blocked raw secret containers were found in the customer package.' `
                -Details 'The package may list secret names and prompts, but should not contain secret values.'
        }
    } else {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'DeploymentSecretsContractMissing' `
            -Summary 'Secret handling contract is missing from the customer package.' `
            -Details 'Add secrets.requiredSecretNames and prompt metadata before production deployment.' `
            -RetrySafe $true
    }

    $warnings = @()
    if ([string]::IsNullOrWhiteSpace([string]$config.contractVersion)) {
        $warnings += 'contractVersion'
    }

    if ([string]::IsNullOrWhiteSpace([string]$config.customer.customerId)) {
        $warnings += 'customer.customerId'
    }

    if ([string]::IsNullOrWhiteSpace([string]$config.customer.installationId)) {
        $warnings += 'customer.installationId'
    }

    if (-not $config.azure.resourceNames) {
        $warnings += 'azure.resourceNames'
    }

    if (-not $config.entra) {
        $warnings += 'entra'
    } elseif ([string]::IsNullOrWhiteSpace([string]$config.entra.appRegistrationMode)) {
        $warnings += 'entra.appRegistrationMode'
    }

    if (-not $config.controlPlane) {
        $warnings += 'controlPlane'
    } else {
        foreach ($field in @('deploymentExportId', 'licenseActivationId', 'entitlementSyncUrl', 'publicKeyId')) {
            if ([string]::IsNullOrWhiteSpace([string]$config.controlPlane.$field)) {
                $warnings += "controlPlane.$field"
            }
        }
    }

    if (-not $config.smokeTests) {
        $warnings += 'smokeTests'
    }

    if ($warnings.Count -gt 0) {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'DeploymentContractIncomplete' `
            -Summary 'Customer package is missing launch deployment contract fields.' `
            -Details ("Missing or incomplete fields: " + ($warnings -join ', ')) `
            -RetrySafe $true
    } else {
        $results += New-PM365Result `
            -Status 'Passed' `
            -Code 'DeploymentContractReady' `
            -Summary 'Customer package includes the launch deployment contract fields.' `
            -Details 'The installer can use the package for production preflight once tenant permissions are ready.'
    }

    $results
}

