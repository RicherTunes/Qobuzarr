# Qobuzzarr Unit Test Runner
# This script runs all unit tests and generates coverage reports

param(
    [string]$Configuration = "Debug",
    [switch]$Coverage = $false,
    [switch]$Verbose = $false,
    [switch]$Full = $false,
    [string]$RunSettings = "",
    [string]$Filter = "",
    [switch]$Live = $false,
    # Per-test-host hang timeout. Bounds a stuck/deadlocked test host (including a
    # host that finishes every test but never EXITS — e.g. a coverage-instrumented
    # shutdown deadlock) to a few minutes plus a captured dump, instead of letting it
    # run all the way to <TestSessionTimeout>. The whole suite's real work is <1 min,
    # so 5 min is comfortably above any legitimate single-host run. Override via env.
    [string]$BlameHangTimeout = $(if ($env:QOBUZ_TEST_BLAME_HANG_TIMEOUT) { $env:QOBUZ_TEST_BLAME_HANG_TIMEOUT } else { "300s" })
)

Write-Host "[TEST] Qobuzzarr Unit Test Runner" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

# Detect UsePluginsBranch from environment variable (set by CI)
$PluginsBranchFlag = if ($env:USE_PLUGINS_BRANCH -eq "true") { "true" } else { "false" }

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$MinimalProject = Join-Path $PSScriptRoot "Minimal.Tests\Minimal.Tests.csproj"
$DefaultProject = Join-Path $PSScriptRoot "Qobuzarr.Tests\Qobuzarr.Tests.csproj"
$OutputDir = Join-Path $PSScriptRoot "TestResults"

# Ensure output directory exists
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

Write-Host "[INFO] Project Root: $ProjectRoot" -ForegroundColor Gray
if ($Full -or $Live) {
    $TestProjects = Get-ChildItem -Path $PSScriptRoot -Filter "*.csproj" -Recurse | Select-Object -ExpandProperty FullName
} else {
    if (Test-Path $MinimalProject) { $TestProjects = @($MinimalProject) } else { $TestProjects = @($DefaultProject) }
}
Write-Host "[INFO] Test Projects:" -ForegroundColor Gray
foreach ($p in $TestProjects) { Write-Host " - $p" -ForegroundColor Gray }
Write-Host "[INFO] Output Directory: $OutputDir" -ForegroundColor Gray
Write-Host ""

# Check if test projects exist
if ($TestProjects.Count -eq 0) {
    Write-Host "[ERROR] No test projects found" -ForegroundColor Red
    exit 1
}

# Build the selected test projects
Write-Host "[BUILD] Building selected test projects..." -ForegroundColor Yellow
foreach ($proj in $TestProjects) {
    $verbosity = if ($Verbose) { "detailed" } else { "minimal" }
    # -m:1 serializes MSBuild to avoid the concurrent Common.dll/deps.json copy race
    # (MSB3026 "being used by another process") when building test projects that share
    # the Common/Abstractions references. Matches Common PR #582/#596.
    $buildArgs = @($proj, "--configuration", $Configuration, "--verbosity", $verbosity, "-m:1", "-p:UsePluginsBranch=$PluginsBranchFlag", "-p:RunAnalyzersDuringBuild=false", "-p:EnableNETAnalyzers=false", "-p:TreatWarningsAsErrors=false", "-p:PluginPackagingDisable=true")
    & dotnet build @buildArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Build failed: $proj" -ForegroundColor Red
        exit 1
    }
}
Write-Host "[OK] Build successful!" -ForegroundColor Green
Write-Host ""

Write-Host "🧪 Running tests..." -ForegroundColor Yellow

# Determine runsettings to use (Default for fast; Full for -Full)
if (-not $RunSettings -or [string]::IsNullOrWhiteSpace($RunSettings)) {
    $RunSettings = Join-Path $PSScriptRoot ($Full ? "Full.runsettings" : "Default.runsettings")
}
if (Test-Path $RunSettings) {
    Write-Host "[INFO] Using runsettings: $RunSettings" -ForegroundColor Gray
}

# Allow CI to control the test filter (e.g., exclude Integration/Performance)
if (-not $Filter -or [string]::IsNullOrWhiteSpace($Filter)) {
    $Filter = $env:CI_TEST_FILTER
}
if ($Filter -and -not [string]::IsNullOrWhiteSpace($Filter)) {
    Write-Host "[INFO] Using test filter: $Filter" -ForegroundColor Gray
}

# Lower log volume during tests to speed runs
$env:LOG_LEVEL = "Error"

