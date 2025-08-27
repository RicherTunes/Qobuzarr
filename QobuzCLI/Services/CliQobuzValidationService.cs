using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;

namespace QobuzCLI.Services
{
    /// <summary>
    /// CLI-specific validation service that uses legacy QobuzQualityService interface
    /// This provides backward compatibility for CLI while main plugin uses consolidated services
    /// </summary>
    public class CliQobuzValidationService
    {
        private readonly QobuzSearchService _searchService;
        private readonly QobuzQualityService _qualityService;
        private readonly IQobuzLogger _logger;
        private readonly IQobuzCache _cache;

        public CliQobuzValidationService(
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
            try
            {
                _logger.Debug("Validating downloadability for album {0}", albumId);

                // Get album details first
                var album = await _searchService.GetAlbumAsync(albumId);
                if (album == null)
                {
                    _logger.Debug("Album {0} not found - not downloadable", albumId);
                    return false;
                }

                // Check a sample of tracks for downloadability
                var tracks = album.GetTracks();
                if (!tracks.Any())
                {
                    _logger.Debug("Album {0} has no tracks - not downloadable", albumId);
                    return false;
                }

                // Test downloadability of first few tracks
                var sampleSize = Math.Min(3, tracks.Count);
                var downloadableCount = 0;

                for (int i = 0; i < sampleSize; i++)
                {
                    var track = tracks[i];
                    try
                    {
                        var (selectedQuality, streamInfo) = await _qualityService.GetBestAvailableStreamAsync(track.Id, preferredQuality);
                        if (!string.IsNullOrWhiteSpace(streamInfo?.Url))
                        {
                            downloadableCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug("Track {0} validation failed: {1}", track.Id, ex.Message);
                    }
                }

                var isDownloadable = downloadableCount > 0;
                _logger.Debug("Album {0} validation result: {1} ({2}/{3} tracks downloadable)", 
                    albumId, isDownloadable ? "DOWNLOADABLE" : "NOT_DOWNLOADABLE", downloadableCount, sampleSize);

                return isDownloadable;
            }
            catch (Exception ex)
            {
                _logger.Error("Album validation failed for {0}: {1}", albumId, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Simple track downloadability check for CLI usage
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
    }
}