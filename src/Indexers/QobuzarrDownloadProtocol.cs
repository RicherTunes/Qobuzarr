using NzbDrone.Core.Indexers;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Download protocol marker for Qobuzarr plugin
    /// </summary>
    public class QobuzarrDownloadProtocol : IDownloadProtocol
    {
        // This is a marker class that identifies our protocol type
        // Used by Lidarr to distinguish between different download protocols
    }
}