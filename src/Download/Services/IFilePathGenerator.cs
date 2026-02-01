using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Interface for generating file paths and names for downloaded tracks
    /// </summary>
    public interface IFilePathGenerator
    {
        /// <summary>
        /// Generates a filename for a track using basic metadata
        /// </summary>
        /// <param name="track">Track metadata</param>
        /// <param name="album">Album metadata</param>
        /// <param name="formatId">Quality format ID for file extension</param>
        /// <returns>Generated filename with extension</returns>
        string GenerateFileName(QobuzTrack track, QobuzAlbum album, int formatId);

        /// <summary>
        /// Generates a filename using optimized metadata
        /// </summary>
        /// <param name="trackDownload">Optimized track metadata</param>
        /// <param name="quality">Quality format ID for file extension</param>
        /// <returns>Generated filename with extension</returns>
        string GenerateOptimizedFileName(TrackDownload trackDownload, int quality);

        /// <summary>
        /// Gets the appropriate file extension for a quality format
        /// </summary>
        /// <param name="formatId">Quality format ID</param>
        /// <returns>File extension (e.g., ".flac", ".mp3")</returns>
        string GetFileExtension(int formatId);

        /// <summary>
        /// Gets a human-readable description of track quality
        /// </summary>
        /// <param name="track">Track with quality information</param>
        /// <returns>Quality description string</returns>
        string GetQualityDescription(QobuzTrack track);
    }
}
