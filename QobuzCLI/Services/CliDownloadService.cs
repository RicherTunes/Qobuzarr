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
            if (string.IsNullOrWhiteSpace(fileName)) return "Unknown";

            // Prefer shared helper for cross-platform safe file segments
            var sanitized = Lidarr.Plugin.Common.Security.Sanitize.PathSegment(fileName);
            if (string.IsNullOrWhiteSpace(sanitized)) return "Unknown";

            // Apply minor cosmetic replacements and collapse whitespace
            sanitized = sanitized
                .Replace(":", " -")
                .Replace("\"", "'")
                .Replace("|", "-")
                .Trim();

            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[\s\-]+", " ").Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "Unknown" : sanitized;
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
            // Use a temporary .partial file and atomic move on success.
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var tempPath = filePath + ".partial";
            long resumeFrom = 0;
            if (File.Exists(tempPath))
            {
                var info = new FileInfo(tempPath);
                resumeFrom = info.Length;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

            // Build request to support range/resume when possible
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (resumeFrom > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);
            }

            // Execute with shared-library resilience helper (429-aware, jitter, gates)
            var response = await Lidarr.Plugin.Common.Utilities.HttpClientExtensions
                .ExecuteWithResilienceAsync(http, request, maxRetries: 5, retryBudget: TimeSpan.FromSeconds(60), maxConcurrencyPerHost: 6, cancellationToken)
                .ConfigureAwait(false);

            // If server didn't honor range, start from scratch
            if (resumeFrom > 0 && response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                // Discard partial and retry without range header
                try { File.Delete(tempPath); } catch { /* ignore */ }
                resumeFrom = 0;
                request = new HttpRequestMessage(HttpMethod.Get, url);
                // Dispose previous response and acquire a fresh one
                response.Dispose();
                response = await Lidarr.Plugin.Common.Utilities.HttpClientExtensions
                    .ExecuteWithResilienceAsync(http, request, maxRetries: 5, retryBudget: TimeSpan.FromSeconds(60), maxConcurrencyPerHost: 6, cancellationToken)
                    .ConfigureAwait(false);
            }

            try
            {
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength;
                var total = contentLength.HasValue ? contentLength.Value + resumeFrom : (long?)null;

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

                // Open output stream in append mode if resuming, else create new
                await using var output = new FileStream(
                    tempPath,
                    resumeFrom > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);

                var buffer = new byte[81920];
                long written = resumeFrom;
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

                // Finalize: atomic move to destination
                try
                {
                    if (File.Exists(filePath))
                    {
                        // Best-effort overwrite-safe move: replace existing
                        File.Delete(filePath);
                    }
                    File.Move(tempPath, filePath);
                }
                catch
                {
                    // If move fails, leave .partial file for manual recovery
                    throw;
                }

                if (progress != null && total.HasValue)
                {
                    progress.Report(100.0);
                }

                return written - resumeFrom;
            }
            finally
            {
                response.Dispose();
            }
        }
    }
}
