using System.Threading.Tasks;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using Lidarr.Plugin.Qobuzarr.Download.Clients;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Service for managing file operations during downloads.
    /// Handles path generation, directory creation, and file cleanup operations.
    /// </summary>
    public interface IDownloadFileService
    {
        /// <summary>
        /// Builds the complete output path for a downloaded album.
        /// </summary>
        /// <param name="remoteAlbum">Album information from remote source</param>
        /// <param name="settings">Download client settings</param>
        /// <returns>Full path where album should be downloaded</returns>
        string BuildOutputPath(RemoteAlbum remoteAlbum, QobuzDownloadSettings settings);

        /// <summary>
        /// Ensures the output directory exists, creating it if necessary.
        /// </summary>
        /// <param name="path">Directory path to create</param>
        void EnsureOutputDirectory(string path);

        /// <summary>
        /// Cleans up files and directories from a failed download.
        /// </summary>
        /// <param name="path">Path to clean up</param>
        /// <returns>Task representing cleanup completion</returns>
        Task CleanupFailedDownloadAsync(string path);

        /// <summary>
        /// Validates that a download path is acceptable and accessible.
        /// </summary>
        /// <param name="path">Path to validate</param>
        /// <returns>True if path is valid and accessible</returns>
        bool ValidateDownloadPath(string path);

        /// <summary>
        /// Gets the available disk space at the specified path.
        /// </summary>
        /// <param name="path">Path to check</param>
        /// <returns>Available space in bytes, or null if unable to determine</returns>
        long? GetAvailableDiskSpace(string path);

        /// <summary>
        /// Creates a unique download directory name to avoid conflicts.
        /// </summary>
        /// <param name="basePath">Base directory path</param>
        /// <param name="albumName">Album name for directory</param>
        /// <returns>Unique directory path</returns>
        string CreateUniqueDownloadDirectory(string basePath, string albumName);
    }
}
