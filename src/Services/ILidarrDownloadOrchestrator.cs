using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for orchestrating the complete download process for Lidarr albums.
    /// </summary>
    public interface ILidarrDownloadOrchestrator
    {
        /// <summary>
        /// Orchestrates the complete download process for Lidarr albums with parallel execution.
        /// </summary>
        /// <param name="downloadItems">Collection of validated album download items.</param>
        /// <param name="outputPath">Output directory path for downloads.</param>
        /// <param name="maxConcurrency">Maximum concurrent download operations.</param>
        /// <param name="progress">Progress reporting callback.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Batch download result with success/failure statistics.</returns>
        Task<DownloadBatchResult> DownloadLidarrAlbumsAsync(
            IEnumerable<AlbumDownloadItem> downloadItems,
            string outputPath,
            int maxConcurrency = 0,
            System.IProgress<DownloadProgressReport> progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retries failed album downloads with exponential backoff.
        /// </summary>
        /// <param name="failedItems">Collection of failed download items to retry.</param>
        /// <param name="maxRetries">Maximum number of retry attempts.</param>
        /// <param name="outputPath">Optional override output path for retries.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Batch download result for retry operations.</returns>
        Task<DownloadBatchResult> RetryFailedDownloadsAsync(
            IEnumerable<DownloadFailureItem> failedItems,
            int maxRetries = 3,
            string outputPath = null,
            CancellationToken cancellationToken = default);
    }
}
