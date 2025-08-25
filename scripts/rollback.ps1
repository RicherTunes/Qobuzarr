# =============================================================================
# Qobuzarr Migration Rollback Script (PowerShell)
# =============================================================================
# Emergency rollback scripts for migration failures

param(
    [string]$CheckpointName = "",
    [switch]$Emergency,
    [switch]$ListCheckpoints,
    [switch]$Force,
    [switch]$SkipValidation,
    [switch]$VerboseOutput,
    [switch]$Help
)

function Show-Help {
    Write-Host "🔄 Qobuzarr Migration Rollback Script" -ForegroundColor Green
    Write-Host ""
    Write-Host "USAGE:" -ForegroundColor Cyan
    Write-Host "  .\scripts\rollback.ps1 [Options]" -ForegroundColor White
    Write-Host ""
    Write-Host "OPTIONS:" -ForegroundColor Cyan
    Write-Host "  -CheckpointName [name]  Rollback to specific checkpoint" -ForegroundColor White
    Write-Host "  -Emergency              Emergency rollback to most recent safe checkpoint" -ForegroundColor White
    Write-Host "  -ListCheckpoints        List available checkpoints for rollback" -ForegroundColor White
    Write-Host "  -Force                  Force rollback even if safety checks fail" -ForegroundColor White
    Write-Host "  -SkipValidation         Skip post-rollback validation" -ForegroundColor White
    Write-Host "  -VerboseOutput          Show detailed rollback output" -ForegroundColor White
    Write-Host "  -Help                   Show this help" -ForegroundColor White
    Write-Host ""
    Write-Host "EXAMPLES:" -ForegroundColor Cyan
    Write-Host "  .\scripts\rollback.ps1 -ListCheckpoints                      # Show available checkpoints" -ForegroundColor Gray
    Write-Host "  .\scripts\rollback.ps1 -CheckpointName pre-migration-123     # Rollback to specific checkpoint" -ForegroundColor Gray
    Write-Host "  .\scripts\rollback.ps1 -Emergency                            # Emergency rollback" -ForegroundColor Gray
    Write-Host "  .\scripts\rollback.ps1 -CheckpointName backup-123 -Force     # Force rollback" -ForegroundColor Gray
    Write-Host ""
    Write-Host "SAFETY:" -ForegroundColor Cyan
    Write-Host "  🚨 This script will overwrite current files - ensure you understand the consequences" -ForegroundColor Red
    Write-Host "  💾 A new emergency backup will be created before rollback" -ForegroundColor Yellow
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

Write-Host "🔄 Qobuzarr Migration Rollback" -ForegroundColor Green
Write-Host "==============================" -ForegroundColor Green

# Check if checkpoints directory exists
$checkpointDir = ".migration-checkpoints"
if (-not (Test-Path $checkpointDir)) {
    Write-Host "❌ No checkpoints directory found" -ForegroundColor Red
    Write-Host "   Expected: $checkpointDir" -ForegroundColor Yellow
    Write-Host "   No rollback possible without checkpoints" -ForegroundColor Yellow
    exit 1
}

# List checkpoints if requested
if ($ListCheckpoints) {
    Write-Host ""
    Write-Host "📋 Available Checkpoints" -ForegroundColor Blue
    
    $checkpoints = Get-ChildItem -Path $checkpointDir -Directory | Sort-Object LastWriteTime -Descending
    
    if ($checkpoints.Count -eq 0) {
        Write-Host "   No checkpoints found" -ForegroundColor Yellow
        exit 0
    }
    
    Write-Host ""
    Write-Host "   Name                              Created              Files" -ForegroundColor Cyan
    Write-Host "   ─────────────────────────────────────────────────────────────" -ForegroundColor Gray
    
    foreach ($checkpoint in $checkpoints) {
        $manifestPath = Join-Path $checkpoint.FullName "backup-manifest.json"
        $fileCount = "?"
        
        if (Test-Path $manifestPath) {
            try {
                $manifest = Get-Content $manifestPath | ConvertFrom-Json
                $fileCount = $manifest.fileCount
            } catch {
                $fileCount = "?"
            }
        }
        
        $nameColumn = $checkpoint.Name.PadRight(35)
        $dateColumn = $checkpoint.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss").PadRight(20)
        Write-Host "   $nameColumn $dateColumn $fileCount" -ForegroundColor White
    }
    
    Write-Host ""
    Write-Host "💡 To rollback to a checkpoint:" -ForegroundColor Cyan
    Write-Host "   .\scripts\rollback.ps1 -CheckpointName <name>" -ForegroundColor White
    
    exit 0
}

# Initialize rollback tracking
$rollbackStart = Get-Date
$rollbackResult = @{
    Success = $true
    CheckpointName = ""
    FilesRestored = 0
    Errors = @()
    EmergencyBackupCreated = $false
    EmergencyBackupName = ""
}

try {
    # Determine target checkpoint
    $targetCheckpoint = ""
    
    if ($Emergency) {
        Write-Host ""
        Write-Host "🚨 Emergency Rollback Mode" -ForegroundColor Red
        Write-Host "Finding most recent safe checkpoint..." -ForegroundColor Yellow
        
        # Find most recent checkpoint
        $checkpoints = Get-ChildItem -Path $checkpointDir -Directory | Sort-Object LastWriteTime -Descending
        
        if ($checkpoints.Count -eq 0) {
            throw "No checkpoints available for emergency rollback"
        }
        
        $targetCheckpoint = $checkpoints[0].Name
        Write-Host "🎯 Emergency target: $targetCheckpoint" -ForegroundColor Yellow
        
    } elseif ($CheckpointName -ne "") {
        $targetCheckpoint = $CheckpointName
        
        # Verify checkpoint exists
        $checkpointPath = Join-Path $checkpointDir $targetCheckpoint
        if (-not (Test-Path $checkpointPath)) {
            throw "Checkpoint not found: $targetCheckpoint"
        }
        
    } else {
        Write-Host "❌ No checkpoint specified for rollback" -ForegroundColor Red
        Write-Host ""
        Write-Host "💡 Available options:" -ForegroundColor Cyan
        Write-Host "• List checkpoints: .\scripts\rollback.ps1 -ListCheckpoints" -ForegroundColor White
        Write-Host "• Emergency rollback: .\scripts\rollback.ps1 -Emergency" -ForegroundColor White
        Write-Host "• Specific checkpoint: .\scripts\rollback.ps1 -CheckpointName <name>" -ForegroundColor White
        exit 1
    }
    
    $rollbackResult.CheckpointName = $targetCheckpoint
    
    Write-Host ""
    Write-Host "🔍 Phase 1: Rollback Analysis" -ForegroundColor Blue
    
    $checkpointPath = Join-Path $checkpointDir $targetCheckpoint
    $manifestPath = Join-Path $checkpointPath "backup-manifest.json"
    
    # Load checkpoint manifest
    if (Test-Path $manifestPath) {
        try {
            $manifest = Get-Content $manifestPath | ConvertFrom-Json
            Write-Host "📄 Checkpoint manifest loaded" -ForegroundColor White
            Write-Host "   Created: $($manifest.createdAt)" -ForegroundColor Gray
            Write-Host "   Files: $($manifest.fileCount)" -ForegroundColor Gray
            
            if ($VerboseOutput) {
                Write-Host "   Project Root: $($manifest.projectRoot)" -ForegroundColor Gray
            }
        } catch {
            Write-Host "⚠️ Could not read checkpoint manifest - proceeding with basic rollback" -ForegroundColor Yellow
        }
    }
    
    # Analyze files to restore
    $filesToRestore = Get-ChildItem -Path $checkpointPath -Filter "*.cs" -File
    Write-Host "📁 Files to restore: $($filesToRestore.Count)" -ForegroundColor White
    
    if ($filesToRestore.Count -eq 0) {
        throw "No files found in checkpoint: $targetCheckpoint"
    }
    
    # Safety checks (unless forced or skipped)
    if (-not $Force -and -not $Emergency) {
        Write-Host ""
        Write-Host "🔍 Phase 2: Safety Checks" -ForegroundColor Blue
        
        $safetyIssues = @()
        
        # Check if current files have been modified since checkpoint
        $checkpointDate = (Get-Item $checkpointPath).LastWriteTime
        
        foreach ($file in $filesToRestore) {
            $targetPath = "src\Services\$($file.Name)"
            
            # Check various common locations
            $possiblePaths = @(
                "src\Services\$($file.Name)",
                "src\Core\$($file.Name)",
                "src\Services\Consolidated\$($file.Name)"
            )
            
            foreach ($possiblePath in $possiblePaths) {
                if (Test-Path $possiblePath) {
                    $targetFile = Get-Item $possiblePath
                    if ($targetFile.LastWriteTime -gt $checkpointDate) {
                        $safetyIssues += "File modified since checkpoint: $possiblePath"
                    }
                    break
                }
            }
        }
        
        if ($safetyIssues.Count -gt 0) {
            Write-Host "⚠️ Safety check warnings:" -ForegroundColor Yellow
            $safetyIssues | ForEach-Object { Write-Host "   - $_" -ForegroundColor Yellow }
            
            if (-not $Force) {
                Write-Host ""
                Write-Host "Continue rollback despite warnings? This will overwrite current changes." -ForegroundColor Yellow
                Write-Host "Type 'yes' to continue, anything else to abort: " -ForegroundColor Yellow -NoNewline
                $response = Read-Host
                
                if ($response -ne "yes") {
                    Write-Host "Rollback aborted by user" -ForegroundColor Yellow
                    exit 0
                }
            }
        } else {
            Write-Host "✅ Safety checks passed" -ForegroundColor Green
        }
    }
    
    # Create emergency backup before rollback
    Write-Host ""
    Write-Host "💾 Phase 3: Emergency Backup" -ForegroundColor Blue
    
    $emergencyBackupName = "emergency-pre-rollback-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    $emergencyBackupPath = Join-Path $checkpointDir $emergencyBackupName
    
    try {
        New-Item -ItemType Directory -Path $emergencyBackupPath -Force | Out-Null
        
        # Backup current state of files that will be restored
        $backedUpCount = 0
        foreach ($file in $filesToRestore) {
            $possiblePaths = @(
                "src\Services\$($file.Name)",
                "src\Core\$($file.Name)",
                "src\Services\Consolidated\$($file.Name)"
            )
            
            foreach ($possiblePath in $possiblePaths) {
                if (Test-Path $possiblePath) {
                    $backupFilePath = Join-Path $emergencyBackupPath $file.Name
                    Copy-Item $possiblePath $backupFilePath -Force
                    $backedUpCount++
                    break
                }
            }
        }
        
        # Create emergency backup manifest
        $emergencyManifest = @{
            backupName = $emergencyBackupName
            createdAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            fileCount = $backedUpCount
            projectRoot = (Get-Location).Path
            rollbackTarget = $targetCheckpoint
            migrationVersion = "2.0.0"
        }
        
        $emergencyManifestPath = Join-Path $emergencyBackupPath "backup-manifest.json"
        $emergencyManifest | ConvertTo-Json -Depth 3 | Set-Content $emergencyManifestPath
        
        Write-Host "✅ Emergency backup created: $backedUpCount files" -ForegroundColor Green
        $rollbackResult.EmergencyBackupCreated = $true
        $rollbackResult.EmergencyBackupName = $emergencyBackupName
        
    } catch {
        Write-Host "⚠️ Failed to create emergency backup: $_" -ForegroundColor Yellow
        $rollbackResult.Errors += "Emergency backup failed: $_"
    }
    
    # Execute rollback
    Write-Host ""
    Write-Host "🔄 Phase 4: File Restoration" -ForegroundColor Blue
    
    foreach ($file in $filesToRestore) {
        try {
            $sourceFile = $file.FullName
            
            # Determine target location
            $targetPath = ""
            $possiblePaths = @(
                "src\Services\$($file.Name)",
                "src\Core\$($file.Name)",
                "src\Services\Consolidated\$($file.Name)"
            )
            
            # Use existing file location if found, otherwise default to Services
            foreach ($possiblePath in $possiblePaths) {
                if (Test-Path $possiblePath) {
                    $targetPath = $possiblePath
                    break
                }
            }
            
            if ($targetPath -eq "") {
                $targetPath = "src\Services\$($file.Name)"
            }
            
            # Ensure target directory exists
            $targetDir = Split-Path $targetPath -Parent
            if (-not (Test-Path $targetDir)) {
                New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
            }
            
            # Restore file
            Copy-Item $sourceFile $targetPath -Force
            $rollbackResult.FilesRestored++
            
            if ($VerboseOutput) {
                Write-Host "   📄 Restored: $targetPath" -ForegroundColor Gray
            }
            
        } catch {
            Write-Host "❌ Failed to restore $($file.Name): $_" -ForegroundColor Red
            $rollbackResult.Errors += "Failed to restore $($file.Name): $_"
        }
    }
    
    Write-Host "✅ Restored $($rollbackResult.FilesRestored) files" -ForegroundColor Green
    
    # Post-rollback validation
    if (-not $SkipValidation) {
        Write-Host ""
        Write-Host "🔍 Phase 5: Post-Rollback Validation" -ForegroundColor Blue
        
        Write-Host "🔨 Testing build after rollback..." -ForegroundColor White
        
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
            Write-Host "✅ Post-rollback build successful" -ForegroundColor Green
        } else {
            Write-Host "⚠️ Post-rollback build issues detected" -ForegroundColor Yellow
            $rollbackResult.Errors += "Post-rollback build failed"
            
            if ($VerboseOutput) {
                Write-Host "Build output:" -ForegroundColor Gray
                $buildResult | ForEach-Object { Write-Host "   $_" -ForegroundColor Gray }
            }
        }
    }
    
} catch {
    Write-Host "💥 Critical rollback error: $_" -ForegroundColor Red
    $rollbackResult.Success = $false
    $rollbackResult.Errors += "Critical rollback error: $_"
}

