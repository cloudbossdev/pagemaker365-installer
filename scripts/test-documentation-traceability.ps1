[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$storyPath = Join-Path $repoRoot 'docs/install-uninstall-user-stories.md'
$scenarioPath = Join-Path $repoRoot 'docs/install-uninstall-test-matrix.md'
$traceabilityPath = Join-Path $repoRoot 'docs/installer-requirements-traceability.md'
$documentationPlanPath = Join-Path $repoRoot 'docs/customer/customer-documentation-delivery-plan.md'
$customerDraftPaths = @(
    (Join-Path $repoRoot 'docs/customer/installer-user-guide.md'),
    (Join-Path $repoRoot 'docs/customer/installer-technical-security-guide.md')
)

foreach ($path in @($storyPath, $scenarioPath, $traceabilityPath, $documentationPlanPath) + $customerDraftPaths) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required customer-readiness document is missing: $path"
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
