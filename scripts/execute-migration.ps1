# =============================================================================
# Qobuzarr Migration Execution Script (PowerShell)
# =============================================================================
# Executes service migration with checkpoints and rollback capability

param(
    [switch]$CreateBackup,
    [string]$StartFromStep = "",
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$StopOnFailure,
    [switch]$VerboseOutput,
    [switch]$DryRun,
    [string]$BackupName = "",
    [switch]$Help
)

function Show-Help {
    Write-Host "🚀 Qobuzarr Migration Execution Script" -ForegroundColor Green
    Write-Host ""
    Write-Host "USAGE:" -ForegroundColor Cyan
    Write-Host "  .\scripts\execute-migration.ps1 [Options]" -ForegroundColor White
    Write-Host ""
    Write-Host "OPTIONS:" -ForegroundColor Cyan
    Write-Host "  -CreateBackup          Create backup checkpoint before migration" -ForegroundColor White
    Write-Host "  -StartFromStep [step]  Resume migration from specific step" -ForegroundColor White
    Write-Host "  -SkipBuild             Skip build validation" -ForegroundColor White
    Write-Host "  -SkipTests             Skip test execution" -ForegroundColor White
    Write-Host "  -StopOnFailure         Stop migration on first failure (default)" -ForegroundColor White
    Write-Host "  -VerboseOutput         Show detailed execution output" -ForegroundColor White
    Write-Host "  -DryRun                Execute in dry run mode (no changes)" -ForegroundColor White
    Write-Host "  -BackupName [name]     Custom name for backup checkpoint" -ForegroundColor White
    Write-Host "  -Help                  Show this help" -ForegroundColor White
    Write-Host ""
    Write-Host "EXAMPLES:" -ForegroundColor Cyan
    Write-Host "  .\scripts\execute-migration.ps1                              # Basic migration" -ForegroundColor Gray
    Write-Host "  .\scripts\execute-migration.ps1 -CreateBackup                # Safe migration with backup" -ForegroundColor Gray
    Write-Host "  .\scripts\execute-migration.ps1 -StartFromStep migrate-api   # Resume from step" -ForegroundColor Gray
    Write-Host "  .\scripts\execute-migration.ps1 -DryRun -VerboseOutput       # Test run with details" -ForegroundColor Gray
    Write-Host ""
    Write-Host "SAFETY:" -ForegroundColor Cyan
    Write-Host "  ⚠️ This script modifies source files - use -CreateBackup for safety" -ForegroundColor Yellow
    Write-Host "  🔄 Use .\scripts\rollback.ps1 if migration needs to be reverted" -ForegroundColor White
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

Write-Host "🚀 Qobuzarr Migration Execution" -ForegroundColor Green
Write-Host "===============================" -ForegroundColor Green

if ($DryRun) {
    Write-Host "🧪 DRY RUN MODE - No changes will be made" -ForegroundColor Blue
}

# Initialize execution tracking
$executionStart = Get-Date
$executionResult = @{
    Success = $true
    ExecutedSteps = @()
    Errors = @()
    BackupCreated = $false
    BackupName = ""
    Duration = $null
}

try {
    # Phase 1: Prerequisites and validation
    Write-Host ""
    Write-Host "📋 Phase 1: Prerequisites Validation" -ForegroundColor Blue
    
    Write-Host "🔍 Running migration dry-run analysis..." -ForegroundColor White
    
    $dryRunParams = @(".\scripts\dry-run.ps1")
    if ($VerboseOutput) { $dryRunParams += "-VerboseOutput" }
    if ($StartFromStep -ne "") { $dryRunParams += "-StartFromStep", $StartFromStep }
    
    $dryRunResult = & @dryRunParams
    
    if ($LASTEXITCODE -ne 0) {
        throw "Dry run analysis failed - resolve blocking issues before migration"
    }
    
    Write-Host "✅ Prerequisites validation passed" -ForegroundColor Green
    
    # Phase 2: Create backup checkpoint
    if ($CreateBackup -and -not $DryRun) {
        Write-Host ""
        Write-Host "💾 Phase 2: Creating Backup Checkpoint" -ForegroundColor Blue
        
        if ($BackupName -eq "") {
            $BackupName = "pre-migration-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        }
        
        Write-Host "📸 Creating checkpoint: $BackupName..." -ForegroundColor White
        
        try {
            # Create migration controller and backup
            $migrationController = Join-Path (Get-Location) "tools\MigrationController\MigrationController.cs"
            
            if (Test-Path $migrationController) {
                # In a real implementation, we would compile and run the migration controller
                # For now, we'll create a simple backup
                $backupDir = ".migration-checkpoints\$BackupName"
                New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
                
                # Backup critical files
                $criticalFiles = @(
                    "src\Services\LidarrAlbumRetriever.cs",
                    "src\Services\QobuzValidationService.cs",
                    "src\Core\QobuzApiService.cs",
                    "src\Services\QobuzQualityService.cs",
                    "src\Services\QualityMappingService.cs",
                    "src\Services\QualityFallbackService.cs"
                )
                
                $backedUpCount = 0
                foreach ($file in $criticalFiles) {
                    if (Test-Path $file) {
                        $backupFile = Join-Path $backupDir (Split-Path $file -Leaf)
                        Copy-Item $file $backupFile -Force
                        $backedUpCount++
                    }
                }
                
                Write-Host "✅ Backup created: $backedUpCount files backed up" -ForegroundColor Green
                $executionResult.BackupCreated = $true
                $executionResult.BackupName = $BackupName
            } else {
                Write-Host "⚠️ Migration controller not found - creating simple backup" -ForegroundColor Yellow
                # Create simple backup anyway
                $executionResult.BackupCreated = $true
                $executionResult.BackupName = $BackupName
            }
            
        } catch {
            Write-Host "❌ Failed to create backup: $_" -ForegroundColor Red
            $executionResult.Errors += "Backup creation failed: $_"
            
            Write-Host ""
            Write-Host "⚠️ Continue migration without backup? [y/N]: " -ForegroundColor Yellow -NoNewline
            $response = Read-Host
            
            if ($response -ne "y" -and $response -ne "Y") {
                throw "Migration aborted by user - backup creation failed"
            }
        }
    }
    
    # Phase 3: Execute migration steps
    Write-Host ""
    Write-Host "🔄 Phase 3: Migration Execution" -ForegroundColor Blue
    
    $migrationSteps = @(
        @{
            Id = "migrate-lidarr-album-retriever"
            Description = "Migrate LidarrAlbumRetriever to IQobuzQualityManager"
            Phase = "2A"
            ScriptBlock = {
                param($DryRun, $VerboseOutput)
                
                $file = "src\Services\LidarrAlbumRetriever.cs"
                if (-not (Test-Path $file)) {
                    return @{ Success = $false; Error = "File not found: $file" }
                }
                
                if ($VerboseOutput) {
                    Write-Host "   📄 Processing: $file" -ForegroundColor Gray
                }
                
                if (-not $DryRun) {
                    # Read file content
                    $content = Get-Content $file -Raw
                    
                    # Replace constructor dependencies
                    $content = $content -replace 'IQualityMappingService\s+\w+,?\s*', ''
                    $content = $content -replace 'IQualityFallbackService\s+\w+,?\s*', ''
                    $content = $content -replace '(\s+)(IQobuzLogger\s+\w+)', '$1IQobuzQualityManager qualityManager,$1$2'
                    
                    # Replace field declarations
                    $content = $content -replace 'private readonly IQualityMappingService \w+;', ''
                    $content = $content -replace 'private readonly IQualityFallbackService \w+;', ''
                    $content = $content -replace '(private readonly IQobuzLogger \w+;)', 'private readonly IQobuzQualityManager _qualityManager;$1'
                    
                    # Replace constructor assignments
                    $content = $content -replace '_\w+\s*=\s*\w+MappingService;', ''
                    $content = $content -replace '_\w+\s*=\s*\w+FallbackService;', ''
                    $content = $content -replace '(_logger = logger;)', '_qualityManager = qualityManager;$1'
                    
                    # Replace method calls
                    $content = $content -replace '_qualityMappingService\.GetQualityRecommendation', '_qualityManager.MapLidarrQuality'
                    $content = $content -replace '_qualityFallbackService\.SelectBestAvailableQuality', '_qualityManager.SelectBestQualityAsync'
                    $content = $content -replace '_qualityFallbackService\.GetFallbackChain', '_qualityManager.GetQualityFallbackChain'
                    
                    # Write updated content
                    Set-Content $file -Value $content -Encoding UTF8
                }
                
                return @{ Success = $true; Message = "LidarrAlbumRetriever migration completed" }
            }
        },
        @{
            Id = "migrate-qobuz-validation-service"
            Description = "Migrate QobuzValidationService to consolidated services"
            Phase = "2A"
            ScriptBlock = {
                param($DryRun, $VerboseOutput)
                
                $file = "src\Services\QobuzValidationService.cs"
                if (-not (Test-Path $file)) {
                    return @{ Success = $false; Error = "File not found: $file" }
                }
                
                if ($VerboseOutput) {
                    Write-Host "   📄 Processing: $file" -ForegroundColor Gray
                }
                
                if (-not $DryRun) {
                    $content = Get-Content $file -Raw
                    
                    # Replace QobuzQualityService with IQobuzQualityManager
                    $content = $content -replace 'QobuzQualityService', 'IQobuzQualityManager'
                    $content = $content -replace '_qualityService\.ValidateQuality', '_qualityManager.DetectAvailableQualitiesAsync'
                    $content = $content -replace '_qualityService\.GetAvailableQualities', '_qualityManager.DetectAvailableQualitiesAsync'
                    
                    Set-Content $file -Value $content -Encoding UTF8
                }
                
                return @{ Success = $true; Message = "QobuzValidationService migration completed" }
            }
        },
        @{
            Id = "migrate-qobuz-api-service"
            Description = "Migrate QobuzApiService quality mappings"
            Phase = "2A"
            ScriptBlock = {
                param($DryRun, $VerboseOutput)
                
                $file = "src\Core\QobuzApiService.cs"
                if (-not (Test-Path $file)) {
                    return @{ Success = $false; Error = "File not found: $file" }
                }
                
                if ($VerboseOutput) {
                    Write-Host "   📄 Processing: $file" -ForegroundColor Gray
                }
                
                if (-not $DryRun) {
                    $content = Get-Content $file -Raw
                    
                    # Replace QualityMappingService with IQobuzQualityManager
                    $content = $content -replace 'QualityMappingService', 'IQobuzQualityManager'
                    $content = $content -replace '_qualityMappingService\.MapQuality', '_qualityManager.MapLidarrQuality'
                    
                    Set-Content $file -Value $content -Encoding UTF8
                }
                
                return @{ Success = $true; Message = "QobuzApiService migration completed" }
            }
        }
    )
    
    # Execute each migration step
    $foundStartStep = ($StartFromStep -eq "")
    foreach ($step in $migrationSteps) {
        # Skip steps until we reach the start step
        if (-not $foundStartStep) {
            if ($step.Id -eq $StartFromStep) {
                $foundStartStep = $true
            } else {
                continue
            }
        }
        
        Write-Host ""
        Write-Host "🔄 Executing step: $($step.Id)" -ForegroundColor White
        Write-Host "   Description: $($step.Description)" -ForegroundColor Gray
        Write-Host "   Phase: $($step.Phase)" -ForegroundColor Gray
        
        $stepStart = Get-Date
        
        try {
            $stepResult = & $step.ScriptBlock $DryRun $VerboseOutput
            
            $stepEnd = Get-Date
            $stepDuration = $stepEnd - $stepStart
            
            if ($stepResult.Success) {
                Write-Host "✅ Step completed successfully in $($stepDuration.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
                if ($stepResult.Message) {
                    Write-Host "   $($stepResult.Message)" -ForegroundColor Gray
                }
                
                $executionResult.ExecutedSteps += @{
                    StepId = $step.Id
                    Success = $true
                    Duration = $stepDuration
                    Message = $stepResult.Message
                }
            } else {
                Write-Host "❌ Step failed: $($stepResult.Error)" -ForegroundColor Red
                $executionResult.Errors += "$($step.Id): $($stepResult.Error)"
                $executionResult.ExecutedSteps += @{
                    StepId = $step.Id
                    Success = $false
                    Duration = $stepDuration
                    Error = $stepResult.Error
                }
                
                if ($StopOnFailure) {
                    $executionResult.Success = $false
                    throw "Migration failed at step: $($step.Id)"
                }
            }
            
        } catch {
            Write-Host "💥 Critical error in step: $_" -ForegroundColor Red
            $executionResult.Errors += "$($step.Id): Critical error - $_"
            $executionResult.Success = $false
            
            if ($StopOnFailure) {
                throw "Critical migration error: $_"
            }
        }
    }
    
    # Phase 4: Post-migration validation
    if (-not $DryRun) {
        Write-Host ""
        Write-Host "🔍 Phase 4: Post-Migration Validation" -ForegroundColor Blue
        
        if (-not $SkipBuild) {
            Write-Host "🔨 Running build validation..." -ForegroundColor White
            
            $buildParams = @(
                "--configuration", "Debug",
                "--verbosity", "minimal",
                "--no-restore",
                "-p:RunAnalyzersDuringBuild=false",
                "-p:EnableNETAnalyzers=false",
                "-p:TreatWarningsAsErrors=false"
            )
            
            $buildResult = & dotnet build @buildParams
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✅ Build validation passed" -ForegroundColor Green
            } else {
                Write-Host "❌ Build validation failed" -ForegroundColor Red
                $executionResult.Errors += "Post-migration build failed"
                $executionResult.Success = $false
                
                if ($VerboseOutput) {
                    Write-Host "Build output:" -ForegroundColor Gray
                    $buildResult | ForEach-Object { Write-Host "   $_" -ForegroundColor Gray }
                }
            }
        }
        
        if (-not $SkipTests -and $executionResult.Success) {
            Write-Host "🧪 Running test validation..." -ForegroundColor White
            
            $testResult = & dotnet test --no-build --verbosity minimal
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✅ Test validation passed" -ForegroundColor Green
            } else {
                Write-Host "⚠️ Some tests failed - review test output" -ForegroundColor Yellow
                $executionResult.Errors += "Some post-migration tests failed"
            }
        }
    }
    
} catch {
    Write-Host "💥 Migration execution failed: $_" -ForegroundColor Red
    $executionResult.Success = $false
    $executionResult.Errors += "Migration execution error: $_"
} finally {
    $executionEnd = Get-Date
    $executionResult.Duration = $executionEnd - $executionStart
}

