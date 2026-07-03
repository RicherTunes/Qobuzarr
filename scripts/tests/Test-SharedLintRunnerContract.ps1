param()

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$workflowPath = Join-Path $repoRoot '.gitea\workflows\ci.yml'
$githubWorkflowPath = Join-Path $repoRoot '.github\workflows\ci.yml'
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
Assert-Condition (Test-Path -LiteralPath $githubWorkflowPath) "Missing GitHub CI mirror workflow: $githubWorkflowPath"

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

if (Test-Path -LiteralPath $githubWorkflowPath) {
    $githubContent = Get-Content -LiteralPath $githubWorkflowPath -Raw
    $githubNonCommentContent = ((Get-Content -LiteralPath $githubWorkflowPath | Where-Object {
        -not $_.TrimStart().StartsWith('#')
    }) -join "`n")
    $githubOnlyGuard = "if: `${{ github.server_url == 'https://github.com' }}"

    Assert-Condition ($githubContent.Contains($githubOnlyGuard)) `
        'GitHub CI mirror jobs must be guarded to run only on github.com.'
    Assert-Condition (([regex]::Matches($githubContent, [regex]::Escape($githubOnlyGuard))).Count -ge 3) `
        'GitHub CI mirror must guard secret-scan, lint, and verify jobs.'
    Assert-Condition ($githubNonCommentContent -match 'run-plugin-lint-gates\.ps1') `
        'GitHub CI mirror lint job must use the shared Common lint runner.'
    Assert-Condition ($githubNonCommentContent -match 'repin-common-submodule\.sh\s+--verify-only') `
        'GitHub CI mirror must include the Common submodule pin guard.'
    Assert-Condition ($githubNonCommentContent -match 'gitleaks\s+detect') `
        'GitHub CI mirror must include the secret-scan gate.'
    Assert-Condition ($githubNonCommentContent -match 'scripts[/\\]verify-local\.ps1') `
        'GitHub CI mirror must invoke scripts/verify-local.ps1.'
    Assert-Condition ($githubNonCommentContent -notmatch $runnerOwnedScriptPattern) `
        'GitHub CI mirror must not call runner-owned Common lint scripts directly.'
    Assert-Condition ($githubNonCommentContent -notmatch $runnerSkipSwitchPattern) `
        'GitHub CI mirror must not pass skip switches to the shared Common lint runner.'
    Assert-Condition ($githubContent -notmatch 'Invoke-FallbackGate') `
        'GitHub CI mirror must not keep fallback lint gate helpers that can drift from Common.'
}

if ($failures.Count -gt 0) {
    Write-Host 'FAIL: Shared lint runner contract'
    foreach ($failure in $failures) {
        Write-Host " - $failure"
    }
    exit 1
}

Write-Host 'PASS: Shared lint runner contract'
