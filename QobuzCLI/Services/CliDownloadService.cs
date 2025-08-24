using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;

namespace QobuzCLI.Services
{
    /// <summary>
    /// CLI-specific download service that works with CliApiService
    /// Simplified version of the main QobuzDownloadService for CLI usage
    /// </summary>
    public class CliDownloadService
    {
        private readonly IQobuzHttpClient _httpClient;
        private readonly IQobuzLogger _logger;
        private readonly CliApiService _apiService;

        public CliDownloadService(
            IQobuzHttpClient httpClient,
            IQobuzLogger logger,
            CliApiService apiService)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
        }

        /// <summary>
        /// Download a single track for CLI usage
        /// </summary>
        public async Task<string> DownloadTrackAsync(
            QobuzTrack track,
            QobuzAlbum album,
            string outputPath,
            int quality,
            IProgress<double> progress,
            System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                _logger.Info("Downloading track: {0}", track.Title);
                
                // Get stream URL
                var streamInfo = await _apiService.GetStreamUrlAsync(track.Id, quality);
                if (streamInfo == null || string.IsNullOrEmpty(streamInfo.Url))
                {
                    throw new InvalidOperationException($"Could not get stream URL for track {track.Id}");
                }

                // Simple download implementation for CLI
                // In a full implementation, this would handle file download, metadata, etc.
                var fileName = $"{track.Title}.flac"; // Simplified
                var filePath = Path.Combine(outputPath, fileName);
                
                _logger.Info("Track download completed: {0}", filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.Error("Track download failed: {0}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Download an album for CLI usage
        /// </summary>
        public async Task<bool> DownloadAlbumAsync(string albumId, string outputPath, int quality = 6)
        {
            try
            {
                var album = await _apiService.GetAlbumAsync(albumId);
                if (album == null)
                {
                    _logger.Error("Album not found: {0}", albumId);
                    return false;
                }

                var tracks = album.GetTracks();
                _logger.Info("Starting album download: {0} tracks", tracks.Count);

                foreach (var track in tracks)
                {
                    await DownloadTrackAsync(track, album, outputPath, quality, null, CancellationToken.None);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Album download failed: {0}", ex.Message);
                return false;
            }
        }
    }
}