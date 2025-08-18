using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    public class QobuzArtist
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("picture")]
        public string Picture { get; set; }

        [JsonProperty("image")]
        public QobuzImage Image { get; set; }

        [JsonProperty("albums_count")]
        public int? AlbumsCount { get; set; }

        [JsonProperty("biography")]
        public QobuzBiography Biography { get; set; }

        [JsonProperty("similar_artist_ids")]
        public List<string> SimilarArtistIds { get; set; }

        /// <summary>
        /// Get the best available artist image URL
        /// </summary>
        public string GetBestImageUrl()
        {
            if (Image?.Large.IsNotNullOrWhiteSpace() == true)
                return Image.Large;

            if (Image?.Medium.IsNotNullOrWhiteSpace() == true)
                return Image.Medium;

            if (Image?.Small.IsNotNullOrWhiteSpace() == true)
                return Image.Small;

            return Picture;
        }

        /// <summary>
        /// Get a clean artist name for file system usage
        /// </summary>
        public string GetSafeArtistName()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return "Unknown Artist";

            // Replace illegal filesystem characters
            var safeName = Name;
            var illegalChars = new char[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
            
            foreach (var c in illegalChars)
            {
                safeName = safeName.Replace(c, '_');
            }

            // Trim and limit length
            return safeName.Trim().Substring(0, Math.Min(safeName.Length, 100));
        }
    }

    public class QobuzBiography
    {
        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }
    }
}