# Rollback Summary
Write-Host ""
Write-Host "📊 Rollback Summary" -ForegroundColor Green
Write-Host "===================" -ForegroundColor Green

$rollbackEnd = Get-Date
$rollbackDuration = $rollbackEnd - $rollbackStart

Write-Host ""
Write-Host "🔄 Rollback Results:" -ForegroundColor Cyan
Write-Host "   Target Checkpoint: $($rollbackResult.CheckpointName)" -ForegroundColor White
Write-Host "   Files Restored: $($rollbackResult.FilesRestored)" -ForegroundColor White
Write-Host "   Emergency Backup Created: $($rollbackResult.EmergencyBackupCreated)" -ForegroundColor White
if ($rollbackResult.EmergencyBackupCreated) {
    Write-Host "   Emergency Backup Name: $($rollbackResult.EmergencyBackupName)" -ForegroundColor Gray
}
Write-Host "   Rollback Time: $($rollbackDuration.ToString('mm\:ss'))" -ForegroundColor White

if ($rollbackResult.Errors.Count -gt 0) {
    Write-Host ""
    Write-Host "⚠️ Issues Encountered:" -ForegroundColor Yellow
    $rollbackResult.Errors | ForEach-Object { Write-Host "   - $_" -ForegroundColor Yellow }
}

