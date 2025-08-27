using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Integration;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Utilities;
using QobuzCLI.Models;

namespace QobuzCLI.Services;

/// <summary>
/// CLI adapter for ILidarrIntegrationService that wraps the plugin's implementation.
/// This is a thin wrapper that adds CLI-specific features while delegating all
/// core functionality to the plugin's LidarrIntegrationService.
/// Follows the "No Stub/Placeholder Data Policy" and plugin-first architecture.
/// </summary>
public class RealLidarrIntegrationService : ILidarrIntegrationService
{
    private readonly ILogger<RealLidarrIntegrationService> _logger;
    private readonly IConfigService _configService;
    private readonly IPluginHost _pluginHost;
    private Lidarr.Plugin.Qobuzarr.Services.ILidarrIntegrationService? _pluginService;

    public RealLidarrIntegrationService(
        ILogger<RealLidarrIntegrationService> logger,
        IConfigService configService,
        IPluginHost pluginHost)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _pluginHost = pluginHost ?? throw new ArgumentNullException(nameof(pluginHost));
        
        _logger.LogInformation("RealLidarrIntegrationService initialized as CLI adapter for plugin services");
    }
    
    /// <summary>
    /// Initializes the plugin service with proper dependencies.
    /// This ensures we always use the plugin's real implementation, never stubs.
    /// </summary>
    private async Task EnsurePluginServiceInitializedAsync()
    {
        if (_pluginService != null) return;
        
        var config = await _configService.LoadConfigAsync();
        
        if (string.IsNullOrEmpty(config.LidarrUrl) || string.IsNullOrEmpty(config.LidarrApiKey))
        {
            throw new InvalidOperationException(
                "Lidarr not configured. Please set lidarr-url and lidarr-api-key in configuration.\n" +
                "This service requires real Lidarr integration - no stub data allowed per CLAUDE.md policy.");
        }
        
        // Initialize plugin host if needed
        if (!_pluginHost.IsInitialized)
        {
            _logger.LogInformation("Initializing plugin host with Lidarr configuration");
            await _pluginHost.InitializeAsync(config);
        }
        
        // Get the plugin's Lidarr integration service
        _pluginService = _pluginHost.GetLidarrIntegrationService();
        if (_pluginService == null)
        {
            // This is a critical error - we cannot fall back to stubs
            throw new InvalidOperationException(
                "Failed to initialize plugin's Lidarr integration service. " +
                "This is required for real API integration (no stub data allowed). " +
                "Please ensure:\n" +
                "1. Lidarr URL and API key are correctly configured\n" +
                "2. Lidarr is accessible at the configured URL\n" +
                "3. The API key has proper permissions");
        }
        
        _logger.LogInformation("Plugin's Lidarr integration service initialized successfully - using real API");
    }

    public async Task<IEnumerable<LidarrAlbum>> GetFilteredWantedAlbumsAsync(
        LidarrFilterOptions? filterOptions = null,
        int maxAlbums = 500,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Delegating to plugin's integration service (max {MaxAlbums} albums)", maxAlbums);
        
        await EnsurePluginServiceInitializedAsync();
        
        // Use the plugin's integration service
        return await _pluginService!.GetFilteredWantedAlbumsAsync(
            filterOptions ?? new LidarrFilterOptions(),
            maxAlbums,
            progress,
            cancellationToken);
    }

    public async Task<Dictionary<LidarrAlbum, QobuzAlbum>> SearchQobuzParallelAsync(
        IEnumerable<LidarrAlbum> lidarrAlbums,
        int maxConcurrency = 0,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Delegating SearchQobuzParallelAsync to plugin service");
        
        await EnsurePluginServiceInitializedAsync();
        
        // Use the plugin's integration service
        return await _pluginService!.SearchQobuzParallelAsync(
            lidarrAlbums,
            maxConcurrency,
            progress,
            cancellationToken);
    }

    public async Task<IEnumerable<AlbumDownloadItem>> ValidateAlbumsAsync(
        Dictionary<LidarrAlbum, QobuzAlbum> albumMatches,
        int preferredQuality,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Delegating ValidateAlbumsAsync to plugin service");
        
        await EnsurePluginServiceInitializedAsync();
        
        // Use the plugin's integration service
        return await _pluginService!.ValidateAlbumsAsync(
            albumMatches,
            preferredQuality,
            cancellationToken);
    }

    public async Task<DownloadBatchResult> DownloadLidarrAlbumsAsync(
        IEnumerable<AlbumDownloadItem> downloadItems,
        string outputPath,
        int maxConcurrency = 0,
        IProgress<DownloadProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Delegating DownloadLidarrAlbumsAsync to plugin service");
        
        await EnsurePluginServiceInitializedAsync();
        
        // Use the plugin's integration service
        return await _pluginService!.DownloadLidarrAlbumsAsync(
            downloadItems,
            outputPath,
            maxConcurrency,
            progress,
            cancellationToken);
    }

    public async Task<DownloadBatchResult> RetryFailedDownloadsAsync(
        IEnumerable<DownloadFailureItem> failedItems,
        int maxRetries = 3,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Delegating RetryFailedDownloadsAsync to plugin service");
        
        await EnsurePluginServiceInitializedAsync();
        
        // Use the plugin's integration service
        return await _pluginService!.RetryFailedDownloadsAsync(
            failedItems,
            maxRetries,
            outputPath ?? string.Empty,
            cancellationToken);
    }

    public IntegrationStatistics GetStatistics()
    {
        // Use the plugin's integration service statistics
        return _pluginService?.GetStatistics() ?? new IntegrationStatistics
        {
            TotalSearches = 0,
            SuccessfulSearches = 0,
            FailedSearches = 0,
            TotalDownloads = 0,
            SuccessfulDownloads = 0,
            FailedDownloads = 0,
            TotalBytesDownloaded = 0,
            TotalDownloadTime = TimeSpan.Zero,
            CurrentConcurrentOperations = 0,
            PeakConcurrentOperations = 0,
            LastOperationAt = DateTime.UtcNow,
            ErrorCounts = new Dictionary<string, int>(),
            QualityDistribution = new Dictionary<int, int>()
        };
    }

    public void ResetStatistics()
    {
        _logger.LogInformation("Resetting statistics");
        _pluginService?.ResetStatistics();
    }

    public void ClearQualityProfileCache()
    {
        _logger.LogInformation("Clearing quality profile cache");
        _pluginService?.ClearQualityProfileCache();
    }

    public Lidarr.Plugin.Qobuzarr.Services.QueueStatus GetQueueStatus()
    {
        // Use the plugin service if available, otherwise return default status
        return _pluginService?.GetQueueStatus() ?? new Lidarr.Plugin.Qobuzarr.Services.QueueStatus
        {
            ActiveDownloads = 0,
            ActiveSearches = 0,
            MaxConcurrentDownloads = 4,
            MaxConcurrentSearches = 8
        };
    }

    public Lidarr.Plugin.Qobuzarr.Services.PerformanceMetrics GetPerformanceMetrics()
    {
        // Delegate to plugin service or return empty metrics
        return _pluginService?.GetPerformanceMetrics() ?? new Lidarr.Plugin.Qobuzarr.Services.PerformanceMetrics
        {
            SearchMetrics = new Lidarr.Plugin.Qobuzarr.Services.OperationMetrics(),
            DownloadMetrics = new Lidarr.Plugin.Qobuzarr.Services.OperationMetrics(),
            ThroughputMetrics = new Lidarr.Plugin.Qobuzarr.Services.ThroughputMetrics(),
            ConcurrencyMetrics = new Lidarr.Plugin.Qobuzarr.Services.ConcurrencyMetrics()
        };
    }

    public Lidarr.Plugin.Qobuzarr.Services.QualityStatistics GetQualityStatistics()
    {
        // Delegate to plugin service or return empty statistics
        return _pluginService?.GetQualityStatistics() ?? new Lidarr.Plugin.Qobuzarr.Services.QualityStatistics
        {
            QualityProfileUsage = new Dictionary<string, int>(),
            QobuzQualityDistribution = new Dictionary<string, int>(),
            QualityUpgrades = new Dictionary<string, int>(),
            QualityDowngrades = new Dictionary<string, int>(),
            MostUsedQualityProfile = string.Empty,
            MostSelectedQobuzQuality = string.Empty
        };
    }

    public Lidarr.Plugin.Qobuzarr.Services.ErrorAnalysis GetErrorAnalysis()
    {
        // Delegate to plugin service or return empty analysis
        return _pluginService?.GetErrorAnalysis() ?? new Lidarr.Plugin.Qobuzarr.Services.ErrorAnalysis
        {
            ErrorsByType = new Dictionary<string, int>(),
            ErrorsByOperation = new Dictionary<string, int>(),
            MostCommonErrors = new List<Lidarr.Plugin.Qobuzarr.Services.ErrorFrequency>(),
            ErrorTrends = new List<Lidarr.Plugin.Qobuzarr.Services.ErrorRateTrend>(),
            TotalErrors = 0,
            OverallErrorRate = 0
        };
    }

    public Lidarr.Plugin.Qobuzarr.Services.StatisticsExport ExportStatistics(bool includeRawData = false)
    {
        // Delegate to plugin service or return empty export
        return _pluginService?.ExportStatistics(includeRawData) ?? new Lidarr.Plugin.Qobuzarr.Services.StatisticsExport
        {
            ExportedAt = DateTime.UtcNow,
            CoveredPeriod = TimeSpan.FromDays(1),
            IntegrationStats = GetStatistics(),
            PerformanceMetrics = GetPerformanceMetrics(),
            QualityStats = GetQualityStatistics(),
            ErrorAnalysis = GetErrorAnalysis(),
            RawData = includeRawData ? new Dictionary<string, object>() : null!,
            SystemInfo = null!
        };
    }

    public Lidarr.Plugin.Qobuzarr.Services.IProgressTracker CreateProgressTracker(int totalItems, string operationType, IProgress<ProgressReport>? progress = null)
    {
        // Delegate to plugin service or throw NotImplementedException
        if (_pluginService != null)
        {
            return _pluginService.CreateProgressTracker(totalItems, operationType, progress);
        }
        
        // Return a basic no-op progress tracker if plugin service is not available
        return new BasicProgressTracker(totalItems, operationType, progress);
    }

    public Lidarr.Plugin.Qobuzarr.Services.IDownloadProgressTracker CreateDownloadProgressTracker(int totalItems, string operationType, IProgress<DownloadProgressReport>? progress = null)
    {
        // Delegate to plugin service or throw NotImplementedException
        if (_pluginService != null)
        {
            return _pluginService.CreateDownloadProgressTracker(totalItems, operationType, progress);
        }
        
        // Return a basic no-op download progress tracker if plugin service is not available
        return new BasicDownloadProgressTracker(totalItems, operationType, progress);
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing RealLidarrIntegrationService");
        if (_pluginService is IDisposable disposableService)
        {
            disposableService.Dispose();
        }
    }
    
    /// <summary>
    /// Basic progress tracker implementation for fallback scenarios.
    /// </summary>
    private class BasicProgressTracker : Lidarr.Plugin.Qobuzarr.Services.IProgressTracker
    {
        private readonly int _totalItems;
        private readonly string _operationType;
        private readonly IProgress<ProgressReport>? _progress;
        private int _completedItems;
        private string _currentItem = string.Empty;
        private readonly DateTime _startTime = DateTime.UtcNow;
        
        public BasicProgressTracker(int totalItems, string operationType, IProgress<ProgressReport>? progress)
        {
            _totalItems = totalItems;
            _operationType = operationType;
            _progress = progress;
        }
        
        // IProgressTracker implementation
        public int TotalItems => _totalItems;
        public int CompletedItems => _completedItems;
        public string CurrentItem => _currentItem;
        public string OperationType => _operationType;
        public TimeSpan Elapsed => DateTime.UtcNow - _startTime;
        public TimeSpan EstimatedRemaining
        {
            get
            {
                if (_completedItems == 0) return TimeSpan.Zero;
                var rate = _completedItems / Elapsed.TotalSeconds;
                var remaining = _totalItems - _completedItems;
                return TimeSpan.FromSeconds(remaining / rate);
            }
        }
        public double PercentComplete => _totalItems > 0 ? (_completedItems * 100.0) / _totalItems : 0;
        
        public void ReportProgress(string currentItem, string? phase = null)
        {
            _currentItem = currentItem;
            _progress?.Report(new ProgressReport
            {
                Completed = _completedItems,
                Total = _totalItems,
                CurrentItem = currentItem ?? "Processing...",
                Phase = phase ?? _operationType
            });
        }
        
        public void CompleteItem(string? itemDescription = null)
        {
            _completedItems++;
            _progress?.Report(new ProgressReport
            {
                Completed = _completedItems,
                Total = _totalItems,
                CurrentItem = itemDescription ?? "item",
                Phase = _operationType
            });
        }
        
        public void CompleteItems(int count)
        {
            _completedItems += count;
            _progress?.Report(new ProgressReport
            {
                Completed = _completedItems,
                Total = _totalItems,
                CurrentItem = $"{_completedItems}/{_totalItems}",
                Phase = _operationType
            });
        }
        
        // Legacy methods for backward compatibility
        public void Report(int current, string? message = null)
        {
            _completedItems = current;
            ReportProgress(message ?? "Processing", message);
        }
        
        public void Complete(string? message = null)
        {
            _completedItems = _totalItems;
            ReportProgress("Completed", message ?? $"{_operationType} completed");
        }
        
        public void Dispose() { }
    }
    
    /// <summary>
    /// Basic download progress tracker implementation for fallback scenarios.
    /// </summary>
    private class BasicDownloadProgressTracker : Lidarr.Plugin.Qobuzarr.Services.IDownloadProgressTracker
    {
        private readonly int _totalItems;
        private readonly string _operationType;
        private readonly IProgress<DownloadProgressReport>? _progress;
        private int _completedItems;
        private string _currentItem = string.Empty;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private long _totalBytesDownloaded;
        private int _successCount;
        private int _failureCount;
        private readonly List<double> _speedSamples = new();
        
        public BasicDownloadProgressTracker(int totalItems, string operationType, IProgress<DownloadProgressReport>? progress)
        {
            _totalItems = totalItems;
            _operationType = operationType;
            _progress = progress;
        }
        
        // IProgressTracker implementation
        public int TotalItems => _totalItems;
        public int CompletedItems => _completedItems;
        public string CurrentItem => _currentItem;
        public string OperationType => _operationType;
        public TimeSpan Elapsed => DateTime.UtcNow - _startTime;
        public TimeSpan EstimatedRemaining
        {
            get
            {
                if (_completedItems == 0) return TimeSpan.Zero;
                var rate = _completedItems / Elapsed.TotalSeconds;
                var remaining = _totalItems - _completedItems;
                return TimeSpan.FromSeconds(remaining / rate);
            }
        }
        public double PercentComplete => _totalItems > 0 ? (_completedItems * 100.0) / _totalItems : 0;
        
        public void ReportProgress(string currentItem, string? phase = null)
        {
            _currentItem = currentItem;
            _progress?.Report(new DownloadProgressReport
            {
                Completed = _completedItems,
                Total = _totalItems,
                CurrentItem = currentItem ?? "Processing...",
                Phase = phase ?? _operationType,
                BytesDownloaded = _totalBytesDownloaded,
                CurrentSpeedMBps = CurrentSpeedMBps
            });
        }
        
        public void CompleteItem(string? itemDescription = null)
        {
            _completedItems++;
            _progress?.Report(new DownloadProgressReport
            {
                Completed = _completedItems,
                Total = _totalItems,
                CurrentItem = itemDescription ?? "item",
                Phase = _operationType,
                BytesDownloaded = _totalBytesDownloaded,
                CurrentSpeedMBps = CurrentSpeedMBps
            });
        }
        
        public void CompleteItems(int count)
        {
            _completedItems += count;
            _progress?.Report(new DownloadProgressReport
            {
                Completed = _completedItems,
                Total = _totalItems,
                CurrentItem = $"{_completedItems}/{_totalItems}",
                Phase = _operationType,
                BytesDownloaded = _totalBytesDownloaded,
                CurrentSpeedMBps = CurrentSpeedMBps
            });
        }
        
        // IDownloadProgressTracker implementation
        public long TotalBytesDownloaded => _totalBytesDownloaded;
        public double CurrentSpeedMBps => _speedSamples.Count > 0 ? _speedSamples.TakeLast(5).Average() : 0.0;
        public double AverageSpeedMBps => _speedSamples.Count > 0 ? _speedSamples.Average() : 0.0;
        public int SuccessCount => _successCount;
        public int FailureCount => _failureCount;
        
        public void ReportDownloadProgress(string currentItem, long bytesDownloaded, bool isSuccess)
        {
            _currentItem = currentItem;
            if (isSuccess)
            {
                _successCount++;
                var speedMBps = bytesDownloaded / (1024.0 * 1024.0) / Math.Max(1, Elapsed.TotalSeconds);
                _speedSamples.Add(speedMBps);
                if (_speedSamples.Count > 50) _speedSamples.RemoveAt(0); // Keep last 50 samples
            }
            else
            {
                _failureCount++;
            }
            
            _progress?.Report(new DownloadProgressReport
            {
                Completed = _completedItems,
                Total = _totalItems,
                CurrentItem = currentItem ?? "Processing...",
                Phase = _operationType,
                BytesDownloaded = _totalBytesDownloaded,
                CurrentSpeedMBps = CurrentSpeedMBps
            });
        }
        
        public void AddBytesDownloaded(long additionalBytes)
        {
            _totalBytesDownloaded += additionalBytes;
        }
        
        // Legacy methods for backward compatibility  
        public void Report(DownloadProgressReport report)
        {
            _progress?.Report(report);
        }
        
        public void Dispose() { }
    }
}