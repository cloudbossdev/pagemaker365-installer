function New-PM365WhatIfArtifact {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [object] $Config,

        [AllowNull()]
        [object] $Context,

        [string] $TemplateFile = '',

        [string] $Method = '',

        [Parameter(Mandatory)]
        [object] $Risk,

        [string] $Status = '',

        [object[]] $Changes = @(),

        [string[]] $Output = @(),

        [AllowNull()]
        [object] $ResultMetadata,

        [string] $ErrorCode = '',

        [string] $ErrorMessage = ''
    )

    $subscription = Get-PM365ObjectProperty -InputObject $Context -Name @('Subscription')
    $tenant = Get-PM365ObjectProperty -InputObject $Context -Name @('Tenant')

    [pscustomobject][ordered]@{
        artifactType = 'PageMaker365.AzureWhatIf'
        schemaVersion = '0.1'
        generatedAt = (Get-Date).ToUniversalTime().ToString('o')
        status = if ([string]::IsNullOrWhiteSpace($Status)) { [string]$Risk.status } else { $Status }
        method = $Method
        azure = [ordered]@{
            tenantId = [string](Get-PM365ObjectProperty -InputObject $tenant -Name @('Id', 'TenantId'))
            subscriptionId = [string](Get-PM365ObjectProperty -InputObject $subscription -Name @('Id', 'SubscriptionId'))
            subscriptionName = [string](Get-PM365ObjectProperty -InputObject $subscription -Name @('Name'))
            resourceGroupName = [string]$Config.azure.resourceGroupName
        }
        template = [ordered]@{
            templateFile = $TemplateFile
        }
        risk = $Risk
        changes = @($Changes)
        output = @($Output)
        resultMetadata = ConvertTo-PM365RedactedObject -InputObject $ResultMetadata -Depth 8
        error = if ([string]::IsNullOrWhiteSpace($ErrorCode) -and [string]::IsNullOrWhiteSpace($ErrorMessage)) {
            $null
        } else {
            [ordered]@{
                code = $ErrorCode
                message = $ErrorMessage
            }
        }
    }
}
