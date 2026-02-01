using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Interface for processing and applying metadata to audio files
    /// </summary>
    public interface IMetadataProcessor
    {
        /// <summary>
        /// Applies basic metadata to an audio file using TagLib
        /// </summary>
        /// <param name="filePath">Path to the audio file</param>
        /// <param name="track">Track metadata from Qobuz</param>
        /// <param name="album">Album metadata from Qobuz</param>
        void ApplyBasicMetadata(string filePath, QobuzTrack track, QobuzAlbum album);

        /// <summary>
        /// Applies optimized metadata using the intelligent metadata system
        /// </summary>
        /// <param name="filePath">Path to the audio file</param>
        /// <param name="trackDownload">Optimized track metadata</param>
        void ApplyOptimizedMetadata(string filePath, TrackDownload trackDownload);

        /// <summary>
        /// Creates a JSON metadata file alongside the audio file
        /// </summary>
        /// <param name="trackFilePath">Path to the audio file</param>
        /// <param name="track">Track metadata</param>
        /// <param name="album">Album metadata</param>
        /// <param name="formatId">Quality format ID</param>
        Task CreateMetadataFileAsync(string trackFilePath, QobuzTrack track, QobuzAlbum album, int formatId);

        /// <summary>
        /// Creates optimized JSON metadata file with rich information
        /// </summary>
        /// <param name="trackFilePath">Path to the audio file</param>
        /// <param name="trackDownload">Optimized track metadata</param>
        Task CreateOptimizedMetadataFileAsync(string trackFilePath, TrackDownload trackDownload);

        /// <summary>
        /// Downloads cover art for an album (once per album)
        /// </summary>
        /// <param name="albumPath">Path to the album directory</param>
        /// <param name="album">Album metadata containing cover art URLs</param>
        Task DownloadCoverArtAsync(string albumPath, QobuzAlbum album);
    }
}
