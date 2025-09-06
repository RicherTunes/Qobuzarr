# Qobuzzarr Unit Test Runner
# This script runs all unit tests and generates coverage reports

param(
    [string]$Configuration = "Debug",
    [switch]$Coverage = $false,
    [switch]$Verbose = $false,
    [switch]$Full = $false,
    [string]$RunSettings = ""
)

Write-Host "[TEST] Qobuzzarr Unit Test Runner" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$TestProject = Join-Path $PSScriptRoot "Qobuzarr.Tests\Qobuzarr.Tests.csproj"
$OutputDir = Join-Path $PSScriptRoot "TestResults"

# Ensure output directory exists
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

Write-Host "[INFO] Project Root: $ProjectRoot" -ForegroundColor Gray
Write-Host "[INFO] Test Project: $TestProject" -ForegroundColor Gray
Write-Host "[INFO] Output Directory: $OutputDir" -ForegroundColor Gray
Write-Host ""

# Check if test project exists
if (!(Test-Path $TestProject)) {
    Write-Host "[ERROR] Test project not found: $TestProject" -ForegroundColor Red
    exit 1
}

# Build the test project
Write-Host "[BUILD] Building test project..." -ForegroundColor Yellow
$buildArgs = @(
    "build"
    $TestProject
    "--configuration", $Configuration
    "--verbosity", "minimal"
)

if ($Verbose) {
    $buildArgs += "--verbosity", "detailed"
}

$buildResult = & dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "[OK] Build successful!" -ForegroundColor Green
Write-Host ""

# Prepare test arguments
$testArgs = @(
    "test"
    $TestProject
    "--configuration", $Configuration
    "--no-build"
    "--logger", "trx;LogFileName=TestResults.trx"
    "--results-directory", $OutputDir
)

# Determine runsettings to use
if (-not $RunSettings -or [string]::IsNullOrWhiteSpace($RunSettings)) {
    if ($Full) {
        $RunSettings = Join-Path $PSScriptRoot "Full.runsettings"
    } else {
        $RunSettings = Join-Path $PSScriptRoot "Default.runsettings"
    }
}

if (Test-Path $RunSettings) {
    $testArgs += "--settings", $RunSettings
    Write-Host "[INFO] Using runsettings: $RunSettings" -ForegroundColor Gray
}

if ($Coverage) {
    Write-Host "📊 Running tests with coverage analysis..." -ForegroundColor Yellow
    $testArgs += @(
        "--collect", "XPlat Code Coverage"
        "--settings", (Join-Path $PSScriptRoot "coverlet.runsettings")
    )
} else {
    Write-Host "🧪 Running tests..." -ForegroundColor Yellow
}

if ($Verbose) {
    $testArgs += "--verbosity", "detailed"
} else {
    $testArgs += "--verbosity", "normal"
}

# Run tests
$testResult = & dotnet @testArgs
$testExitCode = $LASTEXITCODE

Write-Host ""

# Parse test results
$trxFile = Join-Path $OutputDir "TestResults.trx"
if (Test-Path $trxFile) {
    try {
        [xml]$trxXml = Get-Content $trxFile
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
        
    } catch {
        Write-Host "⚠️ Could not parse test results" -ForegroundColor Yellow
    }
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
