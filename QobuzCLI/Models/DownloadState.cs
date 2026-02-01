using Newtonsoft.Json;

namespace QobuzCLI.Models;

public class DownloadState
{
    [JsonProperty("version")]
    public string Version { get; set; } = "1.0";

    [JsonProperty("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    [JsonProperty("activeDownloads")]
    public List<DownloadItem> ActiveDownloads { get; set; } = new();

    [JsonProperty("downloadHistory")]
    public List<DownloadHistoryItem> DownloadHistory { get; set; } = new();

    [JsonProperty("statistics")]
    public DownloadStatistics Statistics { get; set; } = new();
}

public class DownloadItem
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty; // album, artist, track

    [JsonProperty("qobuzId")]
    public string QobuzId { get; set; } = string.Empty;

    [JsonProperty("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("totalTracks")]
    public int TotalTracks { get; set; }

    [JsonProperty("completedTracks")]
    public int CompletedTracks { get; set; }

    [JsonProperty("progress")]
    public double Progress { get; set; }

    [JsonProperty("status")]
    public DownloadStatus Status { get; set; } = DownloadStatus.Pending;

    [JsonProperty("startedAt")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    [JsonProperty("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [JsonProperty("outputPath")]
    public string? OutputPath { get; set; }

    [JsonProperty("quality")]
    public string Quality { get; set; } = string.Empty;

    [JsonProperty("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonProperty("query")]
    public string Query { get; set; } = string.Empty;

    [JsonProperty("currentFile")]
    public string? CurrentFile { get; set; }

    [JsonProperty("totalBytes")]
    public long? TotalBytes { get; set; }

    [JsonProperty("downloadedBytes")]
    public long? DownloadedBytes { get; set; }

    [JsonProperty("downloadSpeed")]
    public double? DownloadSpeed { get; set; }
}

public class DownloadHistoryItem
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("qobuzId")]
    public string QobuzId { get; set; } = string.Empty;

    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("downloadedAt")]
    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;

    [JsonProperty("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [JsonProperty("location")]
    public string Location { get; set; } = string.Empty;

    [JsonProperty("quality")]
    public string Quality { get; set; } = string.Empty;

    [JsonProperty("finalStatus")]
    public DownloadStatus FinalStatus { get; set; }

    [JsonProperty("tracksDownloaded")]
    public int TracksDownloaded { get; set; }

    [JsonProperty("duration")]
    public TimeSpan? Duration { get; set; }

    [JsonProperty("totalBytes")]
    public long? TotalBytes { get; set; }

    [JsonProperty("errorMessage")]
    public string? ErrorMessage { get; set; }
}

public class DownloadStatistics
{
    [JsonProperty("totalDownloads")]
    public int TotalDownloads { get; set; }

    [JsonProperty("completedDownloads")]
    public int CompletedDownloads { get; set; }

    [JsonProperty("failedDownloads")]
    public int FailedDownloads { get; set; }

    [JsonProperty("cancelledDownloads")]
    public int CancelledDownloads { get; set; }

    [JsonProperty("activeDownloads")]
    public int ActiveDownloads { get; set; }

    [JsonProperty("totalTracks")]
    public int TotalTracks { get; set; }

    [JsonProperty("totalBytes")]
    public long TotalBytes { get; set; }

    [JsonProperty("totalTime")]
    public TimeSpan TotalTime { get; set; }

    [JsonProperty("averageSpeed")]
    public double AverageSpeed { get; set; }

    [JsonProperty("lastDownload")]
    public DateTime? LastDownload { get; set; }

    [JsonProperty("mostDownloadedArtist")]
    public string? MostDownloadedArtist { get; set; }

    [JsonProperty("preferredQuality")]
    public string? PreferredQuality { get; set; }
}

public enum DownloadStatus
{
    Pending,
    Downloading,
    Completed,
    Failed,
    Paused,
    Cancelled
}
