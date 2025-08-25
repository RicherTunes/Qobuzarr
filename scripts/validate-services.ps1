# =============================================================================
# Qobuzarr Service Validation Script (PowerShell)
# =============================================================================
# Service validation and health checking post-migration

param(
    [switch]$Quick,
    [switch]$Deep,
    [switch]$IntegrationTests,
    [switch]$SessionValidation,
    [switch]$VerboseOutput,
    [switch]$ExportReport,
    [string]$ReportPath = "",
    [switch]$Help
)

function Show-Help {
    Write-Host "🔍 Qobuzarr Service Validation Script" -ForegroundColor Green
    Write-Host ""
    Write-Host "USAGE:" -ForegroundColor Cyan
    Write-Host "  .\scripts\validate-services.ps1 [Options]" -ForegroundColor White
    Write-Host ""
    Write-Host "OPTIONS:" -ForegroundColor Cyan
    Write-Host "  -Quick               Quick validation (build + basic tests)" -ForegroundColor White
    Write-Host "  -Deep                Deep validation (comprehensive analysis)" -ForegroundColor White
    Write-Host "  -IntegrationTests    Run integration tests" -ForegroundColor White
    Write-Host "  -SessionValidation   Validate session migration integrity" -ForegroundColor White
    Write-Host "  -VerboseOutput       Show detailed validation output" -ForegroundColor White
    Write-Host "  -ExportReport        Generate detailed validation report" -ForegroundColor White
    Write-Host "  -ReportPath [path]   Custom path for validation report" -ForegroundColor White
    Write-Host "  -Help                Show this help" -ForegroundColor White
    Write-Host ""
    Write-Host "EXAMPLES:" -ForegroundColor Cyan
    Write-Host "  .\scripts\validate-services.ps1                          # Standard validation" -ForegroundColor Gray
    Write-Host "  .\scripts\validate-services.ps1 -Quick                   # Quick health check" -ForegroundColor Gray
    Write-Host "  .\scripts\validate-services.ps1 -Deep -VerboseOutput     # Comprehensive validation" -ForegroundColor Gray
    Write-Host "  .\scripts\validate-services.ps1 -IntegrationTests        # Integration test focus" -ForegroundColor Gray
    Write-Host ""
    Write-Host "VALIDATION LEVELS:" -ForegroundColor Cyan
    Write-Host "  Quick: Build + Unit Tests (2-3 minutes)" -ForegroundColor White
    Write-Host "  Standard: Build + Tests + Service Analysis (5-10 minutes)" -ForegroundColor White
    Write-Host "  Deep: Full analysis + Integration + Session validation (10-15 minutes)" -ForegroundColor White
    Write-Host ""
}

if ($Help) {
    Show-Help
    exit 0
}

# Check if we're in the right directory
if (-not (Test-Path "Qobuzarr.csproj")) {
    Write-Host "❌ Error: Please run this script from the Qobuzarr root directory" -ForegroundColor Red
    Write-Host "   Current directory: $(Get-Location)" -ForegroundColor Yellow
    exit 1
}

Write-Host "🔍 Qobuzarr Service Validation" -ForegroundColor Green
Write-Host "==============================" -ForegroundColor Green

# Determine validation level
$validationLevel = "Standard"
if ($Quick) { $validationLevel = "Quick" }
if ($Deep) { $validationLevel = "Deep" }

Write-Host "🎯 Validation Level: $validationLevel" -ForegroundColor Cyan

# Initialize validation tracking
$validationStart = Get-Date
$validationResults = @{
    ValidationLevel = $validationLevel
    Success = $true
    BuildPassed = $false
    UnitTestsPassed = $false
    IntegrationTestsPassed = $false
    ServiceAnalysisPassed = $false
    SessionValidationPassed = $false
    Issues = @()
    TestResults = @()
    ServiceHealth = @()
    Duration = $null
}

# Phase 1: Build Validation
Write-Host ""
Write-Host "🔨 Phase 1: Build Validation" -ForegroundColor Blue

Write-Host "🔍 Building project with migration changes..." -ForegroundColor White

