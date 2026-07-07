function Get-PM365WhatIfChanges {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [object] $WhatIfResult
    )

    if ($null -eq $WhatIfResult) {
        return @()
    }

    $changes = Get-PM365ObjectProperty -InputObject $WhatIfResult -Name @('Changes', 'changes')
    if ($null -eq $changes) {
        $changeType = Get-PM365ObjectProperty -InputObject $WhatIfResult -Name @('ChangeType', 'changeType')
        if ($null -ne $changeType) {
            $changes = @($WhatIfResult)
        } elseif ($WhatIfResult -is [System.Collections.IEnumerable] -and $WhatIfResult -isnot [string]) {
            $changes = $WhatIfResult
        }
    }

    $normalized = @()
    foreach ($change in @($changes)) {
        if ($null -ne $change) {
            $normalized += ConvertTo-PM365WhatIfChange -Change $change
        }
    }

    return $normalized
}
