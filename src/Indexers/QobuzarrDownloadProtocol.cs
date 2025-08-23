using NzbDrone.Core.Indexers;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Download protocol constants for Qobuzarr plugin
    /// Provides the protocol name used by both indexer and download client
    /// </summary>
    public static class QobuzarrDownloadProtocol
    {
        /// <summary>
        /// The protocol name used to identify Qobuzarr downloads
        /// </summary>
        public const string Name = "Qobuzarr";
    }
}