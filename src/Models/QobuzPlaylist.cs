using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Represents a Qobuz playlist
    /// </summary>
    public class QobuzPlaylist
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("tracks_count")]
        public int TracksCount { get; set; }

        [JsonProperty("duration")]
        public int Duration { get; set; }

        [JsonProperty("is_public")]
        public bool IsPublic { get; set; }

        [JsonProperty("is_collaborative")]
        public bool IsCollaborative { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonProperty("owner")]
        public QobuzUser Owner { get; set; }

        [JsonProperty("tracks")]
        public QobuzPlaylistTracksContainer Tracks { get; set; }

        [JsonProperty("images")]
        public List<QobuzImage> Images { get; set; }

        [JsonProperty("image")]
        public QobuzImage Image { get; set; }

        /// <summary>
        /// Get the best available image URL
        /// </summary>
        public string GetImageUrl(int size = 600)
        {
            // Return the best quality image available
            if (Image != null)
            {
                if (!string.IsNullOrEmpty(Image.Mega)) return Image.Mega;
                if (!string.IsNullOrEmpty(Image.ExtraLarge)) return Image.ExtraLarge;
                if (!string.IsNullOrEmpty(Image.Large)) return Image.Large;
                if (!string.IsNullOrEmpty(Image.Medium)) return Image.Medium;
                if (!string.IsNullOrEmpty(Image.Small)) return Image.Small;
            }

            // Check images collection if available
            if (Images != null && Images.Count > 0)
            {
                var firstImage = Images[0];
                if (!string.IsNullOrEmpty(firstImage.Large)) return firstImage.Large;
                if (!string.IsNullOrEmpty(firstImage.Medium)) return firstImage.Medium;
                if (!string.IsNullOrEmpty(firstImage.Small)) return firstImage.Small;
            }

            return null;
        }
    }

    /// <summary>
    /// Container for playlist tracks with pagination support
    /// </summary>
    public class QobuzPlaylistTracksContainer
    {
        [JsonProperty("items")]
        public List<QobuzPlaylistTrack> Items { get; set; } = new List<QobuzPlaylistTrack>();

        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("offset")]
        public int Offset { get; set; }

        [JsonProperty("limit")]
        public int Limit { get; set; }
    }

    /// <summary>
    /// Represents a track within a playlist (includes position info)
    /// </summary>
    public class QobuzPlaylistTrack
    {
        [JsonProperty("id")]
        public long PlaylistTrackId { get; set; }

        [JsonProperty("position")]
        public int Position { get; set; }

        [JsonProperty("track")]
        public QobuzTrack Track { get; set; }

        [JsonProperty("added_at")]
        public DateTime AddedAt { get; set; }
    }

}