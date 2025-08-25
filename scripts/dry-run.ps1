# =============================================================================
# Qobuzarr Migration Dry Run Script (PowerShell)
# =============================================================================
# Full dry run analysis of service migration without making changes

param(
    [string]$StartFromStep = "",
    [switch]$VerboseOutput,
    [switch]$ShowDetails,
    [switch]$ExportReport,
    [string]$ReportPath = "",
    [switch]$Help
)

function Show-Help {
    Write-Host "🧪 Qobuzarr Migration Dry Run Script" -ForegroundColor Green
    Write-Host ""
    Write-Host "USAGE:" -ForegroundColor Cyan
    Write-Host "  .\scripts\dry-run.ps1 [Options]" -ForegroundColor White
    Write-Host ""
    Write-Host "OPTIONS:" -ForegroundColor Cyan
    Write-Host "  -StartFromStep [step]  Start analysis from specific step" -ForegroundColor White
    Write-Host "  -VerboseOutput         Show detailed analysis output" -ForegroundColor White
    Write-Host "  -ShowDetails           Display step-by-step analysis" -ForegroundColor White
    Write-Host "  -ExportReport          Generate detailed analysis report" -ForegroundColor White
    Write-Host "  -ReportPath [path]     Custom path for analysis report" -ForegroundColor White
    Write-Host "  -Help                  Show this help" -ForegroundColor White
    Write-Host ""
    Write-Host "EXAMPLES:" -ForegroundColor Cyan
    Write-Host "  .\scripts\dry-run.ps1                                    # Basic dry run" -ForegroundColor Gray
    Write-Host "  .\scripts\dry-run.ps1 -VerboseOutput -ShowDetails        # Detailed analysis" -ForegroundColor Gray
    Write-Host "  .\scripts\dry-run.ps1 -ExportReport                      # Generate report" -ForegroundColor Gray
    Write-Host "  .\scripts\dry-run.ps1 -StartFromStep migrate-validation  # Partial analysis" -ForegroundColor Gray
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

Write-Host "🧪 Qobuzarr Migration Dry Run Analysis" -ForegroundColor Green
Write-Host "=======================================" -ForegroundColor Green

# Initialize analysis tracking
$analysisStart = Get-Date
$analysisResults = @{
    TotalSteps = 0
    AnalyzedSteps = 0
    BlockingIssues = 0
    Warnings = 0
    EstimatedDuration = [TimeSpan]::Zero
    RiskLevel = "Low"
    Issues = @()
    StepAnalysis = @()
}

# Step 1: Validate prerequisites
Write-Host ""
Write-Host "📋 Phase 1: Prerequisites Analysis" -ForegroundColor Blue

try {
    Write-Host "🔍 Checking project structure..." -ForegroundColor White
    
    # Check source directory structure
    $requiredDirectories = @(
        "src\Services",
        "src\Services\Consolidated", 
        "tools\MigrationController",
        "tools\SessionMigrator"
    )
    
    $missingDirectories = @()
    foreach ($dir in $requiredDirectories) {
        if (-not (Test-Path $dir)) {
            $missingDirectories += $dir
        }
    }
    
    if ($missingDirectories.Count -gt 0) {
        Write-Host "❌ Missing required directories:" -ForegroundColor Red
        $missingDirectories | ForEach-Object { Write-Host "   - $_" -ForegroundColor Red }
        $analysisResults.BlockingIssues++
        $analysisResults.Issues += "Missing required directories: $($missingDirectories -join ', ')"
    } else {
        Write-Host "✅ Project structure validation passed" -ForegroundColor Green
    }
    
    # Check consolidated services exist
    Write-Host "🔍 Checking consolidated services..." -ForegroundColor White
    
    $consolidatedServices = @(
        "src\Services\Consolidated\QobuzQualityManager.cs",
        "src\Services\Consolidated\IQobuzQualityManager.cs",
        "src\Services\Consolidated\ConsolidatedServiceRegistration.cs"
    )
    
    $missingServices = @()
    foreach ($service in $consolidatedServices) {
        if (-not (Test-Path $service)) {
            $missingServices += $service
        }
    }
    
    if ($missingServices.Count -gt 0) {
        Write-Host "❌ Missing consolidated services:" -ForegroundColor Red
        $missingServices | ForEach-Object { Write-Host "   - $_" -ForegroundColor Red }
        $analysisResults.BlockingIssues++
        $analysisResults.Issues += "Missing consolidated services: $($missingServices -join ', ')"
    } else {
        Write-Host "✅ Consolidated services validation passed" -ForegroundColor Green
    }
    
    # Check legacy services to be migrated
    Write-Host "🔍 Analyzing legacy services..." -ForegroundColor White
    
    $legacyServices = @(
        "src\Services\LidarrAlbumRetriever.cs",
        "src\Services\QobuzValidationService.cs",
        "src\Core\QobuzApiService.cs",
        "src\Services\QobuzQualityService.cs",
        "src\Services\QualityMappingService.cs",
        "src\Services\QualityFallbackService.cs"
    )
    
    $existingLegacyServices = @()
    foreach ($service in $legacyServices) {
        if (Test-Path $service) {
            $existingLegacyServices += $service
            
            # Analyze service dependencies
            $content = Get-Content $service -Raw
            $dependencies = @()
            
            # Check for quality service dependencies
            if ($content -match "IQualityMappingService|QualityMappingService") {
                $dependencies += "QualityMappingService"
            }
            if ($content -match "IQualityFallbackService|QualityFallbackService") {
                $dependencies += "QualityFallbackService"
            }
            if ($content -match "QobuzQualityService") {
                $dependencies += "QobuzQualityService"
            }
            
            if ($VerboseOutput -and $dependencies.Count -gt 0) {
                Write-Host "   📄 $service has dependencies: $($dependencies -join ', ')" -ForegroundColor Gray
            }
        }
    }
    
    Write-Host "✅ Found $($existingLegacyServices.Count) legacy services to migrate" -ForegroundColor Green
    
} catch {
    Write-Host "❌ Prerequisites analysis failed: $_" -ForegroundColor Red
    $analysisResults.BlockingIssues++
    $analysisResults.Issues += "Prerequisites check failed: $_"
}

# Step 2: Build validation
Write-Host ""
Write-Host "🔨 Phase 2: Build Analysis" -ForegroundColor Blue

try {
    Write-Host "🔍 Testing current build status..." -ForegroundColor White
    
    # Check if project builds currently
    $buildParams = @(
        "--configuration", "Debug",
        "--verbosity", "quiet",
        "--no-restore",
        "-p:RunAnalyzersDuringBuild=false",
        "-p:EnableNETAnalyzers=false",
        "-p:TreatWarningsAsErrors=false"
    )
    
    $buildResult = & dotnet build @buildParams 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Project builds successfully" -ForegroundColor Green
    } else {
        Write-Host "⚠️ Project has build issues:" -ForegroundColor Yellow
        if ($VerboseOutput) {
            $buildResult | ForEach-Object { Write-Host "   $_" -ForegroundColor Gray }
        }
        $analysisResults.Warnings++
        $analysisResults.Issues += "Current build has issues - migration may resolve or worsen"
    }
    
} catch {
    Write-Host "❌ Build analysis failed: $_" -ForegroundColor Red
    $analysisResults.Warnings++
    $analysisResults.Issues += "Could not analyze build status: $_"
}

