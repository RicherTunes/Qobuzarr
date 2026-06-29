using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download;
using Lidarr.Plugin.Common.HostBridge;
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
                // Capture fields before the item is potentially disposed/GC'd.
                var capturedTask = removedItem.DownloadTask;
                var capturedPath = removedItem.OutputPath;
                var capturedRoot = removedItem.DownloadRoot ?? string.Empty;
                var capturedId = downloadId;

                // Clean up files asynchronously to avoid blocking.
                // CRITICAL RACE GUARD: two races can cause an infinite Lidarr re-grab loop by
                // deleting in-flight .partial files, causing File.Move → FileNotFoundException
                // cascades across all concurrent track downloads:
                //
                //   Race 1 (same-attempt): RemoveItem is called while Task.WhenAll over all tracks
                //   is still running. Without a wait, the 100ms delay fires cleanup while sibling
                //   tracks are still writing .partial files → their File.Move throws.
                //
                //   Race 2 (cross-attempt / re-grab): Lidarr immediately re-grabs a failed album.
                //   The new attempt (same output path) is already writing .partial files when the
                //   old attempt's cleanup fires. Without a path-conflict check, cleanup deletes
                //   the new attempt's files → it fails → Lidarr re-grabs again → infinite loop.
                //
                // Fix: await the removed item's DownloadTask first (Race 1), then skip cleanup
                // if any active download in the queue is still targeting the same path (Race 2).
                Task.Run(async () =>
                {
                    try
                    {
                        // Phase 1 — wait for this item's own download task to complete.
                        // Cancellation was already signalled by the caller; we just observe it.
                        if (capturedTask != null && !capturedTask.IsCompleted)
                        {
                            try
                            {
                                await capturedTask.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                            }
                            catch (TimeoutException)
                            {
                                _logger.Warn(
                                    "Timed out (30 s) waiting for download task to complete before cleanup: {0}. " +
                                    "Skipping cleanup to avoid deleting potentially-in-flight files.",
                                    capturedId);
                                return;
                            }
                            catch (Exception)
                            {
                                // Task ended with an exception (expected for failed/cancelled downloads).
                            }
                        }

                        // Phase 2 — cross-attempt (re-grab) guard.
                        // When Lidarr re-grabs a failed album it immediately queues a new
                        // QobuzDownloadItem with the same OutputPath. If any active download is
                        // now targeting capturedPath, skip cleanup to avoid nuking its in-flight
                        // .partial files. The new download will manage its own cleanup lifecycle.
                        var newDownloadAtSamePath = _activeDownloads.Values.Any(d =>
                            string.Equals(d.OutputPath, capturedPath, StringComparison.OrdinalIgnoreCase) &&
                            (d.GetStatus() == HostBridgeDownloadItemStatus.Downloading ||
                             d.GetStatus() == HostBridgeDownloadItemStatus.Queued));

                        if (newDownloadAtSamePath)
                        {
                            _logger.Warn(
                                "Skipping cleanup of '{0}': a new download is already active at the same path. " +
                                "The directory will be managed by the new download's lifecycle.",
                                capturedPath);
                            return;
                        }

                        await _fileService.CleanupFailedDownloadAsync(capturedPath, capturedRoot).ConfigureAwait(false);
                        _logger.Debug("Cleaned up download data for: {0}", capturedId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to cleanup download data for: {0}", capturedId);
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
                .Where(item => item.GetHostStatus() == DownloadItemStatus.Completed &&
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
            return _activeDownloads.Values.Count(item => item.GetHostStatus() == status);
        }

        public void UpdateDownloadStatus(string downloadId, DownloadItemStatus status, string message = null)
        {
            if (!TryGetDownload(downloadId, out var item))
            {
                _logger.Warn("Cannot update status for unknown download: {0}", downloadId);
                return;
            }

            var previousStatus = item.GetHostStatus();
            item.SetHostStatus(status);

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
                    QueuedDownloads = downloads.Count(d => d.GetHostStatus() == DownloadItemStatus.Queued),
                    DownloadingDownloads = downloads.Count(d => d.GetHostStatus() == DownloadItemStatus.Downloading),
                    CompletedDownloads = downloads.Count(d => d.GetHostStatus() == DownloadItemStatus.Completed),
                    FailedDownloads = downloads.Count(d => d.GetHostStatus() == DownloadItemStatus.Failed),
                    TotalBytesDownloaded = downloads.Sum(d => d.TotalSize),
                    LastUpdated = DateTime.UtcNow
                };
            }
        }
    }
}
