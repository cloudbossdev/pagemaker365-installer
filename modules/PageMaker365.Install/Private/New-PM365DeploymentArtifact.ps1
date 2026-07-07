function New-PM365DeploymentArtifact {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [object] $Config,

        [AllowNull()]
        [object] $Context,

        [AllowNull()]
        [object] $Deployment,

        [object[]] $Operations = @(),

        [string] $Status = '',

        [string] $ErrorCode = '',

        [string] $ErrorMessage = ''
    )

    $subscription = Get-PM365ObjectProperty -InputObject $Context -Name @('Subscription')
    $tenant = Get-PM365ObjectProperty -InputObject $Context -Name @('Tenant')
    $outputs = Get-PM365SafeDeploymentOutputs -Outputs (Get-PM365ObjectProperty -InputObject $Deployment -Name @('Outputs', 'outputs'))
    $deploymentName = [string](Get-PM365ObjectProperty -InputObject $Deployment -Name @('DeploymentName', 'Name', 'name'))
    $provisioningState = [string](Get-PM365ObjectProperty -InputObject $Deployment -Name @('ProvisioningState', 'provisioningState'))
    $correlationId = [string](Get-PM365ObjectProperty -InputObject $Deployment -Name @('CorrelationId', 'correlationId'))

    $operationDetails = @()
    foreach ($operation in @($Operations)) {
        if ($null -eq $operation) {
            continue
        }

        $targetResource = Get-PM365ObjectProperty -InputObject $operation -Name @('TargetResource', 'targetResource')
        $operationDetails += [pscustomobject][ordered]@{
            operationId = [string](Get-PM365ObjectProperty -InputObject $operation -Name @('OperationId', 'operationId', 'Id', 'id'))
            provisioningState = [string](Get-PM365ObjectProperty -InputObject $operation -Name @('ProvisioningState', 'provisioningState'))
            timestamp = [string](Get-PM365ObjectProperty -InputObject $operation -Name @('Timestamp', 'timestamp'))
            statusCode = [string](Get-PM365ObjectProperty -InputObject $operation -Name @('StatusCode', 'statusCode'))
            statusMessage = ConvertTo-PM365RedactedObject -InputObject (Get-PM365ObjectProperty -InputObject $operation -Name @('StatusMessage', 'statusMessage')) -Depth 8
            targetResource = ConvertTo-PM365RedactedObject -InputObject $targetResource -Depth 6
        }
    }

    [pscustomobject][ordered]@{
        artifactType = 'PageMaker365.AzureDeployment'
        schemaVersion = '0.1'
        generatedAt = (Get-Date).ToUniversalTime().ToString('o')
        status = $Status
        azure = [ordered]@{
            tenantId = [string](Get-PM365ObjectProperty -InputObject $tenant -Name @('Id', 'TenantId'))
            subscriptionId = [string](Get-PM365ObjectProperty -InputObject $subscription -Name @('Id', 'SubscriptionId'))
            subscriptionName = [string](Get-PM365ObjectProperty -InputObject $subscription -Name @('Name'))
            resourceGroupName = [string]$Config.azure.resourceGroupName
        }
        deployment = [ordered]@{
            name = $deploymentName
            provisioningState = $provisioningState
            correlationId = $correlationId
            mode = [string](Get-PM365ObjectProperty -InputObject $Deployment -Name @('Mode', 'mode'))
            timestamp = [string](Get-PM365ObjectProperty -InputObject $Deployment -Name @('Timestamp', 'timestamp'))
            id = [string](Get-PM365ObjectProperty -InputObject $Deployment -Name @('Id', 'id'))
        }
        outputs = $outputs.outputs
        outputCount = $outputs.outputCount
        includedOutputCount = $outputs.includedOutputCount
        redactedOutputCount = $outputs.redactedOutputCount
        operations = @($operationDetails)
        operationCount = $operationDetails.Count
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
