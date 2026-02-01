using QobuzCLI.Models;

namespace QobuzCLI.Services;

public interface IQueueService : IDisposable
{
    /// <summary>
    /// Initialize queue service
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Create a new download queue
    /// </summary>
    Task<DownloadQueue> CreateQueueAsync(string name, int maxConcurrent = 4);

    /// <summary>
    /// Get all queues
    /// </summary>
    List<DownloadQueue> GetQueues();

    /// <summary>
    /// Get specific queue
    /// </summary>
    DownloadQueue? GetQueue(string queueId);

    /// <summary>
    /// Add item to queue
    /// </summary>
    Task<string> AddToQueueAsync(string queueId, QueuedDownload item);

    /// <summary>
    /// Add multiple items to queue
    /// </summary>
    Task<List<string>> AddBatchToQueueAsync(string queueId, List<QueuedDownload> items);

    /// <summary>
    /// Remove item from queue
    /// </summary>
    Task<bool> RemoveFromQueueAsync(string queueId, string itemId);

    /// <summary>
    /// Clear completed items from queue
    /// </summary>
    Task<int> ClearCompletedAsync(string queueId);

    /// <summary>
    /// Move item priority in queue
    /// </summary>
    Task<bool> SetPriorityAsync(string queueId, string itemId, int priority);

    /// <summary>
    /// Pause/resume queue
    /// </summary>
    Task<bool> SetQueuePausedAsync(string queueId, bool isPaused);

    /// <summary>
    /// Start queue processing
    /// </summary>
    Task StartQueueProcessingAsync(string queueId);

    /// <summary>
    /// Stop queue processing
    /// </summary>
    Task StopQueueProcessingAsync(string queueId);

    /// <summary>
    /// Get queue statistics
    /// </summary>
    DownloadQueueStatistics GetQueueStatistics(string queueId);

    /// <summary>
    /// Get next item to download
    /// </summary>
    QueuedDownload? GetNextQueuedItem(string queueId);

    /// <summary>
    /// Update queue item status
    /// </summary>
    Task UpdateQueueItemStatusAsync(string queueId, string itemId, QueueStatus status);

    /// <summary>
    /// Retry failed items
    /// </summary>
    Task<int> RetryFailedItemsAsync(string queueId);

    /// <summary>
    /// Export queue to file
    /// </summary>
    Task ExportQueueAsync(string queueId, string filePath);

    /// <summary>
    /// Import queue from file
    /// </summary>
    Task<DownloadQueue> ImportQueueAsync(string filePath);
}
