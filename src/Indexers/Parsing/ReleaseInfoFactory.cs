using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Parser.Model;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Constants;
using NzbDrone.Core.Indexers;
using Lidarr.Plugin.Qobuzarr.Download;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Parsing
{
    /// <summary>
    /// Implementation of release info creation logic.
    /// Extracted from QobuzParser god class to follow Single Responsibility Principle.
    /// </summary>
    internal class ReleaseInfoFactory : IReleaseInfoFactory
    {
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;
        private readonly ITitleGenerator _titleGenerator;

        public ReleaseInfoFactory(QobuzIndexerSettings settings, Logger logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _titleGenerator = new TitleGenerator(logger);
        }

        public ReleaseInfo CreateReleaseInfoForQuality(QobuzAlbum album, QobuzAudioQuality quality, string originalQuery)
        {
            var year = album.ReleaseDate.Year > 1900 ? album.ReleaseDate.Year : 0;
            
            var artistName = album.GetArtistName();
            var albumTitle = album.GetFullTitle();
            
            // Ensure we have valid non-empty names
            if (string.IsNullOrWhiteSpace(artistName))
            {
                _logger.Warn("Album {0} has empty artist name, skipping", album.Id);
                return null; 
            }
            
            if (string.IsNullOrWhiteSpace(albumTitle))
            {
                _logger.Warn("Album {0} has empty title, skipping", album.Id);
                return null;
            }
            
            var release = new ReleaseInfo
            {
                // CRITICAL: Include quality in GUID to differentiate releases
                Guid = $"qobuz-{album.Id}-{(int)quality}",
                
                // Basic metadata - ENSURE NON-EMPTY NAMES
                Artist = artistName,
                Album = albumTitle,
                DownloadUrl = GenerateDownloadUrl(album, quality),
                InfoUrl = GenerateInfoUrl(album),
                PublishDate = album.ReleaseDate,
                Indexer = QobuzarrConstants.PluginName,
                
                // Note: Codec and Container properties are ignored by Lidarr's quality detection
                // Quality is determined solely from the Title using regex patterns
                
                // CRITICAL: Quality-specific size calculation
                Size = CalculateSizeForQuality(album, quality)
            };

            // Backward- and forward-compatible protocol assignment
            TrySetDownloadProtocol(release);

            // Generate quality-specific title
            release.Title = _titleGenerator.GenerateQualitySpecificTitle(album, quality, year);
            
            // Critical debugging for album mapping (only during troubleshooting)
            _logger.Debug("🔍 ALBUM MAPPING: Qobuz '{0}' ({1}) → Title '{2}' → Album '{3}'", 
                album.Id, album.Title, release.Title, release.Album);

            return release;
        }

        public string GenerateDownloadUrl(QobuzAlbum album, QobuzAudioQuality quality)
        {
            // Include quality in download URL so download client knows which quality to fetch
            return $"qobuz://album/{album.Id}/{(int)quality}";
        }

        public long CalculateSizeForQuality(QobuzAlbum album, QobuzAudioQuality quality)
        {
            // Use quality-specific bitrate for accurate size estimation
            var bitrate = quality.GetEstimatedBitrate();
            var durationSeconds = CalculateReliableDuration(album);
            
            // Convert bits per second to bytes per second, then multiply by duration
            var estimatedSize = (long)(durationSeconds * (bitrate / 8.0));
            
            // Ensure we don't return 0 size (causes issues in Lidarr)
            return Math.Max(estimatedSize, 1024 * 1024); // Minimum 1MB
        }

        public double CalculateReliableDuration(QobuzAlbum album)
        {
            // Tier 1: Use album duration if available
            if (album.Duration.TotalSeconds > 0)
                return album.Duration.TotalSeconds;

            // Tier 2: Sum track durations if available
            var tracks = album.GetTracks();
            if (tracks.Any())
            {
                var trackSum = tracks.Sum(t => t.Duration.TotalSeconds);
                if (trackSum > 0) return trackSum;
            }

            // Tier 3: Smart estimation based on singles vs albums
            var trackCount = Math.Max(album.TracksCount > 0 ? album.TracksCount : tracks.Count, 1);
            var isSingle = IsLikelySingle(album);
            var avgDuration = isSingle ? 3.25 * 60 : 3.5 * 60; // Singles: 3.25min, Albums: 3.5min
            
            return Math.Max(trackCount * avgDuration, 30); // 30 second minimum
        }

        private void TrySetDownloadProtocol(ReleaseInfo release)
        {
            try
            {
                var prop = typeof(ReleaseInfo).GetProperty("DownloadProtocol");
                if (prop == null || !prop.CanWrite) return;

                if (prop.PropertyType == typeof(string))
                {
                    prop.SetValue(release, nameof(QobuzarrDownloadProtocol));
                }
                else if (prop.PropertyType.IsEnum)
                {
                    // Set to Unknown for legacy enum-based protocol
                    var unknown = Enum.Parse(prop.PropertyType, "Unknown");
                    prop.SetValue(release, unknown);
                }
            }
            catch
            {
                // no-op: avoid failing on protocol assignment differences
            }
        }

        public string GenerateInfoUrl(QobuzAlbum album)
        {
            return $"https://www.qobuz.com/album/{album.Slug ?? album.Id}";
        }

        public List<int> GetCategories(QobuzAlbum album)
        {
            var categories = new List<int>();

            // Map genres to category IDs (these would need to match Lidarr's categories)
            var genre = album.GetGenre().ToLower();

            // Basic genre categorization
            if (genre.Contains("jazz"))
                categories.Add(1001);
            else if (genre.Contains("classical"))
                categories.Add(1002);
            else if (genre.Contains("rock"))
                categories.Add(1003);
            else if (genre.Contains("electronic"))
                categories.Add(1004);
            else if (genre.Contains("pop"))
                categories.Add(1005);
            else
                categories.Add(1000); // General music

            // Add Hi-Res category if applicable
            if (album.HasHiResQuality())
                categories.Add(2000); // Hi-Res

            return categories;
        }

        private bool IsLikelySingle(QobuzAlbum album)
        {
            // Consider it a single if it has few tracks and is short duration
            return album.TracksCount <= 3 && album.Duration < TimeSpan.FromMinutes(15);
        }
    }
}
