using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Utilities;
using TagLib;

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
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.Debug("Starting download of track: {0}", track.GetFullTitle());

                // Phase 2 & 3: Use smart quality fallback with enhanced preview detection
                _logger.Debug("Attempting download with quality fallback for track {0}, preferred quality {1}", track.Id, preferredQuality);
                
                var (selectedQuality, streamInfo) = await _apiService.GetBestAvailableStreamAsync(track.Id, preferredQuality);
                
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

                // Generate filename
                var fileName = GenerateFileName(track, album, streamInfo.FormatId);
                var filePath = Path.Combine(outputPath, fileName);

                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                // Download file
                _logger.Info("Downloading track to: {0}", filePath);
                var bytes = await _httpClient.GetBytesAsync(streamInfo.Url, progress);
                await System.IO.File.WriteAllBytesAsync(filePath, bytes, cancellationToken);

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
            var extension = GetFileExtension(formatId);
            var safeTitle = SanitizeFileName(track.Title);
            var trackNumber = track.TrackNumber.ToString("00");
            return $"{trackNumber} - {safeTitle}.{extension}";
        }

        private string GetFileExtension(int formatId)
        {
            return formatId switch
            {
                5 => "mp3",
                6 or 7 or 27 => "flac",
                _ => "flac"
            };
        }

        private string SanitizeFileName(string fileName)
        {
            return FileNameSanitizer.SanitizeFileName(fileName);
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