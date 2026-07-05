function New-PM365TemplateParameterObject {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Config
    )

    $resourceNames = @{}
    if ($Config.azure.resourceNames) {
        foreach ($property in $Config.azure.resourceNames.PSObject.Properties) {
            $resourceNames[$property.Name] = [string]$property.Value
        }
    }

    @{
        appName = [string]$Config.app.appName
        environment = [string]$Config.azure.environment
        location = [string]$Config.azure.location
        customerTenantId = [string]$Config.customer.tenantId
        resourceNames = $resourceNames
        tags = @{
            product = 'PageMaker365'
            customer = [string]$Config.customer.tenantName
            environment = [string]$Config.azure.environment
        }
    }
}
