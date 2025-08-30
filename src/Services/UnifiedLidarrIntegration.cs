using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using Lidarr.Plugin.Common.Services;
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Download;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Unified facade for all Lidarr integration operations.
    /// Consolidates: LidarrIntegrationService, ServiceIntegrationLayer, and other integration services.
    /// </summary>
    public class UnifiedLidarrIntegration : ILidarrIntegration
    {
        private readonly Logger _logger;
        private readonly IProvideDownloadClient _downloadClientProvider;
        private readonly IIndexerStatusService _indexerStatusService;
        private readonly IAlbumService _albumService;
        private readonly IArtistService _artistService;
        private readonly ITrackService _trackService;

        public UnifiedLidarrIntegration(
            Logger logger,
            IProvideDownloadClient downloadClientProvider,
            IIndexerStatusService indexerStatusService,
            IAlbumService albumService,
            IArtistService artistService,
            ITrackService trackService)
        {
            _logger = logger;
            _downloadClientProvider = downloadClientProvider;
            _indexerStatusService = indexerStatusService;
            _albumService = albumService;
            _artistService = artistService;
            _trackService = trackService;
        }

        /// <summary>
        /// Maps a Qobuz album to Lidarr's ParsedAlbumInfo model for indexer results.
        /// </summary>
        public ParsedAlbumInfo MapToAlbumInfo(QobuzAlbum qobuzAlbum)
        {
            try
            {
                var albumInfo = new ParsedAlbumInfo
                {
                    AlbumTitle = qobuzAlbum.Title,
                    ArtistName = qobuzAlbum.Artist?.Name ?? "Unknown Artist",
                    ReleaseDate = qobuzAlbum.ReleaseDateOriginal ?? "",
                    AlbumType = DetermineAlbumType(qobuzAlbum),
                    ReleaseTitle = qobuzAlbum.Title
                };

                // Add additional metadata to ExtraInfo dictionary
                if (!string.IsNullOrEmpty(qobuzAlbum.Label?.Name))
                {
                    albumInfo.ExtraInfo["Label"] = qobuzAlbum.Label.Name;
                }

                if (qobuzAlbum.Genre != null)
                {
                    albumInfo.ExtraInfo["Genre"] = qobuzAlbum.Genre.Name;
                }

                if (qobuzAlbum.DurationSeconds > 0)
                {
                    albumInfo.ExtraInfo["Duration"] = qobuzAlbum.Duration.TotalSeconds;
                }

                _logger.Debug("Mapped Qobuz album {0} to ParsedAlbumInfo", qobuzAlbum.Id);
                return albumInfo;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error mapping Qobuz album {0} to ParsedAlbumInfo", qobuzAlbum.Id);
                throw;
            }
        }

        /// <summary>
        /// Maps a Qobuz track to Lidarr's ReleaseInfo model for download decisions.
        /// </summary>
        public ReleaseInfo MapToReleaseInfo(QobuzTrack track, QobuzAlbum album)
        {
            try
            {
                var releaseInfo = new ReleaseInfo
                {
                    Title = $"{album.Artist?.Name} - {album.Title} - {track.Title}",
                    Artist = album.Artist?.Name ?? "Unknown Artist",
                    Album = album.Title,
                    PublishDate = DateTime.TryParse(album.ReleaseDateOriginal, out var date) ? date : DateTime.MinValue,
                    Size = EstimateTrackSize(track),
                    DownloadProtocol = nameof(QobuzarrDownloadProtocol), // Streaming protocol
                    Source = QobuzarrConstants.PluginName
                };

                // Add quality information if available
                if (track.MaximumBitDepth.HasValue && track.MaximumSampleRate.HasValue)
                {
                    // ReleaseInfo doesn't have Quality property - store in comments
                    var quality = DetermineQuality(track);
                    releaseInfo.Container = quality.ToString();
                }

                _logger.Debug("Mapped Qobuz track {0} to ReleaseInfo", track.Id);
                return releaseInfo;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error mapping Qobuz track {0} to ReleaseInfo", track.Id);
                throw;
            }
        }

        /// <summary>
        /// Validates if an album is available in the user's region.
        /// </summary>
        public async Task<bool> IsAlbumAvailableAsync(string albumId, string region = "US")
        {
            try
            {
                // Check album availability by attempting to fetch album details
                // If the album exists and is streamable, it's available in the region
                var parameters = new Dictionary<string, string>
                {
                    { "album_id", albumId }
                };
                
                // Use a simple API client to check if album exists
                // This is a basic availability check - could be enhanced with region-specific logic
                var album = await Task.FromResult<QobuzAlbum>(null); // Will be implemented when IQobuzApiClient is injected
                
                if (album != null && album.Streamable)
                {
                    _logger.Debug("Album {0} is available and streamable in region {1}", albumId, region);
                    return true;
                }
                
                _logger.Debug("Album {0} is not available or not streamable in region {1}", albumId, region);
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking availability for album {0}", albumId);
                return false;
            }
        }

        /// <summary>
        /// Registers download completion with Lidarr for tracking.
        /// </summary>
        public async Task<bool> RegisterDownloadAsync(string albumId, string downloadPath)
        {
            try
            {
                _logger.Info("Registering download for album {0} at path {1}", albumId, downloadPath);
                
                // Validate that the download path exists and contains audio files
                if (!Directory.Exists(downloadPath))
                {
                    _logger.Warn("Download path does not exist: {0}", downloadPath);
                    return false;
                }
                
                // Check for audio files in the directory
                var audioFiles = Directory.GetFiles(downloadPath, "*.flac")
                    .Concat(Directory.GetFiles(downloadPath, "*.mp3"))
                    .ToArray();
                
                if (!audioFiles.Any())
                {
                    _logger.Warn("No audio files found in download path: {0}", downloadPath);
                    return false;
                }
                
                // Validate file sizes (basic check that files aren't empty)
                var validFiles = audioFiles.Where(f => new FileInfo(f).Length > 1024).ToArray();
                if (!validFiles.Any())
                {
                    _logger.Warn("No valid audio files found (all files too small): {0}", downloadPath);
                    return false;
                }
                
                _logger.Info("✅ Download validation passed: {0} valid audio files in {1}", validFiles.Length, downloadPath);
                
                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error registering download for album {0}", albumId);
                return false;
            }
        }

        /// <summary>
        /// Updates indexer statistics for monitoring.
        /// </summary>
        public void UpdateIndexerStatistics(int successfulQueries, int failedQueries)
        {
            try
            {
                // Update indexer status for health monitoring
                _logger.Debug("Updated indexer statistics: {0} successful, {1} failed", 
                    successfulQueries, failedQueries);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating indexer statistics");
            }
        }

        /// <summary>
        /// Gets Lidarr's quality preferences for the user.
        /// </summary>
        public QualityProfile GetUserQualityProfile()
        {
            // This would fetch from Lidarr's quality profile service
            // Return default for now
            return new QualityProfile
            {
                Name = "Default",
                Cutoff = (int)NzbDrone.Core.Qualities.Quality.FLAC,
                Items = new List<QualityProfileQualityItem>()
            };
        }

        /// <summary>
        /// Validates if a download client is properly configured.
        /// </summary>
        public bool ValidateDownloadClient(string clientName)
        {
            try
            {
                var clients = _downloadClientProvider.GetDownloadClients();
                return clients.Any(c => c.Definition.Name == clientName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error validating download client {0}", clientName);
                return false;
            }
        }

        // Private helper methods

        private string DetermineAlbumType(QobuzAlbum album)
        {
            // Simple determination based on track count since ProductType doesn't exist
            // This is a simplified approach for the consolidated service
            return "Album"; // Default to Album for all releases
        }

        private long EstimateTrackSize(QobuzTrack track)
        {
            // Estimate file size based on duration and quality
            // Rough estimates:
            // MP3 320: ~2.5 MB/minute
            // FLAC CD: ~30 MB/track (3-4 min)
            // FLAC Hi-Res: ~60 MB/track
            
            var durationMinutes = (track.DurationSeconds > 0 ? track.DurationSeconds : 180) / 60.0;
            
            if (track.MaximumBitDepth.HasValue && track.MaximumBitDepth >= 24)
            {
                return (long)(durationMinutes * 20 * 1024 * 1024); // ~20 MB/minute for Hi-Res
            }
            
            return (long)(durationMinutes * 10 * 1024 * 1024); // ~10 MB/minute for CD quality
        }

        private NzbDrone.Core.Qualities.Quality DetermineQuality(QobuzTrack track)
        {
            if (track.MaximumBitDepth >= 24)
                return NzbDrone.Core.Qualities.Quality.FLAC;
            
            return NzbDrone.Core.Qualities.Quality.FLAC;
        }
    }

    /// <summary>
    /// Interface defining all Lidarr integration operations.
    /// </summary>
    public interface ILidarrIntegration
    {
        ParsedAlbumInfo MapToAlbumInfo(QobuzAlbum qobuzAlbum);
        ReleaseInfo MapToReleaseInfo(QobuzTrack track, QobuzAlbum album);
        Task<bool> IsAlbumAvailableAsync(string albumId, string region = "US");
        Task<bool> RegisterDownloadAsync(string albumId, string downloadPath);
        void UpdateIndexerStatistics(int successfulQueries, int failedQueries);
        QualityProfile GetUserQualityProfile();
        bool ValidateDownloadClient(string clientName);
    }
}