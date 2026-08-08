[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$verifyPath = Join-Path $PSScriptRoot 'verify.ps1'
$tokens = $null
$parseErrors = $null
$verifyAst = [System.Management.Automation.Language.Parser]::ParseFile(
    $verifyPath,
    [ref] $tokens,
    [ref] $parseErrors
)

if ($parseErrors.Count -gt 0) {
    throw "verify.ps1 has a parse error: $($parseErrors[0].Message)"
}

function Assert-True {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Test-AstContains {
    param(
        [System.Management.Automation.Language.Ast] $Container,
        [System.Management.Automation.Language.Ast] $Candidate
    )

    return $Candidate.Extent.StartOffset -ge $Container.Extent.StartOffset -and
        $Candidate.Extent.EndOffset -le $Container.Extent.EndOffset
}

function Get-PositiveLiveGateClauses {
    param([System.Management.Automation.Language.ScriptBlockAst] $ScriptAst)

    return @($ScriptAst.FindAll({
        param($node)
        if ($node -isnot [System.Management.Automation.Language.IfStatementAst]) {
            return $false
        }

        return @($node.Clauses | Where-Object {
            $_.Item1.Extent.Text.Trim() -ceq '$IncludeLiveCloudChecks'
        }).Count -gt 0
    }, $true))
}

function Assert-MockedContractSuiteInvocations {
    param([System.Management.Automation.Language.ScriptBlockAst] $ScriptAst)

    $positiveGateClauses = @(Get-PositiveLiveGateClauses -ScriptAst $ScriptAst)
    $contractScripts = @(
        'test-discovery.ps1',
        'test-authentication-cancellation.ps1',
        'test-whatif.ps1',
        'test-preflight-blocker-policy.ps1',
        'test-azure-platform-readiness.ps1'
    )

    foreach ($scriptName in $contractScripts) {
        $relativeScriptPath = "scripts\$scriptName"
        $scriptInvocations = @($ScriptAst.FindAll({
            param($node)
            if ($node -isnot [System.Management.Automation.Language.CommandAst] -or
                $node.InvocationOperator -ne [System.Management.Automation.Language.TokenKind]::Ampersand -or
                $node.CommandElements.Count -ne 1 -or
                $node.CommandElements[0] -isnot [System.Management.Automation.Language.ParenExpressionAst]) {
                return $false
            }

            $joinPathCommands = @($node.CommandElements[0].FindAll({
                param($child)
                $child -is [System.Management.Automation.Language.CommandAst] -and
                $child.GetCommandName() -ieq 'Join-Path' -and
                $child.CommandElements.Count -eq 3 -and
                $child.CommandElements[1] -is [System.Management.Automation.Language.VariableExpressionAst] -and
                $child.CommandElements[1].VariablePath.UserPath -ieq 'repoRoot' -and
                $child.CommandElements[2] -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
                $child.CommandElements[2].Value -ceq $relativeScriptPath
            }, $true))
            return $joinPathCommands.Count -eq 1
        }, $true))
        Assert-True ($scriptInvocations.Count -eq 1) "Default verification must invoke the noninteractive $scriptName contract suite exactly once."

        $isLiveGated = $false
        foreach ($ifAst in $positiveGateClauses) {
            foreach ($clause in $ifAst.Clauses) {
                if ($clause.Item1.Extent.Text.Trim() -ceq '$IncludeLiveCloudChecks' -and
                    (Test-AstContains -Container $clause.Item2 -Candidate $scriptInvocations[0])) {
                    $isLiveGated = $true
                }
            }
        }

        Assert-True (-not $isLiveGated) "The noninteractive $scriptName contract suite must run outside IncludeLiveCloudChecks."
    }
}

$gateParameter = @($verifyAst.ParamBlock.Parameters | Where-Object {
    $_.Name.VariablePath.UserPath -ieq 'IncludeLiveCloudChecks'
})
Assert-True ($gateParameter.Count -eq 1) 'verify.ps1 must expose exactly one IncludeLiveCloudChecks parameter.'

$switchTypes = @($gateParameter[0].Attributes | Where-Object {
    $_ -is [System.Management.Automation.Language.TypeConstraintAst] -and
    $_.TypeName.FullName -in @('switch', 'System.Management.Automation.SwitchParameter')
})
Assert-True ($switchTypes.Count -eq 1) 'IncludeLiveCloudChecks must be a switch parameter.'
Assert-True ($null -eq $gateParameter[0].DefaultValue) 'IncludeLiveCloudChecks must default to false by having no default value.'

$liveCommandNames = @(
    'Get-PM365AzureDiscovery',
    'Get-PM365GraphDiscovery',
    'Start-PM365Preflight',
    'Invoke-PM365WhatIf'
)

$gateClauses = @(Get-PositiveLiveGateClauses -ScriptAst $verifyAst)
Assert-True ($gateClauses.Count -gt 0) 'verify.ps1 must contain a positive IncludeLiveCloudChecks gate.'

foreach ($commandName in $liveCommandNames) {
    $commands = @($verifyAst.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -ieq $commandName
    }, $true))
    Assert-True ($commands.Count -eq 1) "verify.ps1 must contain exactly one live $commandName call."

    $isPositivelyGated = $false
    foreach ($ifAst in $gateClauses) {
        foreach ($clause in $ifAst.Clauses) {
            if ($clause.Item1.Extent.Text.Trim() -ceq '$IncludeLiveCloudChecks' -and
                (Test-AstContains -Container $clause.Item2 -Candidate $commands[0])) {
                $isPositivelyGated = $true
            }
        }
    }

    Assert-True $isPositivelyGated "$commandName must be inside the positive IncludeLiveCloudChecks branch."
}

