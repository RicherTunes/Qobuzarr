using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Services.Metadata;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Hybrid metadata service that uses strategy pattern for optimal metadata handling
    /// </summary>
    public class HybridMetadataService
    {
        private readonly Logger _logger;
        private readonly MetadataStrategyEngine _strategyEngine;

        public HybridMetadataService(
            Logger logger,
            MetadataStrategyEngine strategyEngine)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _strategyEngine = strategyEngine ?? throw new ArgumentNullException(nameof(strategyEngine));
        }

        /// <summary>
        /// Downloads album using intelligent metadata strategy based on release compatibility analysis
        /// </summary>
        public async Task<DownloadResult> DownloadAlbumWithIntelligentMetadataAsync(
            QobuzAlbum qobuzAlbum, 
            LidarrAlbum lidarrAlbum = null)
        {
            var result = await _strategyEngine.DownloadAlbumWithOptimalStrategyAsync(qobuzAlbum, lidarrAlbum);
            
            // Convert to legacy format for backward compatibility
            return new DownloadResult
            {
                TrackDownloads = result.TrackDownloads,
                MetadataStrategy = result.MetadataStrategy,
                ApiCallsSaved = result.ApiCallsSaved,
                AdditionalApiCalls = result.AdditionalApiCalls
            };
        }

        /// <summary>
        /// Gets current optimization statistics
        /// </summary>
        public MetadataOptimizationStats GetStatistics()
        {
            return _strategyEngine.GetStatistics();
        }

        /// <summary>
        /// Logs current optimization statistics
        /// </summary>
        public void LogStatistics()
        {
            _strategyEngine.LogStatistics();
        }

        /// <summary>
        /// Gets suggested search queries for improved Qobuz search results
        /// </summary>
        public List<string> GetSmartSearchQueries(LidarrAlbum lidarrAlbum)
        {
            return new List<string>
            {
                $"{lidarrAlbum.ArtistName} {lidarrAlbum.Title}",
                $"\"{lidarrAlbum.ArtistName}\" \"{lidarrAlbum.Title}\"",
                lidarrAlbum.Title,
                lidarrAlbum.ArtistName
            };
        }

        /// <summary>
        /// Gets learning statistics for monitoring
        /// </summary>
        public object GetLearningStatistics()
        {
            return new { Status = "ML Learning Service not available (MusicBrainz integration pending)" };
        }
    }

    /// <summary>
    /// Download result with metadata optimization information
    /// </summary>
    public class DownloadResult
    {
        public List<TrackDownload> TrackDownloads { get; set; } = new();
        public string MetadataStrategy { get; set; }
        public int ApiCallsSaved { get; set; }
        public int AdditionalApiCalls { get; set; }
        public TimeSpan TotalDuration => TimeSpan.FromSeconds(TrackDownloads.Sum(t => t.Duration?.TotalSeconds ?? 0));
        public bool IsSuccessful => TrackDownloads.Any();
    }
}