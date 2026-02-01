using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;

namespace Lidarr.Plugin.Qobuzarr.Models.Lidarr
{
    /// <summary>
    /// Represents an artist in Lidarr with comprehensive metadata and statistics.
    /// Contains artist information, album counts, and monitoring status.
    /// </summary>
    public class LidarrArtist
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("artistMetadataId")]
        public int ArtistMetadataId { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("ended")]
        public bool Ended { get; set; }

        [JsonProperty("artistName")]
        public string ArtistName { get; set; }

        /// <summary>
        /// Get name (alias for ArtistName for compatibility)
        /// </summary>
        public string Name => ArtistName;

        [JsonProperty("foreignArtistId")]
        public string ForeignArtistId { get; set; }

        [JsonProperty("tadbId")]
        public int? TadbId { get; set; }

        [JsonProperty("discogsId")]
        public int? DiscogsId { get; set; }

        [JsonProperty("overview")]
        public string Overview { get; set; }

        [JsonProperty("artistType")]
        public string ArtistType { get; set; }

        [JsonProperty("disambiguation")]
        public string Disambiguation { get; set; }

        [JsonProperty("links")]
        public List<LidarrLink> Links { get; set; } = new List<LidarrLink>();

        [JsonProperty("images")]
        public List<LidarrImage> Images { get; set; } = new List<LidarrImage>();

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("qualityProfileId")]
        public int QualityProfileId { get; set; }

        [JsonProperty("metadataProfileId")]
        public int MetadataProfileId { get; set; }

        [JsonProperty("monitored")]
        public bool Monitored { get; set; }

        [JsonProperty("monitorNewItems")]
        public string MonitorNewItems { get; set; }

        [JsonProperty("genres")]
        public List<string> Genres { get; set; } = new List<string>();

        [JsonProperty("cleanName")]
        public string CleanName { get; set; }

        [JsonProperty("sortName")]
        public string SortName { get; set; }

        [JsonProperty("tags")]
        public List<int> Tags { get; set; } = new List<int>();

        [JsonProperty("added")]
        public DateTime Added { get; set; }

        [JsonProperty("ratings")]
        public LidarrRating Ratings { get; set; }

        [JsonProperty("statistics")]
        public LidarrArtistStatistics Statistics { get; set; }

        [JsonProperty("lastAlbum")]
        public LidarrAlbum LastAlbum { get; set; }

        [JsonProperty("nextAlbum")]
        public LidarrAlbum NextAlbum { get; set; }

        [JsonProperty("remotePoster")]
        public string RemotePoster { get; set; }

        /// <summary>
        /// Get the display name for the artist
        /// </summary>
        public string GetDisplayName()
        {
            if (Disambiguation.IsNotNullOrWhiteSpace() && !ArtistName.Contains(Disambiguation))
            {
                return $"{ArtistName} ({Disambiguation})";
            }
            return ArtistName;
        }

        /// <summary>
        /// Get the best available artist image URL
        /// </summary>
        public string GetBestImageUrl()
        {
            var poster = Images?.Find(i => i.CoverType == "poster");
            if (poster?.RemoteUrl.IsNotNullOrWhiteSpace() == true)
                return poster.RemoteUrl;

            var banner = Images?.Find(i => i.CoverType == "banner");
            if (banner?.RemoteUrl.IsNotNullOrWhiteSpace() == true)
                return banner.RemoteUrl;

            var fanart = Images?.Find(i => i.CoverType == "fanart");
            if (fanart?.RemoteUrl.IsNotNullOrWhiteSpace() == true)
                return fanart.RemoteUrl;

            return RemotePoster;
        }

        /// <summary>
        /// Get primary genre
        /// </summary>
        public string GetPrimaryGenre()
        {
            return Genres?.FirstOrDefault() ?? "Unknown";
        }

        /// <summary>
        /// Get safe folder name for file system
        /// </summary>
        public string GetSafeFolderName()
        {
            var artistName = GetDisplayName();

            // Replace illegal filesystem characters
            var illegalChars = new char[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
            foreach (var c in illegalChars)
            {
                artistName = artistName.Replace(c, '_');
            }

            // Trim and limit length
            artistName = artistName.Trim();
            if (artistName.Length > 200)
            {
                artistName = artistName.Substring(0, 200).Trim();
            }

            return artistName;
        }

        /// <summary>
        /// Check if artist is actively releasing music
        /// </summary>
        public bool IsActive()
        {
            return Status == "continuing" && !Ended;
        }

        /// <summary>
        /// Get percentage of albums that are available
        /// </summary>
        public decimal GetAvailabilityPercentage()
        {
            if (Statistics?.AlbumCount == 0)
                return 0;

            return (decimal)Statistics.AvailableAlbumCount / Statistics.AlbumCount * 100;
        }
    }

    /// <summary>
    /// Represents artist statistics (album counts, sizes, etc.)
    /// </summary>
    public class LidarrArtistStatistics
    {
        [JsonProperty("albumCount")]
        public int AlbumCount { get; set; }

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

        [JsonProperty("availableAlbumCount")]
        public int AvailableAlbumCount { get; set; }
    }
}
