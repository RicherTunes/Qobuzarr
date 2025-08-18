using System;
using System.IO;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Central service integration layer that properly wires defensive services
    /// Provides a single point of initialization and dependency management
    /// </summary>
    public class ServiceIntegrationLayer : IDisposable
    {
        private readonly Logger _logger;
        
        // Core services
        private readonly DataValidationService _validationService;
        private readonly ApiHealthMonitor _apiHealthMonitor;
        
        // Optional services (may be null)
        private CacheValidationService _cacheService;
        private ConfigurationMonitor _configMonitor;
        
        // Defensive wrappers
        private DefensiveServiceWrapper<DataValidationService> _safeValidator;
        private DefensiveServiceWrapper<CacheValidationService> _safeCache;
        
        // Singleton instance for plugin-wide access
        private static ServiceIntegrationLayer _instance;
        private static readonly object _instanceLock = new();

        public ServiceIntegrationLayer(Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            
            try
            {
                _logger.Info("🚀 SERVICE INTEGRATION: Initializing defensive service layer");
                
                // Initialize core services with defensive patterns
                _validationService = new DataValidationService(_logger);
                _apiHealthMonitor = new ApiHealthMonitor(_logger);
                
                // Wrap in defensive layer
                _safeValidator = DefensiveService.Wrap(_validationService, _logger);
                
                _logger.Info("✅ SERVICE INTEGRATION: Core services initialized");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "💥 SERVICE INTEGRATION: Failed to initialize core services");
                throw;
            }
        }

        /// <summary>
        /// Gets singleton instance for plugin-wide access
        /// </summary>
        public static ServiceIntegrationLayer Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        _instance ??= new ServiceIntegrationLayer();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Initializes optional cache service with defensive patterns
        /// </summary>
        public void InitializeCacheService(string cacheDirectory, long maxSizeMB = 1024)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cacheDirectory))
                {
                    _logger.Warn("⚠️ CACHE INIT: No cache directory provided, cache disabled");
                    return;
                }

                // Ensure directory exists with proper error handling
                if (!SafeOperationExecutor.EnsureDirectoryExists(cacheDirectory, "CacheService"))
                {
                    _logger.Warn("⚠️ CACHE INIT: Failed to create cache directory, cache disabled");
                    return;
                }

                _cacheService = new CacheValidationService(cacheDirectory, maxSizeMB, logger: _logger);
                _safeCache = DefensiveService.Wrap(_cacheService, _logger);
                
                _logger.Info("✅ CACHE SERVICE: Initialized with {0}MB limit at {1}", maxSizeMB, cacheDirectory);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "💥 CACHE INIT: Failed to initialize cache service, continuing without cache");
                // Don't throw - cache is optional
            }
        }

        /// <summary>
        /// Initializes optional configuration monitor
        /// </summary>
        public void InitializeConfigurationMonitor(string configFilePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(configFilePath) || !File.Exists(configFilePath))
                {
                    _logger.Warn("⚠️ CONFIG MONITOR: Config file not found, monitoring disabled");
                    return;
                }

                _configMonitor = new ConfigurationMonitor(configFilePath, _logger);
                _logger.Info("✅ CONFIG MONITOR: Initialized for {0}", configFilePath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "💥 CONFIG MONITOR: Failed to initialize, continuing without monitoring");
                // Don't throw - config monitoring is optional
            }
        }


        /// <summary>
        /// Gets safe data validator with defensive wrapper
        /// </summary>
        public DefensiveServiceWrapper<DataValidationService> SafeValidator => _safeValidator;

        /// <summary>
        /// Gets safe cache service with defensive wrapper (may be null)
        /// </summary>
        public DefensiveServiceWrapper<CacheValidationService> SafeCache => _safeCache;

        /// <summary>
        /// Gets API health monitor
        /// </summary>
        public ApiHealthMonitor ApiHealth => _apiHealthMonitor;

        /// <summary>
        /// Gets configuration monitor (may be null)
        /// </summary>
        public ConfigurationMonitor ConfigMonitor => _configMonitor;

        /// <summary>
        /// Validates and sanitizes track data safely
        /// </summary>
        public T ValidateTrackSafely<T>(T track, Func<T, string> getTitle, Func<T, string> getArtist)
        {
            return _safeValidator.ExecuteSafely(
                v => v.ValidateTrackData(track, getTitle, getArtist),
                fallbackValue: new ValidationResult<T> { IsValid = false, Data = track },
                operationName: "ValidateTrack"
            ).Data;
        }

        /// <summary>
        /// Sanitizes file path safely with all defensive measures
        /// </summary>
        public string SanitizePathSafely(string basePath, string fileName)
        {
            // Multiple layers of defense
            var sanitizedFileName = _safeValidator.ExecuteSafely(
                v => v.SanitizeFileName(fileName),
                fallbackValue: $"Fallback_{DateTime.UtcNow.Ticks}",
                operationName: "SanitizeFileName"
            );

            var pathResult = _safeValidator.ExecuteSafely(
                v => v.ValidateFilePath(basePath, sanitizedFileName),
                fallbackValue: new PathValidationResult 
                { 
                    IsValid = false, 
                    SanitizedFileName = sanitizedFileName 
                },
                operationName: "ValidateFilePath"
            );

            return pathResult.IsValid ? pathResult.SanitizedPath : Path.Combine(basePath, sanitizedFileName);
        }

        /// <summary>
        /// Checks cache safely with fallback to cache miss
        /// </summary>
        public bool IsCacheValid(string key)
        {
            if (_safeCache == null) return false;

            var result = _safeCache.ExecuteSafely(
                cache => cache.ValidateCacheEntry(key),
                fallbackValue: new CacheValidationResult { IsValid = false },
                operationName: "CheckCache"
            );

            return result.IsValid;
        }

        /// <summary>
        /// Gets current configuration safely
        /// </summary>
        public QobuzConfiguration GetCurrentConfiguration()
        {
            if (_configMonitor == null)
            {
                return new QobuzConfiguration(); // Return defaults
            }

            return _configMonitor.GetCurrentConfiguration();
        }

        /// <summary>
        /// Records API success for health tracking
        /// </summary>
        public void RecordApiSuccess(string endpoint, TimeSpan responseTime)
        {
            try
            {
                _apiHealthMonitor.RecordSuccess(endpoint, responseTime);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to record API success metric");
                // Don't throw - metrics are non-critical
            }
        }

        /// <summary>
        /// Records API failure for health tracking
        /// </summary>
        public void RecordApiFailure(string endpoint, Exception error)
        {
            try
            {
                _apiHealthMonitor.RecordFailure(endpoint, error?.GetType().Name ?? "Unknown", error);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to record API failure metric");
                // Don't throw - metrics are non-critical
            }
        }

        /// <summary>
        /// Gets recommended delay based on API health
        /// </summary>
        public TimeSpan GetApiDelay(string endpoint)
        {
            try
            {
                return _apiHealthMonitor.GetRecommendedDelay(endpoint);
            }
            catch
            {
                return TimeSpan.Zero; // No delay on error
            }
        }

        /// <summary>
        /// Performs health check on all services
        /// </summary>
        public ServiceHealthReport GetHealthReport()
        {
            return new ServiceHealthReport
            {
                ValidatorHealthy = _safeValidator?.IsHealthy ?? false,
                CacheHealthy = _safeCache?.IsHealthy ?? false,
                ApiHealthSummary = _apiHealthMonitor?.GetHealthSummary(),
                ConfigMonitorActive = _configMonitor != null,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Cleanup and disposal
        /// </summary>
        public void Dispose()
        {
            try
            {
                _configMonitor?.Dispose();
                
                if (_safeCache != null && _cacheService != null)
                {
                    _safeCache.ExecuteSafely(
                        cache => cache.PerformCacheCleanup(forceCleanup: true),
                        operationName: "FinalCacheCleanup"
                    );
                }

                _logger.Info("🧹 SERVICE INTEGRATION: Disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during service integration disposal");
            }
        }
    }

    /// <summary>
    /// Health report for all integrated services
    /// </summary>
    public class ServiceHealthReport
    {
        public bool ValidatorHealthy { get; set; }
        public bool CacheHealthy { get; set; }
        public ApiHealthSummary ApiHealthSummary { get; set; }
        public bool ConfigMonitorActive { get; set; }
        public DateTime Timestamp { get; set; }

        public bool AllHealthy => ValidatorHealthy && CacheHealthy && 
                                  (ApiHealthSummary?.OverallHealth != "Unhealthy");
    }
}