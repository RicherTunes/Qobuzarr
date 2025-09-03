using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using QobuzCLI.Models;
using QobuzCLI.Services.Adapters;
using Lidarr.Plugin.Qobuzarr.Utilities;
using System.Collections.Concurrent;

namespace QobuzCLI.Services;

public class QueueService : IQueueService
{
    private readonly ILogger<QueueService> _logger;
    private readonly IStateService _stateService;
    private readonly IPluginHost _pluginHost;
    private readonly IConfigService _configService;
    private readonly string _queuesFilePath;
    private readonly ConcurrentDictionary<string, DownloadQueue> _queues;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _queueProcessors;
    private readonly object _queuesLock = new();
    private readonly Timer _cleanupTimer;
    private bool _isDisposed;
    
    // Memory leak prevention constants
    private const int MAX_COMPLETED_ITEMS_PER_QUEUE = 500;
    private const int CLEANUP_KEEP_DAYS = 7;
    private const int CLEANUP_INTERVAL_HOURS = 4;

    public QueueService(ILogger<QueueService> logger, IStateService stateService, IPluginHost pluginHost, IConfigService configService)
    {
        _logger = logger;
        _stateService = stateService;
        _pluginHost = pluginHost;
        _configService = configService;
        var queueDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qobuz");
        Directory.CreateDirectory(queueDir);
        _queuesFilePath = Path.Combine(queueDir, "download-queues.json");
        _queues = new ConcurrentDictionary<string, DownloadQueue>();
        _queueProcessors = new ConcurrentDictionary<string, CancellationTokenSource>();
        
        // Auto-cleanup every 4 hours to prevent memory leaks in queue items
        _cleanupTimer = new Timer(_ => 
        {
            Task.Run(async () => 
            {
                try
                {
                    await PerformPeriodicCleanupAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in queue cleanup timer");
                }
            });
        }, null, TimeSpan.FromHours(1), TimeSpan.FromHours(CLEANUP_INTERVAL_HOURS)); // First cleanup after 1 hour
    }

