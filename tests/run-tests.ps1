# Qobuzzarr Unit Test Runner
# This script runs all unit tests and generates coverage reports

param(
    [string]$Configuration = "Debug",
    [switch]$Coverage = $false,
    [switch]$Verbose = $false,
    [switch]$Full = $false,
    [switch]$Live = $false
)

Write-Host "[TEST] Qobuzzarr Unit Test Runner" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

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
    & dotnet build $proj --configuration $Configuration --verbosity $verbosity
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Build failed: $proj" -ForegroundColor Red
        exit 1
    }
}
Write-Host "[OK] Build successful!" -ForegroundColor Green
Write-Host ""

Write-Host "🧪 Running tests..." -ForegroundColor Yellow

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
    if (-not $Full -and -not $Live) { $args += @("--filter", "Category!=LiveIntegration") }
    if ($Live) { $env:ENABLE_LIVE_INTEGRATION_TESTS = "true" }
    if ($Coverage) { $args += @("--collect", "XPlat Code Coverage", "--settings", (Join-Path $PSScriptRoot "coverlet.runsettings")) }
    $args += @("--verbosity", $(if ($Verbose) { "detailed" } else { "normal" }))

    & dotnet @args
    if ($LASTEXITCODE -ne 0) { $overallExit = 1 }
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