$overallExit = 0
foreach ($proj in $TestProjects) {
    $logName = ([IO.Path]::GetFileNameWithoutExtension($proj)) + ".trx"
    $args = @(
        "test", $proj,
        "--configuration", $Configuration,
        "--no-build",
        "--logger", "trx;LogFileName=$logName",
        "--results-directory", $OutputDir
    )
    if (Test-Path $RunSettings) { $args += @("--settings", $RunSettings) }
    if (-not $Live) {
        if ($Filter -and -not [string]::IsNullOrWhiteSpace($Filter)) {
            $args += @("--filter", $Filter)
        } elseif (-not $Full) {
            $args += @("--filter", "Category!=LiveIntegration")
        }
    }
    if ($Live) { $env:ENABLE_LIVE_INTEGRATION_TESTS = "true" }
    if ($Coverage) { $args += @("--collect", "XPlat Code Coverage", "--settings", (Join-Path $PSScriptRoot "coverlet.runsettings")) }
    # Activate the blame hang collector so a stuck or non-exiting test host is killed
    # after $BlameHangTimeout (and a dump captured) rather than blocking until the
    # session timeout. A bare <BlameHangTimeout> in the runsettings <RunConfiguration>
    # is INERT without one of these flags. This is what turns a 2h abort into a
    # ~5-min bounded, diagnosable failure.
    if ($BlameHangTimeout -and -not [string]::IsNullOrWhiteSpace($BlameHangTimeout)) {
        $args += @("--blame-hang-timeout", $BlameHangTimeout, "--blame-hang-dump-type", "full")
    }
    $args += @("--verbosity", $(if ($Verbose) { "detailed" } else { "normal" }))

    # Stream the output live (Tee to host) while capturing it so we can inspect it for the
    # blame-hang teardown signature below.
    & dotnet @args 2>&1 | Tee-Object -Variable testOutput | Out-Host
    $projExit = $LASTEXITCODE

    if ($projExit -eq 0) { continue }

    # Non-zero exit. Distinguish a genuine test failure from the known
    # xunit.runner.visualstudio 2.8.2 *post-completion shutdown-drain hang*: the adapter
    # finishes and reports every test (TRX shows 0 failures and a complete run) but its
    # internal async drain thread (MessageBus.ReporterWorker / ExecutionSink) intermittently
    # loses the stop-event wakeup, so the test host never exits and the active blame-hang
    # collector aborts it. That abort is a teardown defect, not a test result — the
    # authoritative TRX still proves the suite passed. Gate on the TRX, NOT on this exit code,
    # but ONLY for that exact signature (blame-hang abort + complete run + 0 failures/errors).
    # Any real failure, error, timeout, or a truncated/mid-run abort still fails the job.
    $outText = ($testOutput | Out-String)
    $isBlameHangAbort =
        ($outText -match 'inactivity time of .* has elapsed') -or
        ($outText -match 'Test Run Aborted') -or
        ($outText -match 'hangdump')

    # Per-project floor for "passed" so a truncated/mid-run abort can never masquerade as a
    # complete run. A blame-hang INACTIVITY abort already implies the run finished (during
    # active execution tests complete every few ms and keep resetting the inactivity timer;
    # 5 min of zero activity only happens once the host is done but won't exit), but we still
    # require a minimum passed count as belt-and-suspenders. Floors track the known suite size;
    # they only ever need raising as tests grow, and can be overridden per project via
    # QOBUZ_TEST_MIN_PASSED_<PROJECTNAME> (e.g. QOBUZ_TEST_MIN_PASSED_QOBUZARR_TESTS).
    $projName = [IO.Path]::GetFileNameWithoutExtension($proj)   # e.g. Qobuzarr.Tests
    $floorDefaults = @{ 'Qobuzarr.Tests' = 2000 }
    $floor = if ($floorDefaults.ContainsKey($projName)) { $floorDefaults[$projName] } else { 1 }
    $floorEnv = [Environment]::GetEnvironmentVariable("QOBUZ_TEST_MIN_PASSED_" + ($projName.ToUpper() -replace '[^A-Z0-9]','_'))
    if ($floorEnv -and ($floorEnv -as [int])) { $floor = [int]$floorEnv }

    $trxPath = Join-Path $OutputDir $logName
    $treatAsPass = $false
    if ($isBlameHangAbort -and (Test-Path $trxPath)) {
        try {
            [xml]$projTrx = Get-Content $trxPath
            $c = $projTrx.TestRun.ResultSummary.Counters
            $cTotal   = [int]$c.total
            $cExec    = [int]$c.executed
            $cPassed  = [int]$c.passed
            $cFailed  = [int]$c.failed
            $cError   = [int]$c.error
            $cTimeout = [int]$c.timeout
            $cAborted = [int]$c.aborted
            $cInconcl = [int]$c.inconclusive   # xUnit skips land here
            # CLEAN: nothing failed/errored/timed-out/aborted and tests actually ran.
            # COMPLETE: passed count meets the project's expected floor, so a truncated
            # (mid-run) abort with only a partial result set cannot slip through.
            $clean    = ($cFailed -eq 0) -and ($cError -eq 0) -and ($cTimeout -eq 0) -and ($cAborted -eq 0) -and ($cPassed -gt 0)
            $complete = ($cPassed -ge $floor)
            if ($clean -and $complete) { $treatAsPass = $true }
            Write-Host ""
            if ($treatAsPass) {
                Write-Host "[WARN] ${logName}: dotnet test exited $projExit due to a post-completion test-host SHUTDOWN HANG" -ForegroundColor Yellow
                Write-Host "       (xunit.runner.visualstudio async-drain deadlock; killed by blame-hang). The TRX shows a" -ForegroundColor Yellow
                Write-Host "       COMPLETE, fully-passing run (total=$cTotal passed=$cPassed skipped=$($cTotal-$cExec) failed=0 error=0, floor=$floor)" -ForegroundColor Yellow
                Write-Host "       so this project is treated as PASSED. A hang dump was captured for diagnosis." -ForegroundColor Yellow
            } else {
                Write-Host "[ERROR] ${logName}: blame-hang abort but TRX is NOT a clean complete run -> FAIL" -ForegroundColor Red
                Write-Host "        (total=$cTotal passed=$cPassed failed=$cFailed error=$cError aborted=$cAborted; required passed>=$floor, 0 failures)" -ForegroundColor Red
            }
        } catch {
            Write-Host "[ERROR] ${logName}: could not parse TRX to classify the non-zero exit -> FAIL" -ForegroundColor Red
        }
    } elseif ($isBlameHangAbort) {
        Write-Host "[ERROR] ${logName}: blame-hang abort but no TRX found -> FAIL" -ForegroundColor Red
    }

    if (-not $treatAsPass) { $overallExit = 1 }
}