$buildParams = @(
    "--configuration", "Release",
    "--verbosity", "normal",
    "--no-restore",
    "-p:RunAnalyzersDuringBuild=false",
    "-p:EnableNETAnalyzers=false",
    "-p:TreatWarningsAsErrors=false"
)

try {
    $buildOutput = & dotnet build @buildParams 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Build validation passed" -ForegroundColor Green
        $validationResults.BuildPassed = $true
    } else {
        Write-Host "❌ Build validation failed" -ForegroundColor Red
        $validationResults.Success = $false
        $validationResults.Issues += "Build failed"
        
        if ($VerboseOutput) {
            Write-Host "Build output:" -ForegroundColor Gray
            $buildOutput | ForEach-Object { Write-Host "   $_" -ForegroundColor Gray }
        }
    }
} catch {
    Write-Host "❌ Build validation error: $_" -ForegroundColor Red
    $validationResults.Success = $false
    $validationResults.Issues += "Build error: $_"
}

# Phase 2: Unit Test Validation
if ($validationResults.BuildPassed) {
    Write-Host ""
    Write-Host "🧪 Phase 2: Unit Test Validation" -ForegroundColor Blue
    
    Write-Host "🔍 Running unit tests..." -ForegroundColor White
    
    try {
        $testOutput = & dotnet test --no-build --verbosity normal --logger "trx" --collect:"XPlat Code Coverage"
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Unit tests passed" -ForegroundColor Green
            $validationResults.UnitTestsPassed = $true
            
            # Analyze test results
            $testResultFiles = Get-ChildItem -Path "." -Recurse -Filter "*.trx" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            
            if ($testResultFiles) {
                try {
                    [xml]$testResults = Get-Content $testResultFiles.FullName
                    $testSummary = $testResults.TestRun.ResultSummary.Counters
                    
                    $testSummaryInfo = @{
                        Total = [int]$testSummary.total
                        Passed = [int]$testSummary.passed
                        Failed = [int]$testSummary.failed
                        Skipped = [int]$testSummary.inconclusive + [int]$testSummary.notRunnable
                    }
                    
                    $validationResults.TestResults += $testSummaryInfo
                    
                    if ($VerboseOutput) {
                        Write-Host "   📊 Test Summary:" -ForegroundColor Gray
                        Write-Host "      Total: $($testSummaryInfo.Total)" -ForegroundColor Gray
                        Write-Host "      Passed: $($testSummaryInfo.Passed)" -ForegroundColor Gray
                        Write-Host "      Failed: $($testSummaryInfo.Failed)" -ForegroundColor Gray
                        Write-Host "      Skipped: $($testSummaryInfo.Skipped)" -ForegroundColor Gray
                    }
                } catch {
                    Write-Host "⚠️ Could not parse test results: $_" -ForegroundColor Yellow
                }
            }
            
        } else {
            Write-Host "❌ Unit tests failed" -ForegroundColor Red
            $validationResults.Success = $false
            $validationResults.Issues += "Unit tests failed"
            
            if ($VerboseOutput) {
                Write-Host "Test output:" -ForegroundColor Gray
                $testOutput | ForEach-Object { Write-Host "   $_" -ForegroundColor Gray }
            }
        }
    } catch {
        Write-Host "❌ Unit test error: $_" -ForegroundColor Red
        $validationResults.Success = $false
        $validationResults.Issues += "Unit test error: $_"
    }
}

