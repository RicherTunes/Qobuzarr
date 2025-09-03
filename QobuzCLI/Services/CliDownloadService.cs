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
                var desiredPath = Path.Combine(outputPath, fileName);

                // Existing file behavior (suffix | skip | overwrite)
                var existingBehavior = GetExistingFileBehavior();
                if (existingBehavior == "skip" && File.Exists(desiredPath))
                {
                    _logger.Info("Skipping existing file due to configuration: {0}", desiredPath);
                    return desiredPath;
                }
                
                // Ensure directory exists
                Directory.CreateDirectory(outputPath);

                // Download the actual audio file (stream to disk to avoid high memory usage)
                _logger.Info("Downloading audio file from: {0}", streamInfo.Url);
                var result = await DownloadToFileAsync(streamInfo.Url, desiredPath, progress, cancellationToken);

                // Apply basic metadata
                await ApplyMetadataAsync(result.FinalPath, track, album);

                _logger.Info("Track download completed: {0} ({1:N0} bytes)", result.FinalPath, result.BytesWritten);
                return result.FinalPath;
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

        private static async Task<(string FinalPath, long BytesWritten)> DownloadToFileAsync(
            string url,
            string desiredPath,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            // Use a temporary .partial file and atomic move on success.
            var directory = Path.GetDirectoryName(desiredPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Resolve a non-colliding final path and reserve its .partial exclusively
            var baseName = Path.GetFileNameWithoutExtension(desiredPath);
            var ext = Path.GetExtension(desiredPath);
            string finalPath = desiredPath;
            string tempPath = finalPath + ".partial";
            FileStream output = null;
            long resumeFrom = 0;

            for (int i = 0; i < 1000; i++)
            {
                finalPath = i == 0 ? desiredPath : Path.Combine(directory!, $"{baseName} ({i}){ext}");
                tempPath = finalPath + ".partial";
                try
                {
                    if (File.Exists(tempPath))
                    {
                        // Try to take exclusive ownership and resume
                        output = new FileStream(
                            tempPath,
                            FileMode.Open,
                            FileAccess.Write,
                            FileShare.None,
                            bufferSize: 81920,
                            useAsync: true);
                        resumeFrom = output.Length;
                        output.Position = resumeFrom;
                    }
                    else
                    {
                        output = new FileStream(
                            tempPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None,
                            bufferSize: 81920,
                            useAsync: true);
                        resumeFrom = 0;
                    }
                    break; // reserved
                }
                catch (IOException)
                {
                    // Partial file is in use or path collision; try next suffix
                    continue;
                }
            }

            if (output == null)
            {
                throw new IOException("Could not reserve a unique temp path for download.");
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
                try { await output.DisposeAsync().ConfigureAwait(false); } catch { }
                try { File.Delete(tempPath); } catch { /* ignore */ }
                // Recreate reservation from scratch (same finalPath)
                output = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);
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

                // Ensure data is flushed and stream closed before finalize
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                await output.DisposeAsync().ConfigureAwait(false);

                // Finalize: atomic move to destination
                try
                {
                    var existingBehavior = GetExistingFileBehavior();
                    if (File.Exists(finalPath))
                    {
                        if (existingBehavior == "overwrite")
                        {
                            // Replace existing file
                            File.Delete(finalPath);
                            File.Move(tempPath, finalPath);
                        }
                        else if (existingBehavior == "skip")
                        {
                            // Skip finalization, leave existing file untouched
                            try { File.Delete(tempPath); } catch { }
                            if (progress != null && total.HasValue) progress.Report(100.0);
                            return (finalPath, 0);
                        }
                        else // suffix
                        {
                            // Compute a new non-colliding final path
                            var idx = 1;
                            var dir = Path.GetDirectoryName(finalPath)!;
                            var stem = Path.GetFileNameWithoutExtension(finalPath);
                            var extension = Path.GetExtension(finalPath);
                            while (true)
                            {
                                var candidate = Path.Combine(dir, $"{stem} ({idx}){extension}");
                                if (!File.Exists(candidate))
                                {
                                    File.Move(tempPath, candidate);
                                    finalPath = candidate;
                                    break;
                                }
                                idx++;
                            }
                        }
                    }
                    else
                    {
                        File.Move(tempPath, finalPath);
                    }
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

                return (finalPath, written - resumeFrom);
            }
            finally
            {
                response.Dispose();
            }
        }
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
