using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using QobuzCLI.Models;
using QobuzCLI.Services.Adapters;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Lidarr.Plugin.Qobuzarr.Services;

namespace QobuzCLI.Services
{
    /// <summary>
    /// Improved queue service with concurrent download processing and better performance
    /// </summary>
    public class ImprovedQueueService : IQueueService
    {
        private readonly ILogger<ImprovedQueueService> _logger;
        private readonly IStateService _stateService;
        private readonly IPluginHost _pluginHost;
        private readonly IConfigService _configService;
        private readonly IAdaptiveRateLimiter _rateLimiter;
        
        private readonly string _queuesFilePath;
        private readonly ConcurrentDictionary<string, DownloadQueue> _queues;
        private readonly ConcurrentDictionary<string, QueueProcessor> _processors;
        private readonly object _saveLock = new();
        
        // Performance tracking
        private int _totalDownloads;
        private int _successfulDownloads;
        private int _failedDownloads;
        private DateTime _startTime = DateTime.UtcNow;

        public ImprovedQueueService(
            ILogger<ImprovedQueueService> logger,
            IStateService stateService,
            IPluginHost pluginHost,
            IConfigService configService,
            IAdaptiveRateLimiter rateLimiter)
        {
            _logger = logger;
            _stateService = stateService;
            _pluginHost = pluginHost;
            _configService = configService;
            _rateLimiter = rateLimiter;
            
            var queueDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qobuz");
            Directory.CreateDirectory(queueDir);
            _queuesFilePath = Path.Combine(queueDir, "download-queues.json");
            _queues = new ConcurrentDictionary<string, DownloadQueue>();
            _processors = new ConcurrentDictionary<string, QueueProcessor>();
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
                            foreach (var item in queue.Items.Where(i => i.Status == Models.QueueStatus.Downloading))
                            {
                                item.Status = Models.QueueStatus.Queued;
                            }
                        }
                        
