param()

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$workflowPath = Join-Path $repoRoot '.gitea\workflows\ci.yml'
$failures = New-Object System.Collections.Generic.List[string]
$runnerOwnedScriptPattern = '(ecosystem-parity-lint|lint-date-parsing|lint-sync-over-async|lint-test-traits|lint-doc-script-refs|lint-gitea-secret-scan)\.ps1'
$runnerSkipSwitchPattern = '-(SkipDateParsing|SkipSyncOverAsync|SkipTestTraits|SkipEcosystemParity|SkipVersionContract|SkipPluginContractTests|SkipDocRefs|SkipGiteaSecretScan)\b'

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        $script:failures.Add($Message)
    }
}

Assert-Condition (Test-Path -LiteralPath $workflowPath) "Missing Gitea CI workflow: $workflowPath"

if ($failures.Count -eq 0) {
    $content = ((Get-Content -LiteralPath $workflowPath | Where-Object {
        -not $_.TrimStart().StartsWith('#')
    }) -join "`n")

    Assert-Condition ($content -match 'run-plugin-lint-gates\.ps1') `
        'Gitea lint job must invoke Common run-plugin-lint-gates.ps1.'
    Assert-Condition ($content -match '-RepoPath\s+\.') `
        'Gitea lint job must run the shared runner against the plugin repo.'
    Assert-Condition ($content -match '-Mode\s+ci') `
        'Gitea lint job must run the shared runner in CI mode.'
    Assert-Condition ($content -notmatch $runnerOwnedScriptPattern) `
        'Gitea lint job must not call runner-owned Common lint scripts directly.'
    Assert-Condition ($content -notmatch $runnerSkipSwitchPattern) `
        'Gitea lint job must not pass skip switches to the shared Common lint runner.'
    Assert-Condition ($content -notmatch 'Invoke-FallbackGate') `
        'Gitea lint job must not keep fallback lint gate helpers that can drift from Common.'

    $lintIdx = $content.IndexOf('run-plugin-lint-gates.ps1')
    $verifyIdx = $content.IndexOf("`n  verify:")
    Assert-Condition ($lintIdx -ge 0) 'Shared lint runner step must exist.'
    Assert-Condition ($verifyIdx -ge 0) 'Verify job must exist.'
    if ($lintIdx -ge 0 -and $verifyIdx -ge 0) {
        Assert-Condition ($lintIdx -lt $verifyIdx) 'Shared lint runner must appear before the verify job.'
    }
}

if ($failures.Count -gt 0) {
    Write-Host 'FAIL: Shared lint runner contract'
    foreach ($failure in $failures) {
        Write-Host " - $failure"
    }
    exit 1
}

Write-Host 'PASS: Shared lint runner contract'
