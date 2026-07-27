[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$storyPath = Join-Path $repoRoot 'docs/install-uninstall-user-stories.md'
$scenarioPath = Join-Path $repoRoot 'docs/install-uninstall-test-matrix.md'
$traceabilityPath = Join-Path $repoRoot 'docs/installer-requirements-traceability.md'
$removalEvidenceContractPath = Join-Path $repoRoot 'docs/removal-evidence-callback-contract.md'
$documentationPlanPath = Join-Path $repoRoot 'docs/customer/customer-documentation-delivery-plan.md'
$documentationReviewPath = Join-Path $repoRoot 'docs/customer/customer-documentation-review-record.md'
$lifecycleRunbookPath = Join-Path $repoRoot 'docs/testing/customer-lifecycle-acceptance-runbook.md'
$lifecycleResultTemplatePath = Join-Path $repoRoot 'docs/testing/results/customer-lifecycle-result-template.md'
$customerDraftPaths = @(
    (Join-Path $repoRoot 'docs/customer/installer-user-guide.md'),
    (Join-Path $repoRoot 'docs/customer/installer-technical-security-guide.md')
)

foreach ($path in @(
    $storyPath,
    $scenarioPath,
    $traceabilityPath,
    $removalEvidenceContractPath,
    $documentationPlanPath,
    $documentationReviewPath,
    $lifecycleRunbookPath,
    $lifecycleResultTemplatePath) + $customerDraftPaths) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required customer-readiness document is missing: $path"
    }
}

$removalContractText = Get-Content -LiteralPath $removalEvidenceContractPath -Raw
@(
    'removal_started',
    'removal_inventory_completed',
    'removal_execution_completed',
    'removal_validation_completed',
    'removal_completed',
    'removal_blocked',
    'removal_failed',
    'removalAttemptId',
    'RemovalStatusSync',
    'Idempotency-Key'
) | ForEach-Object {
    if ($removalContractText -notmatch [regex]::Escape($_)) {
        throw "Removal evidence callback contract is missing required term: $_"
    }
}

$storyText = Get-Content -LiteralPath $storyPath -Raw
$storyMatches = [regex]::Matches($storyText, '(?m)^## US-(\d{2})\s')
$storyIds = @($storyMatches | ForEach-Object { "US-$($_.Groups[1].Value)" })
$expectedStoryIds = @(1..15 | ForEach-Object { 'US-{0:D2}' -f $_ })

if (($storyIds -join ',') -ne ($expectedStoryIds -join ',')) {
    throw "Canonical story IDs must be exactly US-01 through US-15. Found: $($storyIds -join ', ')"
}

$storySections = [regex]::Split($storyText, '(?m)(?=^## US-\d{2}\s)') |
    Where-Object { $_ -match '^## US-\d{2}\s' }
foreach ($section in $storySections) {
    $storyId = [regex]::Match($section, '^## (US-\d{2})\s').Groups[1].Value
    if ($section -notmatch '(?m)^Acceptance criteria:\s*$') {
        throw "$storyId is missing an Acceptance criteria section."
    }

    $criteriaCount = [regex]::Matches($section, '(?m)^- ').Count
    if ($criteriaCount -lt 5) {
        throw "$storyId must contain at least five acceptance criteria; found $criteriaCount."
    }
}

$scenarioText = Get-Content -LiteralPath $scenarioPath -Raw
$scenarioIds = @(
    [regex]::Matches($scenarioText, '(?m)^\| ([A-Z]\d{2}) \|') |
        ForEach-Object { $_.Groups[1].Value }
)
$duplicateScenarioIds = @(
    $scenarioIds |
        Group-Object |
        Where-Object Count -gt 1 |
        ForEach-Object Name
)
if ($duplicateScenarioIds.Count -gt 0) {
    throw "Scenario IDs must be unique. Duplicates: $($duplicateScenarioIds -join ', ')"
}

$traceabilityText = Get-Content -LiteralPath $traceabilityPath -Raw
foreach ($storyId in $expectedStoryIds) {
    if ($traceabilityText -notmatch "(?m)^\| $([regex]::Escape($storyId))\s") {
        throw "$storyId is missing from the story traceability table."
    }
}

$referencedScenarioIds = @(
    [regex]::Matches($traceabilityText, '\b[A-Z]\d{2}\b') |
        ForEach-Object { $_.Value } |
        Sort-Object -Unique
)
$missingScenarioIds = @($referencedScenarioIds | Where-Object { $_ -notin $scenarioIds })
if ($missingScenarioIds.Count -gt 0) {
    throw "Traceability references undefined scenarios: $($missingScenarioIds -join ', ')"
}

foreach ($path in $customerDraftPaths) {
    $draftText = Get-Content -LiteralPath $path -Raw
    if ($draftText -notmatch '(?im)^Status:\s+controlled draft; not approved for customer publication\s*$') {
        throw "Customer document must retain the controlled-draft publication warning: $path"
    }
}

$userGuideText = Get-Content -LiteralPath $customerDraftPaths[0] -Raw
@(
    '## Before You Begin',
    '## Verify And Start The Installer',
    '## Install PageMaker365',
    '## Recover From A Partial Or Interrupted Install',
    '## Remove PageMaker365 Azure Resources',
    '## Reinstall After Removal',
    '## Evidence And Local Data',
    '## Request Support',
    '## Assistant And Support Handoff',
    '## Publication Gates'
) | ForEach-Object {
    if ($userGuideText -notmatch "(?m)^$([regex]::Escape($_))\s*$") {
        throw "Customer user guide is missing required section: $_"
    }
}

