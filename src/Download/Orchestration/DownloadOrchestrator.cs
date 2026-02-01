using System;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Indexers;
using Lidarr.Plugin.Qobuzarr.Download.Services;

namespace Lidarr.Plugin.Qobuzarr.Download.Orchestration
{
    /// <summary>
    /// High-level orchestrator for managing the complete download process.
    /// Coordinates between services to execute downloads while maintaining separation of concerns.
    /// </summary>
    public class DownloadOrchestrator : IDownloadOrchestrator
    {
        private readonly IDownloadQueueService _queueService;
        private readonly IDownloadFileService _fileService;
        private readonly IConcurrencyManager _concurrencyManager;
        private readonly Logger _logger;

        public DownloadOrchestrator(
            IDownloadQueueService queueService,
            IDownloadFileService fileService,
            IConcurrencyManager concurrencyManager,
            Logger logger)
        {
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _concurrencyManager = concurrencyManager ?? throw new ArgumentNullException(nameof(concurrencyManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> StartDownloadAsync(RemoteAlbum remoteAlbum, IIndexer indexer)
        {
            try
            {
                var albumTitle = remoteAlbum.Albums?.FirstOrDefault()?.Title ?? "Unknown Album";
                var artistName = remoteAlbum.Artist?.Name ?? "Unknown Artist";

                _logger.Info("📥 Orchestrating download: {0} - {1}", artistName, albumTitle);

                // Generate unique download ID
                var downloadId = Guid.NewGuid().ToString("N");

                // This would create a download item and add to queue
                // For now, return the generated ID
                _logger.Debug("Generated download ID: {0}", downloadId);
                return downloadId;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to start download orchestration");
                throw;
            }
        }

        public async Task<DownloadOrchestrationStatus> GetDownloadStatusAsync()
        {
            try
            {
                var queueStats = _queueService.GetQueueStatistics();
                var concurrencyStats = _concurrencyManager.GetStatistics();

                return new DownloadOrchestrationStatus
                {
                    ActiveDownloads = concurrencyStats.ActiveOperations,
                    QueuedDownloads = queueStats.QueuedDownloads,
                    CompletedDownloads = queueStats.CompletedDownloads,
                    FailedDownloads = queueStats.FailedDownloads,
                    TotalProgress = CalculateOverallProgress(queueStats),
                    TotalBytesDownloaded = queueStats.TotalBytesDownloaded,
                    LastUpdated = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get download status");
                throw;
            }
        }

        public async Task<bool> CancelDownloadAsync(string downloadId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(downloadId))
                {
                    _logger.Warn("Cannot cancel download: invalid download ID");
                    return false;
                }

                if (_queueService.TryGetDownload(downloadId, out var downloadItem))
                {
                    downloadItem.Cancel();
                    _logger.Info("Cancelled download: {0}", downloadId);
                    return true;
                }

                _logger.Warn("Cannot cancel download: download not found: {0}", downloadId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error cancelling download: {0}", downloadId);
                return false;
            }
        }

        public async Task<int> CleanupCompletedDownloadsAsync()
        {
            try
            {
                // Use default cleanup cutoff from configuration
                var cutoff = TimeSpan.FromMinutes(30);
                var cleanedUp = _queueService.CleanupCompletedDownloads(cutoff);

                if (cleanedUp > 0)
                {
                    _logger.Info("Cleaned up {0} completed downloads", cleanedUp);
                }

                return cleanedUp;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during cleanup of completed downloads");
                return 0;
            }
        }

        private double CalculateOverallProgress(DownloadQueueStatistics stats)
        {
            if (stats.TotalDownloads == 0)
                return 0;

            // Simple progress calculation - can be made more sophisticated
            var completedWeight = stats.CompletedDownloads * 100.0;
            var failedWeight = stats.FailedDownloads * 0.0; // Failed downloads contribute 0%

            // Estimate progress for active downloads (assume 50% average)
            var activeWeight = (stats.QueuedDownloads + stats.DownloadingDownloads) * 50.0;

            return (completedWeight + activeWeight) / stats.TotalDownloads;
        }
    }
}