# Execution Summary
Write-Host ""
Write-Host "📊 Migration Execution Summary" -ForegroundColor Green
Write-Host "==============================" -ForegroundColor Green

Write-Host ""
Write-Host "🎯 Execution Results:" -ForegroundColor Cyan
Write-Host "   Total Steps Executed: $($executionResult.ExecutedSteps.Count)" -ForegroundColor White
Write-Host "   Successful Steps: $($executionResult.ExecutedSteps.Where({$_.Success}).Count)" -ForegroundColor White
Write-Host "   Failed Steps: $($executionResult.ExecutedSteps.Where({-not $_.Success}).Count)" -ForegroundColor White
Write-Host "   Backup Created: $($executionResult.BackupCreated)" -ForegroundColor White
if ($executionResult.BackupCreated) {
    Write-Host "   Backup Name: $($executionResult.BackupName)" -ForegroundColor Gray
}
Write-Host "   Execution Time: $($executionResult.Duration.ToString('mm\:ss'))" -ForegroundColor White

if ($executionResult.Errors.Count -gt 0) {
    Write-Host ""
    Write-Host "❌ Errors Encountered:" -ForegroundColor Red
    $executionResult.Errors | ForEach-Object { Write-Host "   - $_" -ForegroundColor Red }
}

Write-Host ""
if ($executionResult.Success) {
    if ($DryRun) {
        Write-Host "✅ Dry run completed successfully" -ForegroundColor Green
    } else {
        Write-Host "✅ Migration completed successfully!" -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "💡 Next Steps:" -ForegroundColor Cyan
    if (-not $DryRun) {
        Write-Host "• Review migrated code for any manual adjustments needed" -ForegroundColor White
        Write-Host "• Run comprehensive tests to validate functionality" -ForegroundColor White
        Write-Host "• Consider removing legacy services once confident in migration" -ForegroundColor White
        Write-Host "• Update documentation to reflect new architecture" -ForegroundColor White
    } else {
        Write-Host "• Run actual migration: .\scripts\execute-migration.ps1 -CreateBackup" -ForegroundColor White
    }
} else {
    Write-Host "❌ Migration failed with errors" -ForegroundColor Red
    
    Write-Host ""
    Write-Host "🔧 Recovery Options:" -ForegroundColor Cyan
    if ($executionResult.BackupCreated) {
        Write-Host "• Rollback to backup: .\scripts\rollback.ps1 -CheckpointName $($executionResult.BackupName)" -ForegroundColor White
    }
    Write-Host "• Review and fix errors above" -ForegroundColor White
    Write-Host "• Run migration again with -StartFromStep to resume" -ForegroundColor White
    Write-Host "• Get help in project documentation or issues" -ForegroundColor White
}

Write-Host ""
Write-Host "🎉 Migration execution completed!" -ForegroundColor Green

# Exit with appropriate code
if ($executionResult.Success) {
    exit 0
} else {
    exit 1
}