                        _logger.LogInformation("Loaded {QueueCount} download queues with {TotalItems} items",
                            _queues.Count, _queues.Values.Sum(q => q.Items.Count));
                    }
                }
                else
                {
                    // Create default queue with optimized settings
                    await CreateQueueAsync("Default", 4).ConfigureAwait(false);
                    _logger.LogInformation("Created default download queue");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize queue service");
                await CreateQueueAsync("Default", 4).ConfigureAwait(false);
            }
        }

        public async Task<DownloadQueue> CreateQueueAsync(string name, int maxConcurrent = 4)
        {
            var queue = new DownloadQueue
            {
                Name = name,
                MaxConcurrentDownloads = maxConcurrent,
                RetryCount = 3,
                RetryDelaySeconds = 5
            };

            _queues[queue.Id] = queue;
            await SaveQueuesAsync().ConfigureAwait(false);
            
            _logger.LogInformation("Created queue '{Name}' with max concurrent: {MaxConcurrent}", name, maxConcurrent);
            return queue;
        }

        public List<DownloadQueue> GetQueues() => _queues.Values.ToList();

        public DownloadQueue? GetQueue(string queueId) => 
            _queues.TryGetValue(queueId, out var queue) ? queue : null;

        public async Task<string> AddToQueueAsync(string queueId, QueuedDownload item)
        {
            if (!_queues.TryGetValue(queueId, out var queue))
            {
                throw new ArgumentException($"Queue {queueId} not found");
            }

            item.Status = Models.QueueStatus.Queued;
            item.AddedAt = DateTime.UtcNow;
            
            queue.Items.Add(item);
            await SaveQueuesAsync().ConfigureAwait(false);
            
            _logger.LogDebug("Added item {ItemId} to queue {QueueId}", item.Id, queueId);
            
            // Auto-start processing if not already running
            if (!_processors.ContainsKey(queueId))
            {
                await StartQueueProcessingAsync(queueId).ConfigureAwait(false);
            }
            
            return item.Id;
        }

        public async Task<List<string>> AddBatchToQueueAsync(string queueId, List<QueuedDownload> items)
        {
            if (!_queues.TryGetValue(queueId, out var queue))
            {
                throw new ArgumentException($"Queue {queueId} not found");
            }

            var itemIds = new List<string>();
            var now = DateTime.UtcNow;
            
            foreach (var item in items)
            {
                item.Status = Models.QueueStatus.Queued;
                item.AddedAt = now;
                queue.Items.Add(item);
                itemIds.Add(item.Id);
            }

            await SaveQueuesAsync().ConfigureAwait(false);
            _logger.LogInformation("Added {Count} items to queue {QueueId}", items.Count, queueId);
            
            // Auto-start processing
            if (!_processors.ContainsKey(queueId))
            {
                await StartQueueProcessingAsync(queueId).ConfigureAwait(false);
            }
            
            return itemIds;
        }

        public async Task StartQueueProcessingAsync(string queueId)
        {
            if (!_queues.TryGetValue(queueId, out var queue))
            {
                throw new ArgumentException($"Queue {queueId} not found");
            }

            if (queue.IsPaused)
            {
                _logger.LogWarning("Cannot start processing paused queue {QueueId}", queueId);
                return;
            }

            // Check if already processing
            if (_processors.ContainsKey(queueId))
            {
                _logger.LogDebug("Queue {QueueId} is already being processed", queueId);
                return;
            }

            var processor = new QueueProcessor(this, queue, _logger);
            _processors[queueId] = processor;
            
            // Start processing in background with proper error handling
            _ = Task.Run(async () => 
            {
                try
                {
                    await processor.ProcessAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in queue processor for {QueueId}", queueId);
                    // Clean up the processor on error
                    _processors.TryRemove(queueId, out _);
                }
            });
            
            _logger.LogInformation("Started processing queue {QueueId} with {Count} items", 
                queueId, queue.Items.Count(i => i.Status == Models.QueueStatus.Queued));
        }

        public Task StopQueueProcessingAsync(string queueId)
        {
            if (_processors.TryRemove(queueId, out var processor))
            {
                processor.Cancel();
                _logger.LogInformation("Stopped processing queue {QueueId}", queueId);
            }
            return Task.CompletedTask;
        }

        public async Task<bool> SetQueuePausedAsync(string queueId, bool isPaused)
        {
            if (!_queues.TryGetValue(queueId, out var queue))
                return false;

            queue.IsPaused = isPaused;
            await SaveQueuesAsync().ConfigureAwait(false);

            if (isPaused)
            {
                await StopQueueProcessingAsync(queueId).ConfigureAwait(false);
            }
            else
            {
                await StartQueueProcessingAsync(queueId).ConfigureAwait(false);
            }

            return true;
        }

        public DownloadQueueStatistics GetQueueStatistics(string queueId)
        {
            if (!_queues.TryGetValue(queueId, out var queue))
                return new DownloadQueueStatistics();

            var stats = new DownloadQueueStatistics
            {
                TotalItems = queue.Items.Count,
                PendingItems = queue.Items.Count(i => i.Status == Models.QueueStatus.Queued || i.Status == Models.QueueStatus.Pending),
                ActiveDownloads = queue.Items.Count(i => i.Status == Models.QueueStatus.Downloading),
                CompletedItems = queue.Items.Count(i => i.Status == Models.QueueStatus.Completed),
                FailedItems = queue.Items.Count(i => i.Status == Models.QueueStatus.Failed)
            };

            // Calculate estimated time based on current performance
            if (_totalDownloads > 0)
            {
                var elapsed = DateTime.UtcNow - _startTime;
                var avgTimePerDownload = elapsed.TotalSeconds / _totalDownloads;
                stats.EstimatedTimeRemaining = TimeSpan.FromSeconds(stats.PendingItems * avgTimePerDownload);
                stats.AverageDownloadSpeed = _totalDownloads / elapsed.TotalMinutes;
            }

            return stats;
        }

        public async Task UpdateQueueItemStatusAsync(string queueId, string itemId, Models.QueueStatus status)
        {
            if (!_queues.TryGetValue(queueId, out var queue))
                return;

            var item = queue.Items.FirstOrDefault(i => i.Id == itemId);
            if (item != null)
            {
                item.Status = status;
                
                if (status == Models.QueueStatus.Completed)
                {
                    Interlocked.Increment(ref _successfulDownloads);
                    Interlocked.Increment(ref _totalDownloads);
                }
                else if (status == Models.QueueStatus.Failed)
                {
                    Interlocked.Increment(ref _failedDownloads);
                    Interlocked.Increment(ref _totalDownloads);
                }
                
                await SaveQueuesAsync().ConfigureAwait(false);
            }
        }

        public async Task<int> RetryFailedItemsAsync(string queueId)
        {
            if (!_queues.TryGetValue(queueId, out var queue))
                return 0;

            var retryCount = 0;
            var failedItems = queue.Items
                .Where(i => i.Status == Models.QueueStatus.Failed && i.RetryAttempts < queue.RetryCount)
                .ToList();

            foreach (var item in failedItems)
            {
                item.Status = Models.QueueStatus.Queued;
                item.RetryAttempts++;
                retryCount++;
            }

            if (retryCount > 0)
            {
                await SaveQueuesAsync().ConfigureAwait(false);
                _logger.LogInformation("Queued {Count} failed items for retry in queue {QueueId}", retryCount, queueId);
                
                // Restart processing if needed
                if (!_processors.ContainsKey(queueId))
                {
                    await StartQueueProcessingAsync(queueId).ConfigureAwait(false);
                }
            }

            return retryCount;
        }

        private async Task SaveQueuesAsync()
        {
            try
            {
                lock (_saveLock)
                {
                    var json = JsonConvert.SerializeObject(_queues.Values.ToList(), Formatting.Indented);
                    File.WriteAllText(_queuesFilePath, json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save queues");
            }
        }

        // Inner class for queue processing
        private class QueueProcessor
        {
            private readonly ImprovedQueueService _service;
            private readonly DownloadQueue _queue;
            private readonly ILogger _logger;
            private readonly CancellationTokenSource _cts;
            private readonly SemaphoreSlim _downloadSemaphore;

            public QueueProcessor(ImprovedQueueService service, DownloadQueue queue, ILogger logger)
            {
                _service = service;
                _queue = queue;
                _logger = logger;
                _cts = new CancellationTokenSource();
                _downloadSemaphore = new SemaphoreSlim(queue.MaxConcurrentDownloads);
            }

            public void Cancel() => _cts.Cancel();

            public async Task ProcessAsync()
            {
                _logger.LogInformation("Queue processor started for {QueueId} with {MaxConcurrent} concurrent downloads",
                    _queue.Id, _queue.MaxConcurrentDownloads);

                var tasks = new List<Task>();
                
                try
                {
                    while (!_cts.Token.IsCancellationRequested && !_queue.IsPaused)
                    {
                        // Clean up completed tasks
                        tasks.RemoveAll(t => t.IsCompleted);

                        // Get next items to process
                        var pendingItems = _queue.Items
                            .Where(i => i.Status == Models.QueueStatus.Queued)
                            .OrderByDescending(i => i.Priority)
                            .ThenBy(i => i.AddedAt)
                            .Take(_queue.MaxConcurrentDownloads - tasks.Count)
                            .ToList();

                        if (!pendingItems.Any())
                        {
                            if (!tasks.Any())
                            {
                                // No more work
                                _logger.LogInformation("Queue {QueueId} processing complete", _queue.Id);
                                break;
                            }

                            // Wait for some tasks to complete
                            await Task.Delay(1000, _cts.Token).ConfigureAwait(false);
                            continue;
                        }

                        // Start processing items
                        foreach (var item in pendingItems)
                        {
                            var task = ProcessItemAsync(item, _cts.Token);
                            tasks.Add(task);
                        }

                        // Wait a bit before checking again
                        await Task.Delay(500, _cts.Token).ConfigureAwait(false);
                    }

                    // Wait for remaining tasks
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Queue processor cancelled for {QueueId}", _queue.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Queue processor error for {QueueId}", _queue.Id);
                }
                finally
                {
                    _service._processors.TryRemove(_queue.Id, out _);
                    _downloadSemaphore.Dispose();
                }
            }

            private async Task ProcessItemAsync(QueuedDownload item, CancellationToken ct)
            {
                await _downloadSemaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await _service.UpdateQueueItemStatusAsync(_queue.Id, item.Id, Models.QueueStatus.Downloading).ConfigureAwait(false);
                    
                    _logger.LogInformation("🎵 Downloading: {Query}", item.SearchQuery);
                    
                    // Extract metadata
                    var title = item.Metadata?.GetValueOrDefault("title") ?? item.SearchQuery;
                    var artist = item.Metadata?.GetValueOrDefault("artist") ?? "Unknown";
                    var quality = item.Metadata?.GetValueOrDefault("qualityPreference") ?? "flac-max";
                    var outputDir = item.Metadata?.GetValueOrDefault("outputDirectory") ?? "./Downloads";
                    var qobuzId = item.Metadata?.GetValueOrDefault("qobuzId") ?? "";
                    
                    // Create download tracking
                    var downloadItem = new DownloadItem
                    {
                        Query = item.SearchQuery,
                        Title = title,
                        Artist = artist,
                        Type = item.SearchType.ToString().ToLower(),
                        Quality = quality,
                        QobuzId = qobuzId
                    };

                    var downloadId = await _service._stateService.StartDownloadAsync(downloadItem).ConfigureAwait(false);
                    
                    try
                    {
                        // Apply rate limiting for download
                        await _service._rateLimiter.WaitIfNeededAsync("download", ct).ConfigureAwait(false);
                        
                        // Perform download with retry
                        var success = await DownloadWithRetryAsync(item, downloadId, ct).ConfigureAwait(false);
                        
                        if (success)
                        {
                            await _service.UpdateQueueItemStatusAsync(_queue.Id, item.Id, Models.QueueStatus.Completed).ConfigureAwait(false);
                            _logger.LogInformation("✅ Completed: {Query}", item.SearchQuery);
                        }
                        else
                        {
                            await _service.UpdateQueueItemStatusAsync(_queue.Id, item.Id, Models.QueueStatus.Failed).ConfigureAwait(false);
                            _logger.LogWarning("❌ Failed: {Query}", item.SearchQuery);
                        }
                    }
                    catch (Exception ex)
                    {
                        await _service._stateService.FailDownloadAsync(downloadId, ex.Message).ConfigureAwait(false);
                        await _service.UpdateQueueItemStatusAsync(_queue.Id, item.Id, Models.QueueStatus.Failed).ConfigureAwait(false);
                        _logger.LogError(ex, "Download failed for {Query}", item.SearchQuery);
                    }
                }
                finally
                {
                    _downloadSemaphore.Release();
                }
            }

            private async Task<bool> DownloadWithRetryAsync(QueuedDownload item, string downloadId, CancellationToken ct)
            {
                var retryCount = 0;
                var maxRetries = _queue.RetryCount;
                
                while (retryCount <= maxRetries)
                {
                    try
                    {
                        // Initialize plugin if needed
                        var config = await _service._configService.LoadConfigAsync().ConfigureAwait(false);
                        if (!_service._pluginHost.IsInitialized)
                        {
                            await _service._pluginHost.InitializeAsync(config).ConfigureAwait(false);
                        }
                        
                        // Perform download
                        var qobuzId = item.Metadata?.GetValueOrDefault("qobuzId") ?? "";
                        var outputDir = item.Metadata?.GetValueOrDefault("outputDirectory") ?? "./Downloads";
                        var artist = item.Metadata?.GetValueOrDefault("artist") ?? "Unknown";
                        var title = item.Metadata?.GetValueOrDefault("title") ?? item.SearchQuery;
                        
                        var artistDir = Path.Combine(outputDir, FileSystemUtilities.SanitizeFileName(artist));
                        var albumDir = Path.Combine(artistDir, FileSystemUtilities.SanitizeFileName(title));
                        Directory.CreateDirectory(albumDir);
                        
                        var result = await _service._pluginHost.DownloadAlbumAsync(qobuzId, albumDir).ConfigureAwait(false);
                        
                        if (result.IsSuccessful())
                        {
                            await _service._stateService.CompleteDownloadAsync(downloadId, albumDir, result.GetTracksDownloaded()).ConfigureAwait(false);
                            return true;
                        }
                        
                        throw new Exception(result.GetSummaryMessage());
                    }
                    catch (Exception ex) when (retryCount < maxRetries && !ct.IsCancellationRequested)
                    {
                        retryCount++;
                        var delay = TimeSpan.FromSeconds(_queue.RetryDelaySeconds * Math.Pow(2, retryCount - 1));
                        _logger.LogWarning("Retry {Retry}/{Max} for {Query} after {Delay}s: {Error}",
                            retryCount, maxRetries, item.SearchQuery, delay.TotalSeconds, ex.Message);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                }
                
                return false;
            }
        }

        // IQueueService implementation methods
        public async Task<bool> RemoveFromQueueAsync(string queueId, string itemId)
        {
            if (!_queues.TryGetValue(queueId, out var queue))
                return false;

            var item = queue.Items.FirstOrDefault(i => i.Id == itemId);
            if (item != null && item.Status != Models.QueueStatus.Downloading)
            {
                queue.Items.Remove(item);
                await SaveQueuesAsync().ConfigureAwait(false);
                return true;
            }
            
            return false;
        }

        public async Task<int> ClearCompletedAsync(string queueId)
        {
            if (!_queues.TryGetValue(queueId, out var queue))
                return 0;

            var completed = queue.Items
                .Where(i => i.Status == Models.QueueStatus.Completed || 
                           i.Status == Models.QueueStatus.Failed || 
                           i.Status == Models.QueueStatus.Cancelled)
                .ToList();

            foreach (var item in completed)
            {
                queue.Items.Remove(item);
            }

            if (completed.Any())
            {
                await SaveQueuesAsync().ConfigureAwait(false);
            }

            return completed.Count;
        }

        public async Task<bool> SetPriorityAsync(string queueId, string itemId, int priority)
        {
            if (!_queues.TryGetValue(queueId, out var queue))
                return false;

            var item = queue.Items.FirstOrDefault(i => i.Id == itemId);
            if (item != null && item.Status == Models.QueueStatus.Queued)
            {
                item.Priority = priority;
                await SaveQueuesAsync().ConfigureAwait(false);
                return true;
            }

            return false;
        }

        public QueuedDownload? GetNextQueuedItem(string queueId)
        {
            if (!_queues.TryGetValue(queueId, out var queue))
                return null;

            return queue.Items
                .Where(i => i.Status == Models.QueueStatus.Queued)
                .OrderByDescending(i => i.Priority)
                .ThenBy(i => i.AddedAt)
                .FirstOrDefault();
        }

        public async Task ExportQueueAsync(string queueId, string filePath)
        {
            if (!_queues.TryGetValue(queueId, out var queue))
                throw new ArgumentException($"Queue {queueId} not found");

            var json = JsonConvert.SerializeObject(queue, Formatting.Indented);
            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
        }

        public async Task<DownloadQueue> ImportQueueAsync(string filePath)
        {
            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var queue = JsonConvert.DeserializeObject<DownloadQueue>(json)
                ?? throw new InvalidOperationException("Failed to deserialize queue");

            queue.Id = Guid.NewGuid().ToString("N");
            queue.Name = $"{queue.Name} (Imported)";
            
            _queues[queue.Id] = queue;
            await SaveQueuesAsync().ConfigureAwait(false);
            
            return queue;
        }

        public void Dispose()
        {
            // Stop all processors
            foreach (var processor in _processors.Values)
            {
                processor.Cancel();
            }
            _processors.Clear();

            // Log final statistics
            var elapsed = DateTime.UtcNow - _startTime;
            _logger.LogInformation("Queue Service Statistics:");
            _logger.LogInformation("  Total downloads: {Total}", _totalDownloads);
            _logger.LogInformation("  Successful: {Success}", _successfulDownloads);
            _logger.LogInformation("  Failed: {Failed}", _failedDownloads);
            _logger.LogInformation("  Success rate: {Rate:P}", 
                _totalDownloads > 0 ? (double)_successfulDownloads / _totalDownloads : 0);
            _logger.LogInformation("  Average rate: {Rate:F1} downloads/minute",
                _totalDownloads > 0 ? _totalDownloads / elapsed.TotalMinutes : 0);

            // Save final state - using GetAwaiter().GetResult() is safer than .Wait() in dispose
            try
            {
                SaveQueuesAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save queues during disposal");
            }
        }
    }
}