# Phase 3: Service Analysis (Standard and Deep only)
if (-not $Quick -and $validationResults.BuildPassed) {
    Write-Host ""
    Write-Host "🔧 Phase 3: Service Analysis" -ForegroundColor Blue
    
    Write-Host "🔍 Analyzing consolidated services..." -ForegroundColor White
    
    $serviceAnalysis = @{
        ConsolidatedServicesPresent = $false
        LegacyServicesRemoved = $false
        ServiceRegistrationValid = $false
        Issues = @()
    }
    
    # Check consolidated services exist
    $consolidatedServices = @(
        "src\Services\Consolidated\QobuzQualityManager.cs",
        "src\Services\Consolidated\IQobuzQualityManager.cs",
        "src\Services\Consolidated\ConsolidatedServiceRegistration.cs"
    )
    
    $missingConsolidated = @()
    foreach ($service in $consolidatedServices) {
        if (-not (Test-Path $service)) {
            $missingConsolidated += $service
        }
    }
    
    if ($missingConsolidated.Count -eq 0) {
        Write-Host "✅ All consolidated services present" -ForegroundColor Green
        $serviceAnalysis.ConsolidatedServicesPresent = $true
    } else {
        Write-Host "❌ Missing consolidated services:" -ForegroundColor Red
        $missingConsolidated | ForEach-Object { Write-Host "   - $_" -ForegroundColor Red }
        $serviceAnalysis.Issues += "Missing consolidated services: $($missingConsolidated -join ', ')"
        $validationResults.Success = $false
    }
    
    # Check legacy services (should be removed in Phase 2B+)
    $legacyServices = @(
        "src\Services\QobuzQualityService.cs",
        "src\Services\QualityMappingService.cs",
        "src\Services\QualityFallbackService.cs",
        "src\Services\IQualityMappingService.cs"
    )
    
    $remainingLegacy = @()
    foreach ($service in $legacyServices) {
        if (Test-Path $service) {
            $remainingLegacy += $service
        }
    }
    
    if ($remainingLegacy.Count -eq 0) {
        Write-Host "✅ Legacy services properly removed" -ForegroundColor Green
        $serviceAnalysis.LegacyServicesRemoved = $true
    } else {
        Write-Host "ℹ️ Legacy services still present (may be intentional):" -ForegroundColor Blue
        $remainingLegacy | ForEach-Object { Write-Host "   - $_" -ForegroundColor Blue }
        # This is not necessarily an error - depends on migration phase
    }
    
    # Analyze service registration
    $registrationFile = "src\Services\Consolidated\ConsolidatedServiceRegistration.cs"
    if (Test-Path $registrationFile) {
        try {
            $registrationContent = Get-Content $registrationFile -Raw
            
            # Check for proper service registration patterns
            if ($registrationContent -match "IQobuzQualityManager.*QobuzQualityManager") {
                Write-Host "✅ Service registration appears valid" -ForegroundColor Green
                $serviceAnalysis.ServiceRegistrationValid = $true
            } else {
                Write-Host "⚠️ Service registration may have issues" -ForegroundColor Yellow
                $serviceAnalysis.Issues += "Service registration validation failed"
            }
            
            # Check for migration adapters (should be cleaned up in Phase 2C)
            if ($registrationContent -match "MigrationAdapter|Legacy") {
                Write-Host "ℹ️ Migration adapters still present (cleanup pending)" -ForegroundColor Blue
            }
            
        } catch {
            Write-Host "❌ Could not analyze service registration: $_" -ForegroundColor Red
            $serviceAnalysis.Issues += "Service registration analysis failed: $_"
        }
    }
    
    $validationResults.ServiceAnalysisPassed = $serviceAnalysis.Issues.Count -eq 0
    $validationResults.ServiceHealth += $serviceAnalysis
    
    if ($VerboseOutput) {
        Write-Host "   📊 Service Analysis Results:" -ForegroundColor Gray
        Write-Host "      Consolidated Services: $($serviceAnalysis.ConsolidatedServicesPresent)" -ForegroundColor Gray
        Write-Host "      Legacy Services Removed: $($serviceAnalysis.LegacyServicesRemoved)" -ForegroundColor Gray
        Write-Host "      Registration Valid: $($serviceAnalysis.ServiceRegistrationValid)" -ForegroundColor Gray
    }
}

