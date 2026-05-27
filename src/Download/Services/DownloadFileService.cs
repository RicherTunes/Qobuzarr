using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Common.Security;
using System.Text;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Service for managing file operations during downloads.
    /// Provides centralized file and directory management with proper error handling.
    /// </summary>
    public class DownloadFileService : IDownloadFileService
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IRemotePathMappingService _remotePathMappingService;
        private readonly Logger _logger;

        public DownloadFileService(
            IDiskProvider diskProvider,
            IRemotePathMappingService remotePathMappingService,
            Logger logger)
        {
            _diskProvider = diskProvider ?? throw new ArgumentNullException(nameof(diskProvider));
            _remotePathMappingService = remotePathMappingService ?? throw new ArgumentNullException(nameof(remotePathMappingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string BuildOutputPath(RemoteAlbum remoteAlbum, QobuzDownloadSettings settings)
        {
            try
            {
                var album = remoteAlbum.Albums.FirstOrDefault();
                if (album == null)
                {
                    throw new ArgumentException("Remote album must contain at least one album", nameof(remoteAlbum));
                }

                // Prefer top-level RemoteAlbum artist if available (more reliable in tests)
                var artist = remoteAlbum.Artist?.Name ?? album.Artist?.Value?.Name ?? "Unknown Artist";
                var albumTitle = album.Title ?? remoteAlbum.Albums.FirstOrDefault()?.Title ?? "Unknown Album";

                // Sanitize names for filesystem (NFC + reserved names guard)
                artist = SanitizeFileName(artist);
                albumTitle = SanitizeFileName(albumTitle);

                // Limit length to prevent filesystem issues
                artist = TruncateToLength(artist, QobuzConstants.Download.MaxFolderNameLength / 2);
                albumTitle = TruncateToLength(albumTitle, QobuzConstants.Download.MaxFolderNameLength / 2);

                // Create folder structure based on settings
                string outputPath;
                if (settings.CreateAlbumFolders)
                {
                    // Create Artist/Album folder structure
                    outputPath = Path.Combine(settings.DownloadPath, artist, albumTitle);
                }
                else
                {
                    // All tracks go directly into download path
                    outputPath = settings.DownloadPath;
                }

                // Defense-in-depth path containment check (matches apple PR #130 hardening).
                // SanitizeFileName already strips most traversal vectors, but a future change
                // (or a vector we missed) could let `..` segments through. Canonical-form
                // comparison via Common's PathTraversalGuard is the safety net:
                // OS-aware case-sensitivity, sibling-prefix protection, equals-root edge case.
                if (!Lidarr.Plugin.Common.HostBridge.PathTraversalGuard.IsPathWithinRoot(outputPath, settings.DownloadPath))
                {
                    throw new InvalidOperationException(
                        $"Qobuzarr: refusing to build output path '{outputPath}' — resolves outside the configured DownloadPath '{settings.DownloadPath}'.");
                }

                _logger.Debug("Built output path: {0}", outputPath);
                return outputPath;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to build output path for album {0}", remoteAlbum.Albums.FirstOrDefault()?.Title ?? "Unknown");
                throw;
            }
        }

        public void EnsureOutputDirectory(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    throw new ArgumentException("Path cannot be null or whitespace", nameof(path));

                if (!_diskProvider.FolderExists(path))
                {
                    _logger.Debug("Creating output directory: {0}", path);
                    _diskProvider.CreateFolder(path);
                }

                // Verify directory is accessible
                if (!_diskProvider.FolderWritable(path))
                {
                    throw new UnauthorizedAccessException($"Output directory is not writable: {path}");
                }

                _logger.Debug("Output directory verified: {0}", path);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to ensure output directory: {0}", path);
                throw;
            }
        }

        public async Task CleanupFailedDownloadAsync(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !_diskProvider.FolderExists(path))
                {
                    _logger.Debug("Cleanup path does not exist or is invalid: {0}", path);
                    return;
                }

                _logger.Debug("Cleaning up failed download directory: {0}", path);

                // Delete all files in the directory
                var files = _diskProvider.GetFiles(path, true);
                foreach (var file in files)
                {
                    try
                    {
                        _diskProvider.DeleteFile(file);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to delete file during cleanup: {0}", file);
                    }
                }

                // Remove the directory if it's empty or force removal after delay
                await Task.Delay(QobuzConstants.Timing.FileOperations.FileSystemStabilizationDelayMs).ConfigureAwait(false);

                try
                {
                    _diskProvider.DeleteFolder(path, true);
                    _logger.Debug("Successfully cleaned up directory: {0}", path);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to remove directory during cleanup (may not be empty): {0}", path);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during cleanup of failed download: {0}", path);
                // Don't rethrow - cleanup failure shouldn't break the download process
            }
        }

        public bool ValidateDownloadPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    _logger.Warn("Download path is null or empty");
                    return false;
                }

                // SECURITY: Validate path doesn't contain traversal attempts
                if (path.Contains("..") || !Utilities.LidarrInputValidator.IsInputSafe(path))
                {
                    _logger.Warn("Download path contains potentially unsafe characters: {0}", path);
                    return false;
                }

                var parentDirectory = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(parentDirectory))
                {
                    _logger.Warn("Cannot determine parent directory for path: {0}", path);
                    return false;
                }

                // Check if parent directory exists and is writable
                if (!_diskProvider.FolderExists(parentDirectory))
                {
                    _logger.Warn("Parent directory does not exist: {0}", parentDirectory);
                    return false;
                }

                if (!_diskProvider.FolderWritable(parentDirectory))
                {
                    _logger.Warn("Parent directory is not writable: {0}", parentDirectory);
                    return false;
                }

                // Check available disk space (at least 1 GB for downloads)
                var availableSpace = GetAvailableDiskSpace(parentDirectory);
                if (availableSpace.HasValue && availableSpace < 1024 * 1024 * 1024) // 1 GB
                {
                    _logger.Warn("Insufficient disk space at path: {0} (Available: {1} bytes)", parentDirectory, availableSpace);
                    return false;
                }

                _logger.Debug("Download path validated successfully: {0}", path);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error validating download path: {0}", path);
                return false;
            }
        }

        public long? GetAvailableDiskSpace(string path)
        {
            try
            {
                return _diskProvider.GetAvailableSpace(path);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to determine available disk space for path: {0}", path);
                return null;
            }
        }

        public string CreateUniqueDownloadDirectory(string basePath, string albumName)
        {
            try
            {
                albumName = SanitizeFileName(albumName);
                albumName = TruncateToLength(albumName, QobuzConstants.Download.MaxFolderNameLength);

                var originalPath = Path.Combine(basePath, albumName);

                if (!_diskProvider.FolderExists(originalPath))
                {
                    return originalPath;
                }

                // If directory exists, append timestamp to make it unique
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var uniqueName = $"{albumName}_{timestamp}";
                var uniquePath = Path.Combine(basePath, uniqueName);

                _logger.Debug("Created unique directory name: {0}", uniquePath);
                return uniquePath;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create unique directory name for album: {0}", albumName);
                throw;
            }
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "Unknown";

            var sanitized = Sanitize.PathSegment(fileName);
            return sanitized.Normalize(NormalizationForm.FormC);
        }

        private string TruncateToLength(string input, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            return input.Length <= maxLength ? input : input.Substring(0, maxLength).TrimEnd();
        }
    }
}
