[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Path,

    [switch] $RequireApproval
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$schemaPath = Join-Path $repoRoot 'docs\testing\schemas\customer-lifecycle-result.schema.json'
$policyPath = Join-Path $repoRoot 'config\customer-lifecycle-acceptance.json'
$matrixPath = Join-Path $repoRoot 'docs\install-uninstall-test-matrix.md'
$Path = (Resolve-Path -LiteralPath $Path).Path

function Assert-ExactIdentifiers {
    param(
        [Parameter(Mandatory)]
        [object[]] $Items,

        [Parameter(Mandatory)]
        [string[]] $Expected,

        [Parameter(Mandatory)]
        [string] $Label
    )

    $actual = @($Items | ForEach-Object { [string] $_.id })
    $duplicates = @($actual | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
    if ($duplicates.Count -gt 0) {
        throw "$Label contains duplicate identifiers: $($duplicates -join ', ')."
    }

    $missing = @($Expected | Where-Object { $actual -notcontains $_ })
    $unexpected = @($actual | Where-Object { $Expected -notcontains $_ })
    if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
        throw "$Label identifiers do not match policy. Missing: $($missing -join ', '); unexpected: $($unexpected -join ', ')."
    }
}

function Assert-NonEmpty {
    param(
        [AllowNull()]
        [object] $Value,

        [Parameter(Mandatory)]
        [string] $Label
    )

    if ([string]::IsNullOrWhiteSpace([string] $Value)) {
        throw "$Label is required for an approved lifecycle result."
    }
}

function Assert-UtcRange {
    param(
        [string] $StartedAt,
        [string] $CompletedAt,
        [string] $Label
    )

    $started = [DateTimeOffset]::MinValue
    $completed = [DateTimeOffset]::MinValue
    $startedParsed = [DateTimeOffset]::TryParse($StartedAt, [ref] $started)
    $completedParsed = [DateTimeOffset]::TryParse($CompletedAt, [ref] $completed)
    if (-not $startedParsed -or -not $StartedAt.EndsWith('Z', [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label start time must be an ISO-8601 UTC timestamp. Received '$StartedAt'."
    }
    if (-not $completedParsed -or -not $CompletedAt.EndsWith('Z', [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label completion time must be an ISO-8601 UTC timestamp. Received '$CompletedAt'."
    }
    if ($completed -lt $started) {
        throw "$Label completion time precedes its start time."
    }
}

function Assert-PassingChecks {
    param(
        [object[]] $Checks,
        [string[]] $ExpectedIds,
        [string] $Label
    )

    Assert-ExactIdentifiers -Items $Checks -Expected $ExpectedIds -Label $Label
    foreach ($check in $Checks) {
        if ([string] $check.result -ne 'Pass') {
            throw "$Label '$($check.id)' is not Pass."
        }
        Assert-NonEmpty -Value $check.evidenceLink -Label "$Label '$($check.id)' evidenceLink"
        Assert-NonEmpty -Value $check.reviewer -Label "$Label '$($check.id)' reviewer"
    }
}

function Assert-UniqueValues {
    param(
        [string[]] $Values,
        [string] $Label
    )

    $duplicates = @($Values | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
    if ($duplicates.Count -gt 0) {
        throw "$Label must be unique across the campaign: $($duplicates -join ', ')."
    }
}

try {
    if (-not (Test-Json -LiteralPath $Path -SchemaFile $schemaPath)) {
        throw 'The lifecycle result does not match the JSON schema.'
    }
}
catch {
    throw "Lifecycle result schema validation failed: $($_.Exception.Message)"
}

$raw = Get-Content -LiteralPath $Path -Raw
$prohibitedPatterns = @(
    '(?i)(?<![A-Za-z0-9])(?:[A-Z]:\\|\\\\[^\\\s]+\\)',
    '(?i)(?<![A-Za-z0-9])/(?:home|Users)/[^\s"'']+',
    '(?i)\bBearer\s+[A-Za-z0-9._~-]{8,}',
    '(?i)\b(?:password|client[_-]?secret|access[_-]?token|one[_-]?time[_-]?code|connection[_-]?string)\s*[:=]\s*[^\s,"'']{4,}',
    '\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b'
)
foreach ($pattern in $prohibitedPatterns) {
    if ($raw -match $pattern) {
        throw 'Lifecycle result contains prohibited secret-like or local-path content.'
    }
}

$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json -DateKind String
$result = $raw | ConvertFrom-Json -DateKind String
if ([string] $result.contractVersion -ne [string] $policy.contractVersion) {
    throw 'Lifecycle result contract version does not match the acceptance policy.'
}

$matrixText = Get-Content -LiteralPath $matrixPath -Raw
$canonicalScenarioIds = @(
    [regex]::Matches($matrixText, '(?m)^\| ([A-Z][0-9]{2}) \|') |
        ForEach-Object { $_.Groups[1].Value }
)
if ($canonicalScenarioIds.Count -ne 116 -or ($canonicalScenarioIds | Select-Object -Unique).Count -ne 116) {
    throw 'Canonical lifecycle matrix must contain 116 unique scenarios before results can be validated.'
}

$policyScenarioIds = @($policy.requiredSingleRunScenarioIds) + @($policy.requiredCycleScenarioIds)
$unknownPolicyIds = @($policyScenarioIds | Where-Object { $canonicalScenarioIds -notcontains $_ } | Select-Object -Unique)
if ($unknownPolicyIds.Count -gt 0) {
    throw "Lifecycle acceptance policy references unknown scenarios: $($unknownPolicyIds -join ', ')."
}

$scenarioKeys = @($result.scenarioResults | ForEach-Object { "$($_.segment)|$($_.scenarioId)" })
$duplicateScenarioKeys = @($scenarioKeys | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
if ($duplicateScenarioKeys.Count -gt 0) {
    throw "Lifecycle result contains duplicate scenario executions: $($duplicateScenarioKeys -join ', ')."
}

$unknownResultIds = @(
    $result.scenarioResults |
        Where-Object { $canonicalScenarioIds -notcontains [string] $_.scenarioId } |
        ForEach-Object scenarioId |
        Select-Object -Unique
)
if ($unknownResultIds.Count -gt 0) {
    throw "Lifecycle result references unknown scenarios: $($unknownResultIds -join ', ')."
}

if (-not $RequireApproval) {
    [pscustomobject]@{
        result = 'Valid'
        status = $result.status
        approved = $false
        scenarioExecutions = @($result.scenarioResults).Count
    }
    return
}

if ([string] $result.status -ne 'Completed' -or [string] $result.finalDecision -ne 'Approved') {
    throw 'Lifecycle result must be Completed with finalDecision Approved.'
}

Assert-NonEmpty -Value $result.run.releaseVersion -Label 'run.releaseVersion'
Assert-NonEmpty -Value $result.run.installerSourceCommit -Label 'run.installerSourceCommit'
Assert-NonEmpty -Value $result.run.archiveSha256 -Label 'run.archiveSha256'
Assert-NonEmpty -Value $result.run.publisherEvidenceLink -Label 'run.publisherEvidenceLink'
Assert-NonEmpty -Value $result.run.portalApiBuild -Label 'run.portalApiBuild'
Assert-NonEmpty -Value $result.run.customerEnvironmentAlias -Label 'run.customerEnvironmentAlias'
Assert-NonEmpty -Value $result.run.workstationAlias -Label 'run.workstationAlias'
Assert-NonEmpty -Value $result.run.windowsBuild -Label 'run.windowsBuild'
Assert-NonEmpty -Value $result.run.evidenceRootLink -Label 'run.evidenceRootLink'
Assert-UtcRange -StartedAt $result.run.utcStartedAt -CompletedAt $result.run.utcCompletedAt -Label 'run'

Assert-PassingChecks -Checks @($result.entryGates) -ExpectedIds @($policy.requiredEntryGateIds) -Label 'Entry gate'

Assert-ExactIdentifiers -Items @($result.packages) -Expected @($policy.requiredPackageIds) -Label 'Package set'
foreach ($package in $result.packages) {
    foreach ($field in @(
        'packageHash', 'sessionAlias', 'deploymentExportId', 'resourceGroup', 'keyVault',
        'runtimeVersion', 'portalReadiness', 'producerRef', 'evidenceLink'
    )) {
        Assert-NonEmpty -Value $package.$field -Label "Package '$($package.id)' $field"
    }
    if ([string] $package.portalReadiness -ne 'Ready') {
        throw "Package '$($package.id)' portalReadiness is not Ready."
    }
}
Assert-UniqueValues -Values @($result.packages | ForEach-Object packageHash) -Label 'Package hashes'
Assert-UniqueValues -Values @($result.packages | ForEach-Object sessionAlias) -Label 'Package session aliases'
Assert-UniqueValues -Values @($result.packages | ForEach-Object deploymentExportId) -Label 'Deployment export IDs'
Assert-UniqueValues -Values @($result.packages | ForEach-Object resourceGroup) -Label 'Resource groups'
Assert-UniqueValues -Values @($result.packages | ForEach-Object keyVault) -Label 'Key Vault names'

foreach ($execution in $result.scenarioResults) {
    Assert-UtcRange -StartedAt $execution.utcStartedAt -CompletedAt $execution.utcCompletedAt -Label "Scenario $($execution.segment)/$($execution.scenarioId)"
    Assert-NonEmpty -Value $execution.evidenceLink -Label "Scenario $($execution.segment)/$($execution.scenarioId) evidenceLink"
    Assert-NonEmpty -Value $execution.reviewer -Label "Scenario $($execution.segment)/$($execution.scenarioId) reviewer"
    if ([string] $execution.result -ne 'Pass') {
        Assert-NonEmpty -Value $execution.deviationIssue -Label "Scenario $($execution.segment)/$($execution.scenarioId) deviationIssue"
    }
}

$missingSingleRun = @(
    $policy.requiredSingleRunScenarioIds |
        Where-Object {
            $scenarioId = [string] $_
            -not @($result.scenarioResults | Where-Object {
                [string] $_.scenarioId -eq $scenarioId -and [string] $_.result -eq 'Pass'
            }).Count
        }
)
if ($missingSingleRun.Count -gt 0) {
    throw "Required live scenarios do not have a passing execution: $($missingSingleRun -join ', ')."
}

$missingCycleExecutions = [System.Collections.Generic.List[string]]::new()
foreach ($segment in $policy.requiredCycleSegments) {
    foreach ($scenarioId in $policy.requiredCycleScenarioIds) {
        $passed = @($result.scenarioResults | Where-Object {
            [string] $_.segment -eq [string] $segment -and
            [string] $_.scenarioId -eq [string] $scenarioId -and
            [string] $_.result -eq 'Pass'
        }).Count -eq 1
        if (-not $passed) {
            $missingCycleExecutions.Add("$segment/$scenarioId")
        }
    }
}
if ($missingCycleExecutions.Count -gt 0) {
    throw "Required repeated-cycle scenarios do not have a passing execution: $($missingCycleExecutions -join ', ')."
}

Assert-PassingChecks -Checks @($result.reconciliation) -ExpectedIds @($policy.requiredReconciliationIds) -Label 'Reconciliation check'
Assert-PassingChecks -Checks @($result.securityChecks) -ExpectedIds @($policy.requiredSecurityCheckIds) -Label 'Security check'

Assert-ExactIdentifiers -Items @($result.approvals) -Expected @($policy.requiredApprovalIds) -Label 'Approval set'
foreach ($approval in $result.approvals) {
    if ([string] $approval.decision -ne 'Approved') {
        throw "Approval '$($approval.id)' is not Approved."
    }
    Assert-NonEmpty -Value $approval.approver -Label "Approval '$($approval.id)' approver"
    Assert-NonEmpty -Value $approval.utcDecidedAt -Label "Approval '$($approval.id)' utcDecidedAt"
    Assert-NonEmpty -Value $approval.evidenceLink -Label "Approval '$($approval.id)' evidenceLink"
    Assert-UtcRange -StartedAt $approval.utcDecidedAt -CompletedAt $approval.utcDecidedAt -Label "Approval '$($approval.id)' decision"
}

$deviationIds = @($result.deviations | ForEach-Object id)
if (($deviationIds | Select-Object -Unique).Count -ne $deviationIds.Count) {
    throw 'Lifecycle result contains duplicate deviation identifiers.'
}
foreach ($deviation in $result.deviations) {
    if ($canonicalScenarioIds -notcontains [string] $deviation.scenarioId) {
        throw "Deviation '$($deviation.id)' references an unknown scenario."
    }
    foreach ($field in @('observation', 'decision', 'issue', 'decisionOwner')) {
        Assert-NonEmpty -Value $deviation.$field -Label "Deviation '$($deviation.id)' $field"
    }
    if ([string] $deviation.issue -notlike 'https://github.com/*/issues/*') {
        throw "Deviation '$($deviation.id)' issue must reference a GitHub issue."
    }
}

foreach ($execution in @($result.scenarioResults | Where-Object result -ne 'Pass')) {
    if ([string] $execution.deviationIssue -notlike 'https://github.com/*/issues/*') {
        throw "Scenario $($execution.segment)/$($execution.scenarioId) deviationIssue must reference a GitHub issue."
    }
    $matchingDeviation = @($result.deviations | Where-Object {
        [string] $_.scenarioId -eq [string] $execution.scenarioId -and
        [string] $_.issue -eq [string] $execution.deviationIssue
    })
    if ($matchingDeviation.Count -ne 1) {
        throw "Scenario $($execution.segment)/$($execution.scenarioId) must have one matching deviation decision and issue."
    }
}

[pscustomobject]@{
    result = 'Valid'
    status = $result.status
    approved = $true
    canonicalScenarios = $canonicalScenarioIds.Count
    requiredSingleRunScenarios = @($policy.requiredSingleRunScenarioIds).Count
    requiredCycleScenarios = @($policy.requiredCycleScenarioIds).Count
    requiredCycleCount = @($policy.requiredCycleSegments).Count
    requiredScenarioExecutions = @($policy.requiredSingleRunScenarioIds).Count +
        (@($policy.requiredCycleScenarioIds).Count * @($policy.requiredCycleSegments).Count)
    recordedScenarioExecutions = @($result.scenarioResults).Count
}
