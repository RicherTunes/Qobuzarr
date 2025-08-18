using Newtonsoft.Json;

namespace QobuzCLI.Models;

public class DownloadQueue
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    
    [JsonProperty("name")]
    public string Name { get; set; } = "Default Queue";
    
    [JsonProperty("items")]
    public List<QueuedDownload> Items { get; set; } = new();
    
    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [JsonProperty("maxConcurrent")]
    public int MaxConcurrentDownloads { get; set; } = 4;
    
    [JsonProperty("isPaused")]
    public bool IsPaused { get; set; }
    
    [JsonProperty("autoRetry")]
    public bool AutoRetry { get; set; } = true;
    
    [JsonProperty("retryCount")]
    public int RetryCount { get; set; } = 3;
    
    [JsonProperty("retryDelaySeconds")]
    public int RetryDelaySeconds { get; set; } = 5;
}

public class QueuedDownload
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    
    [JsonProperty("searchQuery")]
    public string SearchQuery { get; set; } = string.Empty;
    
    [JsonProperty("searchType")]
    public SearchType SearchType { get; set; }
    
    [JsonProperty("searchResultId")]
    public string? SearchResultId { get; set; }
    
    [JsonProperty("priority")]
    public int Priority { get; set; } = 0;
    
    [JsonProperty("status")]
    public QueueStatus Status { get; set; } = QueueStatus.Pending;
    
    [JsonProperty("addedAt")]
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    
    [JsonProperty("downloadId")]
    public string? DownloadId { get; set; }
    
    [JsonProperty("retryAttempts")]
    public int RetryAttempts { get; set; }
    
    [JsonProperty("outputPath")]
    public string? OutputPath { get; set; }
    
    [JsonProperty("quality")]
    public string? Quality { get; set; }
    
    [JsonProperty("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    [JsonProperty("completedAt")]
    public DateTime? CompletedAt { get; set; }
}

public enum QueueStatus
{
    Pending,
    Searching,
    WaitingForSelection,
    Queued,
    Downloading,
    Completed,
    Failed,
    Cancelled,
    Retrying
}

public class DownloadQueueStatistics
{
    public int TotalItems { get; set; }
    public int PendingItems { get; set; }
    public int ActiveDownloads { get; set; }
    public int CompletedItems { get; set; }
    public int FailedItems { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    public double AverageDownloadSpeed { get; set; }
}