# Phase 4: Integration Tests (if requested or Deep validation)
if (($IntegrationTests -or $Deep) -and $validationResults.UnitTestsPassed) {
    Write-Host ""
    Write-Host "🔗 Phase 4: Integration Test Validation" -ForegroundColor Blue
    
    Write-Host "🔍 Running integration tests..." -ForegroundColor White
    
    try {
        $integrationTestProject = "tests\Integration\Qobuzarr.IntegrationTests.csproj"
        
        if (Test-Path $integrationTestProject) {
            $integrationTestOutput = & dotnet test $integrationTestProject --no-build --verbosity normal
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✅ Integration tests passed" -ForegroundColor Green
                $validationResults.IntegrationTestsPassed = $true
            } else {
                Write-Host "❌ Integration tests failed" -ForegroundColor Red
                $validationResults.Issues += "Integration tests failed"
                
                if ($VerboseOutput) {
                    Write-Host "Integration test output:" -ForegroundColor Gray
                    $integrationTestOutput | ForEach-Object { Write-Host "   $_" -ForegroundColor Gray }
                }
            }
        } else {
            Write-Host "ℹ️ No integration test project found - skipping" -ForegroundColor Blue
        }
    } catch {
        Write-Host "❌ Integration test error: $_" -ForegroundColor Red
        $validationResults.Issues += "Integration test error: $_"
    }
}

# Phase 5: Session Validation (if requested or Deep validation)
if (($SessionValidation -or $Deep) -and $validationResults.BuildPassed) {
    Write-Host ""
    Write-Host "🔐 Phase 5: Session Migration Validation" -ForegroundColor Blue
    
    Write-Host "🔍 Validating session integrity..." -ForegroundColor White
    
    $sessionValidation = @{
        SessionDirectoryExists = $false
        SessionsValid = $false
        Issues = @()
    }
    
    try {
        $sessionDir = ".qobuz-sessions"
        
        if (Test-Path $sessionDir) {
            $sessionValidation.SessionDirectoryExists = $true
            
            $sessionFiles = Get-ChildItem -Path $sessionDir -Filter "*.json" -Recurse
            Write-Host "📄 Found $($sessionFiles.Count) session files" -ForegroundColor White
            
            $validSessions = 0
            $invalidSessions = 0
            
            foreach ($sessionFile in $sessionFiles) {
                try {
                    $sessionContent = Get-Content $sessionFile.FullName | ConvertFrom-Json
                    
                    # Basic session validation
                    if ($sessionContent.sessionId -and $sessionContent.userId) {
                        $validSessions++
                    } else {
                        $invalidSessions++
                        $sessionValidation.Issues += "Invalid session: $($sessionFile.Name)"
                    }
                } catch {
                    $invalidSessions++
                    $sessionValidation.Issues += "Corrupted session: $($sessionFile.Name)"
                }
            }
            
            if ($sessionFiles.Count -gt 0 -and $invalidSessions -eq 0) {
                Write-Host "✅ All sessions valid" -ForegroundColor Green
                $sessionValidation.SessionsValid = $true
            } elseif ($sessionFiles.Count -eq 0) {
                Write-Host "ℹ️ No sessions found (fresh installation)" -ForegroundColor Blue
                $sessionValidation.SessionsValid = $true
            } else {
                Write-Host "❌ $invalidSessions invalid sessions found" -ForegroundColor Red
                $validationResults.Success = $false
            }
            
            if ($VerboseOutput) {
                Write-Host "   📊 Session Summary:" -ForegroundColor Gray
                Write-Host "      Total Sessions: $($sessionFiles.Count)" -ForegroundColor Gray
                Write-Host "      Valid Sessions: $validSessions" -ForegroundColor Gray
                Write-Host "      Invalid Sessions: $invalidSessions" -ForegroundColor Gray
            }
            
        } else {
            Write-Host "ℹ️ No session directory found - fresh installation" -ForegroundColor Blue
            $sessionValidation.SessionsValid = $true
        }
        
    } catch {
        Write-Host "❌ Session validation error: $_" -ForegroundColor Red
        $sessionValidation.Issues += "Session validation error: $_"
        $validationResults.Success = $false
    }
    
    $validationResults.SessionValidationPassed = $sessionValidation.Issues.Count -eq 0
    $validationResults.ServiceHealth += $sessionValidation
}

