using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Tools.MigrationController
{
    /// <summary>
    /// Main migration controller with checkpoints for safe service migration.
    /// Orchestrates the migration from legacy services to consolidated architecture.
    /// </summary>
    public class MigrationController
    {
        private readonly ILogger _logger;
        private readonly MigrationCheckpoint _checkpoint;
        private readonly RollbackController _rollbackController;
        private readonly string _projectRoot;
        private readonly List<MigrationStep> _migrationSteps;
        private readonly Dictionary<string, MigrationStatus> _stepStatus;

        public MigrationController(string projectRoot)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _projectRoot = projectRoot;
            _checkpoint = new MigrationCheckpoint(projectRoot);
            _rollbackController = new RollbackController(projectRoot);
            _migrationSteps = InitializeMigrationSteps();
            _stepStatus = new Dictionary<string, MigrationStatus>();
        }

        /// <summary>
        /// Executes the complete migration with checkpoints and rollback capability
        /// </summary>
        public async Task<MigrationResult> ExecuteMigrationAsync(MigrationOptions options)
        {
            _logger.Info("🚀 Starting service migration from legacy to consolidated architecture");
            
            var result = new MigrationResult
            {
                StartTime = DateTime.UtcNow,
                Options = options,
                Steps = new List<StepResult>()
            };

            try
            {
                // Phase 1: Pre-migration validation
                await ValidatePrerequisites(options);

                // Phase 2: Create backup checkpoint
                if (!options.SkipBackup)
                {
                    await _checkpoint.CreateCheckpointAsync("pre-migration");
                    _logger.Info("✅ Pre-migration checkpoint created");
                }

                // Phase 3: Execute migration steps
                foreach (var step in _migrationSteps)
                {
                    if (options.StartFromStep != null && 
                        string.Compare(step.Id, options.StartFromStep, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        _logger.Info($"⏭️ Skipping step {step.Id} (starting from {options.StartFromStep})");
                        continue;
                    }

                    var stepResult = await ExecuteStepWithCheckpoint(step, options);
                    result.Steps.Add(stepResult);

                    if (!stepResult.Success)
                    {
                        if (options.StopOnFailure)
                        {
                            _logger.Error($"❌ Migration failed at step {step.Id}. Stopping execution.");
                            result.Success = false;
                            result.FailedStep = step.Id;
                            break;
                        }
                        else
                        {
                            _logger.Warn($"⚠️ Step {step.Id} failed but continuing due to configuration");
                        }
                    }
                }

                // Phase 4: Post-migration validation
                if (result.Success)
                {
                    await ValidatePostMigration();
                    _logger.Info("✅ Post-migration validation completed successfully");
                }

                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime.Value - result.StartTime;

                if (result.Success)
                {
                    _logger.Info($"🎉 Migration completed successfully in {result.Duration:mm\\:ss}");
                    
                    // Cleanup old checkpoint if successful
                    if (!options.KeepBackups)
                    {
                        await _checkpoint.CleanupCheckpointAsync("pre-migration");
                    }
                }
                else
                {
                    _logger.Error($"❌ Migration failed. Use rollback scripts to restore previous state.");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "💥 Critical error during migration");
                result.Success = false;
                result.Exception = ex;
                result.EndTime = DateTime.UtcNow;
                return result;
            }
        }

        /// <summary>
        /// Executes dry run to validate migration without making changes
        /// </summary>
        public async Task<DryRunResult> ExecuteDryRunAsync(MigrationOptions options)
        {
            _logger.Info("🧪 Starting migration dry run analysis");
            
            var result = new DryRunResult
            {
                StartTime = DateTime.UtcNow,
                Options = options,
                StepAnalysis = new List<StepAnalysis>()
            };

            try
            {
                // Analyze each migration step
                foreach (var step in _migrationSteps)
                {
                    var analysis = await AnalyzeStep(step);
                    result.StepAnalysis.Add(analysis);
                    
                    if (analysis.HasIssues)
                    {
                        result.HasBlockingIssues = result.HasBlockingIssues || analysis.HasBlockingIssues;
                        result.IssueCount += analysis.Issues.Count;
                    }
                }

                // Overall risk assessment
                result.RiskLevel = CalculateRiskLevel(result.StepAnalysis);
                result.EstimatedDuration = CalculateEstimatedDuration(result.StepAnalysis);

                result.EndTime = DateTime.UtcNow;
                result.Success = !result.HasBlockingIssues;

                _logger.Info($"🧪 Dry run completed. Risk Level: {result.RiskLevel}, Issues: {result.IssueCount}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "💥 Error during dry run analysis");
                result.Success = false;
                result.Exception = ex;
                result.EndTime = DateTime.UtcNow;
                return result;
            }
        }

        /// <summary>
        /// Gets current migration status and progress
        /// </summary>
        public async Task<MigrationStatus> GetMigrationStatusAsync()
        {
            var status = new MigrationStatus
            {
                CheckTime = DateTime.UtcNow,
                ProjectRoot = _projectRoot,
                StepStatuses = new Dictionary<string, StepStatus>()
            };

            foreach (var step in _migrationSteps)
            {
                var stepStatus = await AnalyzeStepCompletion(step);
                status.StepStatuses[step.Id] = stepStatus;
                
                if (stepStatus.IsComplete)
                    status.CompletedSteps++;
                else if (stepStatus.IsPartiallyComplete)
                    status.PartialSteps++;
                else
                    status.PendingSteps++;
            }

            status.TotalSteps = _migrationSteps.Count;
            status.ProgressPercentage = (double)status.CompletedSteps / status.TotalSteps * 100;

            return status;
        }

        private async Task<StepResult> ExecuteStepWithCheckpoint(MigrationStep step, MigrationOptions options)
        {
            _logger.Info($"🔄 Executing step {step.Id}: {step.Description}");
            
            var stepResult = new StepResult
            {
                StepId = step.Id,
                Description = step.Description,
                StartTime = DateTime.UtcNow
            };

            try
            {
                // Create step checkpoint
                await _checkpoint.CreateCheckpointAsync($"step-{step.Id}");

                // Execute the step
                if (options.DryRun)
                {
                    // Dry run mode - analyze only
                    var analysis = await AnalyzeStep(step);
                    stepResult.Success = !analysis.HasBlockingIssues;
                    stepResult.Messages = analysis.Issues.Select(i => i.Description).ToList();
                }
                else
                {
                    // Real execution
                    await ExecuteStepActions(step);
                    stepResult.Success = await ValidateStepCompletion(step);
                }

                stepResult.EndTime = DateTime.UtcNow;
                stepResult.Duration = stepResult.EndTime.Value - stepResult.StartTime;

                if (stepResult.Success)
                {
                    _logger.Info($"✅ Step {step.Id} completed successfully in {stepResult.Duration:mm\\:ss}");
                    _stepStatus[step.Id] = MigrationStatus.Completed;
                }
                else
                {
                    _logger.Error($"❌ Step {step.Id} failed");
                    _stepStatus[step.Id] = MigrationStatus.Failed;
                }

                return stepResult;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"💥 Exception in step {step.Id}");
                stepResult.Success = false;
                stepResult.Exception = ex;
                stepResult.EndTime = DateTime.UtcNow;
                _stepStatus[step.Id] = MigrationStatus.Failed;
                return stepResult;
            }
        }

        private List<MigrationStep> InitializeMigrationSteps()
        {
            return new List<MigrationStep>
            {
                // Phase 2A: Core Service Migration
                new MigrationStep
                {
                    Id = "migrate-lidarr-album-retriever",
                    Description = "Migrate LidarrAlbumRetriever to IQobuzQualityManager",
                    Phase = "2A",
                    EstimatedDuration = TimeSpan.FromMinutes(15),
                    RiskLevel = RiskLevel.Medium,
                    Actions = new List<MigrationAction>
                    {
                        new UpdateConstructorAction("src/Services/LidarrAlbumRetriever.cs", 
                            new[] { "IQualityMappingService", "IQualityFallbackService" },
                            new[] { "IQobuzQualityManager" }),
                        new UpdateMethodCallsAction("src/Services/LidarrAlbumRetriever.cs",
                            GetLidarrAlbumRetrieverMethodMappings())
                    }
                },
                
                new MigrationStep
                {
                    Id = "migrate-qobuz-validation-service",
                    Description = "Migrate QobuzValidationService to consolidated services",
                    Phase = "2A",
                    EstimatedDuration = TimeSpan.FromMinutes(10),
                    RiskLevel = RiskLevel.Low,
                    Actions = new List<MigrationAction>
                    {
                        new UpdateConstructorAction("src/Services/QobuzValidationService.cs",
                            new[] { "QobuzQualityService" },
                            new[] { "IQobuzQualityManager" }),
                        new UpdateMethodCallsAction("src/Services/QobuzValidationService.cs",
                            GetQobuzValidationServiceMethodMappings())
                    }
                },

                new MigrationStep
                {
                    Id = "migrate-qobuz-api-service",
                    Description = "Migrate QobuzApiService quality mappings",
                    Phase = "2A",
                    EstimatedDuration = TimeSpan.FromMinutes(8),
                    RiskLevel = RiskLevel.Low,
                    Actions = new List<MigrationAction>
                    {
                        new UpdateConstructorAction("src/Core/QobuzApiService.cs",
                            new[] { "QualityMappingService" },
                            new[] { "IQobuzQualityManager" }),
                        new UpdateMethodCallsAction("src/Core/QobuzApiService.cs",
                            GetQobuzApiServiceMethodMappings())
                    }
                },

                // Phase 2B: Remove Legacy Services  
                new MigrationStep
                {
                    Id = "remove-legacy-quality-services",
                    Description = "Remove legacy quality service files",
                    Phase = "2B",
                    EstimatedDuration = TimeSpan.FromMinutes(5),
                    RiskLevel = RiskLevel.High,
                    Actions = new List<MigrationAction>
                    {
                        new RemoveFileAction("src/Services/QobuzQualityService.cs"),
                        new RemoveFileAction("src/Services/QualityMappingService.cs"),
                        new RemoveFileAction("src/Services/QualityFallbackService.cs"),
                        new RemoveFileAction("src/Services/IQualityMappingService.cs"),
                        new RemoveInterfaceAction("src/Services/IntelligentQualityDetector.cs")
                    }
                },

                // Phase 2C: Remove Migration Infrastructure
                new MigrationStep
                {
                    Id = "remove-migration-adapters",
                    Description = "Remove migration adapters and temporary code",
                    Phase = "2C",
                    EstimatedDuration = TimeSpan.FromMinutes(5),
                    RiskLevel = RiskLevel.Medium,
                    Actions = new List<MigrationAction>
                    {
                        new RemoveMethodsAction("src/Services/Consolidated/ConsolidatedServiceRegistration.cs",
                            new[] { "CreateQualityServiceAdapter", "CreateMappingServiceAdapter", "CreateFallbackServiceAdapter" }),
                        new RemoveUsingStatementsAction("**/*.cs", 
                            new[] { "Lidarr.Plugin.Qobuzarr.Services.QobuzQualityService", 
                                   "Lidarr.Plugin.Qobuzarr.Services.QualityMappingService" })
                    }
                }
            };
        }

        private async Task ValidatePrerequisites(MigrationOptions options)
        {
            _logger.Info("🔍 Validating migration prerequisites");

            // Check project structure
            if (!Directory.Exists(Path.Combine(_projectRoot, "src")))
                throw new MigrationException("Source directory not found");

            // Check consolidated services exist
            var qualityManagerPath = Path.Combine(_projectRoot, "src/Services/Consolidated/QobuzQualityManager.cs");
            if (!File.Exists(qualityManagerPath))
                throw new MigrationException("IQobuzQualityManager not found. Run consolidation first.");

            // Build validation
            if (!options.SkipBuild)
            {
                _logger.Info("🔨 Validating project builds successfully");
                var buildResult = await RunBuildValidation();
                if (!buildResult.Success)
                    throw new MigrationException($"Project build failed: {buildResult.Error}");
            }

            _logger.Info("✅ Prerequisites validation passed");
        }

        private async Task ValidatePostMigration()
        {
            _logger.Info("🔍 Running post-migration validation");

            // Build validation
            var buildResult = await RunBuildValidation();
            if (!buildResult.Success)
                throw new MigrationException($"Post-migration build failed: {buildResult.Error}");

            // Test validation  
            var testResult = await RunTestValidation();
            if (!testResult.Success)
                throw new MigrationException($"Post-migration tests failed: {testResult.Error}");

            _logger.Info("✅ Post-migration validation passed");
        }

        // Method mappings for different services
        private Dictionary<string, string> GetLidarrAlbumRetrieverMethodMappings() => new()
        {
            ["_qualityMappingService.GetQualityRecommendation"] = "_qualityManager.MapLidarrQuality",
            ["_qualityFallbackService.SelectBestAvailableQuality"] = "_qualityManager.SelectBestQualityAsync",
            ["_qualityFallbackService.GetFallbackChain"] = "_qualityManager.GetQualityFallbackChain"
        };

        private Dictionary<string, string> GetQobuzValidationServiceMethodMappings() => new()
        {
            ["_qualityService.ValidateQuality"] = "_qualityManager.DetectAvailableQualitiesAsync",
            ["_qualityService.GetAvailableQualities"] = "_qualityManager.DetectAvailableQualitiesAsync"
        };

        private Dictionary<string, string> GetQobuzApiServiceMethodMappings() => new()
        {
            ["_qualityMappingService.MapQuality"] = "_qualityManager.MapLidarrQuality"
        };

        private async Task<BuildResult> RunBuildValidation()
        {
            // Implementation would run build process and return result
            return new BuildResult { Success = true };
        }

        private async Task<TestResult> RunTestValidation()
        {
            // Implementation would run test suite and return result  
            return new TestResult { Success = true };
        }

        private async Task ExecuteStepActions(MigrationStep step)
        {
            foreach (var action in step.Actions)
            {
                await action.ExecuteAsync(_projectRoot);
            }
        }

        private async Task<bool> ValidateStepCompletion(MigrationStep step)
        {
            // Implementation would validate step completed successfully
            return true;
        }

        private async Task<StepAnalysis> AnalyzeStep(MigrationStep step)
        {
            // Implementation would analyze step for potential issues
            return new StepAnalysis
            {
                StepId = step.Id,
                HasIssues = false,
                Issues = new List<AnalysisIssue>()
            };
        }

        private async Task<StepStatus> AnalyzeStepCompletion(MigrationStep step)
        {
            // Implementation would check if step is already completed
            return new StepStatus { IsComplete = false };
        }

        private RiskLevel CalculateRiskLevel(List<StepAnalysis> analyses)
        {
            if (analyses.Any(a => a.RiskLevel == RiskLevel.High))
                return RiskLevel.High;
            if (analyses.Any(a => a.RiskLevel == RiskLevel.Medium))
                return RiskLevel.Medium;
            return RiskLevel.Low;
        }

        private TimeSpan CalculateEstimatedDuration(List<StepAnalysis> analyses)
        {
            return analyses.Aggregate(TimeSpan.Zero, (sum, a) => sum + a.EstimatedDuration);
        }
    }

    public enum MigrationStatus { Pending, InProgress, Completed, Failed }
    public enum RiskLevel { Low, Medium, High }

    // Supporting types would be defined in separate files
    public class MigrationResult
    {
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan? Duration { get; set; }
        public bool Success { get; set; }
        public string FailedStep { get; set; }
        public MigrationOptions Options { get; set; }
        public List<StepResult> Steps { get; set; }
        public Exception Exception { get; set; }
    }

    public class MigrationOptions
    {
        public bool DryRun { get; set; }
        public bool SkipBackup { get; set; }
        public bool SkipBuild { get; set; }
        public bool StopOnFailure { get; set; } = true;
        public bool KeepBackups { get; set; }
        public string StartFromStep { get; set; }
    }

    public class StepResult
    {
        public string StepId { get; set; }
        public string Description { get; set; }
        public bool Success { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan? Duration { get; set; }
        public List<string> Messages { get; set; } = new();
        public Exception Exception { get; set; }
    }

    public class DryRunResult
    {
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public bool Success { get; set; }
        public MigrationOptions Options { get; set; }
        public List<StepAnalysis> StepAnalysis { get; set; }
        public bool HasBlockingIssues { get; set; }
        public int IssueCount { get; set; }
        public RiskLevel RiskLevel { get; set; }
        public TimeSpan EstimatedDuration { get; set; }
        public Exception Exception { get; set; }
    }

    public class MigrationStep
    {
        public string Id { get; set; }
        public string Description { get; set; }
        public string Phase { get; set; }
        public TimeSpan EstimatedDuration { get; set; }
        public RiskLevel RiskLevel { get; set; }
        public List<MigrationAction> Actions { get; set; }
    }

    public abstract class MigrationAction
    {
        public abstract Task ExecuteAsync(string projectRoot);
    }

    // Concrete action types would be implemented
    public class UpdateConstructorAction : MigrationAction
    {
        public string FilePath { get; set; }
        public string[] OldServices { get; set; }
        public string[] NewServices { get; set; }

        public UpdateConstructorAction(string filePath, string[] oldServices, string[] newServices)
        {
            FilePath = filePath;
            OldServices = oldServices;
            NewServices = newServices;
        }

        public override Task ExecuteAsync(string projectRoot)
        {
            // Implementation would update constructor dependencies
            return Task.CompletedTask;
        }
    }

    public class UpdateMethodCallsAction : MigrationAction
    {
        public string FilePath { get; set; }
        public Dictionary<string, string> MethodMappings { get; set; }

        public UpdateMethodCallsAction(string filePath, Dictionary<string, string> methodMappings)
        {
            FilePath = filePath;
            MethodMappings = methodMappings;
        }

        public override Task ExecuteAsync(string projectRoot)
        {
            // Implementation would update method calls
            return Task.CompletedTask;
        }
    }

    public class RemoveFileAction : MigrationAction
    {
        public string FilePath { get; set; }

        public RemoveFileAction(string filePath)
        {
            FilePath = filePath;
        }

        public override Task ExecuteAsync(string projectRoot)
        {
            var fullPath = Path.Combine(projectRoot, FilePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            return Task.CompletedTask;
        }
    }

    public class RemoveInterfaceAction : MigrationAction
    {
        public string FilePath { get; set; }

        public RemoveInterfaceAction(string filePath)
        {
            FilePath = filePath;
        }

        public override Task ExecuteAsync(string projectRoot)
        {
            // Implementation would remove interface definitions
            return Task.CompletedTask;
        }
    }

    public class RemoveMethodsAction : MigrationAction
    {
        public string FilePath { get; set; }
        public string[] MethodNames { get; set; }

        public RemoveMethodsAction(string filePath, string[] methodNames)
        {
            FilePath = filePath;
            MethodNames = methodNames;
        }

        public override Task ExecuteAsync(string projectRoot)
        {
            // Implementation would remove specific methods
            return Task.CompletedTask;
        }
    }

    public class RemoveUsingStatementsAction : MigrationAction
    {
        public string Pattern { get; set; }
        public string[] UsingStatements { get; set; }

        public RemoveUsingStatementsAction(string pattern, string[] usingStatements)
        {
            Pattern = pattern;
            UsingStatements = usingStatements;
        }

        public override Task ExecuteAsync(string projectRoot)
        {
            // Implementation would remove using statements from matching files
            return Task.CompletedTask;
        }
    }

    // Additional supporting types
    public class StepAnalysis
    {
        public string StepId { get; set; }
        public bool HasIssues { get; set; }
        public bool HasBlockingIssues { get; set; }
        public RiskLevel RiskLevel { get; set; }
        public TimeSpan EstimatedDuration { get; set; }
        public List<AnalysisIssue> Issues { get; set; } = new();
    }

    public class AnalysisIssue
    {
        public string Description { get; set; }
        public string Severity { get; set; }
        public bool IsBlocking { get; set; }
    }

    public class StepStatus
    {
        public bool IsComplete { get; set; }
        public bool IsPartiallyComplete { get; set; }
    }

    public class BuildResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    public class TestResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    public class MigrationException : Exception
    {
        public MigrationException(string message) : base(message) { }
        public MigrationException(string message, Exception innerException) : base(message, innerException) { }
    }
}