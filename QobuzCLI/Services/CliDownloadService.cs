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

                // REAL download implementation using stream URL
                var fileName = SanitizeFileName($"{track.TrackNumber:D2} - {track.Performer?.Name ?? track.Album?.Artist?.Name} - {track.Title}.flac");
                var filePath = Path.Combine(outputPath, fileName);
                
                // Ensure directory exists
                Directory.CreateDirectory(outputPath);
                
                // Download the actual audio file (stream to disk to avoid high memory usage)
                _logger.Info("Downloading audio file from: {0}", streamInfo.Url);
                var bytesWritten = await DownloadToFileAsync(streamInfo.Url, filePath, progress, cancellationToken);
                
                // Apply basic metadata
                await ApplyMetadataAsync(filePath, track, album);
                
                _logger.Info("Track download completed: {0} ({1:N0} bytes)", filePath, bytesWritten);
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
                    await DownloadTrackAsync(track, album, outputPath, quality, null!, CancellationToken.None);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Album download failed: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Sanitize filename for cross-platform compatibility
        /// </summary>
        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "Unknown";
            
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = fileName;
            
            foreach (var invalidChar in invalidChars)
            {
                sanitized = sanitized.Replace(invalidChar, '_');
            }
            
            // Additional cleanup for common problematic characters
            sanitized = sanitized
                .Replace(":", " -")
                .Replace("?", "")
                .Replace("*", "")
                .Replace("\"", "'")
                .Replace("|", "-");
            
            return sanitized.Trim();
        }

        /// <summary>
        /// Apply basic metadata to downloaded file
        /// </summary>
        private async Task ApplyMetadataAsync(string filePath, QobuzTrack track, QobuzAlbum album)
        {
            try
            {
                await Task.Run(() =>
                {
                    using var file = TagLib.File.Create(filePath);
                    
                    if (!string.IsNullOrEmpty(track.Title))
                        file.Tag.Title = track.Title;
                    
                    if (!string.IsNullOrEmpty(track.Performer?.Name))
                        file.Tag.Performers = new[] { track.Performer.Name };
                    else if (!string.IsNullOrEmpty(album.Artist?.Name))
                        file.Tag.Performers = new[] { album.Artist.Name };
                        
                    if (!string.IsNullOrEmpty(album.Title))
                        file.Tag.Album = album.Title;
                    
                    if (track.TrackNumber > 0)
                        file.Tag.Track = (uint)track.TrackNumber;
                        
                    if (album.ReleaseDate != null)
                        file.Tag.Year = (uint)album.ReleaseDate.Year;
                    
                    if (!string.IsNullOrEmpty(album.Genre?.Name))
                        file.Tag.Genres = new[] { album.Genre.Name };

                    file.Save();
                });

                _logger.Info("Metadata applied to: {0}", filePath);
            }
            catch (Exception ex)
            {
                // Don't fail download for metadata issues
                _logger.Warn(ex, "Failed to apply metadata to {0}", filePath);
            }
        }

        private static async Task<long> DownloadToFileAsync(
            string url,
            string filePath,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await using var output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                written += read;
                if (progress != null && total.HasValue && total.Value > 0)
                {
                    progress.Report(Math.Min(100.0, written * 100.0 / total.Value));
                }
            }
            if (progress != null && total.HasValue)
            {
                progress.Report(100.0);
            }
            return written;
        }
    }
}
