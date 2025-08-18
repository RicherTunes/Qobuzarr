using System;
using Lidarr.Plugin.Qobuzarr.Models;

namespace QobuzCLI.Services
{
    /// <summary>
    /// Simple model converter that ensures CLI uses plugin models directly.
    /// Following CLAUDE.md principle: CLI never reimplements plugin functionality.
    /// </summary>
    public static class ModelConverter
    {
        /// <summary>
        /// Ensures the track is a plugin QobuzTrack model.
        /// If it's already a QobuzTrack, returns it directly.
        /// </summary>
        public static QobuzTrack EnsurePluginTrack(object track)
        {
            if (track == null)
                throw new ArgumentNullException(nameof(track));
                
            // If it's already a plugin track, return as-is
            if (track is QobuzTrack qobuzTrack)
                return qobuzTrack;
                
            // This should not happen if CLI is properly using plugin models
            throw new InvalidOperationException(
                $"Expected QobuzTrack from plugin but got {track.GetType().Name}. " +
                "CLI must use plugin models directly per CLAUDE.md architecture.");
        }

        /// <summary>
        /// Ensures the album is a plugin QobuzAlbum model.
        /// If it's already a QobuzAlbum, returns it directly.
        /// </summary>
        public static QobuzAlbum EnsurePluginAlbum(object album)
        {
            if (album == null)
                throw new ArgumentNullException(nameof(album));
                
            // If it's already a plugin album, return as-is
            if (album is QobuzAlbum qobuzAlbum)
                return qobuzAlbum;
                
            // This should not happen if CLI is properly using plugin models
            throw new InvalidOperationException(
                $"Expected QobuzAlbum from plugin but got {album.GetType().Name}. " +
                "CLI must use plugin models directly per CLAUDE.md architecture.");
        }
        
        /// <summary>
        /// Creates a simple search result wrapper for CLI display purposes only.
        /// The actual model remains the plugin model.
        /// </summary>
        public static object CreateCliSearchResult(QobuzAlbum album, double score = 0.0)
        {
            // Return an anonymous object for CLI display only
            // The actual album remains the plugin model
            return new
            {
                Album = album,  // Keep the original plugin model
                Score = score,
                DisplayTitle = album.GetFullTitle(),
                DisplayArtist = album.GetArtistName(),
                ReleaseYear = album.ReleaseDate.Year > 1900 ? album.ReleaseDate.Year.ToString() : "Unknown"
            };
        }
    }
}