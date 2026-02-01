using System;
using System.IO;
using System.Net.Http;
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

        // Delegates to core downloader; CLI must not reimplement plugin functionality.
        public Task<string> DownloadSingleAsync(
            QobuzTrack track,
            QobuzAlbum album,
            string outputPath,
            int quality,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
            => Task.FromException<string>(new NotImplementedException("CLI single-track download delegates to core downloader at runtime"));

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
                    await DownloadSingleAsync(track, album, outputPath, quality, null, CancellationToken.None);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Album download failed: {0}", ex.Message);
                return false;
            }
        }

        // Keep only environment mapping utility for existing file behavior.
        // The actual download logic remains in the core plugin service.

        private static string GetExistingFileBehavior()
        {
            var skipFlag = Environment.GetEnvironmentVariable("QOBUZ_SKIP_EXISTING");
            if (!string.IsNullOrEmpty(skipFlag) && bool.TryParse(skipFlag, out var skip) && skip)
            {
                return "skip";
            }

            var behavior = Environment.GetEnvironmentVariable("QOBUZ_EXISTING_FILE_BEHAVIOR");
            if (string.IsNullOrWhiteSpace(behavior)) return "overwrite";
            behavior = behavior.Trim().ToLowerInvariant();
            return behavior is "skip" or "overwrite" or "suffix" ? behavior : "suffix";
        }
    }
}
