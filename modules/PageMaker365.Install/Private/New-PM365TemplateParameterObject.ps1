function New-PM365TemplateParameterObject {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Config
    )

    $validationIssues = @(Get-PM365TemplateParameterValidationIssue -Config $Config)
    if ($validationIssues.Count -gt 0) {
        $details = @($validationIssues | ForEach-Object { "{0}: {1}" -f $_.field, $_.message }) -join '; '
        throw "Customer install package has invalid Azure deployment parameters. $details"
    }

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