$forbiddenDefaultCommands = @(
    'Connect-AzAccount',
    'Connect-MgGraph',
    'Invoke-AzRestMethod',
    'Invoke-MgGraphRequest',
    'Invoke-RestMethod',
    'Invoke-WebRequest',
    'az',
    'azd'
)
foreach ($commandName in $forbiddenDefaultCommands) {
    $directCalls = @($verifyAst.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -ieq $commandName
    }, $true))
    Assert-True ($directCalls.Count -eq 0) "verify.ps1 must not invoke $commandName directly."
}

Assert-MockedContractSuiteInvocations -ScriptAst $verifyAst

$verifySource = Get-Content -LiteralPath $verifyPath -Raw
$discoveryInvocation = "& (Join-Path `$repoRoot 'scripts\test-discovery.ps1')"
Assert-True $verifySource.Contains($discoveryInvocation) 'The adversarial fixtures could not locate the discovery contract invocation.'

$deadStringSource = $verifySource.Replace(
    $discoveryInvocation,
    "`$deadDiscoveryReference = 'scripts\test-discovery.ps1'"
)
$fixtureTokens = $null
$fixtureErrors = $null
$deadStringAst = [System.Management.Automation.Language.Parser]::ParseInput(
    $deadStringSource,
    [ref] $fixtureTokens,
    [ref] $fixtureErrors
)
Assert-True ($fixtureErrors.Count -eq 0) 'The dead-string adversarial fixture did not parse.'
$deadStringRejected = $false
try {
    Assert-MockedContractSuiteInvocations -ScriptAst $deadStringAst
}
catch {
    $deadStringRejected = $true
}
Assert-True $deadStringRejected 'A dead suite-path string must not satisfy the mocked-suite invocation contract.'

$gatedMockSource = $verifySource.Replace(
    $discoveryInvocation,
    "if (`$IncludeLiveCloudChecks) { $discoveryInvocation }"
)
$fixtureTokens = $null
$fixtureErrors = $null
$gatedMockAst = [System.Management.Automation.Language.Parser]::ParseInput(
    $gatedMockSource,
    [ref] $fixtureTokens,
    [ref] $fixtureErrors
)
Assert-True ($fixtureErrors.Count -eq 0) 'The live-gated mock adversarial fixture did not parse.'
$gatedMockRejected = $false
try {
    Assert-MockedContractSuiteInvocations -ScriptAst $gatedMockAst
}
catch {
    $gatedMockRejected = $true
}
Assert-True $gatedMockRejected 'A mocked suite invocation inside IncludeLiveCloudChecks must not satisfy the default contract.'

Write-Host 'Noninteractive verification gate tests passed.'
