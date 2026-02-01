using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using QobuzCLI.Models;
using QobuzCLI.Services.Logging;
using QobuzCLI.Services.Adapters;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Spectre.Console;

namespace QobuzCLI.Services;

/// <summary>
/// Service for handling batch download operations from files
/// </summary>
public class BatchDownloadService : IBatchDownloadService
{
    private readonly ILogger<BatchDownloadService> _logger;
    private readonly IConfigService _configService;
    private readonly IPluginHost _pluginHost;
    private readonly ISearchService _searchService;
    private readonly IQueueService _queueService;
    private readonly IStateService _stateService;
    private readonly Dashboard _dashboard;

    public BatchDownloadService(
        ILogger<BatchDownloadService> logger,
        IConfigService configService,
        IPluginHost pluginHost,
        ISearchService searchService,
        IQueueService queueService,
        IStateService stateService,
        Dashboard dashboard)
    {
        _logger = logger;
        _configService = configService;
        _pluginHost = pluginHost;
        _searchService = searchService;
        _queueService = queueService;
        _stateService = stateService;
        _dashboard = dashboard;
    }

    public async Task ProcessBatchDownloadAsync(BatchDownloadOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(options.FilePath))
            {
                AnsiConsole.MarkupLine($"[red]❌ File not found: {options.FilePath}[/]");
                return;
            }

            AnsiConsole.MarkupLine($"[blue]📁 Reading download list from: {options.FilePath}[/]");

            // Load queries from file
            var (queries, exportData) = await LoadQueriesFromFileAsync(options.FilePath);

            if (!queries.Any())
            {
                AnsiConsole.MarkupLine("[yellow]⚠️ No valid queries found in file[/]");
                return;
            }

            // Apply configuration
            var config = await _configService.LoadConfigAsync();
            var downloadConfig = ApplyBatchConfigOverrides(config, options, exportData);

            AnsiConsole.MarkupLine($"[cyan]🚀 Starting batch download of {queries.Count} items...[/]");

            // Initialize report
            var report = new BatchDownloadReport
            {
                SourceFile = Path.GetFileName(options.FilePath),
                TotalItems = queries.Count,
                Config = new DownloadReportConfig
                {
                    Quality = downloadConfig.Quality,
                    OutputDirectory = downloadConfig.OutputDirectory,
                    Immediate = options.Immediate
                }
            };

            // Process downloads
            if (options.Immediate)
            {
                await ProcessImmediateDownloadsAsync(queries, downloadConfig, report, cancellationToken);
            }
            else
            {
                await ProcessQueuedDownloadsAsync(queries, downloadConfig, options, report, cancellationToken);
            }

