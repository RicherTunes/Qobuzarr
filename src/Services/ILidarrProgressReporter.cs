using System;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for handling progress reporting with advanced time estimation and throughput calculations.
    /// </summary>
    public interface ILidarrProgressReporter
    {
        /// <summary>
        /// Creates a new progress tracker for a batch operation.
        /// </summary>
        /// <param name="totalItems">Total number of items to process.</param>
        /// <param name="operationType">Type of operation being tracked.</param>
        /// <param name="progress">Progress callback to report to.</param>
        /// <returns>Progress tracker instance.</returns>
        IProgressTracker CreateTracker(int totalItems, string operationType, IProgress<ProgressReport> progress = null);

        /// <summary>
        /// Creates a new download progress tracker for a batch download operation.
        /// </summary>
        /// <param name="totalItems">Total number of items to download.</param>
        /// <param name="operationType">Type of download operation being tracked.</param>
        /// <param name="progress">Download progress callback to report to.</param>
        /// <returns>Download progress tracker instance.</returns>
        IDownloadProgressTracker CreateDownloadTracker(int totalItems, string operationType, IProgress<DownloadProgressReport> progress = null);

        /// <summary>
        /// Gets the current progress statistics for all active trackers.
        /// </summary>
        /// <returns>Global progress statistics.</returns>
        GlobalProgressStatistics GetGlobalStatistics();

        /// <summary>
        /// Clears all completed trackers and resets global statistics.
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// Tracks progress for a generic batch operation with time estimation.
    /// </summary>
    public interface IProgressTracker : IDisposable
    {
        /// <summary>
        /// Gets the total number of items being processed.
        /// </summary>
        int TotalItems { get; }

        /// <summary>
        /// Gets the number of completed items.
        /// </summary>
        int CompletedItems { get; }

        /// <summary>
        /// Gets the current item being processed.
        /// </summary>
        string CurrentItem { get; }

        /// <summary>
        /// Gets the operation type.
        /// </summary>
        string OperationType { get; }

        /// <summary>
        /// Gets the elapsed time since operation started.
        /// </summary>
        TimeSpan Elapsed { get; }

        /// <summary>
        /// Gets the estimated remaining time.
        /// </summary>
        TimeSpan EstimatedRemaining { get; }

        /// <summary>
        /// Gets the completion percentage (0-100).
        /// </summary>
        double PercentComplete { get; }

        /// <summary>
        /// Reports progress on an item.
        /// </summary>
        /// <param name="currentItem">Current item being processed.</param>
        /// <param name="phase">Current processing phase.</param>
        void ReportProgress(string currentItem, string phase = null);

        /// <summary>
        /// Marks an item as completed.
        /// </summary>
        /// <param name="itemDescription">Optional description of completed item.</param>
        void CompleteItem(string itemDescription = null);

        /// <summary>
        /// Marks multiple items as completed in a batch.
        /// </summary>
        /// <param name="count">Number of items completed.</param>
        void CompleteItems(int count);
    }

    /// <summary>
    /// Tracks progress for download operations with throughput and bandwidth calculations.
    /// </summary>
    public interface IDownloadProgressTracker : IProgressTracker
    {
        /// <summary>
        /// Gets the total bytes downloaded.
        /// </summary>
        long TotalBytesDownloaded { get; }

        /// <summary>
        /// Gets the current download speed in MB/s.
        /// </summary>
        double CurrentSpeedMBps { get; }

        /// <summary>
        /// Gets the average download speed in MB/s.
        /// </summary>
        double AverageSpeedMBps { get; }

        /// <summary>
        /// Gets the number of successful downloads.
        /// </summary>
        int SuccessCount { get; }

        /// <summary>
        /// Gets the number of failed downloads.
        /// </summary>
        int FailureCount { get; }

        /// <summary>
        /// Reports download progress for an item.
        /// </summary>
        /// <param name="currentItem">Current item being downloaded.</param>
        /// <param name="bytesDownloaded">Bytes downloaded for this item.</param>
        /// <param name="isSuccess">Whether the download was successful.</param>
        void ReportDownloadProgress(string currentItem, long bytesDownloaded, bool isSuccess);

        /// <summary>
        /// Updates the total bytes downloaded across all items.
        /// </summary>
        /// <param name="additionalBytes">Additional bytes to add to the total.</param>
        void AddBytesDownloaded(long additionalBytes);
    }

    /// <summary>
    /// Contains global statistics across all active progress trackers.
    /// </summary>
    public class GlobalProgressStatistics
    {
        /// <summary>
        /// Number of active progress trackers.
        /// </summary>
        public int ActiveTrackers { get; set; }

        /// <summary>
        /// Total items being processed across all trackers.
        /// </summary>
        public int TotalItemsAcrossAllTrackers { get; set; }

        /// <summary>
        /// Total completed items across all trackers.
        /// </summary>
        public int CompletedItemsAcrossAllTrackers { get; set; }

        /// <summary>
        /// Overall completion percentage across all trackers.
        /// </summary>
        public double OverallPercentComplete { get; set; }

        /// <summary>
        /// Combined throughput across all download trackers (MB/s).
        /// </summary>
        public double CombinedDownloadSpeedMBps { get; set; }

        /// <summary>
        /// Total bytes downloaded across all download trackers.
        /// </summary>
        public long TotalBytesDownloadedAcrossAllTrackers { get; set; }

        /// <summary>
        /// Longest running tracker duration.
        /// </summary>
        public TimeSpan LongestRunningTracker { get; set; }

        /// <summary>
        /// Estimated time until all trackers complete.
        /// </summary>
        public TimeSpan EstimatedTimeToCompletion { get; set; }

        /// <summary>
        /// Number of trackers that have completed.
        /// </summary>
        public int CompletedTrackers { get; set; }

        /// <summary>
        /// Number of trackers currently active.
        /// </summary>
        public int RunningTrackers { get; set; }
    }
}