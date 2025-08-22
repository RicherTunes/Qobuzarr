using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Services;
using NzbDrone.Common.Http;

namespace Lidarr.Plugin.Qobuzarr.Download
{
    /// <summary>
    /// Factory interface for creating QobuzTrackDownloader instances with proper dependency injection
    /// </summary>
    public interface IQobuzTrackDownloaderFactory
    {
        /// <summary>
        /// Creates a QobuzTrackDownloader instance with all required dependencies
        /// Note: Metadata optimization is currently disabled to prevent circular dependencies
        /// </summary>
        QobuzTrackDownloader CreateTrackDownloader();
        
        // Legacy method removed - use CreateTrackDownloader() instead
    }
}