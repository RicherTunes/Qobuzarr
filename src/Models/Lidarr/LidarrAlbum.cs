using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;

namespace Lidarr.Plugin.Qobuzarr.Models.Lidarr
{
    /// <summary>
    /// Represents an album in Lidarr with comprehensive metadata and status information.
    /// Used for retrieving wanted albums and tracking album availability across different sources.
    /// </summary>
    public class LidarrAlbum
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("disambiguation")]
        public string Disambiguation { get; set; }

        [JsonProperty("overview")]
        public string Overview { get; set; }

        [JsonProperty("artistId")]
        public int ArtistId { get; set; }

        [JsonProperty("foreignAlbumId")]
        public string ForeignAlbumId { get; set; }

        [JsonProperty("foreignReleaseId")]
        public string ForeignReleaseId { get; set; }

        [JsonProperty("artistForeignId")]
        public string ArtistForeignId { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("country")]
        public string Country { get; set; }

        [JsonProperty("monitored")]
        public bool Monitored { get; set; }

        [JsonProperty("anyReleaseOk")]
        public bool AnyReleaseOk { get; set; }

        [JsonProperty("profileId")]
        public int ProfileId { get; set; }

        [JsonProperty("qualityProfileId")]
        public int QualityProfileId { get; set; }

        [JsonProperty("duration")]
        public int DurationMs { get; set; }

        [JsonProperty("albumType")]
        public string AlbumType { get; set; }

        [JsonProperty("secondaryTypes")]
        public List<LidarrSecondaryType> SecondaryTypes { get; set; } = new List<LidarrSecondaryType>();

        [JsonProperty("mediumCount")]
        public int MediumCount { get; set; }

        [JsonProperty("ratings")]
        public LidarrRating Ratings { get; set; }

        [JsonProperty("releaseDate")]
        public DateTime? ReleaseDate { get; set; }

        [JsonProperty("releases")]
        public List<LidarrRelease> Releases { get; set; } = new List<LidarrRelease>();

        [JsonProperty("genres")]
        public List<string> Genres { get; set; } = new List<string>();

        [JsonProperty("media")]
        public List<LidarrMedium> Media { get; set; } = new List<LidarrMedium>();

        [JsonProperty("artist")]
        public LidarrArtist Artist { get; set; }

        [JsonProperty("images")]
        public List<LidarrImage> Images { get; set; } = new List<LidarrImage>();

        [JsonProperty("links")]
        public List<LidarrLink> Links { get; set; } = new List<LidarrLink>();

        [JsonProperty("statistics")]
        public LidarrAlbumStatistics Statistics { get; set; }

        [JsonProperty("remoteCover")]
        public string RemoteCover { get; set; }

        [JsonProperty("grabbed")]
        public bool Grabbed { get; set; }

        /// <summary>
        /// Get album duration as TimeSpan
        /// </summary>
        public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);

        /// <summary>
        /// Get full album title including disambiguation if available
        /// </summary>
        public string GetFullTitle()
        {
            if (Disambiguation.IsNotNullOrWhiteSpace() && !Title.Contains(Disambiguation))
            {
                return $"{Title} ({Disambiguation})";
            }
            return Title;
        }

        /// <summary>
        /// Get primary artist name
        /// </summary>
        public string GetArtistName()
        {
            return Artist?.ArtistName ?? "Various Artists";
        }

        /// <summary>
        /// Get primary artist name (property for compatibility)
        /// </summary>
        public string ArtistName => GetArtistName();

        /// <summary>
        /// Get track count from the primary release or statistics
        /// </summary>
        public int TrackCount
        {
            get
            {
                // Try statistics first (most reliable)
                if (Statistics?.TotalTrackCount > 0)
                    return Statistics.TotalTrackCount;

                // Try first release
                var firstRelease = Releases?.FirstOrDefault();
                if (firstRelease?.TrackCount > 0)
                    return firstRelease.TrackCount;

                // Try calculating from media - for now just return 0 since LidarrMedium doesn't have TrackCount
                // var mediaTrackCount = Media?.Sum(m => m.TrackCount ?? 0) ?? 0;
                // if (mediaTrackCount > 0)
                //     return mediaTrackCount;

                return 0;
            }
        }

        /// <summary>
        /// Get release year from ReleaseDate
        /// </summary>
        public int? ReleaseYear => ReleaseDate?.Year;

        /// <summary>
        /// Get tracks from the primary release (placeholder - would need actual implementation)
        /// </summary>
        public List<LidarrTrack> Tracks
        {
            get
            {
                // This is a simplified implementation - in practice would need to load track data
                // For now, return empty list to prevent compilation errors
                return new List<LidarrTrack>();
            }
        }

        /// <summary>
        /// Get the best available cover image URL
        /// </summary>
        public string GetBestCoverUrl()
        {
            var cover = Images?.Find(i => i.CoverType == "cover");
            return cover?.RemoteUrl ?? RemoteCover;
        }

        /// <summary>
        /// Get primary genre
        /// </summary>
        public string GetPrimaryGenre()
        {
            return Genres?.FirstOrDefault() ?? "Unknown";
        }

        /// <summary>
        /// Check if album is available (has files)
        /// </summary>
        public bool IsAvailable()
        {
            return Statistics?.SizeOnDisk > 0;
        }

        /// <summary>
        /// Get safe folder name for file system
        /// </summary>
        public string GetSafeFolderName()
        {
            var artist = GetArtistName();
            var title = GetFullTitle();
            var year = ReleaseDate?.Year.ToString() ?? "";

            var folderName = year.IsNotNullOrWhiteSpace() ? $"{artist} - {title} ({year})" : $"{artist} - {title}";

            // Replace illegal filesystem characters
            var illegalChars = new char[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
            foreach (var c in illegalChars)
            {
                folderName = folderName.Replace(c, '_');
            }

            // Trim and limit length
            folderName = folderName.Trim();
            if (folderName.Length > 200)
            {
                folderName = folderName.Substring(0, 200).Trim();
            }

            return folderName;
        }

        /// <summary>
        /// Gets the preferred Qobuz quality for this album based on its quality profile.
        /// </summary>
        /// <param name="qualityProfile">The quality profile associated with this album.</param>
        /// <returns>The recommended Qobuz quality string (e.g., "flac-hires", "flac-cd", "mp3-320").</returns>
        public string GetPreferredQobuzQuality(LidarrQualityProfile qualityProfile = null)
        {
            if (qualityProfile == null)
            {
                // Default fallback quality when no profile is available
                return "flac-cd";
            }

            var preferredQuality = qualityProfile.GetPreferredQuality();
            return preferredQuality?.ToQobuzQuality() ?? "flac-cd";
        }

        /// <summary>
        /// Gets all supported Qobuz qualities for this album based on its quality profile, in order of preference.
        /// </summary>
        /// <param name="qualityProfile">The quality profile associated with this album.</param>
        /// <returns>List of Qobuz quality strings ordered by preference.</returns>
        public List<string> GetSupportedQobuzQualities(LidarrQualityProfile qualityProfile = null)
        {
            if (qualityProfile == null)
            {
                // Default fallback qualities when no profile is available
                return new List<string> { "flac-cd", "mp3-320" };
            }

            return qualityProfile.GetAllowedQualities()
                                 .Select(q => q.ToQobuzQuality())
                                 .Distinct()
                                 .ToList();
        }

        /// <summary>
        /// Determines if this album should prefer lossless quality based on its quality profile.
        /// </summary>
        /// <param name="qualityProfile">The quality profile associated with this album.</param>
        /// <returns>True if lossless formats are preferred; false otherwise.</returns>
        public bool ShouldPreferLossless(LidarrQualityProfile qualityProfile = null)
        {
            return qualityProfile?.PrefersLossless() ?? true; // Default to preferring lossless
        }
    }

    /// <summary>
    /// Represents secondary album types (e.g., Compilation, Soundtrack)
    /// </summary>
    public class LidarrSecondaryType
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }

    /// <summary>
    /// Represents album ratings and scores
    /// </summary>
    public class LidarrRating
    {
        [JsonProperty("votes")]
        public int Votes { get; set; }

        [JsonProperty("value")]
        public decimal Value { get; set; }
    }

    /// <summary>
    /// Represents a specific release of an album
    /// </summary>
    public class LidarrRelease
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("albumId")]
        public int AlbumId { get; set; }

        [JsonProperty("foreignReleaseId")]
        public string ForeignReleaseId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("disambiguation")]
        public string Disambiguation { get; set; }

        [JsonProperty("country")]
        public List<string> Country { get; set; } = new List<string>();

        [JsonProperty("releaseDate")]
        public DateTime? ReleaseDate { get; set; }

        [JsonProperty("media")]
        public List<LidarrMedium> Media { get; set; } = new List<LidarrMedium>();

        [JsonProperty("trackCount")]
        public int TrackCount { get; set; }

        [JsonProperty("monitored")]
        public bool Monitored { get; set; }
    }

    /// <summary>
    /// Represents a physical medium (CD, Vinyl, etc.) within a release
    /// </summary>
    public class LidarrMedium
    {
        [JsonProperty("mediumNumber")]
        public int MediumNumber { get; set; }

        [JsonProperty("mediumName")]
        public string MediumName { get; set; }

        [JsonProperty("mediumFormat")]
        public string MediumFormat { get; set; }
    }

    /// <summary>
    /// Represents album statistics (file counts, sizes, etc.)
    /// </summary>
    public class LidarrAlbumStatistics
    {
        [JsonProperty("trackFileCount")]
        public int TrackFileCount { get; set; }

        [JsonProperty("trackCount")]
        public int TrackCount { get; set; }

        [JsonProperty("totalTrackCount")]
        public int TotalTrackCount { get; set; }

        [JsonProperty("sizeOnDisk")]
        public long SizeOnDisk { get; set; }

        [JsonProperty("percentOfTracks")]
        public decimal PercentOfTracks { get; set; }
    }

    /// <summary>
    /// Represents an image associated with an album
    /// </summary>
    public class LidarrImage
    {
        [JsonProperty("coverType")]
        public string CoverType { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("remoteUrl")]
        public string RemoteUrl { get; set; }
    }

    /// <summary>
    /// Represents external links associated with an album
    /// </summary>
    public class LidarrLink
    {
        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }
}
