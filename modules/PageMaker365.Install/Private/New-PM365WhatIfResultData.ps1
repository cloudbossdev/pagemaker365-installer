function New-PM365WhatIfResultData {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Risk,

        [string] $ArtifactPath = ''
    )

    $data = @{
        riskLevel = [string]$Risk.riskLevel
        riskStatus = [string]$Risk.status
        createCount = [int]$Risk.createCount
        modifyCount = [int]$Risk.modifyCount
        deleteCount = [int]$Risk.deleteCount
        ignoreCount = [int]$Risk.ignoreCount
        noChangeCount = [int]$Risk.noChangeCount
        unknownCount = [int]$Risk.unknownCount
        warningCount = [int]$Risk.warningCount
        blockedCount = [int]$Risk.blockedCount
    }

    if (-not [string]::IsNullOrWhiteSpace($ArtifactPath)) {
        $data.artifactPath = $ArtifactPath
    }

    return $data
}
