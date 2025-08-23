using NzbDrone.Core.Indexers;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Download protocol marker for Qobuzarr plugin.
    /// This registers "Qobuzarr" as a valid download protocol in Lidarr's UI.
    /// Similar to UsenetDownloadProtocol and TorrentDownloadProtocol but for streaming services.
    /// </summary>
    public class QobuzarrDownloadProtocol : IDownloadProtocol
    {
        // This is a marker class that identifies our protocol type
        // Lidarr will automatically discover this and add "Qobuzarr" to the Download Protocols list
        // The Protocol property in indexers and download clients should return nameof(QobuzarrDownloadProtocol)
    }
}