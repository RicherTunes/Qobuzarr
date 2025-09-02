#if PLUGIN_PROTOCOL
using NzbDrone.Core.Indexers;

namespace Lidarr.Plugin.Qobuzarr.Download
{
    /// <summary>
    /// Download protocol marker class for Qobuzarr streaming service
    /// Compiled only when building against Lidarr plugin branch (string Protocol).
    /// </summary>
    public class QobuzarrDownloadProtocol : IDownloadProtocol
    {
    }
}
#endif
