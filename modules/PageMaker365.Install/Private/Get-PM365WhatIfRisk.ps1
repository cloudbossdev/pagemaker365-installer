function Get-PM365WhatIfRisk {
    [CmdletBinding()]
    param(
        [object[]] $Changes = @(),

        [switch] $UnstructuredFallback
    )

    $createCount = 0
    $modifyCount = 0
    $deleteCount = 0
    $ignoreCount = 0
    $noChangeCount = 0
    $unknownCount = 0
    $warningCount = 0
    $blockedCount = 0

    foreach ($change in @($Changes)) {
        $changeType = ([string]$change.changeType).Trim().ToLowerInvariant()
        switch ($changeType) {
            'create' { $createCount++; break }
            'modify' { $modifyCount++; break }
            'delete' { $deleteCount++; $blockedCount++; break }
            'ignore' { $ignoreCount++; $warningCount++; break }
            'nochange' { $noChangeCount++; break }
            'no change' { $noChangeCount++; break }
            default { $unknownCount++; $warningCount++; break }
        }
    }

    if ($UnstructuredFallback) {
        $unknownCount++
        $warningCount++
    }

    $riskStatus = 'Passed'
    $riskLevel = 'Low'
    if ($blockedCount -gt 0) {
        $riskStatus = 'Blocked'
        $riskLevel = 'High'
    } elseif ($warningCount -gt 0) {
        $riskStatus = 'Warning'
        $riskLevel = 'Medium'
    }

    [pscustomobject][ordered]@{
        policy = 'PM365DefaultWhatIfRiskPolicy'
        status = $riskStatus
        riskLevel = $riskLevel
        createCount = $createCount
        modifyCount = $modifyCount
        deleteCount = $deleteCount
        ignoreCount = $ignoreCount
        noChangeCount = $noChangeCount
        unknownCount = $unknownCount
        warningCount = $warningCount
        blockedCount = $blockedCount
    }
}
