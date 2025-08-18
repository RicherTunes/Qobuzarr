using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Services.Metadata;

namespace Lidarr.Plugin.Qobuzarr.Services.Metadata
{
    /// <summary>
    /// Engine that selects and executes the optimal metadata strategy
    /// </summary>
    public class MetadataStrategyEngine
    {
        private readonly Logger _logger;
        private readonly IntelligentReleaseMapper _releaseMapper;
        private readonly List<IMetadataStrategy> _strategies;
        private readonly MetadataOptimizationStats _stats;

        public MetadataStrategyEngine(
            Logger logger,
            IntelligentReleaseMapper releaseMapper,
            IEnumerable<IMetadataStrategy> strategies)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _releaseMapper = releaseMapper ?? throw new ArgumentNullException(nameof(releaseMapper));
            _strategies = strategies?.ToList() ?? throw new ArgumentNullException(nameof(strategies));
            _stats = new MetadataOptimizationStats();
        }

        /// <summary>
        /// Downloads album using intelligent metadata strategy selection
        /// </summary>
        public async Task<MetadataDownloadResult> DownloadAlbumWithOptimalStrategyAsync(
            QobuzAlbum qobuzAlbum, 
            LidarrAlbum lidarrAlbum = null)
        {
            if (qobuzAlbum == null)
                throw new ArgumentNullException(nameof(qobuzAlbum));

            _stats.TotalAlbums++;

            // Strategy 1: No Lidarr data available - use Qobuz metadata
            if (lidarrAlbum == null)
            {
                _logger.Info("No Lidarr data: Using Qobuz metadata for '{0}'", qobuzAlbum.Title);
                _stats.QobuzMetadataUsed++;
                
                var qobuzStrategy = _strategies.FirstOrDefault(s => s.StrategyName == "Qobuz");
                if (qobuzStrategy?.CanHandle(qobuzAlbum, null) == true)
                {
                    return await qobuzStrategy.DownloadAlbumAsync(qobuzAlbum, null);
                }
            }

            // Strategy 2: Validate release compatibility
            var matchResult = _releaseMapper.ValidateReleaseMatch(lidarrAlbum, qobuzAlbum);

            if (!matchResult.IsCompatible)
            {
                _logger.Info("Metadata fallback: {0}. Using Qobuz metadata for safety.", matchResult.Reason);
                _stats.QobuzMetadataUsed++;
                _stats.MatchingFailures++;
                
                var qobuzStrategy = _strategies.FirstOrDefault(s => s.StrategyName == "Qobuz");
                if (qobuzStrategy?.CanHandle(qobuzAlbum, lidarrAlbum) == true)
                {
                    return await qobuzStrategy.DownloadAlbumAsync(qobuzAlbum, lidarrAlbum);
                }
            }

            if (matchResult.RequiresHybridApproach)
            {
                _logger.Info("Hybrid metadata: {0}. Combining Lidarr and Qobuz data.", matchResult.Reason);
                _stats.HybridMetadataUsed++;
                
                var hybridStrategy = _strategies.FirstOrDefault(s => s.StrategyName == "Hybrid");
                if (hybridStrategy?.CanHandle(qobuzAlbum, lidarrAlbum) == true)
                {
                    return await hybridStrategy.DownloadAlbumAsync(qobuzAlbum, lidarrAlbum);
                }
            }

            // Strategy 3: Perfect match - use Lidarr metadata optimization
            _logger.Info("Metadata optimization: Releases match perfectly. Using Lidarr's MusicBrainz data for '{0}'", 
                        lidarrAlbum.Title);
            _stats.LidarrMetadataUsed++;
            
            var lidarrStrategy = _strategies.FirstOrDefault(s => s.StrategyName == "Lidarr");
            if (lidarrStrategy?.CanHandle(qobuzAlbum, lidarrAlbum) == true)
            {
                var result = await lidarrStrategy.DownloadAlbumAsync(qobuzAlbum, lidarrAlbum);
                _stats.ApiCallsSaved += result.ApiCallsSaved;
                return result;
            }

            // Fallback to first available strategy
            var fallbackStrategy = _strategies.FirstOrDefault(s => s.CanHandle(qobuzAlbum, lidarrAlbum));
            if (fallbackStrategy != null)
            {
                _logger.Warn("Using fallback strategy: {0}", fallbackStrategy.StrategyName);
                return await fallbackStrategy.DownloadAlbumAsync(qobuzAlbum, lidarrAlbum);
            }

            throw new InvalidOperationException("No suitable metadata strategy found");
        }

        /// <summary>
        /// Gets current optimization statistics
        /// </summary>
        public MetadataOptimizationStats GetStatistics()
        {
            return _stats.Clone();
        }

        /// <summary>
        /// Logs current optimization statistics
        /// </summary>
        public void LogStatistics()
        {
            _stats.LogStats(_logger);
        }

        /// <summary>
        /// Gets available strategy names
        /// </summary>
        public IReadOnlyList<string> GetAvailableStrategies()
        {
            return _strategies.Select(s => s.StrategyName).ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Statistics tracking for metadata optimization effectiveness
    /// </summary>
    public class MetadataOptimizationStats
    {
        public int TotalAlbums { get; set; }
        public int LidarrMetadataUsed { get; set; }
        public int QobuzMetadataUsed { get; set; }
        public int HybridMetadataUsed { get; set; }
        public int ApiCallsSaved { get; set; }
        public int MatchingFailures { get; set; }
        public double AverageMatchRate { get; set; }

        public MetadataOptimizationStats Clone()
        {
            return new MetadataOptimizationStats
            {
                TotalAlbums = TotalAlbums,
                LidarrMetadataUsed = LidarrMetadataUsed,
                QobuzMetadataUsed = QobuzMetadataUsed,
                HybridMetadataUsed = HybridMetadataUsed,
                ApiCallsSaved = ApiCallsSaved,
                MatchingFailures = MatchingFailures,
                AverageMatchRate = AverageMatchRate
            };
        }

        public void LogStats(Logger logger)
        {
            logger.Info("Metadata optimization statistics:");
            logger.Info("   Albums processed: {0}", TotalAlbums);
            
            if (TotalAlbums > 0)
            {
                logger.Info("   Lidarr metadata: {0} ({1:P1})", LidarrMetadataUsed, (double)LidarrMetadataUsed / TotalAlbums);
                logger.Info("   Qobuz metadata: {0} ({1:P1})", QobuzMetadataUsed, (double)QobuzMetadataUsed / TotalAlbums);
                logger.Info("   Hybrid approach: {0} ({1:P1})", HybridMetadataUsed, (double)HybridMetadataUsed / TotalAlbums);
            }
            
            logger.Info("   API calls saved: {0}", ApiCallsSaved);
            
            if (TotalAlbums > 0)
            {
                logger.Info("   Average match rate: {0:P2}", AverageMatchRate);
                
                if (MatchingFailures > TotalAlbums * 0.1) // More than 10% failures
                {
                    logger.Warn("High matching failure rate: {0} failures ({1:P1}). Consider adjusting matching thresholds.", 
                                MatchingFailures, (double)MatchingFailures / TotalAlbums);
                }
            }
        }
    }
}