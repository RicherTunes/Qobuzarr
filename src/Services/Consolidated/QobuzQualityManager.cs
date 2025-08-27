using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services.Quality;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services.Consolidated
{
    /// <summary>
    /// Orchestrates quality management operations by coordinating decomposed services.
    /// Previously consolidated service now refactored to follow Single Responsibility Principle.
    /// Delegates to: QualityDetectionService, StreamInfoService, QualityCacheService, QualityMappingService.
    /// </summary>
    public class QobuzQualityManager : IQobuzQualityManager
    {
        private readonly IQualityDetectionService _qualityDetectionService;
        private readonly IStreamInfoService _streamInfoService;
        private readonly IQualityCacheService _qualityCacheService;
        private readonly IQualityMappingService _qualityMappingService;
        private readonly IQobuzLogger _logger;

        // Constants moved to decomposed services

        public QobuzQualityManager(
            IQualityDetectionService qualityDetectionService,
            IStreamInfoService streamInfoService,
            IQualityCacheService qualityCacheService,
            IQualityMappingService qualityMappingService,
            IQobuzLogger logger)
        {
            _qualityDetectionService = qualityDetectionService ?? throw new ArgumentNullException(nameof(qualityDetectionService));
            _streamInfoService = streamInfoService ?? throw new ArgumentNullException(nameof(streamInfoService));
            _qualityCacheService = qualityCacheService ?? throw new ArgumentNullException(nameof(qualityCacheService));
            _qualityMappingService = qualityMappingService ?? throw new ArgumentNullException(nameof(qualityMappingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Quality Detection

        /// <summary>
        /// Detects available qualities for a single track.
        /// </summary>
        public async Task<QualityDetectionResult> DetectAvailableQualitiesAsync(string trackId, CancellationToken cancellationToken = default)
        {
            return await _qualityDetectionService.DetectAvailableQualitiesAsync(trackId, cancellationToken);
        }

        /// <summary>
        /// Intelligently detects album-level quality availability using sampling.
        /// </summary>
        public async Task<Models.AlbumQualityResult> DetectAlbumQualityAsync(
            QobuzAlbum album, 
            int preferredQuality,
            CancellationToken cancellationToken = default)
        {
            if (album?.GetTracks()?.Any() != true)
            {
                return Models.AlbumQualityResult.Failed("Album has no tracks");
            }

            var cacheKey = $"album_quality_{album.Id}_{preferredQuality}";
            
            // Check cache first
            var cached = await _qualityCacheService.GetCachedQualityAsync(cacheKey);
            if (cached != null)
            {
                _logger.Info("Using cached quality data for album '{0}'", album.Title);
                return cached;
            }

            // Delegate to quality detection service
            var result = await _qualityDetectionService.DetectAlbumQualityAsync(album, preferredQuality, cancellationToken);
            
            // Cache the result
            await _qualityCacheService.CacheQualityResultAsync(cacheKey, result);
            
            return result;
        }

        #endregion

        #region Quality Mapping

        /// <summary>
        /// Maps a Lidarr quality profile to Qobuz quality.
        /// </summary>
        public QobuzQuality MapLidarrQuality(LidarrQualityProfile profile)
        {
            return _qualityMappingService.MapLidarrQuality(profile);
        }

        /// <summary>
        /// Gets the quality fallback chain for a given preferred quality.
        /// </summary>
        public List<QobuzQuality> GetQualityFallbackChain(QobuzQuality preferred)
        {
            return _qualityMappingService.GetQualityFallbackChain(preferred);
        }

        #endregion

        #region Quality Selection

        /// <summary>
        /// Selects the best available quality for a track with automatic fallback.
        /// </summary>
        public async Task<QualitySelectionResult> SelectBestQualityAsync(
            string trackId, 
            QobuzQuality preferred,
            CancellationToken cancellationToken = default)
        {
            return await _streamInfoService.SelectBestQualityAsync(trackId, preferred, cancellationToken);
        }

        /// <summary>
        /// Executes an operation with automatic quality fallback.
        /// </summary>
        public async Task<T> ExecuteWithQualityFallbackAsync<T>(
            Func<QobuzQuality, Task<T>> operation,
            QobuzQuality preferred = null,
            CancellationToken cancellationToken = default)
        {
            return await _streamInfoService.ExecuteWithQualityFallbackAsync(operation, preferred, cancellationToken);
        }

        #endregion

        #region Stream URL Management

        /// <summary>
        /// Gets stream information for a track with the specified quality.
        /// </summary>
        public async Task<StreamInfo> GetStreamInfoAsync(string trackId, QobuzQuality quality, CancellationToken cancellationToken = default)
        {
            return await _streamInfoService.GetStreamInfoAsync(trackId, quality, cancellationToken);
        }

        /// <summary>
        /// Gets stream information for multiple tracks in batch.
        /// </summary>
        public async Task<BatchStreamResult> GetBatchStreamInfoAsync(
            List<string> trackIds, 
            QobuzQuality quality,
            CancellationToken cancellationToken = default)
        {
            return await _streamInfoService.GetBatchStreamInfoAsync(trackIds, quality, cancellationToken);
        }

        #endregion

        // All implementation logic moved to decomposed services:
        // - Quality detection: QualityDetectionService  
        // - Stream information: StreamInfoService
        // - Caching: QualityCacheService
        // - Quality mapping: QualityMappingService

        // All model classes moved to Models directory
    }
}