$technicalGuideText = Get-Content -LiteralPath $customerDraftPaths[1] -Raw
@(
    '## Trust Boundaries And Data Flow',
    '## Lifecycle And Mutation Controls',
    '## Operator Identities And Permissions',
    '## Azure Resource Inventory',
    '## Network Requirements',
    '## Cryptographic Trust Layers',
    '## Token And Secret Handling',
    '## Local Storage And Retention',
    '## Evidence, Logging, And Portal Sync',
    '## Assistant And Support Handoff Security',
    '## Removal And Recovery Boundaries',
    '## Troubleshooting And Correlation',
    '## Customer Security Review Checklist',
    '## Known Release Blockers'
) | ForEach-Object {
    if ($technicalGuideText -notmatch "(?m)^$([regex]::Escape($_))\s*$") {
        throw "Customer technical/security guide is missing required section: $_"
    }
}

$lifecycleRunbookText = Get-Content -LiteralPath $lifecycleRunbookPath -Raw
@(
    '## Stop Rules',
    '## Phase 1: Distribution And Clean Workstation',
    '## Phase 2: Clean Install And Finish',
    '## Phase 4: Partial Failure, Cleanup, And Reinstall',
    '## Phase 5: Three Consecutive Lifecycle Cycles',
    '## Phase 7: Security And Evidence Review',
    '## Result Record',
    '## Final Reconciliation And Approval'
) | ForEach-Object {
    if ($lifecycleRunbookText -notmatch "(?m)^$([regex]::Escape($_))\s*$") {
        throw "Customer lifecycle acceptance runbook is missing required section: $_"
    }
}

if ($lifecycleRunbookText -notmatch [regex]::Escape('assistant-support-handoff.md')) {
    throw 'Customer lifecycle acceptance runbook must invoke the assistant support-handoff staging runbook.'
}

if ($lifecycleRunbookText -notmatch [regex]::Escape('results/customer-lifecycle-result-template.md')) {
    throw 'Customer lifecycle acceptance runbook must invoke the sanitized campaign result template.'
}

$lifecycleResultTemplateText = Get-Content -LiteralPath $lifecycleResultTemplatePath -Raw
@(
    'Status: template; no test result or approval recorded',
    '## Run Identity',
    '## Entry Gates',
    '## Package Set',
    '## Phase Results',
    '## Reconciliation',
    '## Security Review',
    '## Deviations And Stop Decisions',
    '## Approval',
    '## Machine Approval Record',
    'Final decision: Not approved'
) | ForEach-Object {
    if ($lifecycleResultTemplateText -notmatch [regex]::Escape($_)) {
        throw "Customer lifecycle result template is missing required control: $_"
    }
}

@(
    'docs/testing/results/customer-lifecycle-result.template.json',
    'validate-customer-lifecycle-result.ps1',
    'config/customer-lifecycle-acceptance.json',
    '-RequireApproval'
) | ForEach-Object {
    if ($lifecycleRunbookText -notmatch [regex]::Escape($_)) {
        throw "Customer lifecycle acceptance runbook is missing machine-approval control: $_"
    }
}

@(
    'docs/testing/results/customer-lifecycle-result.template.json',
    'scripts/validate-customer-lifecycle-result.ps1 -RequireApproval',
    'Minimum required passing executions: 192'
) | ForEach-Object {
    if ($lifecycleResultTemplateText -notmatch [regex]::Escape($_)) {
        throw "Customer lifecycle result template is missing machine-approval control: $_"
    }
}

@(
    'W01', 'W06', 'S03', 'P01', 'A01', 'F01', 'D01', 'D07',
    'L01', 'L02', 'L03', 'L04', 'L05', 'L06', 'L07', 'L08', 'L09',
    'R01', 'R09', 'R13', 'R14', 'R15', 'E06', 'E08', 'T04'
) | ForEach-Object {
    if ($lifecycleRunbookText -notmatch "\b$([regex]::Escape($_))\b") {
        throw "Customer lifecycle acceptance runbook is missing release-critical scenario: $_"
    }
}

$documentationReviewText = Get-Content -LiteralPath $documentationReviewPath -Raw
@(
    'Status: template; no approval recorded',
    '## Release Identity',
    '## Claim Review',
    '## Required Decisions',
    '## Publication Decision',
    'Identity and security',
    'Clean test operator',
    'Lifecycle JSON passes `-RequireApproval` for the exact release'
) | ForEach-Object {
    if ($documentationReviewText -notmatch [regex]::Escape($_)) {
        throw "Customer documentation review template is missing required control: $_"
    }
}

$documentationPlanText = Get-Content -LiteralPath $documentationPlanPath -Raw
foreach ($storyId in $expectedStoryIds) {
    if ($documentationPlanText -notmatch "(?m)^\| $([regex]::Escape($storyId))\s") {
        throw "$storyId is missing from the customer documentation story-coverage table."
    }
}

if ($documentationPlanText -notmatch '(?i)controlled draft' -or
    $documentationPlanText -notmatch '(?m)^## Evidence Gates\s*$') {
    throw 'Customer documentation delivery plan must retain its controlled-draft publication and evidence gates.'
}

Write-Host "Documentation traceability checks passed: $($storyIds.Count) stories, $($scenarioIds.Count) scenarios."
