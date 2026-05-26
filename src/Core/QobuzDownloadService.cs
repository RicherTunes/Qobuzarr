using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using TagLib;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Lidarr.Plugin.Qobuzarr.Services.Http;
using Lidarr.Plugin.Common.Utilities;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Core
{
    /// <summary>
    /// Core download service with no Lidarr dependencies
    /// This is what both Lidarr and CLI will use
    /// </summary>
    public class QobuzDownloadService
    {
        private readonly IQobuzHttpClient _httpClient;
        private readonly IQobuzLogger _logger;
        private readonly QobuzApiService _apiService;

        public QobuzDownloadService(
            IQobuzHttpClient httpClient,
            IQobuzLogger logger,
            QobuzApiService apiService)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiService = apiService;
        }

        public async Task<string> DownloadTrackAsync(
            QobuzTrack track,
            QobuzAlbum album,
            string outputPath,
            int preferredQuality,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default,
            bool allowQualityFallback = true)
        {
            try
            {
                _logger.Debug("Starting download of track: {0}", track.GetFullTitle());

                // Phase 2 & 3: Use smart quality fallback with enhanced preview detection
                _logger.Debug("Attempting download with quality fallback for track {0}, preferred quality {1}", track.Id, preferredQuality);

                var (selectedQuality, streamInfo) = await _apiService.GetBestAvailableStreamAsync(track.Id, preferredQuality);

                // Enforce strict-quality policy BEFORE we commit any bytes to disk —
                // if the user has Quality Fallback disabled and the API returned a
                // different quality than requested, fail fast with a clear error.
                QobuzQualityPolicyEnforcer.Enforce(selectedQuality, preferredQuality, allowQualityFallback);

                if (selectedQuality != preferredQuality)
                {
                    var requestedName = GetQualityName(preferredQuality);
                    var selectedName = GetQualityName(selectedQuality);
                    _logger.Info("Quality fallback applied for {0}: {1} → {2}",
                        track.GetFullTitle(), requestedName, selectedName);
                }

                if (string.IsNullOrWhiteSpace(streamInfo?.Url))
                {
                    throw new InvalidOperationException("Could not obtain stream URL despite quality fallback");
                }

                // Generate filename (normalize to NFC, guard reserved names)
                var fileName = GenerateFileName(track, album, streamInfo.FormatId);
                var filePath = Path.Combine(outputPath, fileName);

                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                // Download file (streaming) to .partial then atomic move
                _logger.Info("Downloading track to: {0}", filePath);
                var partialPath = filePath + ".partial";
                if (System.IO.File.Exists(partialPath))
                {
                    try { System.IO.File.Delete(partialPath); } catch { /* best effort */ }
                }

                var http = SharedSystemHttpClient.Instance;
                var request = new HttpRequestMessage(HttpMethod.Get, streamInfo.Url);

                long existing = 0;
                if (System.IO.File.Exists(partialPath))
                {
                    try { existing = new FileInfo(partialPath).Length; } catch { existing = 0; }
                    if (existing > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(existing, null);
                    }
                }

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var isPartial = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                if (!isPartial)
                {
                    // Start fresh if server didn't honor range
                    if (System.IO.File.Exists(partialPath))
                    {
                        try { System.IO.File.Delete(partialPath); } catch { /* best effort */ }
                        existing = 0;
                    }
                }

                var totalBytesHeader = response.Content.Headers.ContentLength;
                long expectedTotal = 0;
                if (isPartial)
                {
                    // Content-Range: bytes start-end/total
                    var contentRange = response.Content.Headers.ContentRange;
                    if (contentRange != null && contentRange.HasLength)
                    {
                        expectedTotal = contentRange.Length!.Value;
                    }
                }

                await using (var network = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var fs = new FileStream(partialPath, FileMode.Append, FileAccess.Write, FileShare.None, bufferSize: 1024 * 128, useAsync: true))
                {
                    var buffer = new byte[1024 * 128];
                    long written = existing;
                    int read;
                    while ((read = await network.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        written += read;
                        if ((totalBytesHeader.HasValue || expectedTotal > 0) && progress != null)
                        {
                            var denom = expectedTotal > 0 ? expectedTotal : (existing + totalBytesHeader.GetValueOrDefault());
                            var pct = denom > 0 ? (double)written / denom * 100d : 0d;
                            progress.Report(Math.Min(100, pct));
                        }
                    }
                    fs.Flush(true);
                }

                // Atomic move into place
                System.IO.File.Move(partialPath, filePath, overwrite: true);

                // Validate downloaded file (size/hash unknown; perform basic validation)
                if (!Lidarr.Plugin.Common.Utilities.ValidationUtilities.ValidateDownloadedFile(filePath))
                {
                    throw new InvalidOperationException($"Downloaded file failed validation: {Path.GetFileName(filePath)}");
                }

                // Apply metadata
                await ApplyMetadataAsync(filePath, track, album);

                _logger.Info("Successfully downloaded track: {0}", track.GetFullTitle());
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to download track: {0}", track.GetFullTitle());
                throw;
            }
        }

        private string GenerateFileName(QobuzTrack track, QobuzAlbum album, int formatId)
        {
            return TrackFileNameBuilder.Build(
                trackNumber: track.TrackNumber,
                trackTitle: track.Title,
                formatId: formatId,
                discNumber: track.DiscNumber,
                totalDiscs: album.MediaCount);
        }


        private async Task ApplyMetadataAsync(string filePath, QobuzTrack track, QobuzAlbum album)
        {
            await Task.Run(() =>
            {
                using var file = TagLib.File.Create(filePath);

                // Basic metadata
                file.Tag.Title = track.Title;
                file.Tag.Album = album.Title;
                file.Tag.AlbumArtists = new[] { album.GetArtistName() };
                file.Tag.Performers = new[] { track.GetPerformerName() };
                file.Tag.Track = (uint)track.TrackNumber;
                file.Tag.TrackCount = (uint)album.TracksCount;
                file.Tag.Disc = (uint)track.DiscNumber;
                file.Tag.Year = album.ReleaseDate.Year > 1900 ? (uint)album.ReleaseDate.Year : 0;

                // Additional metadata
                if (!string.IsNullOrEmpty(album.Genre?.Name))
                {
                    file.Tag.Genres = new[] { album.Genre.Name };
                }

                var composer = track.GetComposerName();
                if (!string.IsNullOrEmpty(composer) && composer != "Unknown")
                {
                    file.Tag.Composers = new[] { composer };
                }

                // Quality info in comment
                file.Tag.Comment = $"Downloaded from Qobuz - Album: {album.Id}, Track: {track.Id}";

                file.Save();
                _logger.Debug("Applied metadata to: {0}", filePath);
            });
        }

        /// <summary>
        /// Get human-readable quality name for error messages and logging
        /// </summary>
        private string GetQualityName(int qualityId)
        {
            return qualityId switch
            {
                5 => "MP3 320kbps",
                6 => "FLAC CD 16bit/44.1kHz",
                7 => "FLAC Hi-Res 24bit/96kHz",
                27 => "FLAC Hi-Res 24bit/192kHz",
                _ => $"Unknown Quality ({qualityId})"
            };
        }
    }
}
