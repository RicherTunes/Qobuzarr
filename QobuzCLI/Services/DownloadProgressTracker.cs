using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using QobuzCLI.Models;

namespace QobuzCLI.Services;

/// <summary>
/// Tracks download progress across multiple concurrent downloads with real-time statistics
/// </summary>
public class DownloadProgressTracker : IDisposable
{
    private readonly ILogger<DownloadProgressTracker> _logger;
    private readonly ConcurrentDictionary<string, DownloadProgress> _activeDownloads = new();
    private readonly ConcurrentDictionary<string, DownloadStats> _downloadStats = new();
    private readonly Timer _displayTimer;
    private readonly object _displayLock = new();
    private DateTime _startTime;
    private long _totalBytesDownloaded;
    private int _completedDownloads;
    private int _failedDownloads;
    private bool _isDisposed;
    private ProgressContext? _progressContext;
    private readonly Dictionary<string, ProgressTask> _progressTasks = new();

    public DownloadProgressTracker(ILogger<DownloadProgressTracker> logger)
    {
        _logger = logger;
        _startTime = DateTime.UtcNow;

        // Update display every 100ms for smooth progress
        _displayTimer = new Timer(UpdateDisplay, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
    }

    /// <summary>
    /// Start tracking a new download
    /// </summary>
    public string StartDownload(string albumName, string artistName, int totalTracks, long? totalBytes = null)
    {
        var downloadId = Guid.NewGuid().ToString();
        var progress = new DownloadProgress
        {
            Id = downloadId,
            AlbumName = albumName,
            ArtistName = artistName,
            TotalTracks = totalTracks,
            TotalBytes = totalBytes ?? 0,
            StartTime = DateTime.UtcNow,
            Status = DownloadStatus.Pending
        };

        _activeDownloads[downloadId] = progress;
        _downloadStats[downloadId] = new DownloadStats();

        _logger.LogDebug("Started tracking download: {Album} by {Artist}", albumName, artistName);
        return downloadId;
    }

    /// <summary>
    /// Update track progress within an album download
    /// </summary>
    public void UpdateTrackProgress(string downloadId, int trackNumber, string trackName, long bytesDownloaded, long totalBytes)
    {
        if (!_activeDownloads.TryGetValue(downloadId, out var progress))
            return;

        // Update or add track progress
        progress.TrackProgress[trackNumber] = new TrackProgress
        {
            TrackNumber = trackNumber,
            TrackName = trackName,
            BytesDownloaded = bytesDownloaded,
            TotalBytes = totalBytes,
            LastUpdate = DateTime.UtcNow
        };

        // Update status
        if (progress.Status == DownloadStatus.Pending)
            progress.Status = DownloadStatus.Downloading;

        // Calculate overall progress
        var totalTrackBytes = progress.TrackProgress.Values.Sum(t => t.TotalBytes);
        var totalTrackBytesDownloaded = progress.TrackProgress.Values.Sum(t => t.BytesDownloaded);

        if (totalTrackBytes > 0)
        {
            progress.BytesDownloaded = totalTrackBytesDownloaded;
            progress.TotalBytes = totalTrackBytes;
        }

        // Update stats for speed calculation
        UpdateStats(downloadId, bytesDownloaded);
    }

    /// <summary>
    /// Mark a track as completed
    /// </summary>
    public void CompleteTrack(string downloadId, int trackNumber)
    {
        if (!_activeDownloads.TryGetValue(downloadId, out var progress))
            return;

        progress.CompletedTracks++;

        if (progress.TrackProgress.TryGetValue(trackNumber, out var track))
        {
            track.IsCompleted = true;
            track.BytesDownloaded = track.TotalBytes; // Ensure it shows 100%
        }

        _logger.LogDebug("Completed track {Track}/{Total} for {Album}",
            trackNumber, progress.TotalTracks, progress.AlbumName);
    }

    /// <summary>
    /// Complete an album download
    /// </summary>
    public void CompleteDownload(string downloadId, bool success = true)
    {
        if (!_activeDownloads.TryGetValue(downloadId, out var progress))
            return;

        progress.Status = success ? DownloadStatus.Completed : DownloadStatus.Failed;
        progress.EndTime = DateTime.UtcNow;

        if (success)
        {
            Interlocked.Increment(ref _completedDownloads);
            Interlocked.Add(ref _totalBytesDownloaded, progress.BytesDownloaded);
        }
        else
        {
            Interlocked.Increment(ref _failedDownloads);
        }

        // Remove from active downloads after a short delay
        Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ =>
        {
            _activeDownloads.TryRemove(downloadId, out var _);
            _downloadStats.TryRemove(downloadId, out var _);
        });
    }

    /// <summary>
    /// Get current statistics
    /// </summary>
    public ProgressStatistics GetStatistics()
    {
        var elapsed = DateTime.UtcNow - _startTime;
        var activeDownloads = _activeDownloads.Values.Where(d => d.Status == DownloadStatus.Downloading).ToList();

        // Calculate current speed from all active downloads
        var currentSpeed = _downloadStats.Values.Sum(s => s.GetCurrentSpeed());

        // Calculate average speed
        var avgSpeed = elapsed.TotalSeconds > 0 ? _totalBytesDownloaded / elapsed.TotalSeconds : 0;

        // Estimate remaining time
        var totalRemaining = activeDownloads.Sum(d => Math.Max(0, d.TotalBytes - d.BytesDownloaded));
        var eta = currentSpeed > 0 ? TimeSpan.FromSeconds(totalRemaining / currentSpeed) : TimeSpan.Zero;

        return new ProgressStatistics
        {
            ActiveDownloads = activeDownloads.Count,
            CompletedDownloads = _completedDownloads,
            FailedDownloads = _failedDownloads,
            TotalBytesDownloaded = _totalBytesDownloaded,
            CurrentSpeedBps = currentSpeed,
            AverageSpeedBps = avgSpeed,
            ElapsedTime = elapsed,
            EstimatedTimeRemaining = eta
        };
    }

