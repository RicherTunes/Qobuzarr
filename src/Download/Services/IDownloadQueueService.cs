using System;
using System.Collections.Generic;
using NzbDrone.Core.Download;
using Lidarr.Plugin.Qobuzarr.Download.Clients;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Service for managing the download queue and tracking active downloads.
    /// Provides thread-safe operations for download lifecycle management.
    /// </summary>
    public interface IDownloadQueueService
    {
        /// <summary>
        /// Adds a download to the active queue.
        /// </summary>
        /// <param name="item">Download item to add</param>
        void AddDownload(QobuzDownloadItem item);

        /// <summary>
        /// Gets all currently active downloads.
        /// </summary>
        /// <returns>Collection of active download items</returns>
        IEnumerable<QobuzDownloadItem> GetActiveDownloads();

        /// <summary>
        /// Attempts to get a specific download by ID.
        /// </summary>
        /// <param name="downloadId">Unique download identifier</param>
        /// <param name="item">Retrieved download item if found</param>
        /// <returns>True if download was found</returns>
        bool TryGetDownload(string downloadId, out QobuzDownloadItem item);

        /// <summary>
        /// Removes a download from the queue.
        /// </summary>
        /// <param name="downloadId">Download identifier to remove</param>
        /// <param name="deleteData">Whether to delete associated files</param>
        /// <returns>True if download was found and removed</returns>
        bool RemoveDownload(string downloadId, bool deleteData = false);

        /// <summary>
        /// Cleans up completed downloads that are older than the specified timespan.
        /// </summary>
        /// <param name="olderThan">Age threshold for cleanup</param>
        /// <returns>Number of downloads cleaned up</returns>
        int CleanupCompletedDownloads(TimeSpan olderThan);

        /// <summary>
        /// Gets the number of active downloads.
        /// </summary>
        int ActiveDownloadCount { get; }

        /// <summary>
        /// Gets the number of downloads in a specific status.
        /// </summary>
        /// <param name="status">Status to count</param>
        /// <returns>Number of downloads in the specified status</returns>
        int GetDownloadCountByStatus(DownloadItemStatus status);

        /// <summary>
        /// Updates the status of an active download.
        /// </summary>
        /// <param name="downloadId">Download identifier</param>
        /// <param name="status">New status</param>
        /// <param name="message">Optional status message</param>
        void UpdateDownloadStatus(string downloadId, DownloadItemStatus status, string message = null);

        /// <summary>
        /// Gets download queue statistics.
        /// </summary>
        /// <returns>Queue statistics summary</returns>
        DownloadQueueStatistics GetQueueStatistics();
    }

    /// <summary>
    /// Statistics about the download queue state.
    /// </summary>
    public class DownloadQueueStatistics
    {
        public int TotalDownloads { get; set; }
        public int QueuedDownloads { get; set; }
        public int DownloadingDownloads { get; set; }
        public int CompletedDownloads { get; set; }
        public int FailedDownloads { get; set; }
        public long TotalBytesDownloaded { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}