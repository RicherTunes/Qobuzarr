using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for managing download queues and concurrent operations.
    /// </summary>
    public interface ILidarrQueueManager : IDisposable
    {
        /// <summary>
        /// Gets the current number of active download operations.
        /// </summary>
        int ActiveDownloadCount { get; }

        /// <summary>
        /// Gets the current number of active search operations.
        /// </summary>
        int ActiveSearchCount { get; }

        /// <summary>
        /// Gets the maximum allowed concurrent downloads.
        /// </summary>
        int MaxConcurrentDownloads { get; }

        /// <summary>
        /// Gets the maximum allowed concurrent searches.
        /// </summary>
        int MaxConcurrentSearches { get; }

        /// <summary>
        /// Acquires a download slot, waiting if necessary until one becomes available.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task that completes when a download slot is available.</returns>
        Task AcquireDownloadSlotAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Releases a previously acquired download slot.
        /// </summary>
        void ReleaseDownloadSlot();

        /// <summary>
        /// Acquires a search slot, waiting if necessary until one becomes available.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task that completes when a search slot is available.</returns>
        Task AcquireSearchSlotAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Releases a previously acquired search slot.
        /// </summary>
        void ReleaseSearchSlot();

        /// <summary>
        /// Gets the current queue status including active operations and available slots.
        /// </summary>
        /// <returns>Queue status information.</returns>
        QueueStatus GetQueueStatus();

        /// <summary>
        /// Updates the maximum concurrency limits for downloads and searches.
        /// </summary>
        /// <param name="maxDownloads">Maximum concurrent downloads.</param>
        /// <param name="maxSearches">Maximum concurrent searches.</param>
        void UpdateConcurrencyLimits(int maxDownloads, int maxSearches);

        /// <summary>
        /// Waits for all active operations to complete.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task that completes when all operations finish.</returns>
        Task WaitForAllOperationsToCompleteAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets queue statistics including operation history and performance metrics.
        /// </summary>
        /// <returns>Queue statistics.</returns>
        QueueStatistics GetQueueStatistics();
    }

    /// <summary>
    /// Represents the current status of the download and search queues.
    /// </summary>
    public class QueueStatus
    {
        /// <summary>
        /// Number of active download operations.
        /// </summary>
        public int ActiveDownloads { get; set; }

        /// <summary>
        /// Number of active search operations.
        /// </summary>
        public int ActiveSearches { get; set; }

        /// <summary>
        /// Maximum concurrent downloads allowed.
        /// </summary>
        public int MaxConcurrentDownloads { get; set; }

        /// <summary>
        /// Maximum concurrent searches allowed.
        /// </summary>
        public int MaxConcurrentSearches { get; set; }

        /// <summary>
        /// Number of download slots available.
        /// </summary>
        public int AvailableDownloadSlots { get; set; }

        /// <summary>
        /// Number of search slots available.
        /// </summary>
        public int AvailableSearchSlots { get; set; }

        /// <summary>
        /// Whether the download queue is at capacity.
        /// </summary>
        public bool IsDownloadQueueFull { get; set; }

        /// <summary>
        /// Whether the search queue is at capacity.
        /// </summary>
        public bool IsSearchQueueFull { get; set; }
    }

    /// <summary>
    /// Contains statistics about queue operations and performance.
    /// </summary>
    public class QueueStatistics
    {
        /// <summary>
        /// Total number of download slots that have been acquired.
        /// </summary>
        public long TotalDownloadSlotAcquisitions { get; set; }

        /// <summary>
        /// Total number of search slots that have been acquired.
        /// </summary>
        public long TotalSearchSlotAcquisitions { get; set; }

        /// <summary>
        /// Average time spent waiting for download slots.
        /// </summary>
        public System.TimeSpan AverageDownloadWaitTime { get; set; }

        /// <summary>
        /// Average time spent waiting for search slots.
        /// </summary>
        public System.TimeSpan AverageSearchWaitTime { get; set; }

        /// <summary>
        /// Peak number of concurrent downloads observed.
        /// </summary>
        public int PeakConcurrentDownloads { get; set; }

        /// <summary>
        /// Peak number of concurrent searches observed.
        /// </summary>
        public int PeakConcurrentSearches { get; set; }

        /// <summary>
        /// Total time slots have been held for downloads.
        /// </summary>
        public System.TimeSpan TotalDownloadSlotHoldTime { get; set; }

        /// <summary>
        /// Total time slots have been held for searches.
        /// </summary>
        public System.TimeSpan TotalSearchSlotHoldTime { get; set; }

        /// <summary>
        /// Number of times download queue reached capacity.
        /// </summary>
        public int DownloadQueueSaturations { get; set; }

        /// <summary>
        /// Number of times search queue reached capacity.
        /// </summary>
        public int SearchQueueSaturations { get; set; }
    }
}