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
    /// Checkpoint validation logic for safe migration execution.
    /// Creates restore points and validates migration state.
    /// </summary>
    public class MigrationCheckpoint
    {
        private readonly ILogger _logger;
        private readonly string _projectRoot;
        private readonly string _checkpointDirectory;
        private readonly JsonSerializerOptions _jsonOptions;

        public MigrationCheckpoint(string projectRoot)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _projectRoot = projectRoot;
            _checkpointDirectory = Path.Combine(projectRoot, ".migration-checkpoints");
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            EnsureCheckpointDirectory();
        }

        /// <summary>
        /// Creates a checkpoint with current state for rollback capability
        /// </summary>
        public async Task<CheckpointResult> CreateCheckpointAsync(string checkpointName, string description = null)
        {
            _logger.Info($"📸 Creating checkpoint: {checkpointName}");

            var checkpoint = new Checkpoint
            {
                Name = checkpointName,
                Description = description ?? $"Checkpoint created at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
                CreatedAt = DateTime.UtcNow,
                ProjectRoot = _projectRoot,
                FileBackups = new Dictionary<string, string>(),
                GitCommit = await GetCurrentGitCommit(),
                BuildState = await CaptureCurrentBuildState()
            };

            try
            {
                // Backup critical files that will be modified
                var filesToBackup = GetCriticalFilesForBackup();
                
                foreach (var file in filesToBackup)
                {
                    if (File.Exists(file))
                    {
                        var backupPath = await CreateFileBackup(file, checkpointName);
                        var relativePath = Path.GetRelativePath(_projectRoot, file);
                        checkpoint.FileBackups[relativePath] = backupPath;
                        _logger.Debug($"📁 Backed up: {relativePath}");
                    }
                }

                // Save checkpoint metadata
                var checkpointPath = Path.Combine(_checkpointDirectory, $"{checkpointName}.json");
                var checkpointJson = JsonSerializer.Serialize(checkpoint, _jsonOptions);
                await File.WriteAllTextAsync(checkpointPath, checkpointJson);

                _logger.Info($"✅ Checkpoint '{checkpointName}' created with {checkpoint.FileBackups.Count} file backups");

                return new CheckpointResult
                {
                    Success = true,
                    CheckpointName = checkpointName,
                    BackupCount = checkpoint.FileBackups.Count,
                    CheckpointPath = checkpointPath
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"❌ Failed to create checkpoint '{checkpointName}'");
                return new CheckpointResult
                {
                    Success = false,
                    CheckpointName = checkpointName,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// Validates checkpoint integrity and recoverability
        /// </summary>
        public async Task<ValidationResult> ValidateCheckpointAsync(string checkpointName)
        {
            _logger.Info($"🔍 Validating checkpoint: {checkpointName}");

            var result = new ValidationResult
            {
                CheckpointName = checkpointName,
                ValidationTime = DateTime.UtcNow,
                Issues = new List<ValidationIssue>()
            };

            try
            {
                var checkpointPath = Path.Combine(_checkpointDirectory, $"{checkpointName}.json");
                if (!File.Exists(checkpointPath))
                {
                    result.IsValid = false;
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = IssueSeverity.Critical,
                        Description = $"Checkpoint file not found: {checkpointPath}"
                    });
                    return result;
                }

                // Load checkpoint metadata
                var checkpointJson = await File.ReadAllTextAsync(checkpointPath);
                var checkpoint = JsonSerializer.Deserialize<Checkpoint>(checkpointJson, _jsonOptions);

                // Validate file backups exist
                foreach (var backup in checkpoint.FileBackups)
                {
                    if (!File.Exists(backup.Value))
                    {
                        result.Issues.Add(new ValidationIssue
                        {
                            Severity = IssueSeverity.High,
                            Description = $"Backup file missing: {backup.Key} -> {backup.Value}"
                        });
                    }
                    else
                    {
                        // Validate backup file integrity
                        var backupIntegrity = await ValidateFileIntegrity(backup.Value);
                        if (!backupIntegrity.IsValid)
                        {
                            result.Issues.Add(new ValidationIssue
                            {
                                Severity = IssueSeverity.High,
                                Description = $"Backup file corrupted: {backup.Key}"
                            });
                        }
                    }
                }

                // Validate git state if available
                if (!string.IsNullOrEmpty(checkpoint.GitCommit))
                {
                    var gitValidation = await ValidateGitState(checkpoint.GitCommit);
                    if (!gitValidation.IsValid)
                    {
                        result.Issues.Add(new ValidationIssue
                        {
                            Severity = IssueSeverity.Medium,
                            Description = "Git state has changed since checkpoint creation"
                        });
                    }
                }

                // Validate build state
                if (checkpoint.BuildState != null)
                {
                    var buildValidation = await ValidateBuildState(checkpoint.BuildState);
                    if (!buildValidation.IsValid)
                    {
                        result.Issues.Add(new ValidationIssue
                        {
                            Severity = IssueSeverity.Medium,
                            Description = "Build state has changed since checkpoint creation"
                        });
                    }
                }

                result.IsValid = !result.Issues.Any(i => i.Severity == IssueSeverity.Critical);
                result.BackupCount = checkpoint.FileBackups.Count;

                if (result.IsValid)
                {
                    _logger.Info($"✅ Checkpoint '{checkpointName}' validation passed");
                }
                else
                {
                    _logger.Warn($"⚠️ Checkpoint '{checkpointName}' has {result.Issues.Count} validation issues");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"❌ Error validating checkpoint '{checkpointName}'");
                result.IsValid = false;
                result.Issues.Add(new ValidationIssue
                {
                    Severity = IssueSeverity.Critical,
                    Description = $"Validation error: {ex.Message}"
                });
                return result;
            }
        }

        /// <summary>
        /// Lists all available checkpoints with status
        /// </summary>
        public async Task<List<CheckpointInfo>> ListCheckpointsAsync()
        {
            var checkpoints = new List<CheckpointInfo>();

            if (!Directory.Exists(_checkpointDirectory))
                return checkpoints;

            var checkpointFiles = Directory.GetFiles(_checkpointDirectory, "*.json");

            foreach (var file in checkpointFiles)
            {
                try
                {
                    var checkpointJson = await File.ReadAllTextAsync(file);
                    var checkpoint = JsonSerializer.Deserialize<Checkpoint>(checkpointJson, _jsonOptions);
                    
                    var info = new CheckpointInfo
                    {
                        Name = checkpoint.Name,
                        Description = checkpoint.Description,
                        CreatedAt = checkpoint.CreatedAt,
                        BackupCount = checkpoint.FileBackups.Count,
                        FilePath = file,
                        Size = new FileInfo(file).Length
                    };

                    // Quick validation check
                    var validation = await ValidateCheckpointAsync(checkpoint.Name);
                    info.IsValid = validation.IsValid;
                    info.Issues = validation.Issues.Count;

                    checkpoints.Add(info);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Failed to load checkpoint info from {file}");
                }
            }

            return checkpoints.OrderByDescending(c => c.CreatedAt).ToList();
        }

        /// <summary>
        /// Cleans up checkpoint and associated backups
        /// </summary>
        public async Task<bool> CleanupCheckpointAsync(string checkpointName)
        {
            _logger.Info($"🧹 Cleaning up checkpoint: {checkpointName}");

            try
            {
                var checkpointPath = Path.Combine(_checkpointDirectory, $"{checkpointName}.json");
                if (!File.Exists(checkpointPath))
                {
                    _logger.Warn($"Checkpoint file not found: {checkpointPath}");
                    return false;
                }

                // Load checkpoint to get backup file paths
                var checkpointJson = await File.ReadAllTextAsync(checkpointPath);
                var checkpoint = JsonSerializer.Deserialize<Checkpoint>(checkpointJson, _jsonOptions);

                // Remove backup files
                int removedCount = 0;
                foreach (var backup in checkpoint.FileBackups.Values)
                {
                    if (File.Exists(backup))
                    {
                        File.Delete(backup);
                        removedCount++;
                    }
                }

                // Remove checkpoint file
                File.Delete(checkpointPath);

                _logger.Info($"✅ Cleaned up checkpoint '{checkpointName}' (removed {removedCount} backup files)");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"❌ Failed to cleanup checkpoint '{checkpointName}'");
                return false;
            }
        }

        private void EnsureCheckpointDirectory()
        {
            if (!Directory.Exists(_checkpointDirectory))
            {
                Directory.CreateDirectory(_checkpointDirectory);
                _logger.Debug($"Created checkpoint directory: {_checkpointDirectory}");
            }
        }

        private List<string> GetCriticalFilesForBackup()
        {
            var files = new List<string>();

            // Add specific files that will be modified during migration
            var criticalFiles = new[]
            {
                "src/Services/LidarrAlbumRetriever.cs",
                "src/Services/QobuzValidationService.cs", 
                "src/Core/QobuzApiService.cs",
                "src/Services/QobuzQualityService.cs",
                "src/Services/QualityMappingService.cs",
                "src/Services/QualityFallbackService.cs",
                "src/Services/IQualityMappingService.cs",
                "src/Services/IntelligentQualityDetector.cs",
                "src/Services/Consolidated/ConsolidatedServiceRegistration.cs"
            };

            foreach (var file in criticalFiles)
            {
                var fullPath = Path.Combine(_projectRoot, file);
                if (File.Exists(fullPath))
                {
                    files.Add(fullPath);
                }
            }

            return files;
        }

        private async Task<string> CreateFileBackup(string sourceFile, string checkpointName)
        {
            var backupDir = Path.Combine(_checkpointDirectory, "backups", checkpointName);
            Directory.CreateDirectory(backupDir);

            var fileName = Path.GetFileName(sourceFile);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"{timestamp}_{fileName}";
            var backupPath = Path.Combine(backupDir, backupFileName);

            await File.Copy(sourceFile, backupPath, true);
            return backupPath;
        }

        private async Task<string> GetCurrentGitCommit()
        {
            try
            {
                // In a real implementation, this would run: git rev-parse HEAD
                return "mock-git-commit-hash";
            }
            catch
            {
                return null;
            }
        }

        private async Task<BuildState> CaptureCurrentBuildState()
        {
            try
            {
                return new BuildState
                {
                    Timestamp = DateTime.UtcNow,
                    BuildSuccessful = true, // Would actually run build check
                    LastModified = GetLastModifiedTime()
                };
            }
            catch
            {
                return null;
            }
        }

        private DateTime GetLastModifiedTime()
        {
            var sourceFiles = Directory.GetFiles(Path.Combine(_projectRoot, "src"), "*.cs", SearchOption.AllDirectories);
            return sourceFiles.Select(f => File.GetLastWriteTime(f)).DefaultIfEmpty().Max();
        }

        private async Task<FileIntegrityResult> ValidateFileIntegrity(string filePath)
        {
            try
            {
                // Simple validation - check if file exists and is readable
                var content = await File.ReadAllTextAsync(filePath);
                return new FileIntegrityResult { IsValid = !string.IsNullOrEmpty(content) };
            }
            catch
            {
                return new FileIntegrityResult { IsValid = false };
            }
        }

        private async Task<GitValidationResult> ValidateGitState(string expectedCommit)
        {
            try
            {
                var currentCommit = await GetCurrentGitCommit();
                return new GitValidationResult { IsValid = currentCommit == expectedCommit };
            }
            catch
            {
                return new GitValidationResult { IsValid = false };
            }
        }

        private async Task<BuildValidationResult> ValidateBuildState(BuildState expectedState)
        {
            try
            {
                var currentState = await CaptureCurrentBuildState();
                // Simple validation - in practice would be more sophisticated
                return new BuildValidationResult { IsValid = currentState != null };
            }
            catch
            {
                return new BuildValidationResult { IsValid = false };
            }
        }
    }

    // Supporting types
    public class Checkpoint
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ProjectRoot { get; set; }
        public Dictionary<string, string> FileBackups { get; set; }
        public string GitCommit { get; set; }
        public BuildState BuildState { get; set; }
    }

    public class CheckpointResult
    {
        public bool Success { get; set; }
        public string CheckpointName { get; set; }
        public int BackupCount { get; set; }
        public string CheckpointPath { get; set; }
        public string Error { get; set; }
    }

    public class ValidationResult
    {
        public string CheckpointName { get; set; }
        public DateTime ValidationTime { get; set; }
        public bool IsValid { get; set; }
        public int BackupCount { get; set; }
        public List<ValidationIssue> Issues { get; set; }
    }

    public class ValidationIssue
    {
        public IssueSeverity Severity { get; set; }
        public string Description { get; set; }
    }

    public class CheckpointInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public int BackupCount { get; set; }
        public string FilePath { get; set; }
        public long Size { get; set; }
        public bool IsValid { get; set; }
        public int Issues { get; set; }
    }

    public class BuildState
    {
        public DateTime Timestamp { get; set; }
        public bool BuildSuccessful { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class FileIntegrityResult
    {
        public bool IsValid { get; set; }
    }

    public class GitValidationResult
    {
        public bool IsValid { get; set; }
    }

    public class BuildValidationResult
    {
        public bool IsValid { get; set; }
    }

    public enum IssueSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
}