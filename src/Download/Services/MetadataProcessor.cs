using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Utilities;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using TagLib;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Handles metadata processing and application to audio files
    /// </summary>
    public class MetadataProcessor : IMetadataProcessor
    {
        private readonly IQobuzLogger _logger;
        private readonly IFilePathGenerator _filePathGenerator;
        private readonly IHttpClient _httpClient;
        private readonly HashSet<string> _failedCoverArtUrls = new HashSet<string>();
        private readonly object _failedUrlsLock = new object();

        public MetadataProcessor(IQobuzLogger logger, IFilePathGenerator filePathGenerator, IHttpClient httpClient)
        {
            _logger = Guard.NotNull(logger, nameof(logger));
            _filePathGenerator = Guard.NotNull(filePathGenerator, nameof(filePathGenerator));
            _httpClient = Guard.NotNull(httpClient, nameof(httpClient));
        }

        public void ApplyBasicMetadata(string filePath, QobuzTrack track, QobuzAlbum album)
        {
            Guard.NotNullOrWhiteSpace(filePath, nameof(filePath));
            Guard.NotNull(track, nameof(track));
            Guard.NotNull(album, nameof(album));

            try
            {
                using var file = TagLib.File.Create(filePath);
                
                // Apply comprehensive metadata tags
                file.Tag.Title = track.GetFullTitle();
                file.Tag.AlbumArtists = new[] { album.GetArtistName() };
                file.Tag.Performers = new[] { track.GetPerformerName() };
                file.Tag.Album = album.GetFullTitle();
                file.Tag.Track = (uint)track.TrackNumber;
                file.Tag.Disc = (uint)track.DiscNumber;
                
                // Release year
                if (album.ReleaseDate.Year > 1900)
                {
                    file.Tag.Year = (uint)album.ReleaseDate.Year;
                }

                // Add genre if available
                var genre = album.GetGenre();
                if (genre.IsNotNullOrWhiteSpace() && genre != "Unknown")
                {
                    file.Tag.Genres = new[] { genre };
                }

                // Add composer if available (for classical music)
                var composer = track.GetComposerName();
                if (composer.IsNotNullOrWhiteSpace() && composer != "Unknown")
                {
                    file.Tag.Composers = new[] { composer };
                }

                // Add additional metadata
                var labelName = album.Label?.Name;
                if (labelName.IsNotNullOrWhiteSpace())
                {
                    // Use Publisher tag for record label
                    file.Tag.Publisher = labelName;
                }

                // Add comment with comprehensive Qobuz source info
                var qualityInfo = _filePathGenerator.GetQualityDescription(track);
                file.Tag.Comment = $"Downloaded from Qobuz - Album: {album.Id}, Track: {track.Id}, Quality: {qualityInfo}";

                // Save the metadata
                file.Save();
                
                _logger.Debug("Applied comprehensive metadata to: {0}", filePath);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to apply metadata to file: {0} - file downloaded successfully but without tags", filePath);
                // Don't throw - file download was successful, metadata is just a bonus
            }
        }

        public void ApplyOptimizedMetadata(string filePath, TrackDownload trackDownload)
        {
            Guard.NotNullOrWhiteSpace(filePath, nameof(filePath));
            Guard.NotNull(trackDownload, nameof(trackDownload));

            try
            {
                using var file = TagLib.File.Create(filePath);

                // Apply metadata from optimized TrackDownload
                if (!string.IsNullOrWhiteSpace(trackDownload.Title))
                    file.Tag.Title = trackDownload.Title;

                if (!string.IsNullOrWhiteSpace(trackDownload.Artist))
                    file.Tag.Performers = new[] { trackDownload.Artist };

                if (!string.IsNullOrWhiteSpace(trackDownload.AlbumArtist))
                    file.Tag.AlbumArtists = new[] { trackDownload.AlbumArtist };

                if (!string.IsNullOrWhiteSpace(trackDownload.Album))
                    file.Tag.Album = trackDownload.Album;

                if (trackDownload.TrackNumber.HasValue)
                    file.Tag.Track = (uint)trackDownload.TrackNumber.Value;

                if (trackDownload.DiscNumber.HasValue && trackDownload.DiscNumber > 0)
                    file.Tag.Disc = (uint)trackDownload.DiscNumber.Value;

                if (trackDownload.ReleaseDate.HasValue && trackDownload.ReleaseDate.Value.Year > 1900)
                    file.Tag.Year = (uint)trackDownload.ReleaseDate.Value.Year;

                if (!string.IsNullOrWhiteSpace(trackDownload.Genre))
                    file.Tag.Genres = new[] { trackDownload.Genre };

                if (!string.IsNullOrWhiteSpace(trackDownload.Composer))
                    file.Tag.Composers = new[] { trackDownload.Composer };

                if (!string.IsNullOrWhiteSpace(trackDownload.Label))
                    file.Tag.Publisher = trackDownload.Label;

                // Add MusicBrainz IDs if available
                if (!string.IsNullOrWhiteSpace(trackDownload.MusicBrainzTrackId))
                    file.Tag.MusicBrainzTrackId = trackDownload.MusicBrainzTrackId;

                if (!string.IsNullOrWhiteSpace(trackDownload.MusicBrainzAlbumId))
                    file.Tag.MusicBrainzReleaseId = trackDownload.MusicBrainzAlbumId;

                if (!string.IsNullOrWhiteSpace(trackDownload.MusicBrainzArtistId))
                    file.Tag.MusicBrainzArtistId = trackDownload.MusicBrainzArtistId;

                // Add quality comment
                var qualityComment = $"Downloaded from Qobuz - Quality: {trackDownload.Quality}, Source: {trackDownload.MetadataSource}";
                file.Tag.Comment = qualityComment;

                file.Save();

                _logger.Debug("Applied optimized metadata to: {0} (Source: {1})", filePath, trackDownload.MetadataSource);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to apply optimized metadata to file: {0}", filePath);
            }
        }

        public async Task CreateMetadataFileAsync(string trackFilePath, QobuzTrack track, QobuzAlbum album, int formatId)
        {
            Guard.NotNullOrWhiteSpace(trackFilePath, nameof(trackFilePath));
            Guard.NotNull(track, nameof(track));
            Guard.NotNull(album, nameof(album));

            try
            {
                var metadata = new QobuzDownloadMetadata
                {
                    TrackId = track.Id,
                    AlbumId = album.Id,
                    DownloadDate = DateTime.UtcNow,
                    Quality = QobuzQualityInfo.FromFormatId(formatId),
                    File = new QobuzFileMetadata()
                };

                // Update file metadata from actual file
                metadata.File.UpdateFromFile(trackFilePath);

                // Save metadata file
                await metadata.SaveAsync(trackFilePath).ConfigureAwait(false);

                _logger.Debug("Created metadata file for: {0}", trackFilePath);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to create metadata file for: {0}", trackFilePath);
            }
        }

        public async Task CreateOptimizedMetadataFileAsync(string trackFilePath, TrackDownload trackDownload)
        {
            Guard.NotNullOrWhiteSpace(trackFilePath, nameof(trackFilePath));
            Guard.NotNull(trackDownload, nameof(trackDownload));

            try
            {
                var metadata = new
                {
                    TrackId = trackDownload.QobuzTrackId,
                    DownloadDate = DateTime.UtcNow,
                    Quality = trackDownload.Quality,
                    MetadataSource = trackDownload.MetadataSource,
                    Title = trackDownload.Title,
                    Artist = trackDownload.Artist,
                    Album = trackDownload.Album,
                    TrackNumber = trackDownload.TrackNumber,
                    Duration = trackDownload.Duration,
                    MusicBrainzIds = new
                    {
                        Track = trackDownload.MusicBrainzTrackId,
                        Album = trackDownload.MusicBrainzAlbumId,
                        Artist = trackDownload.MusicBrainzArtistId
                    }
                };

                var metadataPath = Path.ChangeExtension(trackFilePath, ".metadata.json");
                var json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });

                await System.IO.File.WriteAllTextAsync(metadataPath, json).ConfigureAwait(false);

                _logger.Debug("Created optimized metadata file for: {0}", trackFilePath);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to create optimized metadata file for: {0}", trackFilePath);
            }
        }

        public async Task DownloadCoverArtAsync(string albumPath, QobuzAlbum album)
        {
            Guard.NotNullOrWhiteSpace(albumPath, nameof(albumPath));
            Guard.NotNull(album, nameof(album));

            try
            {
                // Get the highest quality cover art URL available
                var coverArtUrl = album.Image?.Large ?? album.Image?.Medium ?? album.Image?.Small;
                if (string.IsNullOrWhiteSpace(coverArtUrl))
                {
                    _logger.Debug("No cover art URL available for album: {0}", album.Id);
                    return;
                }

                // Check if this URL has already failed to avoid repeated attempts
                lock (_failedUrlsLock)
                {
                    if (_failedCoverArtUrls.Contains(coverArtUrl))
                    {
                        _logger.Debug("Skipping cover art download - URL previously failed: {0}", coverArtUrl);
                        return;
                    }
                }

                var coverArtPath = Path.Combine(albumPath, "cover.jpg");
                
                // Skip if cover art already exists
                if (System.IO.File.Exists(coverArtPath))
                {
                    _logger.Debug("Cover art already exists: {0}", coverArtPath);
                    return;
                }

                // Ensure album directory exists
                Directory.CreateDirectory(albumPath);

                // Download cover art
                var request = new HttpRequestBuilder(coverArtUrl)
                    .SetHeader("User-Agent", "Qobuzarr/1.0.0")
                    .Build();

                var response = await _httpClient.ExecuteAsync(request).ConfigureAwait(false);

                if (response.HasHttpError)
                {
                    _logger.Warn("Failed to download cover art from: {0} (HTTP {1})", coverArtUrl, response.StatusCode);
                    
                    // Add to failed URLs cache to prevent repeated attempts
                    lock (_failedUrlsLock)
                    {
                        _failedCoverArtUrls.Add(coverArtUrl);
                    }
                    return;
                }

                await System.IO.File.WriteAllBytesAsync(coverArtPath, response.ResponseData).ConfigureAwait(false);

                _logger.Debug("Downloaded cover art to: {0} ({1} bytes)", coverArtPath, response.ResponseData.Length);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to download cover art for album: {0}", album.Id);
                
                // Add to failed URLs cache if we have a URL to cache
                var coverArtUrl = album.Image?.Large ?? album.Image?.Medium ?? album.Image?.Small;
                if (!string.IsNullOrWhiteSpace(coverArtUrl))
                {
                    lock (_failedUrlsLock)
                    {
                        _failedCoverArtUrls.Add(coverArtUrl);
                    }
                }
            }
        }

        public string GetQualityDescription(QobuzTrack track)
        {
            // Generate quality description based on track's maximum capabilities
            var bitDepth = track.MaximumBitDepth;
            var sampleRate = track.MaximumSampleRate;
            
            if (bitDepth >= 24 && sampleRate >= 96000)
            {
                return $"Hi-Res FLAC {bitDepth}bit/{sampleRate / 1000}kHz";
            }
            else if (bitDepth >= 16 && sampleRate >= 44100)
            {
                return $"FLAC {bitDepth}bit/{sampleRate / 1000}kHz";
            }
            else
            {
                return "MP3 320kbps";
            }
        }
    }
}