$testExitCode = $overallExit

Write-Host ""

# Parse summary from the most recent TRX
$trx = Get-ChildItem -Path $OutputDir -Filter *.trx -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($trx) {
    try {
        [xml]$trxXml = Get-Content $trx.FullName
        $counters = $trxXml.TestRun.ResultSummary.Counters
        $total = [int]$counters.total
        $passed = [int]$counters.passed
        $failed = [int]$counters.failed
        $skipped = [int]$counters.inconclusive
        Write-Host "📊 Test Results Summary:" -ForegroundColor Cyan
        Write-Host "   Total: $total" -ForegroundColor White
        Write-Host "   Passed: $passed" -ForegroundColor Green
        Write-Host "   Failed: $failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Gray" })
        Write-Host "   Skipped: $skipped" -ForegroundColor Yellow
        $passRate = if ($total -gt 0) { [math]::Round(($passed / $total) * 100, 2) } else { 0 }
        Write-Host "   Pass Rate: $passRate%" -ForegroundColor $(if ($passRate -ge 80) { "Green" } elseif ($passRate -ge 60) { "Yellow" } else { "Red" })
    } catch { Write-Host "⚠️ Could not parse test results" -ForegroundColor Yellow }
}

# Process coverage results
if ($Coverage) {
    Write-Host ""
    Write-Host "📊 Processing coverage results..." -ForegroundColor Yellow
    
    $coverageFiles = Get-ChildItem -Path $OutputDir -Filter "coverage.cobertura.xml" -Recurse
    if ($coverageFiles.Count -gt 0) {
        $coverageFile = $coverageFiles[0].FullName
        Write-Host "📄 Coverage file: $coverageFile" -ForegroundColor Gray
        
        try {
            [xml]$coverageXml = Get-Content $coverageFile
            $lineRate = [double]$coverageXml.coverage.'line-rate'
            $branchRate = [double]$coverageXml.coverage.'branch-rate'
            
            $lineCoverage = [math]::Round($lineRate * 100, 2)
            $branchCoverage = [math]::Round($branchRate * 100, 2)
            
            Write-Host "📊 Coverage Summary:" -ForegroundColor Cyan
            Write-Host "   Line Coverage: $lineCoverage%" -ForegroundColor $(if ($lineCoverage -ge 80) { "Green" } elseif ($lineCoverage -ge 60) { "Yellow" } else { "Red" })
            Write-Host "   Branch Coverage: $branchCoverage%" -ForegroundColor $(if ($branchCoverage -ge 70) { "Green" } elseif ($branchCoverage -ge 50) { "Yellow" } else { "Red" })
            
            # Check if coverage meets targets
            if ($lineCoverage -ge 80) {
                Write-Host "✅ Line coverage target met (≥80%)" -ForegroundColor Green
            } else {
                Write-Host "❌ Line coverage below target (≥80%)" -ForegroundColor Red
            }
            
        } catch {
            Write-Host "⚠️ Could not parse coverage results" -ForegroundColor Yellow
        }
    } else {
        Write-Host "⚠️ No coverage files found" -ForegroundColor Yellow
    }
}

Write-Host ""

# Final result
if ($testExitCode -eq 0) {
    Write-Host "🎉 All tests completed successfully!" -ForegroundColor Green
} else {
    Write-Host "❌ Some tests failed!" -ForegroundColor Red
}

Write-Host ""
Write-Host "📁 Test results saved to: $OutputDir" -ForegroundColor Gray

exit $testExitCode
