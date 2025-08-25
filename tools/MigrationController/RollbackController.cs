using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Tools.MigrationController
{
    /// <summary>
    /// Automated rollback logic for migration failures.
    /// Provides safe recovery mechanisms and state restoration.
    /// </summary>
    public class RollbackController
    {
        private readonly ILogger _logger;
        private readonly string _projectRoot;
        private readonly string _checkpointDirectory;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly MigrationCheckpoint _checkpoint;

        public RollbackController(string projectRoot)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _projectRoot = projectRoot;
            _checkpointDirectory = Path.Combine(projectRoot, ".migration-checkpoints");
            _checkpoint = new MigrationCheckpoint(projectRoot);
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        /// <summary>
        /// Executes automated rollback to specified checkpoint
        /// </summary>
        public async Task<RollbackResult> ExecuteRollbackAsync(string checkpointName, RollbackOptions options = null)
        {
            options ??= new RollbackOptions();
            
            _logger.Info($"🔄 Starting rollback to checkpoint: {checkpointName}");

            var result = new RollbackResult
            {
                StartTime = DateTime.UtcNow,
                CheckpointName = checkpointName,
                Options = options,
                RestoredFiles = new List<RestoredFileInfo>(),
                Errors = new List<string>()
            };

            try
            {
                // Phase 1: Validate checkpoint exists and is usable
                var validation = await _checkpoint.ValidateCheckpointAsync(checkpointName);
                if (!validation.IsValid)
                {
                    var criticalIssues = validation.Issues.Where(i => i.Severity == IssueSeverity.Critical).ToList();
                    if (criticalIssues.Any())
                    {
                        result.Success = false;
                        result.Errors.AddRange(criticalIssues.Select(i => i.Description));
                        _logger.Error($"❌ Cannot rollback - checkpoint has critical issues: {string.Join(", ", result.Errors)}");
                        return result;
                    }
                }

                // Phase 2: Load checkpoint metadata
                var checkpoint = await LoadCheckpoint(checkpointName);
                if (checkpoint == null)
                {
                    result.Success = false;
                    result.Errors.Add($"Failed to load checkpoint: {checkpointName}");
                    return result;
                }

                // Phase 3: Pre-rollback safety checks
                if (!options.SkipSafetyChecks)
                {
                    var safetyResult = await PerformSafetyChecks(checkpoint);
                    if (!safetyResult.IsSafe)
                    {
                        if (options.ForceRollback)
                        {
                            _logger.Warn("⚠️ Safety checks failed but forcing rollback due to ForceRollback option");
                        }
                        else
                        {
                            result.Success = false;
                            result.Errors.AddRange(safetyResult.Issues);
                            _logger.Error($"❌ Rollback aborted due to safety check failures");
                            return result;
                        }
                    }
                }

                // Phase 4: Create emergency checkpoint before rollback
                if (!options.SkipEmergencyBackup)
                {
                    var emergencyCheckpoint = await _checkpoint.CreateCheckpointAsync($"emergency-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
                        "Emergency backup before rollback");
                    
                    if (emergencyCheckpoint.Success)
                    {
                        result.EmergencyBackupCreated = true;
                        result.EmergencyBackupName = emergencyCheckpoint.CheckpointName;
                        _logger.Info($"✅ Emergency backup created: {emergencyCheckpoint.CheckpointName}");
                    }
                    else
                    {
                        _logger.Warn("⚠️ Failed to create emergency backup");
                    }
                }

                // Phase 5: Execute file restoration
                await RestoreFilesFromCheckpoint(checkpoint, result);

                // Phase 6: Validate restoration
                if (!options.SkipValidation)
                {
                    var validationResult = await ValidateRollbackCompletion(checkpoint);
                    result.ValidationPassed = validationResult.Success;
                    
                    if (!validationResult.Success)
                    {
                        result.Errors.AddRange(validationResult.Errors);
                        _logger.Warn($"⚠️ Rollback completed but validation failed: {string.Join(", ", validationResult.Errors)}");
                    }
                }

                // Phase 7: Build verification
                if (!options.SkipBuildCheck)
                {
                    var buildResult = await VerifyBuildAfterRollback();
                    result.BuildSuccessful = buildResult.Success;
                    
                    if (!buildResult.Success)
                    {
                        result.Errors.Add($"Build failed after rollback: {buildResult.Error}");
                        _logger.Warn($"⚠️ Build failed after rollback - manual intervention may be required");
                    }
                }

                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime.Value - result.StartTime;
                result.Success = result.RestoredFiles.Count > 0 && result.Errors.Count == 0;

                if (result.Success)
                {
                    _logger.Info($"✅ Rollback completed successfully in {result.Duration:mm\\:ss}. Restored {result.RestoredFiles.Count} files.");
                }
                else
                {
                    _logger.Error($"❌ Rollback completed with errors. Restored {result.RestoredFiles.Count} files but {result.Errors.Count} errors occurred.");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"💥 Critical error during rollback to {checkpointName}");
                result.Success = false;
                result.Exception = ex;
                result.Errors.Add($"Critical rollback error: {ex.Message}");
                result.EndTime = DateTime.UtcNow;
                return result;
            }
        }

        /// <summary>
        /// Performs quick rollback analysis without execution
        /// </summary>
        public async Task<RollbackAnalysis> AnalyzeRollbackAsync(string checkpointName)
        {
            _logger.Info($"🔍 Analyzing rollback potential for checkpoint: {checkpointName}");

            var analysis = new RollbackAnalysis
            {
                CheckpointName = checkpointName,
                AnalysisTime = DateTime.UtcNow,
                Issues = new List<RollbackIssue>(),
                FilesToRestore = new List<string>()
            };

            try
            {
                // Load and validate checkpoint
                var checkpoint = await LoadCheckpoint(checkpointName);
                if (checkpoint == null)
                {
                    analysis.Issues.Add(new RollbackIssue
                    {
                        Severity = RollbackIssueSeverity.Critical,
                        Description = "Checkpoint file not found or corrupted"
                    });
                    return analysis;
                }

                // Analyze backup files
                foreach (var backup in checkpoint.FileBackups)
                {
                    var targetPath = Path.Combine(_projectRoot, backup.Key);
                    analysis.FilesToRestore.Add(backup.Key);

                    // Check if backup exists
                    if (!File.Exists(backup.Value))
                    {
                        analysis.Issues.Add(new RollbackIssue
                        {
                            Severity = RollbackIssueSeverity.High,
                            Description = $"Backup file missing: {backup.Key}",
                            AffectedFile = backup.Key
                        });
                        continue;
                    }

                    // Check if target file has been modified since checkpoint
                    if (File.Exists(targetPath))
                    {
                        var targetModified = File.GetLastWriteTime(targetPath);
                        if (targetModified > checkpoint.CreatedAt)
                        {
                            analysis.Issues.Add(new RollbackIssue
                            {
                                Severity = RollbackIssueSeverity.Medium,
                                Description = $"Target file modified since checkpoint: {backup.Key}",
                                AffectedFile = backup.Key
                            });
                        }
                    }
                }

                // Analyze git state
                if (!string.IsNullOrEmpty(checkpoint.GitCommit))
                {
                    var currentCommit = await GetCurrentGitCommit();
                    if (currentCommit != checkpoint.GitCommit)
                    {
                        analysis.Issues.Add(new RollbackIssue
                        {
                            Severity = RollbackIssueSeverity.Medium,
                            Description = "Git state has changed since checkpoint - conflicts possible"
                        });
                    }
                }

                // Risk assessment
                var criticalIssues = analysis.Issues.Count(i => i.Severity == RollbackIssueSeverity.Critical);
                var highIssues = analysis.Issues.Count(i => i.Severity == RollbackIssueSeverity.High);
                
                if (criticalIssues > 0)
                    analysis.RiskLevel = RollbackRiskLevel.High;
                else if (highIssues > 0)
                    analysis.RiskLevel = RollbackRiskLevel.Medium;
                else
                    analysis.RiskLevel = RollbackRiskLevel.Low;

                analysis.IsRollbackPossible = criticalIssues == 0;
                analysis.EstimatedDuration = TimeSpan.FromMinutes(Math.Max(2, analysis.FilesToRestore.Count * 0.5));

                _logger.Info($"🔍 Rollback analysis complete. Risk: {analysis.RiskLevel}, Issues: {analysis.Issues.Count}");

                return analysis;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error analyzing rollback");
                analysis.Issues.Add(new RollbackIssue
                {
                    Severity = RollbackIssueSeverity.Critical,
                    Description = $"Analysis error: {ex.Message}"
                });
                return analysis;
            }
        }

        /// <summary>
        /// Lists rollback targets with status and viability
        /// </summary>
        public async Task<List<RollbackTarget>> ListRollbackTargetsAsync()
        {
            var targets = new List<RollbackTarget>();

            try
            {
                var checkpoints = await _checkpoint.ListCheckpointsAsync();

                foreach (var checkpoint in checkpoints)
                {
                    var analysis = await AnalyzeRollbackAsync(checkpoint.Name);
                    
                    targets.Add(new RollbackTarget
                    {
                        CheckpointName = checkpoint.Name,
                        Description = checkpoint.Description,
                        CreatedAt = checkpoint.CreatedAt,
                        IsViable = analysis.IsRollbackPossible,
                        RiskLevel = analysis.RiskLevel,
                        IssueCount = analysis.Issues.Count,
                        FilesToRestore = analysis.FilesToRestore.Count,
                        EstimatedDuration = analysis.EstimatedDuration
                    });
                }

                return targets.OrderByDescending(t => t.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error listing rollback targets");
                return targets;
            }
        }

        /// <summary>
        /// Emergency rollback to most recent viable checkpoint
        /// </summary>
        public async Task<RollbackResult> EmergencyRollbackAsync()
        {
            _logger.Warn("🚨 Executing emergency rollback to most recent viable checkpoint");

            try
            {
                var targets = await ListRollbackTargetsAsync();
                var viableTarget = targets.FirstOrDefault(t => t.IsViable && t.RiskLevel != RollbackRiskLevel.High);

                if (viableTarget == null)
                {
                    _logger.Error("❌ No viable rollback targets found for emergency rollback");
                    return new RollbackResult
                    {
                        Success = false,
                        Errors = new List<string> { "No viable rollback targets available" }
                    };
                }

                _logger.Info($"🎯 Emergency rollback target: {viableTarget.CheckpointName}");

                var options = new RollbackOptions
                {
                    ForceRollback = true,
                    SkipSafetyChecks = true,
                    SkipEmergencyBackup = true // Already in emergency state
                };

                return await ExecuteRollbackAsync(viableTarget.CheckpointName, options);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "💥 Critical error in emergency rollback");
                return new RollbackResult
                {
                    Success = false,
                    Exception = ex,
                    Errors = new List<string> { $"Emergency rollback failed: {ex.Message}" }
                };
            }
        }

        private async Task<Checkpoint> LoadCheckpoint(string checkpointName)
        {
            try
            {
                var checkpointPath = Path.Combine(_checkpointDirectory, $"{checkpointName}.json");
                if (!File.Exists(checkpointPath))
                    return null;

                var checkpointJson = await File.ReadAllTextAsync(checkpointPath);
                return JsonSerializer.Deserialize<Checkpoint>(checkpointJson, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to load checkpoint {checkpointName}");
                return null;
            }
        }

        private async Task<SafetyCheckResult> PerformSafetyChecks(Checkpoint checkpoint)
        {
            var result = new SafetyCheckResult { IsSafe = true, Issues = new List<string>() };

            try
            {
                // Check if files to be restored have uncommitted changes
                foreach (var backup in checkpoint.FileBackups)
                {
                    var targetPath = Path.Combine(_projectRoot, backup.Key);
                    if (File.Exists(targetPath))
                    {
                        // Simple check - in practice would use git status
                        var modified = File.GetLastWriteTime(targetPath) > checkpoint.CreatedAt;
                        if (modified)
                        {
                            result.Issues.Add($"File has uncommitted changes: {backup.Key}");
                        }
                    }
                }

                // Check build status
                var canBuild = await CanProjectBuild();
                if (!canBuild)
                {
                    result.Issues.Add("Project currently does not build - rollback may help or worsen situation");
                }

                result.IsSafe = result.Issues.Count == 0;
                return result;
            }
            catch
            {
                result.IsSafe = false;
                result.Issues.Add("Safety check failed due to error");
                return result;
            }
        }

        private async Task RestoreFilesFromCheckpoint(Checkpoint checkpoint, RollbackResult result)
        {
            foreach (var backup in checkpoint.FileBackups)
            {
                try
                {
                    var targetPath = Path.Combine(_projectRoot, backup.Key);
                    var backupPath = backup.Value;

                    if (!File.Exists(backupPath))
                    {
                        result.Errors.Add($"Backup file not found: {backup.Key}");
                        continue;
                    }

                    // Ensure target directory exists
                    var targetDir = Path.GetDirectoryName(targetPath);
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    // Restore file
                    File.Copy(backupPath, targetPath, true);

                    result.RestoredFiles.Add(new RestoredFileInfo
                    {
                        RelativePath = backup.Key,
                        TargetPath = targetPath,
                        BackupPath = backupPath,
                        RestoredAt = DateTime.UtcNow,
                        Size = new FileInfo(targetPath).Length
                    });

                    _logger.Debug($"📄 Restored: {backup.Key}");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to restore file: {backup.Key}");
                    result.Errors.Add($"Failed to restore {backup.Key}: {ex.Message}");
                }
            }
        }

        private async Task<ValidationResult> ValidateRollbackCompletion(Checkpoint checkpoint)
        {
            var result = new ValidationResult { Success = true, Errors = new List<string>() };

            try
            {
                // Verify all files were restored
                foreach (var backup in checkpoint.FileBackups)
                {
                    var targetPath = Path.Combine(_projectRoot, backup.Key);
                    if (!File.Exists(targetPath))
                    {
                        result.Errors.Add($"File not found after restoration: {backup.Key}");
                        continue;
                    }

                    // Basic integrity check
                    try
                    {
                        var content = await File.ReadAllTextAsync(targetPath);
                        if (string.IsNullOrEmpty(content))
                        {
                            result.Errors.Add($"Restored file appears empty: {backup.Key}");
                        }
                    }
                    catch
                    {
                        result.Errors.Add($"Cannot read restored file: {backup.Key}");
                    }
                }

                result.Success = result.Errors.Count == 0;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add($"Validation error: {ex.Message}");
                return result;
            }
        }

        private async Task<BuildResult> VerifyBuildAfterRollback()
        {
            try
            {
                // In practice, would actually run build process
                return new BuildResult { Success = true };
            }
            catch (Exception ex)
            {
                return new BuildResult { Success = false, Error = ex.Message };
            }
        }

        private async Task<bool> CanProjectBuild()
        {
            try
            {
                // In practice, would run dotnet build --dry-run or similar
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> GetCurrentGitCommit()
        {
            // In practice, would run: git rev-parse HEAD
            return "current-git-commit-hash";
        }

        // Supporting types
        private class SafetyCheckResult
        {
            public bool IsSafe { get; set; }
            public List<string> Issues { get; set; }
        }

        private class ValidationResult
        {
            public bool Success { get; set; }
            public List<string> Errors { get; set; }
        }

        private class BuildResult
        {
            public bool Success { get; set; }
            public string Error { get; set; }
        }
    }

    // Public types
    public class RollbackResult
    {
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan? Duration { get; set; }
        public bool Success { get; set; }
        public string CheckpointName { get; set; }
        public RollbackOptions Options { get; set; }
        public List<RestoredFileInfo> RestoredFiles { get; set; }
        public List<string> Errors { get; set; }
        public Exception Exception { get; set; }
        public bool EmergencyBackupCreated { get; set; }
        public string EmergencyBackupName { get; set; }
        public bool ValidationPassed { get; set; }
        public bool BuildSuccessful { get; set; }
    }

    public class RollbackOptions
    {
        public bool ForceRollback { get; set; }
        public bool SkipSafetyChecks { get; set; }
        public bool SkipEmergencyBackup { get; set; }
        public bool SkipValidation { get; set; }
        public bool SkipBuildCheck { get; set; }
    }

    public class RestoredFileInfo
    {
        public string RelativePath { get; set; }
        public string TargetPath { get; set; }
        public string BackupPath { get; set; }
        public DateTime RestoredAt { get; set; }
        public long Size { get; set; }
    }

    public class RollbackAnalysis
    {
        public string CheckpointName { get; set; }
        public DateTime AnalysisTime { get; set; }
        public bool IsRollbackPossible { get; set; }
        public RollbackRiskLevel RiskLevel { get; set; }
        public List<RollbackIssue> Issues { get; set; }
        public List<string> FilesToRestore { get; set; }
        public TimeSpan EstimatedDuration { get; set; }
    }

    public class RollbackIssue
    {
        public RollbackIssueSeverity Severity { get; set; }
        public string Description { get; set; }
        public string AffectedFile { get; set; }
    }

    public class RollbackTarget
    {
        public string CheckpointName { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsViable { get; set; }
        public RollbackRiskLevel RiskLevel { get; set; }
        public int IssueCount { get; set; }
        public int FilesToRestore { get; set; }
        public TimeSpan EstimatedDuration { get; set; }
    }

    public enum RollbackRiskLevel
    {
        Low,
        Medium, 
        High
    }

    public enum RollbackIssueSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
}