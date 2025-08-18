using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for validating album and track downloadability
    /// </summary>
    public class QobuzValidationService
    {
        private readonly QobuzSearchService _searchService;
        private readonly QobuzQualityService _qualityService;
        private readonly IQobuzLogger _logger;
        private readonly IQobuzCache _cache;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(10);

        public QobuzValidationService(
            QobuzSearchService searchService,
            QobuzQualityService qualityService,
            IQobuzLogger logger,
            IQobuzCache cache = null)
        {
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
            _qualityService = qualityService ?? throw new ArgumentNullException(nameof(qualityService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache;
        }

        /// <summary>
        /// Validate that an album is actually downloadable before queuing
        /// </summary>
        public async Task<bool> ValidateAlbumDownloadabilityAsync(string albumId, int preferredQuality = 27)
        {
            var cacheKey = $"validation_{albumId}_{preferredQuality}";
            
            // Check cache first if available
            if (_cache != null && _cache.Contains(cacheKey))
            {
                var cachedResult = _cache.Get<ValidationCacheEntry>(cacheKey);
                if (cachedResult != null)
                {
                    _logger.Debug("Using cached validation result for album {0}: {1}", 
                        albumId, cachedResult.IsDownloadable ? "DOWNLOADABLE" : "NOT_DOWNLOADABLE");
                    return cachedResult.IsDownloadable;
                }
            }

            try
            {
                _logger.Debug("Validating downloadability for album {0}", albumId);

                // Get album details first
                var album = await _searchService.GetAlbumAsync(albumId);
                if (album == null)
                {
                    _logger.Debug("Album {0} not found, not downloadable", albumId);
                    CacheValidationResult(cacheKey, false);
                    return false;
                }

                // Get tracks for the album
                var tracks = await _searchService.GetAlbumTracksAsync(albumId);
                if (tracks == null || tracks.Count == 0)
                {
                    _logger.Debug("Album {0} has no tracks, not downloadable", albumId);
                    CacheValidationResult(cacheKey, false);
                    return false;
                }

                // Use optimized sampling strategy
                var tracksToCheck = GetOptimalSampleSize(tracks.Count);
                var downloadableCount = 0;

                // Smart track selection - check beginning, middle, and end
                var trackIndices = GetSmartSampleIndices(tracks.Count, tracksToCheck);

                foreach (var trackIndex in trackIndices)
                {
                    var track = tracks[trackIndex];
                    try
                    {
                        // Try to get stream URL for this track with quality fallback
                        var (selectedQuality, streamInfo) = await _qualityService.GetBestAvailableStreamAsync(track.Id, preferredQuality);
                        
                        if (!string.IsNullOrWhiteSpace(streamInfo?.Url))
                        {
                            downloadableCount++;
                            _logger.Debug("Track {0} from album {1} is downloadable (quality {2})", 
                                track.Id, albumId, selectedQuality);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug("Track {0} from album {1} not downloadable: {2}", 
                            track.Id, albumId, ex.Message);
                    }
                }

                var isDownloadable = downloadableCount > 0;
                _logger.Info("Album {0} downloadability validation: {1}/{2} sample tracks downloadable, result: {3}", 
                    albumId, downloadableCount, tracksToCheck, isDownloadable ? "DOWNLOADABLE" : "NOT_DOWNLOADABLE");

                CacheValidationResult(cacheKey, isDownloadable);
                return isDownloadable;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to validate downloadability for album {0}", albumId);
                // If validation fails, assume downloadable to avoid false negatives
                return true;
            }
        }

        /// <summary>
        /// Batch validate multiple albums efficiently
        /// </summary>
        public async Task<Dictionary<string, bool>> BatchValidateAlbumsAsync(
            List<string> albumIds, 
            int preferredQuality = 27, 
            int maxConcurrency = 3)
        {
            var results = new Dictionary<string, bool>();
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            
            var tasks = albumIds.Select(async albumId =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var isDownloadable = await ValidateAlbumDownloadabilityAsync(albumId, preferredQuality);
                    lock (results)
                    {
                        results[albumId] = isDownloadable;
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            _logger.Info("Batch validation completed: {0} albums, {1} downloadable", 
                albumIds.Count, results.Values.Count(x => x));
            
            return results;
        }

        /// <summary>
        /// Validate individual track downloadability
        /// </summary>
        public async Task<bool> ValidateTrackDownloadabilityAsync(string trackId, int preferredQuality = 27)
        {
            try
            {
                var (_, streamInfo) = await _qualityService.GetBestAvailableStreamAsync(trackId, preferredQuality);
                return !string.IsNullOrWhiteSpace(streamInfo?.Url);
            }
            catch (Exception ex)
            {
                _logger.Debug("Track {0} not downloadable: {1}", trackId, ex.Message);
                return false;
            }
        }

        private void CacheValidationResult(string cacheKey, bool isDownloadable)
        {
            if (_cache != null)
            {
                _cache.Set(cacheKey, new ValidationCacheEntry { IsDownloadable = isDownloadable }, _cacheExpiry);
            }
        }

        private int GetOptimalSampleSize(int totalTracks)
        {
            // For small albums, check all tracks
            if (totalTracks <= 3) return totalTracks;
            
            // For medium albums, check up to 5 tracks
            if (totalTracks <= 10) return Math.Min(5, totalTracks);
            
            // For large albums, check 3-5 tracks
            return Math.Min(5, Math.Max(3, totalTracks / 5));
        }

        private List<int> GetSmartSampleIndices(int totalTracks, int sampleSize)
        {
            var indices = new List<int>();
            
            if (sampleSize >= totalTracks)
            {
                // Check all tracks
                for (int i = 0; i < totalTracks; i++)
                {
                    indices.Add(i);
                }
            }
            else
            {
                // Smart sampling: beginning, middle, and end
                indices.Add(0); // First track
                
                if (sampleSize > 1)
                {
                    indices.Add(totalTracks - 1); // Last track
                }
                
                if (sampleSize > 2)
                {
                    // Add middle tracks
                    var step = totalTracks / (sampleSize - 1);
                    for (int i = 1; i < sampleSize - 1; i++)
                    {
                        indices.Add(Math.Min(i * step, totalTracks - 1));
                    }
                }
                
                // Remove duplicates and sort
                indices = indices.Distinct().OrderBy(i => i).ToList();
            }
            
            return indices;
        }
    }

    /// <summary>
    /// Exception thrown when a track is only available as preview/sample
    /// </summary>
    public class QobuzPreviewOnlyException : Exception
    {
        public QobuzPreviewOnlyException(string message) : base(message) { }
        public QobuzPreviewOnlyException(string message, Exception innerException) : base(message, innerException) { }
    }
    
    /// <summary>
    /// Cache entry for validation results
    /// </summary>
    internal class ValidationCacheEntry
    {
        public bool IsDownloadable { get; set; }
    }
}