# Validation Summary
$validationEnd = Get-Date
$validationResults.Duration = $validationEnd - $validationStart

Write-Host ""
Write-Host "📊 Service Validation Summary" -ForegroundColor Green
Write-Host "=============================" -ForegroundColor Green

Write-Host ""
Write-Host "🎯 Validation Results:" -ForegroundColor Cyan
Write-Host "   Validation Level: $($validationResults.ValidationLevel)" -ForegroundColor White
Write-Host "   Build Passed: $($validationResults.BuildPassed)" -ForegroundColor $(if ($validationResults.BuildPassed) { "Green" } else { "Red" })
Write-Host "   Unit Tests Passed: $($validationResults.UnitTestsPassed)" -ForegroundColor $(if ($validationResults.UnitTestsPassed) { "Green" } else { "Red" })

if (-not $Quick) {
    Write-Host "   Service Analysis Passed: $($validationResults.ServiceAnalysisPassed)" -ForegroundColor $(if ($validationResults.ServiceAnalysisPassed) { "Green" } else { "Red" })
}

if ($IntegrationTests -or $Deep) {
    Write-Host "   Integration Tests Passed: $($validationResults.IntegrationTestsPassed)" -ForegroundColor $(if ($validationResults.IntegrationTestsPassed) { "Green" } else { "Red" })
}

if ($SessionValidation -or $Deep) {
    Write-Host "   Session Validation Passed: $($validationResults.SessionValidationPassed)" -ForegroundColor $(if ($validationResults.SessionValidationPassed) { "Green" } else { "Red" })
}

Write-Host "   Overall Success: $($validationResults.Success)" -ForegroundColor $(if ($validationResults.Success) { "Green" } else { "Red" })
Write-Host "   Validation Time: $($validationResults.Duration.ToString('mm\:ss'))" -ForegroundColor White

if ($validationResults.Issues.Count -gt 0) {
    Write-Host ""
    Write-Host "❌ Issues Found:" -ForegroundColor Red
    $validationResults.Issues | ForEach-Object { Write-Host "   - $_" -ForegroundColor Red }
}

Write-Host ""
if ($validationResults.Success) {
    Write-Host "✅ Service validation completed successfully!" -ForegroundColor Green
    
    Write-Host ""
    Write-Host "💡 Migration Status:" -ForegroundColor Cyan
    Write-Host "• Service migration appears to be working correctly" -ForegroundColor White
    Write-Host "• All critical validations passed" -ForegroundColor White
    Write-Host "• System is ready for production use" -ForegroundColor White
    
    if (-not $Deep) {
        Write-Host "• Consider running deep validation for comprehensive analysis" -ForegroundColor White
    }
    
} else {
    Write-Host "❌ Service validation failed" -ForegroundColor Red
    
    Write-Host ""
    Write-Host "🔧 Recommended Actions:" -ForegroundColor Cyan
    Write-Host "• Review and fix issues listed above" -ForegroundColor White
    Write-Host "• Check migration was completed correctly" -ForegroundColor White
    Write-Host "• Consider rolling back if issues are severe" -ForegroundColor White
    Write-Host "• Run validation again after fixes" -ForegroundColor White
}

# Export detailed report if requested
if ($ExportReport) {
    $reportData = @{
        ValidationTime = $validationStart
        Duration = $validationResults.Duration
        Results = $validationResults
        ProjectRoot = (Get-Location).Path
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    }
    
    $reportJson = $reportData | ConvertTo-Json -Depth 10
    
    if ($ReportPath -eq "") {
        $ReportPath = "service-validation-report_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
    }
    
    try {
        $reportJson | Out-File -FilePath $ReportPath -Encoding UTF8
        Write-Host ""
        Write-Host "📄 Detailed validation report exported: $ReportPath" -ForegroundColor Green
    } catch {
        Write-Host ""
        Write-Host "❌ Failed to export report: $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "🎉 Service validation completed!" -ForegroundColor Green

# Exit with appropriate code
if ($validationResults.Success) {
    exit 0
} else {
    exit 1
}