            // Generate report if requested
            if (!string.IsNullOrEmpty(options.ReportFormat))
            {
                await GenerateReportAsync(report, options.ReportFormat, options.ReportOutput);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Error processing batch download: {ex.Message}[/]");
            _logger.LogError(ex, "Failed to process batch download from {FilePath}", options.FilePath);
        }
    }

    private async Task<(List<string> queries, LidarrExportData? exportData)> LoadQueriesFromFileAsync(string filePath)
    {
        try
        {
            var jsonContent = await File.ReadAllTextAsync(filePath);
            var exportData = JsonConvert.DeserializeObject<LidarrExportData>(jsonContent);

            if (exportData?.Albums != null)
            {
                var queries = exportData.Albums.Select(a => a.SearchQuery).ToList();
                AnsiConsole.MarkupLine($"[green]✅ Loaded {queries.Count} albums from Lidarr export[/]");

                if (!string.IsNullOrEmpty(exportData.SchemaVersion))
                {
                    AnsiConsole.MarkupLine($"[dim]📋 Format: {exportData.Format} v{exportData.SchemaVersion}[/]");
                }

                return (queries, exportData);
            }
        }
        catch
        {
            // Not JSON, fallback to text format
        }

        // Load as text file
        var lines = await File.ReadAllLinesAsync(filePath);
        var textQueries = lines
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
            .Select(line => line.Trim())
            .ToList();

        AnsiConsole.MarkupLine($"[green]✅ Loaded {textQueries.Count} search queries from text file[/]");
        return (textQueries, null);
    }

    private QobuzConfig ApplyBatchConfigOverrides(QobuzConfig config, BatchDownloadOptions options, LidarrExportData? exportData)
    {
        // Command line args take precedence over JSON config
        string? effectiveOutput = options.OutputDirectory;
        string? effectiveQuality = options.Quality;

        if (exportData?.Config != null)
        {
            effectiveOutput = options.OutputDirectory ?? exportData.Config.OutputDirectory;
            effectiveQuality = options.Quality ?? exportData.Config.Quality;

            if (effectiveOutput != options.OutputDirectory || effectiveQuality != options.Quality)
            {
                AnsiConsole.MarkupLine($"[dim]⚙️ Using config overrides from JSON[/]");
            }
        }

        // Clone config with overrides
        return new QobuzConfig
        {
            Email = config.Email,
            Password = config.Password,
            UserId = config.UserId,
            AuthToken = config.AuthToken,
            AuthMethod = config.AuthMethod,
            Quality = effectiveQuality ?? config.Quality,
            OutputDirectory = effectiveOutput ?? config.OutputDirectory,
            MaxConcurrentDownloads = options.Concurrency ?? config.MaxConcurrentDownloads,
            MaxConcurrentSearches = config.MaxConcurrentSearches,
            EnableLocalCache = config.EnableLocalCache,
            SearchResultLimit = config.SearchResultLimit,
            AutoResolveExactMatches = config.AutoResolveExactMatches
        };
    }

    private async Task ProcessImmediateDownloadsAsync(List<string> queries, QobuzConfig config, BatchDownloadReport report, CancellationToken cancellationToken = default)
    {
        _dashboard.Start("🚀 Immediate Batch Download", queries.Count);

        try
        {
            int successful = 0;
            int failed = 0;

            foreach (var query in queries)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var reportItem = new DownloadReportItem { Query = query };

                _dashboard.UpdateProgress(successful + failed, successful, failed, query);

                try
                {
                    // Search and download
                    var result = await SearchAndDownloadAsync(query, config);

                    if (result.Success)
                    {
                        successful++;
                        reportItem.Status = result.TracksDownloaded > 0 ? "Success" : "Skipped";
                        reportItem.MatchedAlbum = result.MatchedAlbum;
                        reportItem.SizeBytes = result.EstimatedSize;
                    }
                    else
                    {
                        failed++;
                        reportItem.Status = result.ErrorMessage?.StartsWith("No results") == true ? "No Results" : "Failed";
                        reportItem.ErrorMessage = result.ErrorMessage;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    reportItem.Status = "Failed";
                    reportItem.ErrorMessage = ex.Message;
                    _logger.LogError(ex, "Error processing query: {Query}", query);
                }

                stopwatch.Stop();
                reportItem.ProcessingTime = stopwatch.Elapsed;
                report.Items.Add(reportItem);

                await Task.Delay(100); // Be nice to the API
            }

            report.SuccessfulItems = successful;
            report.FailedItems = failed;

            // Show summary
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]✅ Batch download completed: {successful} successful, {failed} failed[/]");
        }
        finally
        {
            _dashboard.StopOperation();
        }
    }

    private async Task ProcessQueuedDownloadsAsync(List<string> queries, QobuzConfig config, BatchDownloadOptions options, BatchDownloadReport report, CancellationToken cancellationToken = default)
    {
        _dashboard.Start("🚀 Queue-Based Batch Processing", queries.Count);

        try
        {
            int successful = 0;
            int failed = 0;
            using var semaphore = new SemaphoreSlim(config.MaxConcurrentSearches, config.MaxConcurrentSearches);

            var tasks = queries.Select(async query =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var reportItem = new DownloadReportItem { Query = query };

                    _dashboard.UpdateProgress(successful + failed, successful, failed, query);

                    try
                    {
                        // Search and queue
                        var result = await SearchAndQueueAsync(query, config, options.Priority, options.QueueId);

                        if (result.Success)
                        {
                            Interlocked.Increment(ref successful);
                            reportItem.Status = "Success";
                            reportItem.MatchedAlbum = result.MatchedAlbum;
                            reportItem.SizeBytes = result.EstimatedSize;
                        }
                        else
                        {
                            Interlocked.Increment(ref failed);
                            reportItem.Status = "No Results";
                            reportItem.ErrorMessage = result.ErrorMessage;
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        reportItem.Status = "Failed";
                        reportItem.ErrorMessage = ex.Message;
                        _logger.LogError(ex, "Error processing query: {Query}", query);
                    }

                    stopwatch.Stop();
                    reportItem.ProcessingTime = stopwatch.Elapsed;
                    report.Items.Add(reportItem);

                    await Task.Delay(50); // Be nice to the API
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            report.SuccessfulItems = successful;
            report.FailedItems = failed;

            // Dashboard has finished tracking the queuing process
            _dashboard.StopOperation();

            // Start queue monitoring if items were added
            if (successful > 0)
            {
                var queues = _queueService.GetQueues();
                var targetQueue = queues.FirstOrDefault();
                if (targetQueue != null)
                {
                    // Monitor the actual download queue processing
                    await MonitorQueueWithDashboardAsync(targetQueue.Id);
                }
            }
        }
        finally
        {
            // Ensure dashboard is stopped if monitoring was interrupted
            if (_dashboard.IsActive)
            {
                _dashboard.StopOperation();
            }
        }
    }

    private async Task<BatchDownloadResult> SearchAndDownloadAsync(string query, QobuzConfig config)
    {
        // Search for the item
        var searchType = _searchService.DetectSearchType(query);
        var rawResults = await _pluginHost.SearchAsync(query, searchType);
        var results = _searchService.ScoreResults(rawResults, query);

        if (!results.Any())
        {
            return new BatchDownloadResult
            {
                Success = false,
                ErrorMessage = "No matching albums found"
            };
        }

        // Auto-select best match
        var bestResult = results.First();

        // Create output directory
        var artistDir = Path.Combine(config.OutputDirectory, Lidarr.Plugin.Common.Utilities.FileSystemUtilities.SanitizeFileName(bestResult.Artist));
        var albumDir = Path.Combine(artistDir, Lidarr.Plugin.Common.Utilities.FileSystemUtilities.CreateAlbumDirectoryName(bestResult.Title, bestResult.Year));

        // Download using plugin host
        var downloadResult = await _pluginHost.DownloadAlbumAsync(bestResult.Id, albumDir, config.Quality);

        return new BatchDownloadResult
        {
            Success = downloadResult.IsSuccessful(),
            TracksDownloaded = downloadResult.GetTracksDownloaded(),
            ErrorMessage = downloadResult.IsSuccessful() ? null : downloadResult.GetSummaryMessage(),
            MatchedAlbum = CreateMatchedAlbumInfo(bestResult, config.Quality),
            EstimatedSize = EstimateAlbumSize(bestResult.TrackCount, config.Quality)
        };
    }

    private async Task<BatchDownloadResult> SearchAndQueueAsync(string query, QobuzConfig config, int priority, string? queueId)
    {
        // Search for the item
        var searchType = _searchService.DetectSearchType(query);
        var rawResults = await _pluginHost.SearchAsync(query, searchType);
        var results = _searchService.ScoreResults(rawResults, query);

        if (!results.Any())
        {
            return new BatchDownloadResult
            {
                Success = false,
                ErrorMessage = "No matching albums found"
            };
        }

        // Auto-select best match
        var bestResult = results.First();

        // Add to queue
        var queueItem = new QueuedDownload
        {
            SearchQuery = bestResult.Title,
            SearchType = ParseSearchType(bestResult.Type),
            Priority = priority,
            Metadata = new Dictionary<string, string>
            {
                ["title"] = bestResult.Title,
                ["artist"] = bestResult.Artist,
                ["quality"] = bestResult.Quality,
                ["qobuzId"] = bestResult.Id,
                ["trackCount"] = bestResult.TrackCount.ToString(),
                ["year"] = bestResult.Year?.ToString() ?? "",
                ["outputDirectory"] = config.OutputDirectory,
                ["qualityPreference"] = config.Quality
            }
        };

        // Get target queue
        var queues = _queueService.GetQueues();
        var targetQueue = string.IsNullOrEmpty(queueId)
            ? queues.FirstOrDefault()
            : _queueService.GetQueue(queueId);

        if (targetQueue == null)
        {
            targetQueue = await _queueService.CreateQueueAsync("Default", config.MaxConcurrentDownloads);
        }

        await _queueService.AddBatchToQueueAsync(targetQueue.Id, new List<QueuedDownload> { queueItem });
        await _queueService.StartQueueProcessingAsync(targetQueue.Id);

        return new BatchDownloadResult
        {
            Success = true,
            MatchedAlbum = CreateMatchedAlbumInfo(bestResult, config.Quality),
            EstimatedSize = EstimateAlbumSize(bestResult.TrackCount, config.Quality)
        };
    }

    private MatchedAlbumInfo CreateMatchedAlbumInfo(SearchResult result, string quality)
    {
        return new MatchedAlbumInfo
        {
            Title = result.Title,
            Artist = result.Artist,
            AvailableFormats = result.Quality,
            SelectedFormat = quality,
            Year = result.Year,
            TrackCount = result.TrackCount,
            Id = result.Id,
            EstimatedSizeBytes = EstimateAlbumSize(result.TrackCount, quality)
        };
    }

    private long EstimateAlbumSize(int trackCount, string quality)
    {
        var sizePerTrack = quality?.Contains("flac") == true ? 50_000_000L : 8_000_000L;
        return trackCount * sizePerTrack;
    }

    private SearchType ParseSearchType(string type)
    {
        return type?.ToLower() switch
        {
            "album" => SearchType.Album,
            "artist" => SearchType.Artist,
            "track" => SearchType.Track,
            _ => SearchType.Auto
        };
    }

    private async Task MonitorQueueSimpleAsync(string queueId)
    {
        AnsiConsole.MarkupLine("[cyan]🔄 Monitoring queue progress...[/]");
        AnsiConsole.MarkupLine("[dim]Press Ctrl+C to stop monitoring (queue will continue in background)[/]");

        while (true)
        {
            var stats = _queueService.GetQueueStatistics(queueId);
            var remaining = stats.PendingItems + stats.ActiveDownloads;

            if (remaining == 0)
            {
                AnsiConsole.MarkupLine("[green]✅ All downloads completed[/]");
                break;
            }

            AnsiConsole.MarkupLine($"[blue]Progress:[/] Active: {stats.ActiveDownloads}, Completed: {stats.CompletedItems}, Failed: {stats.FailedItems}, Remaining: {remaining}");

            try
            {
                await Task.Delay(2000);
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]⏹️ Monitoring stopped. Queue continues processing in background.[/]");
                break;
            }
        }
    }

    private async Task MonitorQueueWithDashboardAsync(string queueId)
    {
        var stats = _queueService.GetQueueStatistics(queueId);
        var totalItems = stats.TotalItems;

        // Start a new dashboard for monitoring the actual downloads
        _dashboard.Start("📥 Downloading Queued Items", totalItems);

        try
        {
            while (true)
            {
                stats = _queueService.GetQueueStatistics(queueId);
                var remaining = stats.PendingItems + stats.ActiveDownloads;
                var completed = stats.CompletedItems;
                var failed = stats.FailedItems;

                // Update dashboard with download progress
                var currentItem = "";
                var lastSuccessful = "";

                // Try to get current downloading items from queue
                var queue = _queueService.GetQueue(queueId);
                if (queue != null)
                {
                    var activeItems = queue.Items.Where(i => i.Status == QueueStatus.Downloading).ToList();
                    if (activeItems.Any())
                    {
                        var active = activeItems.First();
                        currentItem = $"{active.Metadata?.GetValueOrDefault("artist")} - {active.Metadata?.GetValueOrDefault("title")}";
                    }

                    var lastCompleted = queue.Items
                        .Where(i => i.Status == QueueStatus.Completed && i.CompletedAt.HasValue)
                        .OrderByDescending(i => i.CompletedAt!.Value)
                        .FirstOrDefault();
                    if (lastCompleted != null)
                    {
                        lastSuccessful = $"{lastCompleted.Metadata?.GetValueOrDefault("artist")} - {lastCompleted.Metadata?.GetValueOrDefault("title")}";
                    }
                }

                _dashboard.UpdateProgress(completed + failed, completed, failed, currentItem, lastSuccessful);

                // Add log messages for milestones
                if (completed % 100 == 0 && completed > 0)
                {
                    _dashboard.AddLogMessage($"Milestone: {completed} albums downloaded");
                }

                // Check if all downloads are complete
                if (remaining == 0)
                {
                    _dashboard.AddLogMessage("All downloads completed!");
                    await Task.Delay(2000); // Show final state briefly
                    break;
                }

                try
                {
                    await Task.Delay(500); // Update every 500ms for smoother dashboard
                }
                catch (OperationCanceledException)
                {
                    _dashboard.AddLogMessage("Monitoring stopped by user");
                    break;
                }
            }
        }
        finally
        {
            _dashboard.StopOperation();

            // Show final summary
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]✅ Download queue processing complete![/]");
            AnsiConsole.MarkupLine($"[cyan]Total: {stats.TotalItems} | Completed: {stats.CompletedItems} | Failed: {stats.FailedItems}[/]");
        }
    }

    private async Task GenerateReportAsync(BatchDownloadReport report, string format, string? outputPath)
    {
        try
        {
            string content = format.ToLower() switch
            {
                "html" => GenerateHtmlReport(report),
                "text" => GenerateTextReport(report),
                "json" => JsonConvert.SerializeObject(report, Formatting.Indented),
                _ => throw new ArgumentException($"Unsupported report format: {format}")
            };

            var fileName = outputPath ?? $"qobuz_batch_report_{DateTime.Now:yyyyMMdd_HHmmss}.{format}";
            await File.WriteAllTextAsync(fileName, content);

            AnsiConsole.MarkupLine($"[green]📊 Report generated: {fileName}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to generate report: {ex.Message}[/]");
            _logger.LogError(ex, "Failed to generate batch download report");
        }
    }

    private string GenerateHtmlReport(BatchDownloadReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><title>Qobuz Batch Download Report</title>");
        sb.AppendLine("<style>body{font-family:Arial,sans-serif;margin:20px;}");
        sb.AppendLine("table{border-collapse:collapse;width:100%;}");
        sb.AppendLine("th,td{border:1px solid #ddd;padding:8px;text-align:left;}");
        sb.AppendLine("th{background-color:#f2f2f2;}");
        sb.AppendLine(".success{color:green;}.failed{color:red;}</style></head><body>");
        sb.AppendLine($"<h1>Qobuz Batch Download Report</h1>");
        sb.AppendLine($"<p>Generated: {report.GeneratedAt}</p>");
        sb.AppendLine($"<p>Source: {report.SourceFile}</p>");
        sb.AppendLine($"<p>Total: {report.TotalItems} | Success: {report.SuccessfulItems} | Failed: {report.FailedItems}</p>");
        sb.AppendLine("<table><tr><th>Query</th><th>Status</th><th>Album</th><th>Size</th><th>Time</th></tr>");

        foreach (var item in report.Items)
        {
            var statusClass = item.Status == "Success" ? "success" : "failed";
            sb.AppendLine($"<tr><td>{item.Query}</td>");
            sb.AppendLine($"<td class='{statusClass}'>{item.Status}</td>");
            sb.AppendLine($"<td>{item.MatchedAlbum?.Artist} - {item.MatchedAlbum?.Title}</td>");
            sb.AppendLine($"<td>{item.FormattedSize}</td>");
            sb.AppendLine($"<td>{item.ProcessingTime.TotalSeconds:F1}s</td></tr>");
        }

        sb.AppendLine("</table></body></html>");
        return sb.ToString();
    }

    private string GenerateTextReport(BatchDownloadReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("QOBUZ BATCH DOWNLOAD REPORT");
        sb.AppendLine("===========================");
        sb.AppendLine($"Generated: {report.GeneratedAt}");
        sb.AppendLine($"Source: {report.SourceFile}");
        sb.AppendLine($"Total: {report.TotalItems} | Success: {report.SuccessfulItems} | Failed: {report.FailedItems}");
        sb.AppendLine();
        sb.AppendLine("RESULTS:");

        foreach (var item in report.Items)
        {
            sb.AppendLine($"- {item.Query}: {item.Status}");
            if (item.MatchedAlbum != null)
            {
                sb.AppendLine($"  Album: {item.MatchedAlbum.Artist} - {item.MatchedAlbum.Title}");
            }
            if (!string.IsNullOrEmpty(item.ErrorMessage))
            {
                sb.AppendLine($"  Error: {item.ErrorMessage}");
            }
        }

        return sb.ToString();
    }

    // Helper classes
    private class BatchDownloadResult
    {
        public bool Success { get; set; }
        public int TracksDownloaded { get; set; }
        public string? ErrorMessage { get; set; }
        public MatchedAlbumInfo? MatchedAlbum { get; set; }
        public long EstimatedSize { get; set; }
    }

    private class LidarrExportData
    {
        [JsonProperty("schema_version")]
        public string? SchemaVersion { get; set; }
        public string? Format { get; set; }
        public List<LidarrAlbumData> Albums { get; set; } = new();
        public LidarrConfigData? Config { get; set; }
    }

    private class LidarrAlbumData
    {
        [JsonProperty("search_query")]
        public string SearchQuery { get; set; } = "";
    }

    private class LidarrConfigData
    {
        [JsonProperty("output_directory")]
        public string? OutputDirectory { get; set; }
        public string? Quality { get; set; }
    }

    private class BatchDownloadReport
    {
        public string GeneratedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
        public string SourceFile { get; set; } = "";
        public int TotalItems { get; set; }
        public int SuccessfulItems { get; set; }
        public int FailedItems { get; set; }
        public long TotalSizeBytes { get; set; }
        public TimeSpan TotalProcessingTime { get; set; }
        public List<DownloadReportItem> Items { get; set; } = new();
        public DownloadReportConfig Config { get; set; } = new();
        public string FormattedTotalSize => FormatFileSize(TotalSizeBytes);

        private static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:F1} {sizes[order]}";
        }
    }

    private class DownloadReportItem
    {
        public string Query { get; set; } = "";
        public string Status { get; set; } = "";
        public string? ErrorMessage { get; set; }
        public MatchedAlbumInfo? MatchedAlbum { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
        public TimeSpan ProcessingTime { get; set; }
        public long SizeBytes { get; set; }
        public string FormattedSize => FormatFileSize(SizeBytes);

        private static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:F1} {sizes[order]}";
        }
    }

    private class MatchedAlbumInfo
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string AvailableFormats { get; set; } = "";
        public string SelectedFormat { get; set; } = "";
        public int? Year { get; set; }
        public int TrackCount { get; set; }
        public string Id { get; set; } = "";
        public long EstimatedSizeBytes { get; set; }
    }

    private class DownloadReportConfig
    {
        public string Quality { get; set; } = "";
        public string OutputDirectory { get; set; } = "";
        public bool Immediate { get; set; }
    }
}
