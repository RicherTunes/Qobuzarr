#Requires -Modules Pester

<#
.SYNOPSIS
    TRACKING RED — Asserts that the analyzer-disable flags are eventually removed from CI.

.DESCRIPTION
    This test stays RED (skipped) until Phase 1.3 removes the following flags from all
    jobs in .github/workflows/ci.yml (and other affected workflows):

        -p:RunAnalyzersDuringBuild=false
        -p:EnableNETAnalyzers=false
        -p:TreatWarningsAsErrors=false

    Context: 863 analyzer warnings exist at Phase 1 baseline (see docs/ANALYZER_BASELINE.md).
    CI analyzer flags will be removed once warnings are reduced below 50.

    Owner: RicherTunes
    Removal condition: All three flags removed from production CI jobs.
    See: docs/ANALYZER_BASELINE.md, docs/PHASE1_TRACKING_REDS.md
#>

BeforeAll {
    $script:RepoRoot    = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
    $script:WorkflowDir = Join-Path $script:RepoRoot '.github' 'workflows'

    $script:WorkflowFiles = if (Test-Path $script:WorkflowDir) {
        Get-ChildItem -Path $script:WorkflowDir -Filter '*.yml' -Recurse
    } else { @() }

    $script:CombinedContent = if ($script:WorkflowFiles.Count -gt 0) {
        ($script:WorkflowFiles | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    } else { '' }
}

# TRACKING RED: all three tests in this block are intentionally skipped.
# Set $script:SkipTracking = $false and remove -Skip to activate them in Phase 1.3.
$script:SkipTrackingReds = $true

Describe 'CI Analyzer flags — TRACKING RED (phase-1.3 removal target)' {

    It '[TRACKING RED] RunAnalyzersDuringBuild=false is not present in any workflow' -Skip:$script:SkipTrackingReds {
        # TRACKING RED — 863 baseline warnings. Unskip after Phase 1.1+1.2 reduce count below 50.
        # See docs/ANALYZER_BASELINE.md
        $script:CombinedContent | Should -Not -Match 'RunAnalyzersDuringBuild\s*=\s*false'
    }

    It '[TRACKING RED] EnableNETAnalyzers=false is not present in any workflow' -Skip:$script:SkipTrackingReds {
        # TRACKING RED — 863 baseline warnings. Unskip after Phase 1.1+1.2 reduce count below 50.
        # See docs/ANALYZER_BASELINE.md
        $script:CombinedContent | Should -Not -Match 'EnableNETAnalyzers\s*=\s*false'
    }

    It '[TRACKING RED] TreatWarningsAsErrors=false is not present in any workflow' -Skip:$script:SkipTrackingReds {
        # TRACKING RED — 863 baseline warnings. Unskip after Phase 1.3 adds NoWarn suppressions.
        # See docs/ANALYZER_BASELINE.md
        $script:CombinedContent | Should -Not -Match 'TreatWarningsAsErrors\s*=\s*false'
    }
}