# Step 3: Migration steps analysis
Write-Host ""
Write-Host "📝 Phase 3: Migration Steps Analysis" -ForegroundColor Blue

$migrationSteps = @(
    @{
        Id = "migrate-lidarr-album-retriever"
        Description = "Migrate LidarrAlbumRetriever to IQobuzQualityManager"
        Phase = "2A"
        EstimatedMinutes = 15
        RiskLevel = "Medium"
        Dependencies = @("IQobuzQualityManager implemented")
    },
    @{
        Id = "migrate-qobuz-validation-service"
        Description = "Migrate QobuzValidationService to consolidated services"
        Phase = "2A" 
        EstimatedMinutes = 10
        RiskLevel = "Low"
        Dependencies = @("IQobuzQualityManager implemented")
    },
    @{
        Id = "migrate-qobuz-api-service"
        Description = "Migrate QobuzApiService quality mappings"
        Phase = "2A"
        EstimatedMinutes = 8
        RiskLevel = "Low"
        Dependencies = @("IQobuzQualityManager implemented")
    },
    @{
        Id = "remove-legacy-quality-services"
        Description = "Remove legacy quality service files"
        Phase = "2B"
        EstimatedMinutes = 5
        RiskLevel = "High"
        Dependencies = @("All services migrated", "Build tests passing")
    },
    @{
        Id = "remove-migration-adapters"
        Description = "Remove migration adapters and temporary code"
        Phase = "2C"
        EstimatedMinutes = 5
        RiskLevel = "Medium"
        Dependencies = @("Legacy services removed", "Integration tests passing")
    }
)

