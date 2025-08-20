using System;
using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Services.Migration;
using Lidarr.Plugin.Qobuzarr.Services.Monitoring;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Indexers;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services.Consolidated
{
    /// <summary>
    /// Service registration helper for consolidated services.
    /// This class provides factory methods that Lidarr's DryIoC container will use
    /// for automatic dependency injection registration.
    /// </summary>
    /// <remarks>
    /// Lidarr automatically registers classes that implement interfaces in the plugin assembly.
    /// During the transition period, we register both the new consolidated services
    /// and migration adapters for backward compatibility.
    /// </remarks>
    public static class ConsolidatedServiceRegistration
    {
        /// <summary>
        /// Creates the consolidated QobuzQualityManager instance with performance monitoring.
        /// This will be automatically registered as a singleton by Lidarr's DI container.
        /// </summary>
        public static IQobuzQualityManager CreateQualityManager(
            IQobuzApiClient apiClient,
            IQobuzLogger logger)
        {
            var performanceMonitor = new PerformanceMonitor(logger);
            return new QobuzQualityManager(apiClient, logger, performanceMonitor);
        }

        /// <summary>
        /// Creates the performance monitor for tracking production operations.
        /// </summary>
        public static IPerformanceMonitor CreatePerformanceMonitor(IQobuzLogger logger)
        {
            return new PerformanceMonitor(logger);
        }

        /// <summary>
        /// Creates the Unicode query builder for international character handling.
        /// This service helps solve the core problem of missed albums due to special characters.
        /// </summary>
        public static IUnicodeQueryBuilder CreateUnicodeQueryBuilder(IQobuzLogger logger)
        {
            // Convert IQobuzLogger to NLog Logger for compatibility
            var nlogLogger = logger as Logger ?? LogManager.GetCurrentClassLogger();
            return new UnicodeQueryBuilder(nlogLogger);
        }

        /// <summary>
        /// Creates migration adapters for backward compatibility.
        /// These allow existing code to continue working during the transition period.
        /// </summary>
        public static class MigrationAdapters
        {
            /// <summary>
            /// Creates a QobuzQualityService compatible adapter.
            /// </summary>
            [Obsolete("For migration only. Use IQobuzQualityManager directly.")]
            public static QobuzQualityService CreateQualityServiceAdapter(
                IQobuzQualityManager qualityManager,
                IQobuzLogger logger)
            {
                var adapter = new QualityServiceMigrationAdapter(qualityManager, logger);
                
                // Return a wrapper that looks like the old QobuzQualityService
                return new QobuzQualityService(adapter, logger);
            }

            /// <summary>
            /// Creates a QualityMappingService compatible adapter.
            /// </summary>
            [Obsolete("For migration only. Use IQobuzQualityManager directly.")]
            public static IQualityMappingService CreateMappingServiceAdapter(
                IQobuzQualityManager qualityManager,
                Logger logger)
            {
                var qobuzLogger = new QobuzLoggerAdapter(logger);
                var adapter = new QualityServiceMigrationAdapter(qualityManager, qobuzLogger);
                
                // Return a wrapper that implements IQualityMappingService
                return new QualityMappingServiceAdapter(adapter, logger);
            }

            /// <summary>
            /// Creates a QualityFallbackService compatible adapter.
            /// </summary>
            [Obsolete("For migration only. Use IQobuzQualityManager directly.")]
            public static IQualityFallbackService CreateFallbackServiceAdapter(
                IQobuzQualityManager qualityManager,
                Logger logger)
            {
                var qobuzLogger = new QobuzLoggerAdapter(logger);
                var adapter = new QualityServiceMigrationAdapter(qualityManager, qobuzLogger);
                
                // Return the adapter cast to the legacy interface
                return adapter as IQualityFallbackService;
            }
        }

        #region Adapter Wrapper Classes

        /// <summary>
        /// Wrapper that makes the migration adapter look like the old QobuzQualityService.
        /// </summary>
        [Obsolete("For migration only")]
        public class QobuzQualityService
        {
            private readonly QualityServiceMigrationAdapter _adapter;
            private readonly IQobuzLogger _logger;

            public QobuzQualityService(QualityServiceMigrationAdapter adapter, IQobuzLogger logger)
            {
                _adapter = adapter;
                _logger = logger;
            }

            public async System.Threading.Tasks.Task<System.Collections.Generic.List<int>> GetAvailableQualitiesAsync(string trackId)
            {
                return await _adapter.GetAvailableQualitiesAsync(trackId);
            }

            public async System.Threading.Tasks.Task<(int selectedQuality, Migration.QobuzStreamInfo streamInfo)> GetBestAvailableStreamAsync(
                string trackId, int preferredQuality)
            {
                return await _adapter.GetBestAvailableStreamAsync(trackId, preferredQuality);
            }

            public string GetQualityDescription(int qualityId)
            {
                return _adapter.GetQualityDescription(qualityId);
            }

            public System.Collections.Generic.IReadOnlyList<int> GetSupportedQualities()
            {
                return new[] { 27, 7, 6, 5 };
            }
        }

        /// <summary>
        /// Wrapper that implements IQualityMappingService using the migration adapter.
        /// </summary>
        [Obsolete("For migration only")]
        private class QualityMappingServiceAdapter : IQualityMappingService
        {
            private readonly QualityServiceMigrationAdapter _adapter;
            private readonly Logger _logger;

            public QualityMappingServiceAdapter(QualityServiceMigrationAdapter adapter, Logger logger)
            {
                _adapter = adapter;
                _logger = logger;
            }

            public string GetPreferredQobuzQuality(Models.Lidarr.LidarrQualityProfile qualityProfile)
            {
                return _adapter.GetPreferredQobuzQuality(qualityProfile);
            }

            public System.Collections.Generic.List<string> GetQualityFallbackChain(Models.Lidarr.LidarrQualityProfile qualityProfile)
            {
                return _adapter.GetQualityFallbackChain(qualityProfile);
            }

            public string SelectBestAvailableQuality(
                Models.Lidarr.LidarrQualityProfile qualityProfile, 
                System.Collections.Generic.List<string> availableQualities)
            {
                return _adapter.SelectBestAvailableQuality(qualityProfile, availableQualities);
            }

            public string GetDefaultQobuzQuality()
            {
                return "flac-cd";
            }

            public bool IsValidQobuzQuality(string qobuzQuality)
            {
                var validQualities = new[] { "flac-hires", "flac-cd", "mp3-320" };
                return !string.IsNullOrEmpty(qobuzQuality) && 
                       System.Linq.Enumerable.Contains(validQualities, qobuzQuality, StringComparer.OrdinalIgnoreCase);
            }

            public System.Collections.Generic.Dictionary<string, string> GetSupportedQobuzQualities()
            {
                return new System.Collections.Generic.Dictionary<string, string>
                {
                    ["flac-hires"] = "Hi-Res FLAC (up to 24-bit/192kHz)",
                    ["flac-cd"] = "CD Quality FLAC (16-bit/44.1kHz)",
                    ["mp3-320"] = "High Quality MP3 (320kbps)"
                };
            }

            public bool DoesQualityMeetProfileRequirements(
                Models.Lidarr.LidarrQualityProfile qualityProfile, 
                string qobuzQuality)
            {
                var allowedQualities = GetQualityFallbackChain(qualityProfile);
                return System.Linq.Enumerable.Contains(allowedQualities, qobuzQuality, StringComparer.OrdinalIgnoreCase);
            }

            public QualityRecommendation GetQualityRecommendation(
                Models.Lidarr.LidarrAlbum album, 
                Models.Lidarr.LidarrQualityProfile qualityProfile)
            {
                // Simple implementation for migration
                return new QualityRecommendation
                {
                    PrimaryQuality = GetPreferredQobuzQuality(qualityProfile),
                    FallbackQualities = GetQualityFallbackChain(qualityProfile),
                    Reason = "Migration adapter recommendation"
                };
            }
        }

        /// <summary>
        /// Logger adapter to convert between NLog and IQobuzLogger.
        /// </summary>
        private class QobuzLoggerAdapter : IQobuzLogger
        {
            private readonly Logger _logger;

            public QobuzLoggerAdapter(Logger logger)
            {
                _logger = logger;
            }

            public void Debug(string message, params object[] args)
            {
                _logger.Debug(message, args);
            }

            public void Info(string message, params object[] args)
            {
                _logger.Info(message, args);
            }

            public void Warn(string message, params object[] args)
            {
                _logger.Warn(message, args);
            }

            public void Error(string message, params object[] args)
            {
                _logger.Error(message, args);
            }

            public void Error(Exception ex, string message, params object[] args)
            {
                _logger.Error(ex, message, args);
            }

            public void Warn(Exception ex, string message, params object[] args)
            {
                _logger.Warn(ex, message, args);
            }
        }

        #endregion
    }
}