Write-Host ""
if ($rollbackResult.Success -and $rollbackResult.FilesRestored -gt 0) {
    Write-Host "✅ Rollback completed successfully!" -ForegroundColor Green
    
    Write-Host ""
    Write-Host "💡 Next Steps:" -ForegroundColor Cyan
    Write-Host "• Verify that the application works as expected" -ForegroundColor White
    Write-Host "• Review what caused the need for rollback" -ForegroundColor White
    Write-Host "• Fix migration issues before attempting again" -ForegroundColor White
    if ($rollbackResult.EmergencyBackupCreated) {
        Write-Host "• Emergency backup is available: $($rollbackResult.EmergencyBackupName)" -ForegroundColor White
    }
    
} else {
    Write-Host "❌ Rollback completed with issues" -ForegroundColor Red
    
    Write-Host ""
    Write-Host "🔧 Recovery Options:" -ForegroundColor Cyan
    Write-Host "• Review errors above and address manually" -ForegroundColor White
    Write-Host "• Try rollback with -Force if safety checks are blocking" -ForegroundColor White
    if ($rollbackResult.EmergencyBackupCreated) {
        Write-Host "• Emergency backup available if further rollback needed" -ForegroundColor White
    }
    Write-Host "• Seek help in project documentation or issues" -ForegroundColor White
}

Write-Host ""
Write-Host "🎉 Rollback operation completed!" -ForegroundColor Green

# Exit with appropriate code
if ($rollbackResult.Success -and $rollbackResult.FilesRestored -gt 0) {
    exit 0
} else {
    exit 1
}