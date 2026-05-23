#Requires -Modules Pester

<#
.SYNOPSIS
    Asserts that analyzer-disable flags are absent from PRODUCTION build steps in CI.

.DESCRIPTION
    Phase 3.2 update: The blanket analyzer-disable flags have been removed from
    production build steps in .github/workflows/ci.yml. Test/pre-build steps
    intentionally retain -p:RunAnalyzersDuringBuild=false and -p:EnableNETAnalyzers=false
    for build speed — those steps do not produce the plugin artifact and are excluded
    from the analyzer gate by design.

    The assertions below verify:
    1. The PRODUCTION "Restore and build" steps no longer carry RunAnalyzersDuringBuild=false
       or EnableNETAnalyzers=false (analyzers are now enabled).
    2. TreatWarningsAsErrors=false remains on production builds (baseline is 29 warnings —
       below 50 threshold, deferred to Phase 4 to zero-out all remaining warnings).
    3. A warning-count gate step IS present to prevent net-new warnings.

    Phase history:
    - Phase 1 baseline: 863 warnings. Flags added to unblock CI.
    - Phase 3.2: 29 warnings remain. Production builds now run analyzers.
      Remaining 29 are in in-flight files (TokenRefresher, HybridMLQueryOptimizer,
      AuthTokenManager, QobuzDownloadClient) owned by ZaiCoding/AuthGate workstream.

    Owner: RicherTunes
    See: docs/ANALYZER_BASELINE.md, docs/PHASE1_TRACKING_REDS.md
#>

BeforeAll {
    $script:RepoRoot    = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
    $script:CiYml       = Join-Path $script:RepoRoot '.github' 'workflows' 'ci.yml'
    $script:CiContent   = if (Test-Path $script:CiYml) {
        Get-Content $script:CiYml -Raw
    } else { '' }

    # Extract only lines that are part of production "Restore and build" steps.
    # We identify these by extracting content between "name: Restore and build"
    # and the next step boundary ("    - name:").
    # This excludes test pre-build steps which intentionally keep analyzers disabled.
    $script:ProductionBuildLines = if ($script:CiContent) {
        $lines = $script:CiContent -split "`n"
        $inProductionBuild = $false
        $productionLines = @()
        foreach ($line in $lines) {
            if ($line -match "name:\s*Restore and build") {
                $inProductionBuild = $true
            }
            elseif ($inProductionBuild -and $line -match "^\s{4}-\s+name:") {
                $inProductionBuild = $false
            }
            if ($inProductionBuild) {
                $productionLines += $line
            }
        }
        $productionLines -join "`n"
    } else { '' }
}

# Phase 3.2: Un-skipped. Production build steps no longer carry blanket analyzer-disable flags.
# Test pre-build steps retain the flags for speed (excluded from assertions below).
$script:SkipTrackingReds = $false

Describe 'CI Analyzer flags — production build steps (Phase 3.2)' {

    It 'RunAnalyzersDuringBuild=false is not in production build steps' -Skip:$script:SkipTrackingReds {
        # Phase 3.2: Analyzers enabled on production builds. Warning count gate added.
        # Remaining flags exist only in test pre-build steps (intentional — speed optimisation).
        $script:ProductionBuildLines | Should -Not -Match 'RunAnalyzersDuringBuild\s*=\s*false'
    }

    It 'EnableNETAnalyzers=false is not in production build steps' -Skip:$script:SkipTrackingReds {
        # Phase 3.2: Analyzers enabled on production builds.
        $script:ProductionBuildLines | Should -Not -Match 'EnableNETAnalyzers\s*=\s*false'
    }

    It 'Warning count gate step is present in CI workflow' -Skip:$script:SkipTrackingReds {
        # Phase 3.2: A warning-count gate was added to prevent net-new warnings.
        # The gate reads qobuzarr-warning-baseline.txt and fails if current > baseline.
        $script:CiContent | Should -Match 'qobuzarr-warning-baseline\.txt'
    }

    It 'Security-critical warnaserror flag is present for CA2012' -Skip:$script:SkipTrackingReds {
        # Phase 3.2: CA2012 (ValueTask misuse) promoted to error on production builds.
        $script:ProductionBuildLines | Should -Match '-warnaserror:CA2012'
    }
}

Describe 'CI Analyzer flags — DEFERRED to Phase 4' {

    It '[DEFERRED Phase 4] TreatWarningsAsErrors=false is not present in any workflow' -Skip {
        # DEFERRED: 29 warnings remain in in-flight files owned by ZaiCoding/AuthGate.
        # Un-skip in Phase 4 once those files are resolved and baseline reaches 0.
        # See docs/ANALYZER_BASELINE.md for the remaining warning list.
        $script:CiContent | Should -Not -Match 'TreatWarningsAsErrors\s*=\s*false'
    }

    It '[DEFERRED Phase 4] RunAnalyzersDuringBuild=false is not in any workflow step' -Skip {
        # DEFERRED: Test pre-build steps retain this flag intentionally for speed.
        # Remove when test projects are updated to run analyzers too.
        $script:CiContent | Should -Not -Match 'RunAnalyzersDuringBuild\s*=\s*false'
    }
}