    public async Task InitializeAsync()
    {
        try
        {
            if (File.Exists(_queuesFilePath))
            {
                var json = await File.ReadAllTextAsync(_queuesFilePath).ConfigureAwait(false);
                var loadedQueues = JsonConvert.DeserializeObject<List<DownloadQueue>>(json);
                
                if (loadedQueues != null)
                {
                    foreach (var queue in loadedQueues)
                    {
                        _queues[queue.Id] = queue;
                        
                        // Reset any downloading items to queued on startup
                        foreach (var item in queue.Items.Where(i => i.Status == QueueStatus.Downloading))
                        {
                            item.Status = QueueStatus.Queued;
                        }
                        
                        // Clean up stale queue items (older than 7 days)
                        var staleItems = queue.Items
                            .Where(i => i.Status == QueueStatus.Completed || i.Status == QueueStatus.Failed)
                            .Where(i => DateTime.UtcNow - i.AddedAt > TimeSpan.FromDays(7))
                            .ToList();
                            
                        if (staleItems.Any())
                        {
                            foreach (var staleItem in staleItems)
                            {
                                queue.Items.Remove(staleItem);
                            }
                            _logger.LogInformation("Cleaned up {Count} stale items from queue {QueueName}", 
                                staleItems.Count, queue.Name);
                        }
                    }
                    
                    _logger.LogInformation("Loaded {QueueCount} download queues", _queues.Count);
                }
            }
            else
            {
                // Create default queue
                await CreateQueueAsync("Default", 4).ConfigureAwait(false);
                _logger.LogInformation("Created default download queue");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize queue service");
            // Create default queue on error
            await CreateQueueAsync("Default", 4).ConfigureAwait(false);
        }
    }

    public async Task<DownloadQueue> CreateQueueAsync(string name, int maxConcurrent = 4)
    {
        var queue = new DownloadQueue
        {
            Name = name,
            MaxConcurrentDownloads = maxConcurrent
        };

        _queues[queue.Id] = queue;
        await SaveQueuesAsync().ConfigureAwait(false);
        
        _logger.LogInformation("Created queue '{Name}' with max concurrent: {MaxConcurrent}", name, maxConcurrent);
        return queue;
    }

    public List<DownloadQueue> GetQueues()
    {
        return _queues.Values.ToList();
    }

    public DownloadQueue? GetQueue(string queueId)
    {
        return _queues.TryGetValue(queueId, out var queue) ? queue : null;
    }

    public async Task<string> AddToQueueAsync(string queueId, QueuedDownload item)
    {
        if (!_queues.TryGetValue(queueId, out var queue))
        {
            throw new ArgumentException($"Queue {queueId} not found");
        }

        lock (_queuesLock)
        {
            item.Status = QueueStatus.Queued;
            queue.Items.Add(item);
        }

        await SaveQueuesAsync().ConfigureAwait(false);
        _logger.LogInformation("Added item {ItemId} to queue {QueueId}", item.Id, queueId);
        
        return item.Id;
    }

    public async Task<List<string>> AddBatchToQueueAsync(string queueId, List<QueuedDownload> items)
    {
        if (!_queues.TryGetValue(queueId, out var queue))
        {
            throw new ArgumentException($"Queue {queueId} not found");
        }

        var itemIds = new List<string>();
        
        lock (_queuesLock)
        {
            foreach (var item in items)
            {
                item.Status = QueueStatus.Queued;
                queue.Items.Add(item);
                itemIds.Add(item.Id);
            }
        }

        await SaveQueuesAsync().ConfigureAwait(false);
        _logger.LogDebug("Added {Count} items to queue {QueueId}", items.Count, queueId);
        
        return itemIds;
    }

    public async Task<bool> RemoveFromQueueAsync(string queueId, string itemId)
    {
        if (!_queues.TryGetValue(queueId, out var queue))
        {
            return false;
        }

        bool removed;
        lock (_queuesLock)
        {
            var item = queue.Items.FirstOrDefault(i => i.Id == itemId);
            if (item != null && item.Status != QueueStatus.Downloading)
            {
                removed = queue.Items.Remove(item);
            }
            else
            {
                removed = false;
            }
        }

        if (removed)
        {
            await SaveQueuesAsync().ConfigureAwait(false);
            _logger.LogInformation("Removed item {ItemId} from queue {QueueId}", itemId, queueId);
        }

        return removed;
    }

    public async Task<int> ClearCompletedAsync(string queueId)
    {
        if (!_queues.TryGetValue(queueId, out var queue))
        {
            return 0;
        }

        int removedCount;
        lock (_queuesLock)
        {
            lock (queue.Items)
            {
                var completedItems = queue.Items
                    .Where(i => i.Status == QueueStatus.Completed || i.Status == QueueStatus.Failed || i.Status == QueueStatus.Cancelled)
                    .ToList();
                
                foreach (var item in completedItems)
                {
                    queue.Items.Remove(item);
                }
                
                removedCount = completedItems.Count;
            }
        }

        if (removedCount > 0)
        {
            await SaveQueuesAsync().ConfigureAwait(false);
            _logger.LogInformation("Cleared {Count} completed items from queue {QueueId}", removedCount, queueId);
        }

        return removedCount;
    }

    public async Task<bool> SetPriorityAsync(string queueId, string itemId, int priority)
    {
        if (!_queues.TryGetValue(queueId, out var queue))
        {
            return false;
        }

        bool updated = false;
        lock (_queuesLock)
        {
            var item = queue.Items.FirstOrDefault(i => i.Id == itemId);
            if (item != null && item.Status == QueueStatus.Queued)
            {
                item.Priority = priority;
                updated = true;
            }
        }

        if (updated)
        {
            await SaveQueuesAsync().ConfigureAwait(false);
            _logger.LogInformation("Set priority {Priority} for item {ItemId} in queue {QueueId}", priority, itemId, queueId);
        }

        return updated;
    }

    public async Task<bool> SetQueuePausedAsync(string queueId, bool isPaused)
    {
        if (!_queues.TryGetValue(queueId, out var queue))
        {
            return false;
        }

        queue.IsPaused = isPaused;
        await SaveQueuesAsync().ConfigureAwait(false);

        if (isPaused)
        {
            // Stop processing if pausing
            if (_queueProcessors.TryRemove(queueId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
            _logger.LogInformation("Paused queue {QueueId}", queueId);
        }
        else
        {
            // Resume processing if unpausing
            await StartQueueProcessingAsync(queueId).ConfigureAwait(false);
            _logger.LogInformation("Resumed queue {QueueId}", queueId);
        }

        return true;
    }

    public Task StartQueueProcessingAsync(string queueId)
    {
        if (!_queues.TryGetValue(queueId, out var queue))
        {
            throw new ArgumentException($"Queue {queueId} not found");
        }

        if (queue.IsPaused)
        {
            _logger.LogWarning("Cannot start processing paused queue {QueueId}", queueId);
            return Task.CompletedTask;
        }

        // Check if already processing
        if (_queueProcessors.ContainsKey(queueId))
        {
            _logger.LogDebug("Queue {QueueId} is already being processed", queueId);
            return Task.CompletedTask;
        }

        var cts = new CancellationTokenSource();
        _queueProcessors[queueId] = cts;

        // Start processing in background with proper error handling
        Task.Run(async () => 
        {
            try
            {
                await ProcessQueueAsync(queueId, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Queue processing cancelled for {QueueId}", queueId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in queue processor for {QueueId}", queueId);
            }
        }, cts.Token);
        
        _logger.LogInformation("Started processing queue {QueueId}", queueId);
        return Task.CompletedTask;
    }

    public Task StopQueueProcessingAsync(string queueId)
    {
        if (_queueProcessors.TryRemove(queueId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _logger.LogInformation("Stopped processing queue {QueueId}", queueId);
        }

        return Task.CompletedTask;
    }

    public DownloadQueueStatistics GetQueueStatistics(string queueId)
    {
        if (!_queues.TryGetValue(queueId, out var queue))
        {
            return new DownloadQueueStatistics();
        }

        // Create thread-safe snapshot of items to avoid "Collection was modified" errors
        List<QueuedDownload> itemsSnapshot;
        lock (queue.Items)
        {
            itemsSnapshot = new List<QueuedDownload>(queue.Items);
        }

        var stats = new DownloadQueueStatistics
        {
            TotalItems = itemsSnapshot.Count,
            PendingItems = itemsSnapshot.Count(i => i.Status == QueueStatus.Pending || i.Status == QueueStatus.Queued),
            ActiveDownloads = itemsSnapshot.Count(i => i.Status == QueueStatus.Downloading),
            CompletedItems = itemsSnapshot.Count(i => i.Status == QueueStatus.Completed),
            FailedItems = itemsSnapshot.Count(i => i.Status == QueueStatus.Failed)
        };

        // Calculate estimated time based on current download speeds
        var activeDownloads = _stateService.GetActiveDownloads();
        if (activeDownloads.Any())
        {
            var avgSpeed = activeDownloads
                .Where(d => d.DownloadSpeed.HasValue && d.DownloadSpeed.Value > 0)
                .Select(d => d.DownloadSpeed!.Value)
                .DefaultIfEmpty(0)
                .Average();
            
            stats.AverageDownloadSpeed = avgSpeed;
            
            // Rough estimate: assume 50MB per track average
            var pendingTracks = stats.PendingItems * 10; // assume 10 tracks per item average
            var pendingBytes = pendingTracks * 50_000_000L; // 50MB per track
            
            if (avgSpeed > 0)
            {
                stats.EstimatedTimeRemaining = TimeSpan.FromSeconds(pendingBytes / avgSpeed);
            }
        }

        return stats;
    }

    public QueuedDownload? GetNextQueuedItem(string queueId)
    {
        if (!_queues.TryGetValue(queueId, out var queue))
        {
            return null;
        }

        lock (_queuesLock)
        {
            return queue.Items
                .Where(i => i.Status == QueueStatus.Queued)
                .OrderByDescending(i => i.Priority)
                .ThenBy(i => i.AddedAt)
                .FirstOrDefault();
        }
    }

    public async Task UpdateQueueItemStatusAsync(string queueId, string itemId, QueueStatus status)
    {
        if (!_queues.TryGetValue(queueId, out var queue))
        {
            return;
        }

        lock (_queuesLock)
        {
            var item = queue.Items.FirstOrDefault(i => i.Id == itemId);
            if (item != null)
            {
                item.Status = status;
                
                // If item is now completed/failed/cancelled, enforce size limits to prevent memory leaks
                if (status == QueueStatus.Completed || status == QueueStatus.Failed || status == QueueStatus.Cancelled)
                {
                    EnforceQueueSizeLimit(queue);
                }
            }
        }

        await SaveQueuesAsync().ConfigureAwait(false);
    }

    public async Task<int> RetryFailedItemsAsync(string queueId)
    {
        if (!_queues.TryGetValue(queueId, out var queue))
        {
            return 0;
        }

        int retryCount = 0;
        lock (_queuesLock)
        {
            var failedItems = queue.Items
                .Where(i => i.Status == QueueStatus.Failed && i.RetryAttempts < queue.RetryCount)
                .ToList();

            foreach (var item in failedItems)
            {
                item.Status = QueueStatus.Queued;
                item.RetryAttempts++;
                retryCount++;
            }
        }

        if (retryCount > 0)
        {
            await SaveQueuesAsync().ConfigureAwait(false);
            _logger.LogInformation("Queued {Count} failed items for retry in queue {QueueId}", retryCount, queueId);
        }

        return retryCount;
    }

    public async Task ExportQueueAsync(string queueId, string filePath)
    {
        if (!_queues.TryGetValue(queueId, out var queue))
        {
            throw new ArgumentException($"Queue {queueId} not found");
        }

        var json = JsonConvert.SerializeObject(queue, Formatting.Indented);
        await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
        
        _logger.LogInformation("Exported queue {QueueId} to {FilePath}", queueId, filePath);
    }

    public async Task<DownloadQueue> ImportQueueAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Queue file not found: {filePath}");
        }

        var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        var importedQueue = JsonConvert.DeserializeObject<DownloadQueue>(json);
        
        if (importedQueue == null)
        {
            throw new InvalidOperationException("Failed to deserialize queue file");
        }

        // Generate new ID to avoid conflicts
        importedQueue.Id = Guid.NewGuid().ToString("N");
        importedQueue.Name = $"{importedQueue.Name} (Imported)";
        
        _queues[importedQueue.Id] = importedQueue;
        await SaveQueuesAsync().ConfigureAwait(false);
        
        _logger.LogInformation("Imported queue from {FilePath} as {QueueId}", filePath, importedQueue.Id);
        return importedQueue;
    }

    private async Task ProcessQueueAsync(string queueId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting queue processor for {QueueId}", queueId);

        while (!cancellationToken.IsCancellationRequested && _queues.TryGetValue(queueId, out var queue))
        {
            try
            {
                if (queue.IsPaused)
                {
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Check current active downloads
                var activeCount = queue.Items.Count(i => i.Status == QueueStatus.Downloading);
                if (activeCount >= queue.MaxConcurrentDownloads)
                {
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Get next item to process
                var nextItem = GetNextQueuedItem(queueId);
                if (nextItem == null)
                {
                    // No more items to process
                    if (!queue.Items.Any(i => i.Status == QueueStatus.Downloading))
                    {
                        _logger.LogDebug("Queue {QueueId} processing complete", queueId);
                        break;
                    }

                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Process the item
                await ProcessQueueItemAsync(queueId, nextItem, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queue {QueueId}", queueId);
                await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
            }
        }

        _queueProcessors.TryRemove(queueId, out _);
        _logger.LogDebug("Queue processor for {QueueId} stopped", queueId);
    }

    private async Task ProcessQueueItemAsync(string queueId, QueuedDownload item, CancellationToken cancellationToken)
    {
        try
        {
            await UpdateQueueItemStatusAsync(queueId, item.Id, QueueStatus.Downloading).ConfigureAwait(false);
            
            _logger.LogDebug("🎵 Starting download: {Query}", item.SearchQuery);
            
            // Extract metadata
            var title = item.Metadata?.GetValueOrDefault("title") ?? item.SearchQuery;
            var artist = item.Metadata?.GetValueOrDefault("artist") ?? "Unknown";
            var quality = item.Metadata?.GetValueOrDefault("qualityPreference") ?? "flac-max";
            var outputDir = item.Metadata?.GetValueOrDefault("outputDirectory") ?? "./Downloads";
            var qobuzId = item.Metadata?.GetValueOrDefault("qobuzId") ?? "";
            var trackCountStr = item.Metadata?.GetValueOrDefault("trackCount") ?? "1";
            int.TryParse(trackCountStr, out var trackCount);
            trackCount = Math.Max(1, trackCount);
            
            // Create download item for state tracking
            var downloadItem = new DownloadItem
            {
                Query = item.SearchQuery,
                Title = title,
                Artist = artist,
                Type = item.SearchType.ToString().ToLower(),
                Quality = quality,
                TotalTracks = trackCount,
                QobuzId = qobuzId
            };

            var downloadId = await _stateService.StartDownloadAsync(downloadItem).ConfigureAwait(false);
            
            try
            {
                // Initialize plugin if needed
                var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
                if (!_pluginHost.IsInitialized)
                {
                    await _pluginHost.InitializeAsync(config).ConfigureAwait(false);
                }
                
                // Perform the actual download
                var downloadResult = await DownloadItemAsync(downloadId, title, artist, qobuzId, quality, outputDir, trackCount, item.SearchType, cancellationToken).ConfigureAwait(false);
                
                if (downloadResult.IsSuccessful)
                {
                    await _stateService.CompleteDownloadAsync(downloadId, outputDir, downloadResult.TrackDownloads?.Count ?? 0).ConfigureAwait(false);
                    await UpdateQueueItemStatusAsync(queueId, item.Id, QueueStatus.Completed).ConfigureAwait(false);
                }
                else
                {
                    await _stateService.FailDownloadAsync(downloadId, downloadResult.Message ?? "Download failed").ConfigureAwait(false);
                    await UpdateQueueItemStatusAsync(queueId, item.Id, QueueStatus.Failed).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                await _stateService.FailDownloadAsync(downloadId, ex.Message).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process queue item {ItemId}", item.Id);
            await UpdateQueueItemStatusAsync(queueId, item.Id, QueueStatus.Failed).ConfigureAwait(false);
        }
    }

    private async Task SaveQueuesAsync()
    {
        try
        {
            string json;
            lock (_queuesLock)
            {
                var queues = _queues.Values.ToList();
                json = JsonConvert.SerializeObject(queues, Formatting.Indented);
            }
            await File.WriteAllTextAsync(_queuesFilePath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save queues");
        }
    }

    /// <summary>
    /// Enforces size limits on queue items to prevent memory leaks
    /// </summary>
    private void EnforceQueueSizeLimit(DownloadQueue queue)
    {
        var completedItems = queue.Items
            .Where(i => i.Status == QueueStatus.Completed || i.Status == QueueStatus.Failed || i.Status == QueueStatus.Cancelled)
            .OrderBy(i => i.AddedAt)
            .ToList();

        if (completedItems.Count > MAX_COMPLETED_ITEMS_PER_QUEUE)
        {
            var itemsToRemove = completedItems.Take(completedItems.Count - MAX_COMPLETED_ITEMS_PER_QUEUE).ToList();

            foreach (var item in itemsToRemove)
            {
                queue.Items.Remove(item);
            }

            _logger.LogInformation("Enforced queue size limit in '{QueueName}': removed {Count} old completed items, keeping {Remaining}", 
                queue.Name, itemsToRemove.Count, completedItems.Count - itemsToRemove.Count);
        }
    }

    /// <summary>
    /// Performs periodic cleanup of old completed items to prevent memory leaks
    /// </summary>
    private async Task PerformPeriodicCleanupAsync()
    {
        var totalRemoved = 0;
        var cutoffDate = DateTime.UtcNow.AddDays(-CLEANUP_KEEP_DAYS);

        lock (_queuesLock)
        {
            foreach (var queuePair in _queues)
            {
                var queue = queuePair.Value;
                var initialCount = queue.Items.Count;

                // Remove old completed items
                var itemsToRemove = queue.Items
                    .Where(i => (i.Status == QueueStatus.Completed || i.Status == QueueStatus.Failed || i.Status == QueueStatus.Cancelled) &&
                               i.AddedAt < cutoffDate)
                    .ToList();

                foreach (var item in itemsToRemove)
                {
                    queue.Items.Remove(item);
                }

                // Also enforce absolute size limit
                EnforceQueueSizeLimit(queue);

                var removedFromQueue = initialCount - queue.Items.Count;
                totalRemoved += removedFromQueue;

                if (removedFromQueue > 0)
                {
                    _logger.LogDebug("Cleaned up {Count} old items from queue '{QueueName}'", removedFromQueue, queue.Name);
                }
            }
        }

        if (totalRemoved > 0)
        {
            _logger.LogInformation("Periodic queue cleanup: removed {Count} old completed items ({Days} days+) across all queues", 
                totalRemoved, CLEANUP_KEEP_DAYS);
            await SaveQueuesAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        // Stop all queue processors
        foreach (var processor in _queueProcessors)
        {
            processor.Value.Cancel();
            processor.Value.Dispose();
        }
        _queueProcessors.Clear();

        // Dispose cleanup timer
        _cleanupTimer?.Dispose();

        // Save final state - using GetAwaiter().GetResult() is safer than .Wait() in dispose
        try
        {
            SaveQueuesAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save queues during disposal");
        }
        
        _isDisposed = true;
    }
    
    private async Task<CliDownloadResult> DownloadItemAsync(
        string downloadId, 
        string title, 
        string artist, 
        string qobuzId, 
        string quality, 
        string outputDir, 
        int trackCount,
        SearchType searchType,
        CancellationToken cancellationToken)
    {
        try
        {
            // Use real Qobuz service for download
            _logger.LogDebug("📥 Downloading album: {Artist} - {Title}", artist, title);
            
            // Create output directory structure
            var artistDir = Path.Combine(outputDir, Lidarr.Plugin.Common.Utilities.FileSystemUtilities.SanitizeFileName(artist));
            var albumDir = Path.Combine(artistDir, Lidarr.Plugin.Common.Utilities.FileSystemUtilities.SanitizeFileName(title));
            
            Directory.CreateDirectory(albumDir);
            
            // Call the real download service - check if it's an artist
            CliDownloadResult downloadResult;
            var itemType = searchType.ToString().ToLower();
            
            if (itemType == "artist")
            {
                _logger.LogDebug("Downloading artist discography");
                downloadResult = await _pluginHost.DownloadArtistAsync(qobuzId, artistDir).ConfigureAwait(false);
            }
            else
            {
                downloadResult = await _pluginHost.DownloadAlbumAsync(qobuzId, albumDir, null).ConfigureAwait(false);
            }
            
            if (downloadResult.IsSuccessful)
            {
                // Update progress to 100% on success
                await _stateService.UpdateDownloadProgressAsync(downloadId, 100, 
                    $"Download completed - {downloadResult.Message}").ConfigureAwait(false);
            }
            
            return downloadResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed for {Title} by {Artist}", title, artist);
            return new CliDownloadResult
            {
                Success = false,
                Message = ex.Message,
                TrackDownloads = new List<TrackDownloadInfo>(),
                MetadataStrategy = "Failed",
                ApiCallsSaved = 0,
                AdditionalApiCalls = 0,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };
        }
    }
    
}
