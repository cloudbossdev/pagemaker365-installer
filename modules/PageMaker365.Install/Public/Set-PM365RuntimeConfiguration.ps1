function Set-PM365RuntimeConfiguration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath,

        [string] $TemplateFile = (Get-PM365DefaultRuntimeConfigurationTemplateFile),

        [string] $OutputPath = ''
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $runtimeItems = @()
    $referenceStatuses = @()
    $configuredAt = [DateTimeOffset]::UtcNow

    try {
        $metadataLine = [Console]::In.ReadLine()
        if ([string]::IsNullOrWhiteSpace($metadataLine)) {
            throw [System.IO.InvalidDataException]::new('Protected runtime configuration input was not provided.')
        }

        $metadata = $metadataLine | ConvertFrom-Json -Depth 12
        $declaredSecrets = @($config.secrets.runtimeSecrets)
        $inputSecrets = @($metadata.secrets)
        if ($metadata.contractVersion -ne '0.1' -or $declaredSecrets.Count -eq 0 -or $inputSecrets.Count -ne $declaredSecrets.Count) {
            throw [System.IO.InvalidDataException]::new('Protected runtime configuration metadata does not match the signed customer package.')
        }

        foreach ($inputSecret in $inputSecrets) {
            $appSettingName = [string]$inputSecret.appSettingName
            $keyVaultSecretName = [string]$inputSecret.keyVaultSecretName
            $declared = @(
                $declaredSecrets |
                    Where-Object {
                        [string]$_.appSettingName -ceq $appSettingName -and
                        [string]$_.keyVaultSecretName -ieq $keyVaultSecretName
                    }
            )
            if ($declared.Count -ne 1) {
                throw [System.IO.InvalidDataException]::new('Protected runtime configuration metadata contains an undeclared secret reference.')
            }

            $value = [Console]::In.ReadLine()
            $minimumLength = [int]$declared[0].minimumLength
            if ($null -eq $value -or $value.Length -lt $minimumLength) {
                $value = $null
                throw [System.IO.InvalidDataException]::new("Protected runtime configuration is incomplete for $appSettingName.")
            }

            $runtimeItems += @{
                keyVaultSecretName = $keyVaultSecretName
                value = $value
            }
            $value = $null
        }

        if ($null -ne [Console]::In.ReadLine()) {
            throw [System.IO.InvalidDataException]::new('Protected runtime configuration contains undeclared input.')
        }

        if (-not (Test-Path -LiteralPath $TemplateFile)) {
            throw [System.IO.FileNotFoundException]::new('Runtime configuration template was not found.')
        }

        Import-Module Az.Accounts -ErrorAction Stop
        Import-Module Az.Resources -ErrorAction Stop

        $context = Get-AzContext -ErrorAction Stop
        if (-not $context.Subscription.Id -or [string]$context.Subscription.Id -ne [string]$config.azure.subscriptionId) {
            throw [System.InvalidOperationException]::new('Azure subscription context no longer matches the signed customer package.')
        }

        $deploymentName = 'pm365-runtime-config-{0}' -f ([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))
        $deployment = New-AzResourceGroupDeployment `
            -Name $deploymentName `
            -ResourceGroupName ([string]$config.azure.resourceGroupName) `
            -TemplateFile $TemplateFile `
            -TemplateParameterObject @{
                keyVaultName = [string]$config.secrets.keyVaultName
                runtimeSecrets = @{
                    items = $runtimeItems
                }
            } `
            -ErrorAction Stop

        foreach ($item in $runtimeItems) {
            $item.value = $null
        }

        $subscriptionId = [string]$config.azure.subscriptionId
        $resourceGroupName = [string]$config.azure.resourceGroupName
        $apiAppName = [string]$config.azure.resourceNames.apiAppName
        $apiResourcePath = "/subscriptions/$subscriptionId/resourceGroups/$resourceGroupName/providers/Microsoft.Web/sites/$apiAppName"
        Invoke-AzRestMethod `
            -Method POST `
            -Path "$apiResourcePath/config/configreferences/appsettings/refresh?api-version=2025-03-01" `
            -ErrorAction Stop | Out-Null

        $expectedAppSettings = @($declaredSecrets | ForEach-Object { [string]$_.appSettingName })
        for ($attempt = 1; $attempt -le 12; $attempt++) {
            $response = Invoke-AzRestMethod `
                -Method GET `
                -Path "$apiResourcePath/config/configreferences/appsettings?api-version=2025-03-01" `
                -ErrorAction Stop
            $referenceStatuses = @(ConvertTo-PM365KeyVaultReferenceStatus `
                -Response $response `
                -ExpectedAppSettings $expectedAppSettings)

            $unresolved = @(
                $expectedAppSettings |
                    Where-Object {
                        $setting = $_
                        -not ($referenceStatuses | Where-Object {
                            $_.appSettingName -ceq $setting -and $_.status -eq 'Resolved'
                        })
                    }
            )
            if ($unresolved.Count -eq 0) {
                break
            }

            if ($attempt -lt 12) {
                Start-Sleep -Seconds 10
            }
        }

        $unresolved = @(
            $expectedAppSettings |
                Where-Object {
                    $setting = $_
                    -not ($referenceStatuses | Where-Object {
                        $_.appSettingName -ceq $setting -and $_.status -eq 'Resolved'
                    })
                }
        )
        if ($unresolved.Count -gt 0) {
            throw [System.InvalidOperationException]::new(
                'App Service could not resolve all Key Vault references through the customer managed identity: ' +
                ($unresolved -join ', '))
        }

        $artifactPath = ''
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'runtime-configuration.json' `
                -InputObject ([ordered]@{
                    contractVersion = '0.1'
                    configuredAt = $configuredAt.ToString('O')
                    status = 'Passed'
                    subscriptionId = $subscriptionId
                    resourceGroupName = $resourceGroupName
                    keyVaultName = [string]$config.secrets.keyVaultName
                    apiAppName = $apiAppName
                    deploymentName = [string]$deployment.DeploymentName
                    secretNames = @($declaredSecrets | ForEach-Object { [string]$_.keyVaultSecretName })
                    appSettingNames = $expectedAppSettings
                    valuesPersisted = $false
                    referenceStatuses = $referenceStatuses
                })
        }

        New-PM365Result `
            -Status 'Passed' `
            -Code 'RuntimeConfigurationReady' `
            -Summary 'Runtime secrets were provisioned and App Service Key Vault references resolved.' `
            -Details "Configured $($declaredSecrets.Count) customer-owned runtime secret references without persisting values." `
            -Data @{
                artifactPath = $artifactPath
                configuredSecretCount = $declaredSecrets.Count
                appSettingNames = $expectedAppSettings
                valuesPersisted = $false
            }
    } catch [System.IO.InvalidDataException] {
        New-PM365Result `
            -Status 'Failed' `
            -Code 'RuntimeConfigurationInputInvalid' `
            -Summary 'Protected runtime configuration input is incomplete or does not match the package.' `
            -Details $_.Exception.Message `
            -RetrySafe $true
    } catch {
        $correlationId = ''
        if ($_.Exception.Message -match '(?i)CorrelationId:\s*([0-9a-f-]{36})') {
            $correlationId = $Matches[1]
        }
        $details = 'Azure did not accept or resolve the protected runtime configuration. Retry after verifying the package, deployment identity, and Key Vault state.'
        if (-not [string]::IsNullOrWhiteSpace($correlationId)) {
            $details = "$details Azure correlation ID: $correlationId"
        }

        New-PM365Result `
            -Status 'Failed' `
            -Code 'RuntimeConfigurationFailed' `
            -Summary 'Runtime configuration could not be completed.' `
            -Details $details `
            -RetrySafe $true
    } finally {
        foreach ($item in $runtimeItems) {
            $item.value = $null
        }
        $runtimeItems = @()
        $metadataLine = $null
        $metadata = $null
        $value = $null
    }
}
