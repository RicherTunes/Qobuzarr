using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;

namespace Lidarr.Plugin.Qobuzarr.Models.Lidarr
{
    /// <summary>
    /// Represents a track in Lidarr with comprehensive metadata and file information.
    /// Contains track details, quality information, and file status.
    /// </summary>
    public class LidarrTrack
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("albumId")]
        public int AlbumId { get; set; }

        [JsonProperty("foreignTrackId")]
        public string ForeignTrackId { get; set; }

        [JsonProperty("foreignRecordingId")]
        public string ForeignRecordingId { get; set; }

        [JsonProperty("trackNumber")]
        public int TrackNumber { get; set; }

        [JsonProperty("absoluteTrackNumber")]
        public int AbsoluteTrackNumber { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("duration")]
        public int DurationMs { get; set; }

        [JsonProperty("explicit")]
        public bool Explicit { get; set; }

        [JsonProperty("ratings")]
        public LidarrRating Ratings { get; set; }

        [JsonProperty("mediumNumber")]
        public int MediumNumber { get; set; }

        /// <summary>
        /// Get disc number (same as MediumNumber for compatibility)
        /// </summary>
        public int DiscNumber => MediumNumber;

        [JsonProperty("trackFileId")]
        public int? TrackFileId { get; set; }

        [JsonProperty("hasFile")]
        public bool HasFile { get; set; }

        [JsonProperty("monitored")]
        public bool Monitored { get; set; }

        [JsonProperty("grabbed")]
        public bool Grabbed { get; set; }

        [JsonProperty("trackFile")]
        public LidarrTrackFile TrackFile { get; set; }

        [JsonProperty("artist")]
        public LidarrArtist Artist { get; set; }

        [JsonProperty("albumRelease")]
        public LidarrRelease AlbumRelease { get; set; }

        /// <summary>
        /// Get track duration as TimeSpan
        /// </summary>
        public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);

        /// <summary>
        /// Get formatted track number (e.g., "01", "02")
        /// </summary>
        public string GetFormattedTrackNumber()
        {
            return TrackNumber.ToString("D2");
        }

        /// <summary>
        /// Get full track title including track number
        /// </summary>
        public string GetFullTitle()
        {
            return $"{GetFormattedTrackNumber()} - {Title}";
        }

        /// <summary>
        /// Get safe filename for file system
        /// </summary>
        public string GetSafeFileName(string extension = "flac")
        {
            var fileName = GetFullTitle();
            
            // Replace illegal filesystem characters
            var illegalChars = new char[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
            foreach (var c in illegalChars)
            {
                fileName = fileName.Replace(c, '_');
            }

            // Trim and limit length
            fileName = fileName.Trim();
            if (fileName.Length > 200)
            {
                fileName = fileName.Substring(0, 200).Trim();
            }

            return $"{fileName}.{extension}";
        }

        /// <summary>
        /// Check if track is available on disk
        /// </summary>
        public bool IsAvailable()
        {
            return HasFile && TrackFile != null;
        }

        /// <summary>
        /// Get quality profile of the track file
        /// </summary>
        public string GetQualityName()
        {
            return TrackFile?.Quality?.Name ?? "Unknown";
        }

        /// <summary>
        /// Get file size in MB
        /// </summary>
        public decimal GetFileSizeMB()
        {
            if (TrackFile?.Size > 0)
            {
                return (decimal)TrackFile.Size / (1024 * 1024);
            }
            return 0;
        }

        /// <summary>
        /// Get artist name
        /// </summary>
        public string GetArtistName()
        {
            return Artist?.ArtistName ?? "Unknown Artist";
        }

        /// <summary>
        /// Get artist name (alias for compatibility)
        /// </summary>
        public string ArtistName => GetArtistName();
    }

    /// <summary>
    /// Represents a track file on disk with quality and path information
    /// </summary>
    public class LidarrTrackFile
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("albumId")]
        public int AlbumId { get; set; }

        [JsonProperty("relativePath")]
        public string RelativePath { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("dateAdded")]
        public DateTime DateAdded { get; set; }

        [JsonProperty("sceneName")]
        public string SceneName { get; set; }

        [JsonProperty("releaseGroup")]
        public string ReleaseGroup { get; set; }

        [JsonProperty("quality")]
        public LidarrQuality Quality { get; set; }

        [JsonProperty("qualityCutoffNotMet")]
        public bool QualityCutoffNotMet { get; set; }

        [JsonProperty("mediaInfo")]
        public LidarrMediaInfo MediaInfo { get; set; }

        /// <summary>
        /// Get file extension
        /// </summary>
        public string GetFileExtension()
        {
            return System.IO.Path.GetExtension(Path)?.TrimStart('.');
        }

        /// <summary>
        /// Get filename without path
        /// </summary>
        public string GetFileName()
        {
            return System.IO.Path.GetFileName(Path);
        }
    }

    // LidarrQuality class moved to LidarrQualityProfile.cs to avoid duplication

    /// <summary>
    /// Represents a quality model (FLAC, MP3, etc.)
    /// </summary>
    public class LidarrQualityModel
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }

    /// <summary>
    /// Represents quality revision information
    /// </summary>
    public class LidarrRevision
    {
        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("real")]
        public int Real { get; set; }

        [JsonProperty("isRepack")]
        public bool IsRepack { get; set; }
    }

    /// <summary>
    /// Represents media information for a track file
    /// </summary>
    public class LidarrMediaInfo
    {
        [JsonProperty("audioFormat")]
        public string AudioFormat { get; set; }

        [JsonProperty("audioBitrate")]
        public int AudioBitrate { get; set; }

        [JsonProperty("audioChannels")]
        public decimal AudioChannels { get; set; }

        [JsonProperty("bitsPerSample")]
        public int BitsPerSample { get; set; }

        [JsonProperty("sampleRate")]
        public int SampleRate { get; set; }
    }
}