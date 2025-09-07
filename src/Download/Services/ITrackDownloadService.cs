using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Handles album/track-level download work: concurrency, HTTP streaming, tagging, and summary.
    /// </summary>
    public interface ITrackDownloadService
    {
        Task DownloadAlbumAsync(QobuzDownloadItem downloadItem, QobuzAlbum album, QobuzDownloadSettings settings, CancellationToken cancellationToken);
    }
}

