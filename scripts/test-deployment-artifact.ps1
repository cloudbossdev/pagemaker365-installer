[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $repoRoot 'modules\PageMaker365.Install\PageMaker365.Install.psd1'
$module = Import-Module $modulePath -Force -PassThru

Import-Module Az.Resources -ErrorAction Stop
$jsonObject = [Newtonsoft.Json.Linq.JObject]::Parse('{"product":"PageMaker365","environment":"sandbox"}')
$sensitiveValue = [Newtonsoft.Json.Linq.JValue]::CreateString('secret-value')
$outputs = [ordered]@{
    tags = [pscustomobject]@{
        Type = 'Object'
        Value = $jsonObject
    }
    clientSecret = [pscustomobject]@{
        Type = 'String'
        Value = $sensitiveValue
    }
}

$safeOutputs = & $module {
    param($value)
    Get-PM365SafeDeploymentOutputs -Outputs $value
} $outputs

if ($safeOutputs.outputCount -ne 2) {
    throw "Expected two deployment outputs, got $($safeOutputs.outputCount)."
}

if ($safeOutputs.includedOutputCount -ne 1 -or $safeOutputs.redactedOutputCount -ne 1) {
    throw 'Deployment output redaction counts were incorrect.'
}

if ($safeOutputs.outputs.tags.value.product -ne 'PageMaker365') {
    throw 'Newtonsoft JObject deployment output was not converted to a JSON-safe object.'
}

if ($safeOutputs.outputs.Contains('clientSecret')) {
    throw 'Sensitive deployment output was not removed.'
}

Write-Host 'Deployment artifact contract tests passed.'
