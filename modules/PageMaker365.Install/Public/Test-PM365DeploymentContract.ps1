function Test-PM365DeploymentContract {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $results = @()
    $hasBlockingContractFailure = $false
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
            $hasBlockingContractFailure = $true
            $results += New-PM365Result `
                -Status 'Failed' `
                -Code 'DeploymentPackageContainsRawSecrets' `
                -Summary 'Customer install package appears to contain raw secret values.' `
                -Details ("Remove these properties from the package: " + ($presentBlockedProperties -join ', ')) `
                -RetrySafe $false
        } elseif (-not $secrets.runtimeSecrets -or @($secrets.runtimeSecrets).Count -eq 0) {
            $hasBlockingContractFailure = $true
            $results += New-PM365Result `
                -Status 'Failed' `
                -Code 'DeploymentSecretsContractMissing' `
                -Summary 'The signed runtime secret metadata contract is missing.' `
                -Details 'Generate a new package that declares secrets.runtimeSecrets with source, ownership, target app, and app-setting metadata.' `
                -RetrySafe $false
        } else {
            $runtimeSecrets = @($secrets.runtimeSecrets)
            $requiredSettings = @('DATABASE_URL', 'API_ENTRA_CLIENT_SECRET', 'API_IMAGE_ASSET_CURSOR_SECRET')
            $declaredSettings = @($runtimeSecrets | ForEach-Object { [string]$_.appSettingName })
            $invalidRuntimeDefinitions = @(
                $runtimeSecrets |
                    Where-Object {
                        [string]::IsNullOrWhiteSpace([string]$_.keyVaultSecretName) -or
                        [string]::IsNullOrWhiteSpace([string]$_.appSettingName) -or
                        [string]$_.owner -cne 'customer' -or
                        [string]$_.targetApp -cne 'api' -or
                        -not [bool]$_.required -or
                        [int]$_.minimumLength -lt 1 -or
                        [int]$_.minimumLength -gt 4096
                    }
            )
            $databaseDefinition = @($runtimeSecrets | Where-Object { [string]$_.appSettingName -ceq 'DATABASE_URL' })
            $entraDefinition = @($runtimeSecrets | Where-Object { [string]$_.appSettingName -ceq 'API_ENTRA_CLIENT_SECRET' })
            $imageCursorDefinition = @($runtimeSecrets | Where-Object { [string]$_.appSettingName -ceq 'API_IMAGE_ASSET_CURSOR_SECRET' })
            $contractMatches = `
                $runtimeSecrets.Count -eq 3 -and `
                @($requiredSettings | Where-Object { $declaredSettings -notcontains $_ }).Count -eq 0 -and `
                $invalidRuntimeDefinitions.Count -eq 0 -and `
                $databaseDefinition.Count -eq 1 -and [string]$databaseDefinition[0].source -ceq 'operator' -and [int]$databaseDefinition[0].minimumLength -ge 12 -and `
                $entraDefinition.Count -eq 1 -and [string]$entraDefinition[0].source -ceq 'operator' -and [int]$entraDefinition[0].minimumLength -ge 16 -and `
                $imageCursorDefinition.Count -eq 1 -and [string]$imageCursorDefinition[0].source -ceq 'installerGenerated' -and [int]$imageCursorDefinition[0].minimumLength -ge 32

            if (-not $contractMatches) {
                $hasBlockingContractFailure = $true
                $results += New-PM365Result `
                    -Status 'Failed' `
                    -Code 'DeploymentSecretsContractInvalid' `
                    -Summary 'The signed runtime secret metadata contract is invalid.' `
                    -Details 'Generate a contractVersion 0.4 package containing only the required DATABASE_URL, API_ENTRA_CLIENT_SECRET, and API_IMAGE_ASSET_CURSOR_SECRET definitions.' `
                    -RetrySafe $false
            } else {
            $results += New-PM365Result `
                -Status 'Passed' `
                -Code 'DeploymentPackageSecretSafe' `
                -Summary 'No blocked raw secret containers were found in the customer package.' `
                -Details 'The package declares secret metadata only and does not contain secret values.'
            }
        }
    } else {
        $hasBlockingContractFailure = $true
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'DeploymentSecretsContractMissing' `
            -Summary 'Secret handling contract is missing from the customer package.' `
            -Details 'Generate a new package with the complete secrets.runtimeSecrets metadata contract before deployment.' `
            -RetrySafe $false
    }

    $warnings = @()
    if ([string]$config.contractVersion -cne '0.4') {
        $hasBlockingContractFailure = $true
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'DeploymentContractVersionUnsupported' `
            -Summary 'Customer install package contract version is unsupported.' `
            -Details 'Generate a new package using contractVersion 0.4.' `
            -RetrySafe $false
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

    $parameterValidationIssues = @(Get-PM365TemplateParameterValidationIssue -Config $config)
    if ($parameterValidationIssues.Count -gt 0) {
        $hasBlockingContractFailure = $true
        $details = @($parameterValidationIssues | ForEach-Object { "{0}: {1}" -f $_.field, $_.message }) -join [Environment]::NewLine
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'DeploymentParametersInvalid' `
            -Summary 'Azure deployment parameters are missing or invalid.' `
            -Details $details `
            -RetrySafe $false `
            -Data @{
                issues = @($parameterValidationIssues)
            }
    } else {
        $results += New-PM365Result `
            -Status 'Passed' `
            -Code 'DeploymentParametersReady' `
            -Summary 'Azure deployment parameters satisfy installer validation rules.' `
            -Details 'The package can be converted into Bicep deployment parameters.'
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

    $trustWarnings = @()
    $trustFailures = @()
    if (-not $config.controlPlane) {
        $trustWarnings += 'controlPlane'
    } else {
        $trustMode = [string]$config.controlPlane.trustMode
        $signedRequired = $trustMode -eq 'SignedRequired'
        foreach ($field in @('deploymentExportId', 'exportedAt', 'issuer', 'schemaId', 'packageHash', 'packageHashAlgorithm', 'canonicalization', 'publicKeyId', 'signature', 'signatureAlgorithm')) {
            if ([string]::IsNullOrWhiteSpace([string]$config.controlPlane.$field)) {
                if ($signedRequired) {
                    $trustFailures += "controlPlane.$field"
                } else {
                    $trustWarnings += "controlPlane.$field"
                }
            }
        }

        $hashAlgorithm = [string]$config.controlPlane.packageHashAlgorithm
        if (-not [string]::IsNullOrWhiteSpace($hashAlgorithm) -and $hashAlgorithm -ne 'SHA-256') {
            $trustFailures += 'controlPlane.packageHashAlgorithm'
        }
    }

    if ($trustFailures.Count -gt 0) {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'DeploymentPackageTrustMetadataInvalid' `
            -Summary 'Customer package is missing required signed export metadata.' `
            -Details ("Missing or invalid fields: " + ($trustFailures -join ', ')) `
            -RetrySafe $false
    } elseif ($trustWarnings.Count -gt 0) {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'DeploymentPackageTrustMetadataIncomplete' `
            -Summary 'Customer package export trust metadata is incomplete.' `
            -Details ("Missing fields: " + ($trustWarnings -join ', ')) `
            -RetrySafe $true
    } else {
        $results += New-PM365Result `
            -Status 'Passed' `
            -Code 'DeploymentPackageTrustMetadataReady' `
            -Summary 'Customer package includes export trust metadata.' `
            -Details 'Hash and signature metadata are present for installer-side trust validation.'
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
    }

    if ($hasBlockingContractFailure) {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'DeploymentContractBlocked' `
            -Summary 'Customer package cannot be used for deployment until blocking issues are fixed.' `
            -Details 'Resolve the failed deployment contract checks above, then run preflight again.' `
            -RetrySafe $false
    } elseif ($warnings.Count -eq 0) {
        $results += New-PM365Result `
            -Status 'Passed' `
            -Code 'DeploymentContractReady' `
            -Summary 'Customer package includes the launch deployment contract fields.' `
            -Details 'The installer can use the package for production preflight once tenant permissions are ready.'
    }

    $results
}
