using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using QobuzCLI.Models;

namespace QobuzCLI.Services;

public class StateService : IStateService
{
    private readonly ILogger<StateService> _logger;
    private readonly string _stateFilePath;
    private DownloadState _state;
    private readonly object _stateLock = new();
    private readonly Timer _autoSaveTimer;
    private readonly Timer _cleanupTimer;
    private bool _isDirty = false;
    private DateTime _lastSave = DateTime.UtcNow;

    // Memory leak prevention constants
    private const int MAX_HISTORY_ITEMS = 1000;
    private const int CLEANUP_KEEP_DAYS = 30;
    private const int CLEANUP_INTERVAL_HOURS = 6;

    public StateService(ILogger<StateService> logger)
    {
        _logger = logger;
        var stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qobuz");
        Directory.CreateDirectory(stateDir);
        _stateFilePath = Path.Combine(stateDir, "download-state.json");
        _state = new DownloadState();

        // Auto-save every 30 seconds only if state has changed
        _autoSaveTimer = new Timer(_ =>
        {
            Task.Run(async () =>
            {
                try
                {
                    await AutoSaveAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in auto-save timer");
                }
            });
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        // Auto-cleanup every 6 hours to prevent memory leaks
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
                    _logger.LogError(ex, "Error in cleanup timer");
                }
            });
        }, null, TimeSpan.FromHours(1), TimeSpan.FromHours(CLEANUP_INTERVAL_HOURS)); // First cleanup after 1 hour
    }

    private async Task AutoSaveAsync()
    {
        if (_isDirty)
        {
            await SaveStateAsync().ConfigureAwait(false);
            _isDirty = false;
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                var json = await File.ReadAllTextAsync(_stateFilePath).ConfigureAwait(false);
                var loadedState = JsonConvert.DeserializeObject<DownloadState>(json);

                if (loadedState != null)
                {
                    lock (_stateLock)
                    {
                        _state = loadedState;

                        // Reset any in-progress downloads to failed on startup
                        foreach (var download in _state.ActiveDownloads.Where(d => d.Status == DownloadStatus.Downloading))
                        {
                            download.Status = DownloadStatus.Failed;
                            download.ErrorMessage = "Application restart - download interrupted";
                            download.CompletedAt = DateTime.UtcNow;
                        }

                        // Update statistics
                        RecalculateStatistics();
                    }

                    _logger.LogInformation("State loaded: {ActiveDownloads} active, {HistoryCount} history items",
                        _state.ActiveDownloads.Count, _state.DownloadHistory.Count);
                }
            }
            else
            {
                _logger.LogInformation("No existing state found, starting fresh");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load download state, starting fresh");
            _state = new DownloadState();
        }
    }

    public async Task SaveStateAsync()
    {
        try
        {
            string json;
            lock (_stateLock)
            {
                _state.LastUpdated = DateTime.UtcNow;
                RecalculateStatistics();
                json = JsonConvert.SerializeObject(_state, Formatting.Indented);
            }

            await File.WriteAllTextAsync(_stateFilePath, json).ConfigureAwait(false);

            _logger.LogDebug("Download state saved to {Path}", _stateFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save download state");
        }
    }

    public Task<string> StartDownloadAsync(DownloadItem item)
    {
        lock (_stateLock)
        {
            item.Status = DownloadStatus.Downloading;
            item.StartedAt = DateTime.UtcNow;
            _state.ActiveDownloads.Add(item);
            _isDirty = true;

            _logger.LogDebug("▶️  Started download: {Artist} - {Title}", item.Artist, item.Title);
        }

        return Task.FromResult(item.Id);
    }

    public Task UpdateDownloadProgressAsync(string downloadId, int progress, string? currentFile = null)
    {
        lock (_stateLock)
        {
            var download = _state.ActiveDownloads.FirstOrDefault(d => d.Id == downloadId);
            if (download != null)
            {
                download.Progress = progress;
                download.CurrentFile = currentFile;

                // Calculate speed if we have byte information
                if (download.DownloadedBytes.HasValue && download.TotalBytes.HasValue)
                {
                    var elapsed = DateTime.UtcNow - download.StartedAt;
                    if (elapsed.TotalSeconds > 0)
                    {
                        download.DownloadSpeed = download.DownloadedBytes.Value / elapsed.TotalSeconds;
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task CompleteDownloadAsync(string downloadId, string outputPath, int tracksDownloaded)
    {
        lock (_stateLock)
        {
            var download = _state.ActiveDownloads.FirstOrDefault(d => d.Id == downloadId);
            if (download != null)
            {
                download.Status = DownloadStatus.Completed;
                download.CompletedAt = DateTime.UtcNow;
                download.OutputPath = outputPath;
                download.CompletedTracks = tracksDownloaded;
                download.Progress = 100;

                // Move to history
                var historyItem = new DownloadHistoryItem
                {
                    Id = download.Id,
                    QobuzId = download.QobuzId,
                    Type = download.Type,
                    Artist = download.Artist,
                    Title = download.Title,
                    DownloadedAt = download.StartedAt,
                    CompletedAt = download.CompletedAt,
                    Location = outputPath,
                    Quality = download.Quality,
                    FinalStatus = DownloadStatus.Completed,
                    TracksDownloaded = tracksDownloaded,
                    Duration = download.CompletedAt - download.StartedAt,
                    TotalBytes = download.TotalBytes
                };

                _state.DownloadHistory.Add(historyItem);
                _state.ActiveDownloads.Remove(download);

                // Prevent memory leaks by enforcing size limits
                EnforceHistorySizeLimit();

                _logger.LogDebug("✅ Completed: {Artist} - {Title} ({TracksDownloaded} tracks)",
                    download.Artist, download.Title, tracksDownloaded);
            }
        }

        return Task.CompletedTask;
    }

    public Task FailDownloadAsync(string downloadId, string errorMessage)
    {
        lock (_stateLock)
        {
            var download = _state.ActiveDownloads.FirstOrDefault(d => d.Id == downloadId);
            if (download != null)
            {
                download.Status = DownloadStatus.Failed;
                download.CompletedAt = DateTime.UtcNow;
                download.ErrorMessage = errorMessage;

                // Move to history
                var historyItem = new DownloadHistoryItem
                {
                    Id = download.Id,
                    QobuzId = download.QobuzId,
                    Type = download.Type,
                    Artist = download.Artist,
                    Title = download.Title,
                    DownloadedAt = download.StartedAt,
                    CompletedAt = download.CompletedAt,
                    Location = download.OutputPath ?? string.Empty,
                    Quality = download.Quality,
                    FinalStatus = DownloadStatus.Failed,
                    TracksDownloaded = download.CompletedTracks,
                    Duration = download.CompletedAt - download.StartedAt,
                    TotalBytes = download.TotalBytes,
                    ErrorMessage = errorMessage
                };

                _state.DownloadHistory.Add(historyItem);
                _state.ActiveDownloads.Remove(download);

                // Prevent memory leaks by enforcing size limits
                EnforceHistorySizeLimit();

                _logger.LogWarning("❌ Failed: {Artist} - {Title}: {Error}",
                    download.Artist, download.Title, errorMessage);
            }
        }

        return Task.CompletedTask;
    }

    public Task CancelDownloadAsync(string downloadId)
    {
        lock (_stateLock)
        {
            var download = _state.ActiveDownloads.FirstOrDefault(d => d.Id == downloadId);
            if (download != null)
            {
                download.Status = DownloadStatus.Cancelled;
                download.CompletedAt = DateTime.UtcNow;

                // Move to history
                var historyItem = new DownloadHistoryItem
                {
                    Id = download.Id,
                    QobuzId = download.QobuzId,
                    Type = download.Type,
                    Artist = download.Artist,
                    Title = download.Title,
                    DownloadedAt = download.StartedAt,
                    CompletedAt = download.CompletedAt,
                    Location = download.OutputPath ?? string.Empty,
                    Quality = download.Quality,
                    FinalStatus = DownloadStatus.Cancelled,
                    TracksDownloaded = download.CompletedTracks,
                    Duration = download.CompletedAt - download.StartedAt,
                    TotalBytes = download.TotalBytes
                };

                _state.DownloadHistory.Add(historyItem);
                _state.ActiveDownloads.Remove(download);

                // Prevent memory leaks by enforcing size limits
                EnforceHistorySizeLimit();

                _logger.LogInformation("Cancelled download: {Id} - {Title} by {Artist}",
                    downloadId, download.Title, download.Artist);
            }
        }

        return Task.CompletedTask;
    }

    public List<DownloadItem> GetActiveDownloads()
    {
        lock (_stateLock)
        {
            return _state.ActiveDownloads.ToList();
        }
    }

    public List<DownloadHistoryItem> GetDownloadHistory(int limit = 50)
    {
        lock (_stateLock)
        {
            return _state.DownloadHistory
                .OrderByDescending(h => h.DownloadedAt)
                .Take(limit)
                .ToList();
        }
    }

    public DownloadItem? GetDownload(string downloadId)
    {
        lock (_stateLock)
        {
            return _state.ActiveDownloads.FirstOrDefault(d => d.Id == downloadId);
        }
    }

    public Task CleanupHistoryAsync(int keepDays = 30)
    {
        lock (_stateLock)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-keepDays);
            var itemsToRemove = _state.DownloadHistory
                .Where(h => h.CompletedAt.HasValue && h.CompletedAt.Value < cutoffDate)
                .ToList();

            foreach (var item in itemsToRemove)
            {
                _state.DownloadHistory.Remove(item);
            }

            if (itemsToRemove.Any())
            {
                _logger.LogInformation("Cleaned up {Count} old history items older than {Days} days",
                    itemsToRemove.Count, keepDays);
            }
        }

        return Task.CompletedTask;
    }

    public DownloadStatistics GetStatistics()
    {
        lock (_stateLock)
        {
            return _state.Statistics;
        }
    }

    public Task<List<DownloadItem>> GetResumableDownloadsAsync()
    {
        lock (_stateLock)
        {
            var resumable = _state.ActiveDownloads
                .Where(d => d.Status == DownloadStatus.Failed || d.Status == DownloadStatus.Cancelled || d.Status == DownloadStatus.Paused)
                .ToList();

            return Task.FromResult(resumable);
        }
    }

    public async Task ExportHistoryAsync(string filePath, string format = "json")
    {
        try
        {
            List<DownloadHistoryItem> history;
            lock (_stateLock)
            {
                history = _state.DownloadHistory.ToList();
            }

            if (format.ToLower() == "json")
            {
                var json = JsonConvert.SerializeObject(history, Formatting.Indented);
                await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
            }
            else if (format.ToLower() == "csv")
            {
                var csv = CreateCsvExport(history);
                await File.WriteAllTextAsync(filePath, csv).ConfigureAwait(false);
            }
            else
            {
                throw new ArgumentException($"Unsupported export format: {format}");
            }

            _logger.LogInformation("Exported {Count} history items to {Path} ({Format})",
                history.Count, filePath, format.ToUpper());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export download history to {Path}", filePath);
            throw;
        }
    }

    private void RecalculateStatistics()
    {
        var stats = new DownloadStatistics();

        // Count by status
        stats.ActiveDownloads = _state.ActiveDownloads.Count;
        stats.TotalDownloads = _state.DownloadHistory.Count;
        stats.CompletedDownloads = _state.DownloadHistory.Count(h => h.FinalStatus == DownloadStatus.Completed);
        stats.FailedDownloads = _state.DownloadHistory.Count(h => h.FinalStatus == DownloadStatus.Failed);
        stats.CancelledDownloads = _state.DownloadHistory.Count(h => h.FinalStatus == DownloadStatus.Cancelled);

        // Aggregate totals
        stats.TotalTracks = _state.DownloadHistory.Sum(h => h.TracksDownloaded);
        stats.TotalBytes = _state.DownloadHistory.Where(h => h.TotalBytes.HasValue).Sum(h => h.TotalBytes!.Value);
        stats.TotalTime = TimeSpan.FromTicks(_state.DownloadHistory.Where(h => h.Duration.HasValue).Sum(h => h.Duration!.Value.Ticks));

        // Calculate average speed
        if (stats.TotalTime.TotalSeconds > 0)
        {
            stats.AverageSpeed = stats.TotalBytes / stats.TotalTime.TotalSeconds;
        }

        // Most recent download
        stats.LastDownload = _state.DownloadHistory.OrderByDescending(h => h.CompletedAt).FirstOrDefault()?.CompletedAt;

        // Most downloaded artist
        stats.MostDownloadedArtist = _state.DownloadHistory
            .GroupBy(h => h.Artist)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;

        // Preferred quality
        stats.PreferredQuality = _state.DownloadHistory
            .GroupBy(h => h.Quality)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;

        _state.Statistics = stats;
    }

    private string CreateCsvExport(List<DownloadHistoryItem> history)
    {
        var lines = new List<string>
        {
            "ID,Type,Artist,Title,Quality,Status,Downloaded At,Completed At,Duration,Tracks,Location,Error"
        };

        foreach (var item in history)
        {
            var duration = item.Duration?.ToString(@"hh\:mm\:ss") ?? "";
            var completedAt = item.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
            var error = item.ErrorMessage?.Replace("\"", "\"\"") ?? "";

            lines.Add($"\"{item.Id}\",\"{item.Type}\",\"{item.Artist}\",\"{item.Title}\",\"{item.Quality}\"," +
                     $"\"{item.FinalStatus}\",\"{item.DownloadedAt:yyyy-MM-dd HH:mm:ss}\",\"{completedAt}\"," +
                     $"\"{duration}\",{item.TracksDownloaded},\"{item.Location}\",\"{error}\"");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Enforces size limits on download history to prevent memory leaks
    /// </summary>
    private void EnforceHistorySizeLimit()
    {
        if (_state.DownloadHistory.Count > MAX_HISTORY_ITEMS)
        {
            // Keep only the most recent items
            var itemsToRemove = _state.DownloadHistory
                .OrderBy(h => h.DownloadedAt)
                .Take(_state.DownloadHistory.Count - MAX_HISTORY_ITEMS)
                .ToList();

            foreach (var item in itemsToRemove)
            {
                _state.DownloadHistory.Remove(item);
            }

            _logger.LogInformation("Enforced history size limit: removed {Count} old items, keeping {Remaining}",
                itemsToRemove.Count, _state.DownloadHistory.Count);

            _isDirty = true;
        }
    }

    /// <summary>
    /// Performs periodic cleanup of old history items to prevent memory leaks
    /// </summary>
    private async Task PerformPeriodicCleanupAsync()
    {
        lock (_stateLock)
        {
            var initialCount = _state.DownloadHistory.Count;

            // Remove items older than the configured days
            var cutoffDate = DateTime.UtcNow.AddDays(-CLEANUP_KEEP_DAYS);
            var itemsToRemove = _state.DownloadHistory
                .Where(h => h.CompletedAt.HasValue && h.CompletedAt.Value < cutoffDate)
                .ToList();

            foreach (var item in itemsToRemove)
            {
                _state.DownloadHistory.Remove(item);
            }

            // Also enforce absolute size limit
            EnforceHistorySizeLimit();

            var finalCount = _state.DownloadHistory.Count;
            var totalRemoved = initialCount - finalCount;

            if (totalRemoved > 0)
            {
                _logger.LogInformation("Periodic cleanup: removed {Count} old history items ({Days} days+), {Remaining} items remaining",
                    totalRemoved, CLEANUP_KEEP_DAYS, finalCount);
                _isDirty = true;
            }
        }

        // Save state if we made changes
        if (_isDirty)
        {
            await SaveStateAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _autoSaveTimer?.Dispose();
        _cleanupTimer?.Dispose();

        // Save state - using GetAwaiter().GetResult() is safer than .Wait() in dispose
        try
        {
            SaveStateAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state during disposal");
        }
    }
}
