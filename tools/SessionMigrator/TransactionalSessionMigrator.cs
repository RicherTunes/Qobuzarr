using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Tools.SessionMigrator
{
    /// <summary>
    /// Safe session migration with rollback capabilities.
    /// Handles migration of authentication sessions and configuration data.
    /// </summary>
    public class TransactionalSessionMigrator
    {
        private readonly ILogger _logger;
        private readonly string _projectRoot;
        private readonly string _sessionDirectory;
        private readonly string _backupDirectory;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly SemaphoreSlim _migrationLock;

        public TransactionalSessionMigrator(string projectRoot)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _projectRoot = projectRoot;
            _sessionDirectory = Path.Combine(projectRoot, ".qobuz-sessions");
            _backupDirectory = Path.Combine(projectRoot, ".session-backups");
            _migrationLock = new SemaphoreSlim(1, 1);
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            EnsureDirectories();
        }

        /// <summary>
        /// Migrates authentication sessions with transactional safety
        /// </summary>
        public async Task<SessionMigrationResult> MigrateSessionsAsync(SessionMigrationOptions options)
        {
            await _migrationLock.WaitAsync();

            try
            {
                _logger.Info("🔐 Starting transactional session migration");

                var result = new SessionMigrationResult
                {
                    StartTime = DateTime.UtcNow,
                    Options = options,
                    MigratedSessions = new List<SessionInfo>(),
                    Errors = new List<string>()
                };

                // Phase 1: Discovery and validation
                var discoveryResult = await DiscoverExistingSessions();
                result.TotalSessionsFound = discoveryResult.SessionCount;

                if (discoveryResult.SessionCount == 0 && !options.AllowEmptyMigration)
                {
                    _logger.Info("ℹ️ No sessions found to migrate");
                    result.Success = true;
                    return result;
                }

                // Phase 2: Create transaction backup
                var transactionId = Guid.NewGuid().ToString("N")[..8];
                var backupResult = await CreateTransactionBackup(transactionId, discoveryResult.Sessions);
                
                if (!backupResult.Success)
                {
                    result.Success = false;
                    result.Errors.Add($"Failed to create transaction backup: {backupResult.Error}");
                    return result;
                }

                result.BackupId = transactionId;

                try
                {
                    // Phase 3: Migrate sessions with validation
                    foreach (var sessionFile in discoveryResult.Sessions)
                    {
                        var migrationResult = await MigrateSingleSession(sessionFile, options);
                        
                        if (migrationResult.Success)
                        {
                            result.MigratedSessions.Add(migrationResult.SessionInfo);
                            _logger.Debug($"✅ Migrated session: {migrationResult.SessionInfo.SessionId}");
                        }
                        else
                        {
                            result.Errors.Add($"Failed to migrate session {sessionFile}: {migrationResult.Error}");
                            
                            if (options.StopOnFailure)
                            {
                                _logger.Error("❌ Migration failed, initiating rollback");
                                await RollbackTransaction(transactionId);
                                result.Success = false;
                                result.RolledBack = true;
                                return result;
                            }
                        }
                    }

                    // Phase 4: Validate migrated sessions
                    var validationResult = await ValidateMigratedSessions(result.MigratedSessions);
                    result.ValidationPassed = validationResult.Success;
                    
                    if (!validationResult.Success)
                    {
                        result.Errors.AddRange(validationResult.Errors);
                        
                        if (options.RollbackOnValidationFailure)
                        {
                            _logger.Warn("⚠️ Validation failed, rolling back migration");
                            await RollbackTransaction(transactionId);
                            result.Success = false;
                            result.RolledBack = true;
                            return result;
                        }
                    }

                    // Phase 5: Commit transaction
                    await CommitTransaction(transactionId);
                    result.Success = true;
                    result.Committed = true;

                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime.Value - result.StartTime;

                    _logger.Info($"✅ Session migration completed successfully. Migrated {result.MigratedSessions.Count} sessions in {result.Duration:mm\\:ss}");

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "💥 Critical error during session migration, rolling back");
                    await RollbackTransaction(transactionId);
                    
                    result.Success = false;
                    result.RolledBack = true;
                    result.Exception = ex;
                    result.Errors.Add($"Critical migration error: {ex.Message}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "💥 Fatal error in session migration");
                return new SessionMigrationResult
                {
                    Success = false,
                    Exception = ex,
                    Errors = new List<string> { $"Fatal migration error: {ex.Message}" }
                };
            }
            finally
            {
                _migrationLock.Release();
            }
        }

        /// <summary>
        /// Migrates configuration data with session dependencies
        /// </summary>
        public async Task<ConfigurationMigrationResult> MigrateConfigurationAsync(ConfigurationMigrationOptions options)
        {
            _logger.Info("⚙️ Starting configuration migration with session validation");

            var result = new ConfigurationMigrationResult
            {
                StartTime = DateTime.UtcNow,
                Options = options,
                MigratedConfigurations = new List<ConfigurationInfo>(),
                Errors = new List<string>()
            };

            try
            {
                // Phase 1: Discover configuration files
                var configFiles = await DiscoverConfigurationFiles(options);
                result.TotalConfigurationsFound = configFiles.Count;

                if (configFiles.Count == 0)
                {
                    _logger.Info("ℹ️ No configuration files found to migrate");
                    result.Success = true;
                    return result;
                }

                // Phase 2: Validate session dependencies
                foreach (var configFile in configFiles)
                {
                    var sessionDeps = await ExtractSessionDependencies(configFile);
                    var validationResult = await ValidateSessionDependencies(sessionDeps);
                    
                    if (!validationResult.AllValid)
                    {
                        result.Errors.Add($"Configuration {configFile.Name} has invalid session dependencies");
                        
                        if (options.StopOnInvalidDependencies)
                        {
                            result.Success = false;
                            return result;
                        }
                    }
                }

                // Phase 3: Migrate configurations with session linking
                foreach (var configFile in configFiles)
                {
                    var migrationResult = await MigrateSingleConfiguration(configFile, options);
                    
                    if (migrationResult.Success)
                    {
                        result.MigratedConfigurations.Add(migrationResult.ConfigInfo);
                        _logger.Debug($"✅ Migrated configuration: {configFile.Name}");
                    }
                    else
                    {
                        result.Errors.Add($"Failed to migrate configuration {configFile.Name}: {migrationResult.Error}");
                    }
                }

                result.Success = result.Errors.Count == 0;
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime.Value - result.StartTime;

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in configuration migration");
                result.Success = false;
                result.Exception = ex;
                result.EndTime = DateTime.UtcNow;
                return result;
            }
        }

        /// <summary>
        /// Validates session integrity and dependencies
        /// </summary>
        public async Task<SessionValidationResult> ValidateSessionIntegrityAsync()
        {
            _logger.Info("🔍 Validating session integrity");

            var result = new SessionValidationResult
            {
                ValidationTime = DateTime.UtcNow,
                ValidatedSessions = new List<SessionValidationInfo>(),
                Issues = new List<ValidationIssue>()
            };

            try
            {
                var discovery = await DiscoverExistingSessions();
                
                foreach (var sessionFile in discovery.Sessions)
                {
                    var validation = await ValidateSingleSession(sessionFile);
                    result.ValidatedSessions.Add(validation);
                    
                    if (!validation.IsValid)
                    {
                        result.Issues.AddRange(validation.Issues);
                    }
                }

                // Cross-session validation
                var crossValidation = await ValidateCrossSessionDependencies(result.ValidatedSessions);
                if (!crossValidation.Success)
                {
                    result.Issues.AddRange(crossValidation.Issues);
                }

                result.IsValid = !result.Issues.Any(i => i.Severity == ValidationSeverity.Critical);
                result.TotalSessions = result.ValidatedSessions.Count;
                result.ValidSessions = result.ValidatedSessions.Count(s => s.IsValid);

                _logger.Info($"🔍 Session validation complete. {result.ValidSessions}/{result.TotalSessions} sessions valid, {result.Issues.Count} issues found");

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error validating session integrity");
                result.IsValid = false;
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Critical,
                    Description = $"Validation error: {ex.Message}"
                });
                return result;
            }
        }

        /// <summary>
        /// Creates secure backup of current session state
        /// </summary>
        public async Task<BackupResult> CreateSessionBackupAsync(string backupName)
        {
            _logger.Info($"💾 Creating session backup: {backupName}");

            var result = new BackupResult
            {
                BackupName = backupName,
                StartTime = DateTime.UtcNow
            };

            try
            {
                var backupId = $"{backupName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                var backupPath = Path.Combine(_backupDirectory, backupId);
                Directory.CreateDirectory(backupPath);

                // Backup session files
                if (Directory.Exists(_sessionDirectory))
                {
                    var sessionFiles = Directory.GetFiles(_sessionDirectory, "*.json", SearchOption.AllDirectories);
                    
                    foreach (var sessionFile in sessionFiles)
                    {
                        var relativePath = Path.GetRelativePath(_sessionDirectory, sessionFile);
                        var backupFilePath = Path.Combine(backupPath, "sessions", relativePath);
                        
                        Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath));
                        await File.Copy(sessionFile, backupFilePath, true);
                        result.BackedUpFiles++;
                    }
                }

                // Backup configuration files
                var configBackup = await BackupRelatedConfigurations(backupPath);
                result.BackedUpFiles += configBackup.FileCount;

                // Create backup manifest
                var manifest = new BackupManifest
                {
                    BackupId = backupId,
                    BackupName = backupName,
                    CreatedAt = DateTime.UtcNow,
                    FileCount = result.BackedUpFiles,
                    ProjectRoot = _projectRoot
                };

                var manifestPath = Path.Combine(backupPath, "backup-manifest.json");
                var manifestJson = JsonSerializer.Serialize(manifest, _jsonOptions);
                await File.WriteAllTextAsync(manifestPath, manifestJson);

                result.Success = true;
                result.BackupPath = backupPath;
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime.Value - result.StartTime;

                _logger.Info($"✅ Session backup created: {backupId} ({result.BackedUpFiles} files)");

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create session backup: {backupName}");
                result.Success = false;
                result.Error = ex.Message;
                result.EndTime = DateTime.UtcNow;
                return result;
            }
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(_sessionDirectory);
            Directory.CreateDirectory(_backupDirectory);
        }

        private async Task<SessionDiscoveryResult> DiscoverExistingSessions()
        {
            var result = new SessionDiscoveryResult
            {
                Sessions = new List<string>(),
                SessionCount = 0
            };

            if (!Directory.Exists(_sessionDirectory))
                return result;

            var sessionFiles = Directory.GetFiles(_sessionDirectory, "*.json", SearchOption.AllDirectories);
            result.Sessions = sessionFiles.ToList();
            result.SessionCount = sessionFiles.Length;

            return result;
        }

        private async Task<BackupCreationResult> CreateTransactionBackup(string transactionId, List<string> sessionFiles)
        {
            try
            {
                var backupPath = Path.Combine(_backupDirectory, $"transaction_{transactionId}");
                Directory.CreateDirectory(backupPath);

                foreach (var sessionFile in sessionFiles)
                {
                    var fileName = Path.GetFileName(sessionFile);
                    var backupFilePath = Path.Combine(backupPath, fileName);
                    await File.Copy(sessionFile, backupFilePath, true);
                }

                return new BackupCreationResult { Success = true, BackupPath = backupPath };
            }
            catch (Exception ex)
            {
                return new BackupCreationResult { Success = false, Error = ex.Message };
            }
        }

        private async Task<SingleSessionMigrationResult> MigrateSingleSession(string sessionFile, SessionMigrationOptions options)
        {
            try
            {
                // Load existing session
                var sessionContent = await File.ReadAllTextAsync(sessionFile);
                var session = JsonSerializer.Deserialize<LegacySession>(sessionContent, _jsonOptions);

                // Transform to new format
                var newSession = new ModernSession
                {
                    SessionId = session.SessionId ?? Guid.NewGuid().ToString(),
                    UserId = session.UserId,
                    UserName = session.UserName,
                    AccessToken = session.AccessToken,
                    RefreshToken = session.RefreshToken,
                    TokenExpiry = session.TokenExpiry,
                    CreatedAt = session.CreatedAt ?? DateTime.UtcNow,
                    LastUsed = session.LastUsed ?? DateTime.UtcNow,
                    IsActive = session.IsActive ?? true,
                    Subscription = TransformSubscriptionInfo(session.Subscription),
                    Preferences = TransformUserPreferences(session.Preferences),
                    SecurityContext = CreateSecurityContext(session),
                    MigrationInfo = new SessionMigrationInfo
                    {
                        MigratedAt = DateTime.UtcNow,
                        SourceVersion = session.Version ?? "unknown",
                        TargetVersion = "2.0.0"
                    }
                };

                // Validate transformed session
                var validation = await ValidateTransformedSession(newSession);
                if (!validation.IsValid)
                {
                    return new SingleSessionMigrationResult
                    {
                        Success = false,
                        Error = $"Session validation failed: {string.Join(", ", validation.Errors)}"
                    };
                }

                // Save migrated session
                var newSessionPath = Path.Combine(_sessionDirectory, $"{newSession.SessionId}.json");
                var newSessionJson = JsonSerializer.Serialize(newSession, _jsonOptions);
                await File.WriteAllTextAsync(newSessionPath, newSessionJson);

                return new SingleSessionMigrationResult
                {
                    Success = true,
                    SessionInfo = new SessionInfo
                    {
                        SessionId = newSession.SessionId,
                        UserId = newSession.UserId,
                        UserName = newSession.UserName,
                        MigratedAt = DateTime.UtcNow
                    }
                };
            }
            catch (Exception ex)
            {
                return new SingleSessionMigrationResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        private async Task RollbackTransaction(string transactionId)
        {
            try
            {
                _logger.Info($"🔄 Rolling back transaction: {transactionId}");

                var backupPath = Path.Combine(_backupDirectory, $"transaction_{transactionId}");
                if (!Directory.Exists(backupPath))
                {
                    _logger.Error($"Transaction backup not found: {transactionId}");
                    return;
                }

                var backupFiles = Directory.GetFiles(backupPath, "*.json");
                foreach (var backupFile in backupFiles)
                {
                    var fileName = Path.GetFileName(backupFile);
                    var restorePath = Path.Combine(_sessionDirectory, fileName);
                    await File.Copy(backupFile, restorePath, true);
                }

                _logger.Info($"✅ Transaction rollback completed: {transactionId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to rollback transaction: {transactionId}");
            }
        }

        private async Task CommitTransaction(string transactionId)
        {
            try
            {
                // Clean up transaction backup after successful commit
                var backupPath = Path.Combine(_backupDirectory, $"transaction_{transactionId}");
                if (Directory.Exists(backupPath))
                {
                    Directory.Delete(backupPath, true);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to cleanup transaction backup: {transactionId}");
            }
        }

        // Additional helper methods would be implemented here...
        private async Task<SessionValidationResult> ValidateMigratedSessions(List<SessionInfo> sessions) => 
            new SessionValidationResult { Success = true, Errors = new List<string>() };

        private async Task<List<ConfigurationFileInfo>> DiscoverConfigurationFiles(ConfigurationMigrationOptions options) =>
            new List<ConfigurationFileInfo>();

        private async Task<List<string>> ExtractSessionDependencies(ConfigurationFileInfo configFile) =>
            new List<string>();

        private async Task<DependencyValidationResult> ValidateSessionDependencies(List<string> dependencies) =>
            new DependencyValidationResult { AllValid = true };

        private async Task<SingleConfigurationMigrationResult> MigrateSingleConfiguration(ConfigurationFileInfo configFile, ConfigurationMigrationOptions options) =>
            new SingleConfigurationMigrationResult { Success = true, ConfigInfo = new ConfigurationInfo() };

        private async Task<SessionValidationInfo> ValidateSingleSession(string sessionFile) =>
            new SessionValidationInfo { IsValid = true, Issues = new List<ValidationIssue>() };

        private async Task<CrossValidationResult> ValidateCrossSessionDependencies(List<SessionValidationInfo> sessions) =>
            new CrossValidationResult { Success = true, Issues = new List<ValidationIssue>() };

        private async Task<ConfigurationBackupResult> BackupRelatedConfigurations(string backupPath) =>
            new ConfigurationBackupResult { FileCount = 0 };

        private async Task<SessionValidationResult> ValidateTransformedSession(ModernSession session) =>
            new SessionValidationResult { IsValid = true, Errors = new List<string>() };

        private SubscriptionInfo TransformSubscriptionInfo(object subscription) => new SubscriptionInfo();
        private UserPreferences TransformUserPreferences(object preferences) => new UserPreferences();
        private SecurityContext CreateSecurityContext(LegacySession session) => new SecurityContext();
    }

    // Supporting types would be defined in separate files for production use
    public class SessionMigrationResult
    {
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan? Duration { get; set; }
        public bool Success { get; set; }
        public SessionMigrationOptions Options { get; set; }
        public int TotalSessionsFound { get; set; }
        public List<SessionInfo> MigratedSessions { get; set; }
        public List<string> Errors { get; set; }
        public Exception Exception { get; set; }
        public string BackupId { get; set; }
        public bool ValidationPassed { get; set; }
        public bool RolledBack { get; set; }
        public bool Committed { get; set; }
    }

    public class SessionMigrationOptions
    {
        public bool AllowEmptyMigration { get; set; }
        public bool StopOnFailure { get; set; } = true;
        public bool RollbackOnValidationFailure { get; set; } = true;
        public bool PreserveBackups { get; set; }
        public bool ValidateAfterMigration { get; set; } = true;
    }

    // Additional supporting classes would be defined...
    public class SessionInfo
    {
        public string SessionId { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public DateTime MigratedAt { get; set; }
    }

    public class ConfigurationMigrationResult
    {
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan? Duration { get; set; }
        public bool Success { get; set; }
        public ConfigurationMigrationOptions Options { get; set; }
        public int TotalConfigurationsFound { get; set; }
        public List<ConfigurationInfo> MigratedConfigurations { get; set; }
        public List<string> Errors { get; set; }
        public Exception Exception { get; set; }
    }

    public class ConfigurationMigrationOptions
    {
        public bool StopOnInvalidDependencies { get; set; } = true;
    }

    // Many more supporting types would be defined in a real implementation...
    public class ConfigurationInfo { }
    public class SessionValidationResult { public bool Success { get; set; } public List<string> Errors { get; set; } public bool IsValid { get; set; } public List<SessionValidationInfo> ValidatedSessions { get; set; } public DateTime ValidationTime { get; set; } public List<ValidationIssue> Issues { get; set; } public int TotalSessions { get; set; } public int ValidSessions { get; set; } }
    public class BackupResult { public string BackupName { get; set; } public DateTime StartTime { get; set; } public DateTime? EndTime { get; set; } public TimeSpan? Duration { get; set; } public bool Success { get; set; } public string BackupPath { get; set; } public int BackedUpFiles { get; set; } public string Error { get; set; } }
    
    // Legacy and modern session classes
    public class LegacySession
    {
        public string SessionId { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime? TokenExpiry { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? LastUsed { get; set; }
        public bool? IsActive { get; set; }
        public object Subscription { get; set; }
        public object Preferences { get; set; }
        public string Version { get; set; }
    }

    public class ModernSession
    {
        public string SessionId { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime TokenExpiry { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUsed { get; set; }
        public bool IsActive { get; set; }
        public SubscriptionInfo Subscription { get; set; }
        public UserPreferences Preferences { get; set; }
        public SecurityContext SecurityContext { get; set; }
        public SessionMigrationInfo MigrationInfo { get; set; }
    }

    public class SubscriptionInfo { }
    public class UserPreferences { }
    public class SecurityContext { }
    public class SessionMigrationInfo 
    { 
        public DateTime MigratedAt { get; set; }
        public string SourceVersion { get; set; }
        public string TargetVersion { get; set; }
    }

    // Additional implementation classes...
    public class SessionDiscoveryResult { public List<string> Sessions { get; set; } public int SessionCount { get; set; } }
    public class BackupCreationResult { public bool Success { get; set; } public string Error { get; set; } public string BackupPath { get; set; } }
    public class SingleSessionMigrationResult { public bool Success { get; set; } public string Error { get; set; } public SessionInfo SessionInfo { get; set; } }
    public class ConfigurationFileInfo { public string Name { get; set; } }
    public class DependencyValidationResult { public bool AllValid { get; set; } }
    public class SingleConfigurationMigrationResult { public bool Success { get; set; } public ConfigurationInfo ConfigInfo { get; set; } }
    public class SessionValidationInfo { public bool IsValid { get; set; } public List<ValidationIssue> Issues { get; set; } }
    public class ValidationIssue { public ValidationSeverity Severity { get; set; } public string Description { get; set; } }
    public class CrossValidationResult { public bool Success { get; set; } public List<ValidationIssue> Issues { get; set; } }
    public class ConfigurationBackupResult { public int FileCount { get; set; } }
    public class BackupManifest { public string BackupId { get; set; } public string BackupName { get; set; } public DateTime CreatedAt { get; set; } public int FileCount { get; set; } public string ProjectRoot { get; set; } }
    
    public enum ValidationSeverity { Low, Medium, High, Critical }
}