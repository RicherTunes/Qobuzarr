using QobuzCLI.Models;

namespace QobuzCLI.Services;

public interface IStateService : IDisposable
{
    /// <summary>
    /// Initialize state service and load existing state
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Save current state to disk
    /// </summary>
    Task SaveStateAsync();

    /// <summary>
    /// Add a new download to active downloads
    /// </summary>
    Task<string> StartDownloadAsync(DownloadItem item);

    /// <summary>
    /// Update download progress
    /// </summary>
    Task UpdateDownloadProgressAsync(string downloadId, int progress, string? currentFile = null);

    /// <summary>
    /// Mark download as completed successfully
    /// </summary>
    Task CompleteDownloadAsync(string downloadId, string outputPath, int tracksDownloaded);

    /// <summary>
    /// Mark download as failed
    /// </summary>
    Task FailDownloadAsync(string downloadId, string errorMessage);

    /// <summary>
    /// Cancel an active download
    /// </summary>
    Task CancelDownloadAsync(string downloadId);

    /// <summary>
    /// Get all active downloads
    /// </summary>
    List<DownloadItem> GetActiveDownloads();

    /// <summary>
    /// Get download history (completed, failed, cancelled)
    /// </summary>
    List<DownloadHistoryItem> GetDownloadHistory(int limit = 50);

    /// <summary>
    /// Get specific download by ID
    /// </summary>
    DownloadItem? GetDownload(string downloadId);

    /// <summary>
    /// Clear old history entries
    /// </summary>
    Task CleanupHistoryAsync(int keepDays = 30);

    /// <summary>
    /// Get summary statistics
    /// </summary>
    DownloadStatistics GetStatistics();

    /// <summary>
    /// Resume failed or cancelled downloads
    /// </summary>
    Task<List<DownloadItem>> GetResumableDownloadsAsync();

    /// <summary>
    /// Export download history for analysis
    /// </summary>
    Task ExportHistoryAsync(string filePath, string format = "json");
}
