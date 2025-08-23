using NzbDrone.Core.Indexers;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Download protocol marker for Qobuzarr plugin
    /// This registers "Qobuzarr" as a valid download protocol in Lidarr's UI
    /// Following working approach from fix/test-infrastructure branch
    /// </summary>
    public class QobuzarrDownloadProtocol : IDownloadProtocol
    {
        // This is a marker class that identifies our protocol type
        // Lidarr will automatically discover this and add "Qobuzarr" to the Download Protocols list
        
        /// <summary>
        /// The display name for this protocol (to avoid hardcoding)
        /// </summary>
        public const string DisplayName = "Qobuzarr";
    }
}