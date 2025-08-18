using System.Collections.Generic;
using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    public class QobuzSearchResponse
    {
        [JsonProperty("albums")]
        public QobuzSearchResultContainer<QobuzAlbum> Albums { get; set; }

        [JsonProperty("artists")]
        public QobuzSearchResultContainer<QobuzArtist> Artists { get; set; }

        [JsonProperty("tracks")]
        public QobuzSearchResultContainer<QobuzTrack> Tracks { get; set; }

        [JsonProperty("query")]
        public string Query { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        public bool IsSuccess => Status?.ToLower() == "success" || string.IsNullOrEmpty(Status);
    }

    public class QobuzSearchResultContainer<T>
    {
        [JsonProperty("items")]
        public List<T> Items { get; set; } = new List<T>();

        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("offset")]
        public int Offset { get; set; }

        [JsonProperty("limit")]
        public int Limit { get; set; }

        /// <summary>
        /// Check if there are more results available
        /// </summary>
        public bool HasMoreResults => (Offset + Items.Count) < Total;

        /// <summary>
        /// Get the next offset for pagination
        /// </summary>
        public int GetNextOffset() => Offset + Items.Count;
    }

    public class QobuzAlbumSearchResponse
    {
        [JsonProperty("albums")]
        public QobuzSearchResultContainer<QobuzAlbum> Albums { get; set; }

        [JsonProperty("query")]
        public string Query { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        public bool IsSuccess => Status?.ToLower() == "success" || string.IsNullOrEmpty(Status);

        /// <summary>
        /// Get all album results
        /// </summary>
        public List<QobuzAlbum> GetAlbums()
        {
            return Albums?.Items ?? new List<QobuzAlbum>();
        }

        /// <summary>
        /// Check if search has any results
        /// </summary>
        public bool HasResults()
        {
            return Albums?.Items?.Count > 0;
        }
    }

    public class QobuzArtistSearchResponse
    {
        [JsonProperty("artists")]
        public QobuzSearchResultContainer<QobuzArtist> Artists { get; set; }

        [JsonProperty("query")]
        public string Query { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        public bool IsSuccess => Status?.ToLower() == "success" || string.IsNullOrEmpty(Status);

        /// <summary>
        /// Get all artist results
        /// </summary>
        public List<QobuzArtist> GetArtists()
        {
            return Artists?.Items ?? new List<QobuzArtist>();
        }
    }

    /// <summary>
    /// Search response for playlists
    /// </summary>
    public class QobuzPlaylistSearchResponse
    {
        [JsonProperty("playlists")]
        public QobuzSearchResultContainer<QobuzPlaylist> Playlists { get; set; }

        [JsonProperty("query")]
        public string Query { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        public bool IsSuccess => Status?.ToLower() == "success" || string.IsNullOrEmpty(Status);
    }

    /// <summary>
    /// Search response for labels
    /// </summary>
    public class QobuzLabelSearchResponse
    {
        [JsonProperty("labels")]
        public QobuzSearchResultContainer<QobuzLabel> Labels { get; set; }

        [JsonProperty("query")]
        public string Query { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        public bool IsSuccess => Status?.ToLower() == "success" || string.IsNullOrEmpty(Status);
    }
}