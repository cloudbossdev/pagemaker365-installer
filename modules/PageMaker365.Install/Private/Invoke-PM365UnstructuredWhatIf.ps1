function Invoke-PM365UnstructuredWhatIf {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Config,

        [Parameter(Mandatory)]
        [object] $Context,

        [Parameter(Mandatory)]
        [string] $TemplateFile,

        [Parameter(Mandatory)]
        [hashtable] $DeploymentArguments,

        [string] $OutputPath = '',

        [string] $ReasonCode = 'StructuredWhatIfUnavailable',

        [string] $ReasonMessage = 'Get-AzResourceGroupDeploymentWhatIfResult is unavailable; unstructured what-if output was captured.'
    )

    $method = 'New-AzResourceGroupDeployment -WhatIf'

    try {
        $whatIf = New-AzResourceGroupDeployment @DeploymentArguments -WhatIf *>&1
        $outputText = @($whatIf | ForEach-Object { [string]$_ })
        $risk = Get-PM365WhatIfRisk -UnstructuredFallback
        $artifactPath = ''

        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365WhatIfArtifact `
                -Config $Config `
                -Context $Context `
                -TemplateFile $TemplateFile `
                -Method $method `
                -Risk $risk `
                -Status 'Warning' `
                -Output $outputText `
                -ErrorCode $ReasonCode `
                -ErrorMessage $ReasonMessage
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-whatif.json' `
                -InputObject $artifact
        }

        $details = ($outputText -join [Environment]::NewLine)
        if ([string]::IsNullOrWhiteSpace($details)) {
            $details = $ReasonMessage
        } elseif (-not [string]::IsNullOrWhiteSpace($ReasonMessage)) {
            $details = "$ReasonMessage$([Environment]::NewLine)$details"
        }

        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $details = "$details$([Environment]::NewLine)Artifact: $artifactPath"
        }

        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzureWhatIfReady' `
            -Summary 'Azure deployment preview completed without structured change data.' `
            -Details $details `
            -RetrySafe $true `
            -Data (New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath)
    } catch {
        $risk = Get-PM365WhatIfRisk -UnstructuredFallback
        $artifactPath = ''
        $errorMessage = $_.Exception.Message
        if (-not [string]::IsNullOrWhiteSpace($ReasonMessage)) {
            $errorMessage = "$ReasonMessage Fallback failed: $errorMessage"
        }

        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365WhatIfArtifact `
                -Config $Config `
                -Context $Context `
                -TemplateFile $TemplateFile `
                -Method $method `
                -Risk $risk `
                -Status 'Failed' `
                -ErrorCode 'AzureWhatIfFailed' `
                -ErrorMessage $errorMessage
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-whatif.json' `
                -InputObject $artifact
        }

        $details = $errorMessage
        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $details = "$details$([Environment]::NewLine)Artifact: $artifactPath"
        }

        New-PM365Result `
            -Status 'Failed' `
            -Code 'AzureWhatIfFailed' `
            -Summary 'Azure deployment preview failed.' `
            -Details $details `
            -RetrySafe $true `
            -Data (New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath)
    }
}
