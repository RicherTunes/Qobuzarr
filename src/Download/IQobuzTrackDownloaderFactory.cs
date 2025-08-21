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
        /// </summary>
        QobuzTrackDownloader CreateTrackDownloader();

        /// <summary>
        /// Creates a simple QobuzTrackDownloader instance without metadata optimizer to avoid circular dependencies
        /// Used by IntelligentReleaseMapper which doesn't need optimization capabilities
        /// </summary>
        /// <returns>A QobuzTrackDownloader instance without metadata optimizer</returns>
        QobuzTrackDownloader CreateSimpleTrackDownloader();
        
        // Legacy method removed - use CreateTrackDownloader() instead
    }
}