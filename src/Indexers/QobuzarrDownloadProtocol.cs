using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Indexers
{
    /// <summary>
    /// Download protocol marker for Qobuzarr plugin
    /// This registers "Qobuzarr" as a valid download protocol in Lidarr's UI
    /// Following TrevTV's proven approach for Qobuz plugin compatibility
    /// </summary>
    public class QobuzarrDownloadProtocol : IDownloadProtocol
    {
        // This is a marker class that identifies our protocol type
        // Lidarr will automatically discover this and add "Qobuzarr" to the Download Protocols list
    }
}