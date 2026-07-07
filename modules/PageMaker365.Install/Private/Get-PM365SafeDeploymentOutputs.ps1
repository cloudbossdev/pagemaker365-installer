function Get-PM365SafeDeploymentOutputs {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [object] $Outputs
    )

    $safeOutputs = [ordered]@{}
    $outputCount = 0
    $redactedOutputCount = 0

    if ($null -eq $Outputs) {
        return [pscustomobject][ordered]@{
            outputs = $safeOutputs
            outputCount = 0
            includedOutputCount = 0
            redactedOutputCount = 0
        }
    }

    $entries = @()
    if ($Outputs -is [System.Collections.IDictionary]) {
        foreach ($key in $Outputs.Keys) {
            $entries += [pscustomobject]@{
                Name = [string]$key
                Value = $Outputs[$key]
            }
        }
    } else {
        foreach ($property in $Outputs.PSObject.Properties) {
            $entries += [pscustomobject]@{
                Name = $property.Name
                Value = $property.Value
            }
        }
    }

    foreach ($entry in $entries) {
        $outputCount++
        $name = [string]$entry.Name
        $output = $entry.Value
        $type = Get-PM365ObjectProperty -InputObject $output -Name @('Type', 'type')
        $value = Get-PM365ObjectProperty -InputObject $output -Name @('Value', 'value')
        if ($null -eq $value) {
            $value = $output
        }

        if ((Test-PM365SensitiveName -Name $name) -or (Test-PM365SensitiveValue -Value $value)) {
            $redactedOutputCount++
            continue
        }

        $safeOutputs[$name] = [ordered]@{
            type = if ($null -eq $type) { $null } else { [string]$type }
            value = ConvertTo-PM365RedactedObject -InputObject $value -Depth 8
        }
    }

    [pscustomobject][ordered]@{
        outputs = $safeOutputs
        outputCount = $outputCount
        includedOutputCount = $safeOutputs.Count
        redactedOutputCount = $redactedOutputCount
    }
}
