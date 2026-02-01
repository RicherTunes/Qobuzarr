using System.Threading.Tasks;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Indexers;

namespace Lidarr.Plugin.Qobuzarr.Download.Orchestration
{
    /// <summary>
    /// High-level orchestrator for managing the complete download process.
    /// Coordinates between services to execute downloads while maintaining separation of concerns.
    /// </summary>
    public interface IDownloadOrchestrator
    {
        /// <summary>
        /// Initiates a new download for the specified remote album.
        /// </summary>
        /// <param name="remoteAlbum">Remote album information</param>
        /// <param name="indexer">Source indexer</param>
        /// <returns>Download identifier for tracking</returns>
        Task<string> StartDownloadAsync(RemoteAlbum remoteAlbum, IIndexer indexer);

        /// <summary>
        /// Gets the current status of all active downloads.
        /// </summary>
        /// <returns>Collection of download status information</returns>
        Task<DownloadOrchestrationStatus> GetDownloadStatusAsync();

        /// <summary>
        /// Cancels an active download.
        /// </summary>
        /// <param name="downloadId">Download identifier to cancel</param>
        /// <returns>True if download was found and cancelled</returns>
        Task<bool> CancelDownloadAsync(string downloadId);

        /// <summary>
        /// Performs cleanup of completed or failed downloads.
        /// </summary>
        /// <returns>Number of downloads cleaned up</returns>
        Task<int> CleanupCompletedDownloadsAsync();
    }

    /// <summary>
    /// Overall status information for the download orchestration system.
    /// </summary>
    public class DownloadOrchestrationStatus
    {
        public int ActiveDownloads { get; set; }
        public int QueuedDownloads { get; set; }
        public int CompletedDownloads { get; set; }
        public int FailedDownloads { get; set; }
        public double TotalProgress { get; set; }
        public long TotalBytesDownloaded { get; set; }
        public System.DateTime LastUpdated { get; set; } = System.DateTime.UtcNow;
    }
}