    /// <summary>
    /// Create a progress context for Spectre.Console
    /// </summary>
    public async Task RunWithProgress(Func<ProgressContext, Task> action)
    {
        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new TransferSpeedColumn(),
                new DownloadedColumn(),
                new RemainingTimeColumn()
            })
            .StartAsync(async ctx =>
            {
                _progressContext = ctx;
                await action(ctx);
            });
    }

    /// <summary>
    /// Get or create a progress task for a download
    /// </summary>
    public ProgressTask GetProgressTask(string downloadId, ProgressContext context)
    {
        if (_progressTasks.TryGetValue(downloadId, out var task))
            return task;

        if (_activeDownloads.TryGetValue(downloadId, out var download))
        {
            var description = $"[yellow]{download.ArtistName}[/] - [cyan]{download.AlbumName}[/]";
            task = context.AddTask(description);
            _progressTasks[downloadId] = task;
            return task;
        }

        // Fallback
        return context.AddTask($"Download {downloadId}");
    }

    private void UpdateStats(string downloadId, long newBytes)
    {
        if (_downloadStats.TryGetValue(downloadId, out var stats))
        {
            stats.AddSample(newBytes);
        }
    }

    private void UpdateDisplay(object? state)
    {
        if (_isDisposed || !Monitor.TryEnter(_displayLock))
            return;

        try
        {
            // Update progress tasks if we have a context
            if (_progressContext != null)
            {
                foreach (var download in _activeDownloads.Values)
                {
                    if (_progressTasks.TryGetValue(download.Id, out var task))
                    {
                        if (download.TotalBytes > 0)
                        {
                            var percentage = (double)download.BytesDownloaded / download.TotalBytes * 100;
                            task.Value = percentage;

                            // Update description with current track
                            var currentTrack = download.TrackProgress.Values
                                .FirstOrDefault(t => !t.IsCompleted && t.BytesDownloaded > 0);

                            if (currentTrack != null)
                            {
                                task.Description = $"[yellow]{download.ArtistName}[/] - [cyan]{download.AlbumName}[/] " +
                                    $"[dim]Track {currentTrack.TrackNumber}: {currentTrack.TrackName}[/]";
                            }
                        }

                        if (download.Status == DownloadStatus.Completed)
                        {
                            task.Value = 100;
                            task.StopTask();
                        }
                        else if (download.Status == DownloadStatus.Failed)
                        {
                            task.Description = $"[red]❌ {download.ArtistName} - {download.AlbumName}[/]";
                            task.StopTask();
                        }
                    }
                }
            }
        }
        finally
        {
            Monitor.Exit(_displayLock);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        _isDisposed = true;
        _displayTimer?.Dispose();

        // Complete any remaining progress tasks
        foreach (var task in _progressTasks.Values)
        {
            task.StopTask();
        }

        _progressTasks.Clear();
    }
}

// Supporting classes
public class DownloadProgress
{
    public string Id { get; set; } = "";
    public string AlbumName { get; set; } = "";
    public string ArtistName { get; set; } = "";
    public int TotalTracks { get; set; }
    public int CompletedTracks { get; set; }
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public DownloadStatus Status { get; set; }
    public Dictionary<int, TrackProgress> TrackProgress { get; set; } = new();
}

public class TrackProgress
{
    public int TrackNumber { get; set; }
    public string TrackName { get; set; } = "";
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime LastUpdate { get; set; }
}

// Using DownloadStatus from QobuzCLI.Models

public class DownloadStats
{
    private readonly Queue<SpeedSample> _speedSamples = new();
    private readonly object _lock = new();
    private const int MaxSamples = 10;

    public void AddSample(long bytes)
    {
        lock (_lock)
        {
            _speedSamples.Enqueue(new SpeedSample
            {
                Bytes = bytes,
                Timestamp = DateTime.UtcNow
            });

            while (_speedSamples.Count > MaxSamples)
                _speedSamples.Dequeue();
        }
    }

    public double GetCurrentSpeed()
    {
        lock (_lock)
        {
            if (_speedSamples.Count < 2)
                return 0;

            var samples = _speedSamples.ToArray();
            var timeSpan = samples[^1].Timestamp - samples[0].Timestamp;
            var totalBytes = samples.Sum(s => s.Bytes);

            return timeSpan.TotalSeconds > 0 ? totalBytes / timeSpan.TotalSeconds : 0;
        }
    }

    private class SpeedSample
    {
        public long Bytes { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

public class ProgressStatistics
{
    public int ActiveDownloads { get; set; }
    public int CompletedDownloads { get; set; }
    public int FailedDownloads { get; set; }
    public long TotalBytesDownloaded { get; set; }
    public double CurrentSpeedBps { get; set; }
    public double AverageSpeedBps { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan EstimatedTimeRemaining { get; set; }

    public string FormatSpeed(double bytesPerSecond)
    {
        return bytesPerSecond switch
        {
            >= 1_048_576 => $"{bytesPerSecond / 1_048_576:F1} MB/s",
            >= 1_024 => $"{bytesPerSecond / 1_024:F1} KB/s",
            _ => $"{bytesPerSecond:F0} B/s"
        };
    }

    public string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F0} KB",
            _ => $"{bytes} B"
        };
    }
}