$analysisResults.TotalSteps = $migrationSteps.Count

foreach ($step in $migrationSteps) {
    if ($StartFromStep -ne "" -and $step.Id -ne $StartFromStep -and -not $foundStartStep) {
        if ($step.Id -eq $StartFromStep) {
            $foundStartStep = $true
        } else {
            continue
        }
    }
    
    $analysisResults.AnalyzedSteps++
    $analysisResults.EstimatedDuration = $analysisResults.EstimatedDuration.Add([TimeSpan]::FromMinutes($step.EstimatedMinutes))
    
    Write-Host "🔍 Analyzing step: $($step.Id)" -ForegroundColor White
    
    $stepAnalysis = @{
        StepId = $step.Id
        Description = $step.Description
        Phase = $step.Phase
        RiskLevel = $step.RiskLevel
        EstimatedMinutes = $step.EstimatedMinutes
        Issues = @()
        Dependencies = $step.Dependencies
        CanExecute = $true
    }
    
    # Check step dependencies
    foreach ($dependency in $step.Dependencies) {
        switch ($dependency) {
            "IQobuzQualityManager implemented" {
                if (-not (Test-Path "src\Services\Consolidated\QobuzQualityManager.cs")) {
                    $stepAnalysis.Issues += "Dependency missing: $dependency"
                    $stepAnalysis.CanExecute = $false
                }
            }
            "All services migrated" {
                # Would check if previous migration steps completed
                if ($VerboseOutput) {
                    Write-Host "   ℹ️ Dependency check: $dependency (would be verified at runtime)" -ForegroundColor Gray
                }
            }
            "Build tests passing" {
                if ($LASTEXITCODE -ne 0) {
                    $stepAnalysis.Issues += "Dependency not met: Build currently failing"
                    $analysisResults.Warnings++
                }
            }
        }
    }
    
    # Risk level aggregation
    if ($step.RiskLevel -eq "High") {
        $analysisResults.RiskLevel = "High"
    } elseif ($step.RiskLevel -eq "Medium" -and $analysisResults.RiskLevel -ne "High") {
        $analysisResults.RiskLevel = "Medium"
    }
    
    if ($stepAnalysis.Issues.Count -gt 0) {
        Write-Host "   ⚠️ Issues found:" -ForegroundColor Yellow
        $stepAnalysis.Issues | ForEach-Object { Write-Host "     - $_" -ForegroundColor Yellow }
        $analysisResults.Warnings += $stepAnalysis.Issues.Count
    } else {
        Write-Host "   ✅ Step analysis passed" -ForegroundColor Green
    }
    
    if ($ShowDetails) {
        Write-Host "   📊 Details:" -ForegroundColor Cyan
        Write-Host "     Phase: $($step.Phase)" -ForegroundColor Gray
        Write-Host "     Risk: $($step.RiskLevel)" -ForegroundColor Gray
        Write-Host "     Time: $($step.EstimatedMinutes) minutes" -ForegroundColor Gray
        Write-Host "     Dependencies: $($step.Dependencies -join ', ')" -ForegroundColor Gray
    }
    
    $analysisResults.StepAnalysis += $stepAnalysis
}

# Step 4: Session migration analysis
Write-Host ""
Write-Host "🔐 Phase 4: Session Migration Analysis" -ForegroundColor Blue

