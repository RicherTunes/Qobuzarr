using Lidarr.Plugin.Qobuzarr.API;
using QobuzCLI.Models;

namespace QobuzCLI.Utilities
{
    /// <summary>
    /// Centralizes search type parsing logic to eliminate code duplication.
    /// </summary>
    public static class SearchTypeParser
    {
        /// <summary>
        /// Parses a string representation of search type into the SearchType enum.
        /// </summary>
        /// <param name="type">The search type string (case-insensitive).</param>
        /// <returns>The corresponding SearchType enum value, or SearchType.Auto if invalid/null.</returns>
        public static SearchType Parse(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return SearchType.Auto;

            return type.ToLowerInvariant() switch
            {
                "album" or "albums" => SearchType.Album,
                "artist" or "artists" => SearchType.Artist,
                "track" or "tracks" or "song" or "songs" => SearchType.Track,
                "playlist" or "playlists" => SearchType.Playlist,
                "label" or "labels" => SearchType.Label,
                "auto" or "automatic" => SearchType.Auto,
                _ => SearchType.Auto
            };
        }

        /// <summary>
        /// Attempts to parse a search type string, returning success status.
        /// </summary>
        /// <param name="type">The search type string to parse.</param>
        /// <param name="searchType">The parsed SearchType if successful.</param>
        /// <returns>True if parsing was successful and matched a known type; otherwise, false.</returns>
        public static bool TryParse(string type, out SearchType searchType)
        {
            searchType = Parse(type);
            
            // Only return true if we matched a specific type (not defaulted to Auto)
            if (string.IsNullOrWhiteSpace(type))
                return false;

            var normalized = type.ToLowerInvariant();
            return normalized switch
            {
                "album" or "albums" or "artist" or "artists" or 
                "track" or "tracks" or "song" or "songs" or
                "playlist" or "playlists" or "label" or "labels" => true,
                _ => false
            };
        }

        /// <summary>
        /// Gets a human-readable description of the search type.
        /// </summary>
        /// <param name="searchType">The search type to describe.</param>
        /// <returns>A friendly description of the search type.</returns>
        public static string GetDescription(SearchType searchType)
        {
            return searchType switch
            {
                SearchType.Album => "Albums",
                SearchType.Artist => "Artists",
                SearchType.Track => "Tracks",
                SearchType.Playlist => "Playlists",
                SearchType.Label => "Labels",
                SearchType.Auto => "Automatic Detection",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Gets all valid search type string values for help text.
        /// </summary>
        /// <returns>Array of valid search type strings.</returns>
        public static string[] GetValidTypeStrings()
        {
            return new[]
            {
                "album", "albums",
                "artist", "artists",
                "track", "tracks", "song", "songs",
                "label", "labels",
                "playlist", "playlists",
                "auto", "automatic"
            };
        }
    }
}