[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$validatorPath = Join-Path $PSScriptRoot 'validate-customer-lifecycle-result.ps1'
$templatePath = Join-Path $repoRoot 'docs\testing\results\customer-lifecycle-result.template.json'
$policyPath = Join-Path $repoRoot 'config\customer-lifecycle-acceptance.json'
$tempPath = Join-Path ([System.IO.Path]::GetTempPath()) ("pm365-lifecycle-result-{0}.json" -f [guid]::NewGuid())

function Copy-Result {
    param([object] $Value)

    return ($Value | ConvertTo-Json -Depth 20 | ConvertFrom-Json -DateKind String)
}

function Write-Candidate {
    param([object] $Value)

    $Value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $tempPath -Encoding utf8
}

function Assert-ValidationFails {
    param(
        [object] $Candidate,
        [string] $ExpectedPattern,
        [string] $Scenario
    )

    Write-Candidate $Candidate
    $failed = $false
    try {
        & $validatorPath -Path $tempPath -RequireApproval | Out-Null
    }
    catch {
        $failed = $true
        if ($_.Exception.Message -notmatch $ExpectedPattern) {
            throw "$Scenario failed with an unexpected message: $($_.Exception.Message)"
        }
    }

    if (-not $failed) {
        throw "$Scenario did not fail validation."
    }
}

function New-PassCheck {
    param([string] $Id, [string] $Group)

    return [pscustomobject]@{
        id = $Id
        result = 'Pass'
        evidenceLink = "evidence://campaign/$Group/$Id"
        reviewer = 'reviewer-alias'
    }
}

function New-PassScenario {
    param([string] $ScenarioId, [string] $Segment)

    return [pscustomobject]@{
        scenarioId = $ScenarioId
        segment = $Segment
        result = 'Pass'
        utcStartedAt = '2026-07-27T12:00:00Z'
        utcCompletedAt = '2026-07-27T12:01:00Z'
        evidenceLink = "evidence://campaign/scenarios/$Segment/$ScenarioId"
        correlationAliases = @("corr-$Segment-$ScenarioId")
        deviationIssue = ''
        reviewer = 'scenario-reviewer'
    }
}

$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json -DateKind String
$planned = Get-Content -LiteralPath $templatePath -Raw | ConvertFrom-Json -DateKind String

try {
    $plannedResult = & $validatorPath -Path $templatePath
    if ($plannedResult.result -ne 'Valid' -or $plannedResult.status -ne 'Planned' -or $plannedResult.approved) {
        throw 'Planned lifecycle template did not return the expected non-approved validation result.'
    }

    Assert-ValidationFails -Candidate $planned -ExpectedPattern 'Completed.*Approved' -Scenario 'Planned template approval'

    $completed = Copy-Result $planned
    $completed.status = 'Completed'
    $completed.finalDecision = 'Approved'
    $completed.run = [pscustomobject]@{
        runId = 'PM365-ACCEPT-20260727-1200-1.0.0'
        utcStartedAt = '2026-07-27T12:00:00Z'
        utcCompletedAt = '2026-07-27T18:00:00Z'
        releaseVersion = '1.0.0'
        installerSourceCommit = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        archiveSha256 = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
        publisherEvidenceLink = 'evidence://campaign/release/publisher'
        portalApiBuild = 'staging-build-20260727'
        customerEnvironmentAlias = 'cloudboss-sandbox'
        workstationAlias = 'clean-win11-01'
        windowsBuild = 'Windows-11-24H2-test'
        evidenceRootLink = 'evidence://campaign/root'
    }
    $completed.entryGates = @($policy.requiredEntryGateIds | ForEach-Object { New-PassCheck $_ 'entry-gates' })

    $hashCharacters = @('1', '2', '3', '4')
    $exportIds = @(
        '11111111-1111-4111-8111-111111111111',
        '22222222-2222-4222-8222-222222222222',
        '33333333-3333-4333-8333-333333333333',
        '44444444-4444-4444-8444-444444444444'
    )
    $completed.packages = @(
        for ($index = 0; $index -lt $policy.requiredPackageIds.Count; $index++) {
            $id = [string] $policy.requiredPackageIds[$index]
            [pscustomobject]@{
                id = $id
                packageHash = "sha256:$($hashCharacters[$index] * 64)"
                sessionAlias = "session-$($index + 1)"
                deploymentExportId = $exportIds[$index]
                resourceGroup = "rg-pm365-accept-$($index + 1)"
                keyVault = "kvpm365accept$($index + 1)"
                runtimeVersion = '1.0.0'
                portalReadiness = 'Ready'
                producerRef = 'https://github.com/cloudbossdev/pagemaker365/issues/1'
                evidenceLink = "evidence://campaign/packages/$id"
            }
        }
    )

    $completed.scenarioResults = @(
        $policy.requiredSingleRunScenarioIds | ForEach-Object { New-PassScenario $_ 'baseline' }
        foreach ($segment in $policy.requiredCycleSegments) {
            $policy.requiredCycleScenarioIds | ForEach-Object { New-PassScenario $_ $segment }
        }
    )
    $completed.reconciliation = @($policy.requiredReconciliationIds | ForEach-Object { New-PassCheck $_ 'reconciliation' })
    $completed.securityChecks = @($policy.requiredSecurityCheckIds | ForEach-Object { New-PassCheck $_ 'security' })
    $completed.deviations = @()
    $completed.approvals = @(
        $policy.requiredApprovalIds | ForEach-Object {
            [pscustomobject]@{
                id = $_
                decision = 'Approved'
                approver = "approver-$_"
                utcDecidedAt = '2026-07-27T19:00:00Z'
                evidenceLink = "evidence://campaign/approvals/$_"
            }
        }
    )

    Write-Candidate $completed
    $approvedResult = & $validatorPath -Path $tempPath -RequireApproval
    if (-not $approvedResult.approved -or
        $approvedResult.canonicalScenarios -ne 116 -or
        $approvedResult.requiredSingleRunScenarios -ne 84 -or
        $approvedResult.requiredCycleScenarios -ne 36 -or
        $approvedResult.requiredCycleCount -ne 3 -or
        $approvedResult.requiredScenarioExecutions -ne 192 -or
        $approvedResult.recordedScenarioExecutions -ne 192) {
        throw 'Completed lifecycle result returned incorrect approval coverage counts.'
    }

    $candidate = Copy-Result $completed
    $candidate.scenarioResults = @($candidate.scenarioResults | Where-Object {
        -not ($_.segment -eq 'baseline' -and $_.scenarioId -eq 'W01')
    })
    Assert-ValidationFails -Candidate $candidate -ExpectedPattern 'do not have a passing execution.*W01' -Scenario 'Missing required scenario'

    $candidate = Copy-Result $completed
    $candidate.scenarioResults += Copy-Result $candidate.scenarioResults[0]
    Assert-ValidationFails -Candidate $candidate -ExpectedPattern 'duplicate scenario executions' -Scenario 'Duplicate scenario execution'

    $candidate = Copy-Result $completed
    $candidate.scenarioResults[0].scenarioId = 'Z99'
    Assert-ValidationFails -Candidate $candidate -ExpectedPattern 'unknown scenarios.*Z99' -Scenario 'Unknown scenario'

    $candidate = Copy-Result $completed
    $candidate.entryGates[0].result = 'Blocked'
    Assert-ValidationFails -Candidate $candidate -ExpectedPattern 'Entry gate.*is not Pass' -Scenario 'Blocked entry gate'

    $candidate = Copy-Result $completed
    $candidate.scenarioResults[0].evidenceLink = ''
    Assert-ValidationFails -Candidate $candidate -ExpectedPattern 'evidenceLink.*required' -Scenario 'Missing scenario evidence'

    $candidate = Copy-Result $completed
    $candidate.approvals[0].decision = 'NotApproved'
    Assert-ValidationFails -Candidate $candidate -ExpectedPattern 'Approval.*is not Approved' -Scenario 'Missing approval'

    $candidate = Copy-Result $completed
    $candidate.packages[1].keyVault = $candidate.packages[0].keyVault
    Assert-ValidationFails -Candidate $candidate -ExpectedPattern 'Key Vault names must be unique' -Scenario 'Reused Key Vault'

    $candidate = Copy-Result $completed
    $candidate.packages[0].portalReadiness = 'Generating'
    Assert-ValidationFails -Candidate $candidate -ExpectedPattern 'portalReadiness is not Ready' -Scenario 'Package not ready'

    $candidate = Copy-Result $completed
    $candidate.securityChecks[0].reviewer = 'C:\Users\operator\review.txt'
    Assert-ValidationFails -Candidate $candidate -ExpectedPattern 'prohibited secret-like or local-path' -Scenario 'Local path disclosure'

    $candidate = Copy-Result $completed
    $candidate.scenarioResults[0].result = 'Fail'
    Assert-ValidationFails -Candidate $candidate -ExpectedPattern 'deviationIssue.*required' -Scenario 'Undocumented failed scenario'

    $candidate = Copy-Result $completed
    $candidate.scenarioResults[0].result = 'Fail'
    $candidate.scenarioResults[0].deviationIssue = 'https://github.com/cloudbossdev/pagemaker365-installer/issues/10'
    $candidate.scenarioResults += New-PassScenario $candidate.scenarioResults[0].scenarioId 'negative'
    Assert-ValidationFails -Candidate $candidate -ExpectedPattern 'matching deviation decision and issue' -Scenario 'Failed scenario without deviation decision'
}
finally {
    Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
}

Write-Host 'Customer lifecycle result contract tests passed: planned template plus 192 required approved executions and negative gates.'