try {
    $sessionDirectory = ".qobuz-sessions"
    
    if (Test-Path $sessionDirectory) {
        $sessionFiles = Get-ChildItem -Path $sessionDirectory -Filter "*.json" -Recurse
        Write-Host "📄 Found $($sessionFiles.Count) session files to analyze" -ForegroundColor White
        
        if ($sessionFiles.Count -gt 0) {
            Write-Host "✅ Session migration will be included in migration plan" -ForegroundColor Green
            $analysisResults.EstimatedDuration = $analysisResults.EstimatedDuration.Add([TimeSpan]::FromMinutes(5))
        }
    } else {
        Write-Host "ℹ️ No existing sessions found - fresh installation" -ForegroundColor Gray
    }
    
} catch {
    Write-Host "❌ Session analysis failed: $_" -ForegroundColor Red
    $analysisResults.Warnings++
}

# Analysis Summary
Write-Host ""
Write-Host "📊 Migration Analysis Summary" -ForegroundColor Green
Write-Host "=============================" -ForegroundColor Green

$analysisEnd = Get-Date
$analysisDuration = $analysisEnd - $analysisStart

Write-Host ""
Write-Host "🔍 Analysis Results:" -ForegroundColor Cyan
Write-Host "   Total Steps: $($analysisResults.TotalSteps)" -ForegroundColor White
Write-Host "   Analyzed Steps: $($analysisResults.AnalyzedSteps)" -ForegroundColor White
Write-Host "   Blocking Issues: $($analysisResults.BlockingIssues)" -ForegroundColor $(if ($analysisResults.BlockingIssues -gt 0) { "Red" } else { "Green" })
Write-Host "   Warnings: $($analysisResults.Warnings)" -ForegroundColor $(if ($analysisResults.Warnings -gt 0) { "Yellow" } else { "Green" })
Write-Host "   Risk Level: $($analysisResults.RiskLevel)" -ForegroundColor $(switch ($analysisResults.RiskLevel) { "High" { "Red" } "Medium" { "Yellow" } default { "Green" } })
Write-Host "   Estimated Duration: $($analysisResults.EstimatedDuration.ToString('mm\:ss'))" -ForegroundColor White
Write-Host "   Analysis Time: $($analysisDuration.ToString('mm\:ss'))" -ForegroundColor Gray

if ($analysisResults.Issues.Count -gt 0) {
    Write-Host ""
    Write-Host "⚠️ Issues Detected:" -ForegroundColor Yellow
    $analysisResults.Issues | ForEach-Object { Write-Host "   - $_" -ForegroundColor Yellow }
}

Write-Host ""
if ($analysisResults.BlockingIssues -eq 0) {
    Write-Host "✅ Migration is ready to execute" -ForegroundColor Green
    Write-Host ""
    Write-Host "💡 Next Steps:" -ForegroundColor Cyan
    Write-Host "• To execute migration: .\scripts\execute-migration.ps1" -ForegroundColor White
    Write-Host "• To create backup first: .\scripts\execute-migration.ps1 -CreateBackup" -ForegroundColor White
    if ($analysisResults.RiskLevel -eq "High") {
        Write-Host "• ⚠️ High risk migration - consider manual review first" -ForegroundColor Yellow
    }
} else {
    Write-Host "❌ Migration cannot proceed due to blocking issues" -ForegroundColor Red
    Write-Host ""
    Write-Host "🔧 Required Actions:" -ForegroundColor Cyan
    Write-Host "• Resolve blocking issues listed above" -ForegroundColor White
    Write-Host "• Run dry-run again to verify fixes" -ForegroundColor White
}

# Export detailed report if requested
if ($ExportReport) {
    $reportData = @{
        AnalysisTime = $analysisStart
        Duration = $analysisDuration
        Results = $analysisResults
        ProjectRoot = (Get-Location).Path
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    }
    
    $reportJson = $reportData | ConvertTo-Json -Depth 10
    
    if ($ReportPath -eq "") {
        $ReportPath = "migration-analysis-report_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
    }
    
    try {
        $reportJson | Out-File -FilePath $ReportPath -Encoding UTF8
        Write-Host ""
        Write-Host "📄 Detailed analysis report exported: $ReportPath" -ForegroundColor Green
    } catch {
        Write-Host ""
        Write-Host "❌ Failed to export report: $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "🎉 Dry run analysis completed!" -ForegroundColor Green

# Exit with appropriate code
if ($analysisResults.BlockingIssues -gt 0) {
    exit 1
} else {
    exit 0
}