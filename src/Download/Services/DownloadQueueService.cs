using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Download.Clients;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Thread-safe service for managing download queue operations.
    /// Tracks active downloads and provides lifecycle management.
    /// </summary>
    public class DownloadQueueService : IDownloadQueueService
    {
        private readonly ConcurrentDictionary<string, QobuzDownloadItem> _activeDownloads;
        private readonly IDownloadFileService _fileService;
        private readonly Logger _logger;
        private readonly object _statsLock = new object();

        public DownloadQueueService(IDownloadFileService fileService, Logger logger)
        {
            _activeDownloads = new ConcurrentDictionary<string, QobuzDownloadItem>();
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void AddDownload(QobuzDownloadItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            if (string.IsNullOrWhiteSpace(item.DownloadId))
                throw new ArgumentException("Download item must have a valid DownloadId", nameof(item));

            if (_activeDownloads.TryAdd(item.DownloadId, item))
            {
                _logger.Debug("Added download to queue: {0} - {1}", item.DownloadId, item.Title);
            }
            else
            {
                _logger.Warn("Download already exists in queue: {0}", item.DownloadId);
            }
        }

        public IEnumerable<QobuzDownloadItem> GetActiveDownloads()
        {
            // Return a snapshot to avoid concurrent modification issues
            return _activeDownloads.Values.ToList();
        }

        public bool TryGetDownload(string downloadId, out QobuzDownloadItem item)
        {
            if (string.IsNullOrWhiteSpace(downloadId))
            {
                item = null;
                return false;
            }

            return _activeDownloads.TryGetValue(downloadId, out item);
        }

        public bool RemoveDownload(string downloadId, bool deleteData = false)
        {
            if (string.IsNullOrWhiteSpace(downloadId))
                return false;

            if (!_activeDownloads.TryRemove(downloadId, out var removedItem))
            {
                _logger.Debug("Download not found in queue for removal: {0}", downloadId);
                return false;
            }

            _logger.Debug("Removed download from queue: {0}", downloadId);

            if (deleteData && !string.IsNullOrWhiteSpace(removedItem.OutputPath))
            {
                // Clean up files asynchronously to avoid blocking
                Task.Run(async () =>
                {
                    try
                    {
                        await _fileService.CleanupFailedDownloadAsync(removedItem.OutputPath).ConfigureAwait(false);
                        _logger.Debug("Cleaned up download data for: {0}", downloadId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to cleanup download data for: {0}", downloadId);
                    }
                });
            }

            return true;
        }

        public int CleanupCompletedDownloads(TimeSpan olderThan)
        {
            var cutoffTime = DateTime.UtcNow - olderThan;
            var cleanedUp = 0;

            var itemsToRemove = _activeDownloads.Values
                .Where(item => item.Status == DownloadItemStatus.Completed && 
                              item.StartedAt < cutoffTime)
                .ToList();

            foreach (var item in itemsToRemove)
            {
                if (_activeDownloads.TryRemove(item.DownloadId, out _))
                {
                    cleanedUp++;
                    _logger.Debug("Cleaned up completed download: {0}", item.DownloadId);
                }
            }

            if (cleanedUp > 0)
            {
                _logger.Info("Cleaned up {0} completed downloads older than {1}", cleanedUp, olderThan);
            }

            return cleanedUp;
        }

        public int ActiveDownloadCount => _activeDownloads.Count;

        public int GetDownloadCountByStatus(DownloadItemStatus status)
        {
            return _activeDownloads.Values.Count(item => item.Status == status);
        }

        public void UpdateDownloadStatus(string downloadId, DownloadItemStatus status, string message = null)
        {
            if (!TryGetDownload(downloadId, out var item))
            {
                _logger.Warn("Cannot update status for unknown download: {0}", downloadId);
                return;
            }

            var previousStatus = item.Status;
            item.Status = status;

            if (!string.IsNullOrWhiteSpace(message))
            {
                item.Message = message;
            }

            _logger.Debug("Updated download status: {0} - {1} -> {2}", 
                downloadId, previousStatus, status);

            // Log significant status changes
            if (status == DownloadItemStatus.Completed)
            {
                _logger.Info("Download completed: {0} - {1}", downloadId, item.Title);
            }
            else if (status == DownloadItemStatus.Failed)
            {
                _logger.Warn("Download failed: {0} - {1} ({2})", downloadId, item.Title, message);
            }
        }

        public DownloadQueueStatistics GetQueueStatistics()
        {
            lock (_statsLock)
            {
                var downloads = _activeDownloads.Values.ToList();
                
                return new DownloadQueueStatistics
                {
                    TotalDownloads = downloads.Count,
                    QueuedDownloads = downloads.Count(d => d.Status == DownloadItemStatus.Queued),
                    DownloadingDownloads = downloads.Count(d => d.Status == DownloadItemStatus.Downloading),
                    CompletedDownloads = downloads.Count(d => d.Status == DownloadItemStatus.Completed),
                    FailedDownloads = downloads.Count(d => d.Status == DownloadItemStatus.Failed),
                    TotalBytesDownloaded = downloads.Sum(d => d.TotalSize),
                    LastUpdated = DateTime.UtcNow
                };
            }
        }
    }
}