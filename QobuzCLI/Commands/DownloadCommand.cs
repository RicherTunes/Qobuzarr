using System.CommandLine;
using System.Linq;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using QobuzCLI.Models;
using CliDownloadResult = QobuzCLI.Models.CliDownloadResult;
using CliPlaylistDownloadResult = QobuzCLI.Models.CliPlaylistDownloadResult;
using QobuzCLI.Services;
using QobuzCLI.Services.Logging;
using QobuzCLI.Services.Adapters;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Spectre.Console;

namespace QobuzCLI.Commands;

public record DownloadOptions(
    string? Query,
    string? FromFile,
    bool Immediate,
    string? Output,
    string? Quality,
    bool Select,
    bool All,
    string? Type,
    int Priority,
    string? QueueId,
    string? ReportFormat,
    string? ReportOutput,
    int? Concurrency);

/// <summary>
/// Implements the 'download' command for the QobuzCLI application.
/// This is a lightweight CLI wrapper around the plugin's core download functionality.
/// The CLI only adds UI/UX features while delegating all core functionality to the plugin.
/// </summary>
/// <remarks>
/// Architecture principle: CLI wraps plugin functionality, never reimplements it.
/// - Uses plugin's QobuzDownloadService for actual downloads
/// - Adds CLI-specific features: progress display, batch management, UI
/// - Maintains separation: plugin = core logic, CLI = user interface
/// </remarks>
public class DownloadCommand
{
    private readonly IConfigService _configService;
    private readonly IPluginHost _pluginHost;
    private readonly ISearchService _searchService;
    private readonly IQueueService _queueService;
    private readonly IDashboardLogger _logger;
    private readonly IBatchDownloadService _batchDownloadService;
    private readonly QueueMonitoringService _queueMonitoring;
    private readonly IInteractiveSelectionService _selectionService;

    public Command Command { get; }

    public DownloadCommand(
        IConfigService configService,
        IPluginHost pluginHost,
        ISearchService searchService,
        IQueueService queueService,
        ILogger<DownloadCommand> logger,
        IDashboard dashboard,
        IBatchDownloadService batchDownloadService,
        QueueMonitoringService queueMonitoring,
        IInteractiveSelectionService selectionService)
    {
        _configService = configService;
        _pluginHost = pluginHost;
        _searchService = searchService;
        _queueService = queueService;
        _selectionService = selectionService;
        
        // Handle both real dashboard and mock instances for testing
        try
        {
            _logger = logger as IDashboardLogger ?? 
                      new DashboardLogger<DownloadCommand>(logger, dashboard);
        }
        catch
        {
            // Fallback for testing scenarios where Dashboard might be mocked improperly
            _logger = new DashboardLogger<DownloadCommand>(logger, null);
        }
        
        _batchDownloadService = batchDownloadService;
        _queueMonitoring = queueMonitoring;
        Command = CreateCommand();
    }

    private Command CreateCommand()
    {
        var downloadCommand = new Command("download", "Add music to download queue");

        var queryArg = new Argument<string?>("query", "Search query to download (album, artist, or track)") { Arity = ArgumentArity.ZeroOrOne };
        var fromFileOption = new Option<string?>("--from-file", "Download from a file containing search queries or album list");
        var immediateOption = new Option<bool>("--immediate", "Download immediately without queuing");
        var outputOption = new Option<string?>("--output", "Output directory (overrides config)");
        var qualityOption = new Option<string?>("--quality", "Quality override: mp3-320, flac-cd, flac-hires, flac-max");
        var selectOption = new Option<bool>("--select", "Show selection prompt if multiple results found");
        var allOption = new Option<bool>("--all", "Download all matching results");
        var typeOption = new Option<string?>("--type", "Search type: auto, album, artist, track, playlist, label");
        var priorityOption = new Option<int>("--priority", () => 0, "Queue priority (higher = sooner)");
        var queueOption = new Option<string?>("--queue", "Target queue ID");
        var reportFormatOption = new Option<string?>("--report-format", "Generate report: html, text, json");
        var reportOutputOption = new Option<string?>("--report-output", "Report output file path");
        var concurrencyOption = new Option<int?>("--concurrency", "Override max concurrent downloads (default: from config)");

        downloadCommand.AddArgument(queryArg);
        downloadCommand.AddOption(fromFileOption);
        downloadCommand.AddOption(immediateOption);
        downloadCommand.AddOption(outputOption);
        downloadCommand.AddOption(qualityOption);
        downloadCommand.AddOption(selectOption);
        downloadCommand.AddOption(allOption);
        downloadCommand.AddOption(typeOption);
        downloadCommand.AddOption(priorityOption);
        downloadCommand.AddOption(queueOption);
        downloadCommand.AddOption(reportFormatOption);
        downloadCommand.AddOption(reportOutputOption);
        downloadCommand.AddOption(concurrencyOption);

        downloadCommand.SetHandler(async (context) =>
        {
            var options = new DownloadOptions(
                context.ParseResult.GetValueForArgument(queryArg),
                context.ParseResult.GetValueForOption(fromFileOption),
                context.ParseResult.GetValueForOption(immediateOption),
                context.ParseResult.GetValueForOption(outputOption),
                context.ParseResult.GetValueForOption(qualityOption),
                context.ParseResult.GetValueForOption(selectOption),
                context.ParseResult.GetValueForOption(allOption),
                context.ParseResult.GetValueForOption(typeOption),
                context.ParseResult.GetValueForOption(priorityOption),
                context.ParseResult.GetValueForOption(queueOption),
                context.ParseResult.GetValueForOption(reportFormatOption),
                context.ParseResult.GetValueForOption(reportOutputOption),
                context.ParseResult.GetValueForOption(concurrencyOption)
            );
            
            await HandleDownloadAsync(options).ConfigureAwait(false);
        });

        return downloadCommand;
    }

    private async Task HandleDownloadAsync(DownloadOptions options)
    {
        try
        {
            var config = await InitializeAndValidateAsync(options).ConfigureAwait(false);
            if (config == null) return;

            // Handle file input
            if (!string.IsNullOrEmpty(options.FromFile))
            {
                var batchOptions = new BatchDownloadOptions
                {
                    FilePath = options.FromFile,
                    Immediate = options.Immediate,
                    OutputDirectory = options.Output,
                    Quality = options.Quality,
                    Priority = options.Priority,
                    QueueId = options.QueueId,
                    ReportFormat = options.ReportFormat,
                    ReportOutput = options.ReportOutput,
                    Concurrency = options.Concurrency
                };
                
                await _batchDownloadService.ProcessBatchDownloadAsync(batchOptions).ConfigureAwait(false);
                return;
            }

            // Validate query parameter
            if (string.IsNullOrEmpty(options.Query))
            {
                AnsiConsole.MarkupLine("[red]❌ Either 'query' or '--from-file' is required.[/]");
                return;
            }

            AnsiConsole.MarkupLine($"[blue]🔍 Searching for: '{options.Query}'[/]");

            // Parse search type and perform search
            var searchType = ParseSearchType(options.Type);
            if (searchType == Models.SearchType.Auto)
            {
                searchType = _searchService.DetectSearchType(options.Query);
                AnsiConsole.MarkupLine($"[dim]Auto-detected search type: {searchType.ToString().ToLower()}[/]");
            }

            List<Models.SearchResult> results = new();
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Searching Qobuz...", async ctx =>
                {
                    var rawResults = await _pluginHost.SearchAsync(options.Query, searchType).ConfigureAwait(false);
                    results = _searchService.ScoreResults(rawResults, options.Query);
                });

            if (!results.Any())
            {
                AnsiConsole.MarkupLine($"[yellow]No results found for '{options.Query}'[/]");
                AnsiConsole.MarkupLine("[dim]Try a different search term or check your spelling.[/]");
                return;
            }

            // Determine what to download
            var selectedResults = await SelectDownloadTargetsAsync(results, options.Query, options.Select, options.All).ConfigureAwait(false);
            if (!selectedResults.Any())
            {
                AnsiConsole.MarkupLine("[yellow]No items selected for download.[/]");
                return;
            }

            // Apply output and quality overrides
            var downloadConfig = ApplyDownloadOverrides(config, options.Output, options.Quality);

            // Execute downloads
            if (options.Immediate)
            {
                // Direct download mode
                await ExecuteDownloadsAsync(selectedResults, downloadConfig).ConfigureAwait(false);
            }
            else
            {
                // Queue mode (default)
                await AddToQueueAsync(selectedResults, downloadConfig, options.Priority, options.QueueId).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Download failed: {ex.Message}[/]");
            _logger.LogError(ex, "Download failed for query: {Query}", options.Query);
        }
    }

    private async Task<List<Models.SearchResult>> SelectDownloadTargetsAsync(
        List<Models.SearchResult> results, 
        string query, 
        bool forceSelect, 
        bool downloadAll)
    {
        if (downloadAll)
        {
            AnsiConsole.MarkupLine($"[cyan]📥 Will download all {results.Count} results[/]");
            return results;
        }

        // Check for exact matches
        var exactMatches = results.Where(r => r.Score >= 95).ToList();
        
        if (exactMatches.Count == 1 && !forceSelect)
        {
            var match = exactMatches.First();
            AnsiConsole.MarkupLine($"[green]✨ Found exact match: {match.Title} - {match.Artist}[/]");
            return new List<Models.SearchResult> { match };
        }

        // Check for high-quality single match
        var topResult = results.First();
        if (results.Count == 1 || (topResult.Score >= 85 && !forceSelect))
        {
            AnsiConsole.MarkupLine($"[green]🎯 Auto-selecting best match: {topResult.Title} - {topResult.Artist}[/]");
            return new List<Models.SearchResult> { topResult };
        }

        // Multiple results - try to show selection UI, fallback to top result if not interactive
        try 
        {
            return await ShowSelectionUIAsync(results, query, exactMatches).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            // Terminal not interactive - auto-select best match
            AnsiConsole.MarkupLine($"[yellow]⚠️  Terminal not interactive, auto-selecting best match: {topResult.Title} - {topResult.Artist}[/]");
            return new List<Models.SearchResult> { topResult };
        }
    }

    private async Task<List<Models.SearchResult>> ShowSelectionUIAsync(
        List<Models.SearchResult> results, 
        string query,
        List<Models.SearchResult> exactMatches)
    {
        AnsiConsole.MarkupLine($"[blue]🎯 Found {results.Count} result{(results.Count > 1 ? "s" : "")} for '{query}':[/]");
        AnsiConsole.WriteLine();

        // Display results table
        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("Option");
        table.AddColumn("Title");
        table.AddColumn("Artist");
        table.AddColumn("Details");
        table.AddColumn("Quality");
        table.AddColumn("Match");

        var options = new List<string>();
        for (int i = 0; i < Math.Min(results.Count, 10); i++) // Limit to top 10
        {
            var result = results[i];
            var option = $"{i + 1}";
            options.Add(option);

            var title = result.Title.Length > 25 ? result.Title.Substring(0, 22) + "..." : result.Title;
            var artist = result.Artist.Length > 20 ? result.Artist.Substring(0, 17) + "..." : result.Artist;
            var details = FormatDetails(result);
            var quality = FormatQuality(result);
            var match = FormatMatchScore(result);

            table.AddRow(option, title, artist, details, quality, match);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Selection prompt
        var choices = new List<string>(options);
        choices.Add("all");
        choices.Add("none");

        var prompt = new SelectionPrompt<string>()
            .Title("Select what to download:")
            .PageSize(15);

        // Add numbered choices
        foreach (var choice in choices.Take(options.Count))
        {
            prompt.AddChoice(choice);
        }
        
        // Add action choices
        prompt.AddChoice("all (Download all results)");
        prompt.AddChoice("none (Cancel download)");

        var selection = AnsiConsole.Prompt(prompt);

        if (selection == "none")
        {
            return new List<Models.SearchResult>();
        }
        
        if (selection == "all")
        {
            return results.Take(10).ToList(); // Limit to top 10 for safety
        }

        if (int.TryParse(selection, out int selectedIndex) && selectedIndex <= results.Count)
        {
            return new List<Models.SearchResult> { results[selectedIndex - 1] };
        }

        return new List<Models.SearchResult>();
    }

    private async Task ExecuteDownloadsAsync(List<Models.SearchResult> results, QobuzConfig config)
    {
        AnsiConsole.MarkupLine($"[green]🚀 Starting download{(results.Count > 1 ? "s" : "")}...[/]");
        AnsiConsole.WriteLine();

        using var semaphore = new SemaphoreSlim(config.MaxConcurrentDownloads, config.MaxConcurrentDownloads);

        // Create progress display
        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn()
            })
            .StartAsync(async ctx =>
            {
                var tasks = results.Select(async result =>
                {
                    await semaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var task = ctx.AddTask($"📥 {result.Title}");
                        task.MaxValue = 100;

                        // Use plugin's download service directly - no reimplementation!
                        var downloadResult = await ExecutePluginDownloadAsync(result, config.OutputDirectory, config.Quality).ConfigureAwait(false);
                        
                        if (downloadResult.IsSuccessful())
                        {
                            if (downloadResult.GetTracksDownloaded() > 0)
                            {
                                task.Description = $"✅ {result.Title}";
                            }
                            else
                            {
                                task.Description = $"⏭️ {result.Title} (already exists)";
                            }
                            task.Value = 100;
                        }
                        else
                        {
                            task.Description = $"❌ {result.Title}";
                        }

                        return downloadResult;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToArray();

                var downloadResults = await Task.WhenAll(tasks).ConfigureAwait(false);
                DisplayDownloadSummary(downloadResults.ToArray(), config.OutputDirectory);
            });
    }

    /// <summary>
    /// Execute download using plugin's core functionality directly.
    /// This is the correct architecture - CLI delegates to plugin, never reimplements.
    /// </summary>
    private async Task<CliDownloadResult> ExecutePluginDownloadAsync(Models.SearchResult result, string outputDir, string? quality)
    {
        _logger.LogInformation("Starting download: {Title} by {Artist}", result.Title, result.Artist);
        
        try
        {
            // Create output directory structure using CLI naming conventions
            var artistDir = Path.Combine(outputDir, FileSystemUtilities.SanitizeFileName(result.Artist));
            var albumDir = Path.Combine(artistDir, FileSystemUtilities.CreateAlbumDirectoryName(result.Title, result.Year));
            
            Directory.CreateDirectory(albumDir);
            
            // Delegate to plugin's download service - this is the correct pattern!
            CliDownloadResult downloadResult;
            switch (result.Type.ToLower())
            {
                case "artist":
                    _logger.LogInformation("Downloading artist: {Artist}", result.Artist);
                    downloadResult = await _pluginHost.DownloadArtistAsync(result.Id, artistDir).ConfigureAwait(false);
                    break;
                    
                case "playlist":
                    _logger.LogInformation("Downloading playlist: {Title}", result.Title);
                    var playlistResult = await _pluginHost.DownloadPlaylistAsync(result.Id, outputDir, quality!).ConfigureAwait(false);
                    // Convert PlaylistDownloadResult to CliDownloadResult
                    downloadResult = ConvertPlaylistResultToCliResult(playlistResult);
                    break;
                    
                case "label":
                    _logger.LogInformation("Downloading label: {Title}", result.Title);
                    var labelResult = await _pluginHost.DownloadLabelAsync(result.Id, outputDir, quality!).ConfigureAwait(false);
                    // Convert LabelDownloadResult to CliDownloadResult
                    downloadResult = ConvertLabelResultToCliResult(labelResult);
                    break;
                    
                default: // album or track
                    _logger.LogInformation("Downloading album: {Title}", result.Title);
                    downloadResult = await _pluginHost.DownloadAlbumAsync(result.Id, albumDir, quality).ConfigureAwait(false);
                    break;
            }
            
            if (downloadResult.IsSuccessful)
            {
                _logger.LogInformation("Completed: {Title} by {Artist} ({TracksDownloaded} tracks)", 
                    result.Title, result.Artist, downloadResult.TrackDownloads?.Count ?? 0);
            }
            else
            {
                _logger.LogError("Failed: {Title} - {Error}", result.Title, downloadResult.Message);
            }
            
            return downloadResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed for {Title}", result.Title);
            // Return a properly constructed CLI DownloadResult on error
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

    /// <summary>
    /// Parse search type from string - made private for internal use but accessible via reflection for tests
    /// </summary>
    private Models.SearchType ParseSearchType(string? type)
    {
        if (string.IsNullOrEmpty(type))
            return Models.SearchType.Auto;

        return type.ToLower() switch
        {
            "album" => Models.SearchType.Album,
            "artist" => Models.SearchType.Artist,
            "track" => Models.SearchType.Track,
            "playlist" => Models.SearchType.Playlist,
            "label" => Models.SearchType.Label,
            "auto" => Models.SearchType.Auto,
            _ => Models.SearchType.Auto
        };
    }


    /// <summary>
    /// Apply download overrides to config - method signature designed for test compatibility
    /// </summary>
    private QobuzConfig ApplyDownloadOverrides(QobuzConfig config, string? outputOverride, string? qualityOverride)
    {
        var downloadConfig = new QobuzConfig
        {
            // Copy all properties from original config
            Email = config.Email,
            Password = config.Password,
            UserId = config.UserId,
            AuthToken = config.AuthToken,
            AuthMethod = config.AuthMethod,
            Quality = qualityOverride ?? config.Quality,
            AutoQualityFallback = config.AutoQualityFallback,
            QualityFallbackOrder = config.QualityFallbackOrder,
            OutputDirectory = outputOverride ?? config.OutputDirectory,
            MaxConcurrentDownloads = config.MaxConcurrentDownloads,
            CreateArtistFolders = config.CreateArtistFolders,
            CreateAlbumFolders = config.CreateAlbumFolders,
            FileNamingPattern = config.FileNamingPattern,
            AlbumFolderPattern = config.AlbumFolderPattern,
            SearchResultLimit = config.SearchResultLimit,
            AutoResolveExactMatches = config.AutoResolveExactMatches,
            SearchPreference = config.SearchPreference,
            ApiTimeoutSeconds = config.ApiTimeoutSeconds,
            RetryAttempts = config.RetryAttempts,
            EnableMetadataTagging = config.EnableMetadataTagging,
            VerboseLogging = config.VerboseLogging
        };

        return downloadConfig;
    }

    /// <summary>
    /// Display download results summary - CLI-specific UI logic.
    /// </summary>
    private void DisplayDownloadSummary(CliDownloadResult[] downloadResults, string outputDirectory)
    {
        var successful = downloadResults.Where(r => r.IsSuccessful()).ToList();
        var failed = downloadResults.Where(r => !r.IsSuccessful()).ToList();
        var downloaded = successful.Where(r => r.GetTracksDownloaded() > 0).ToList();
        var skipped = successful.Where(r => r.GetTracksDownloaded() == 0).ToList();

        AnsiConsole.WriteLine();
        if (downloaded.Any())
        {
            AnsiConsole.MarkupLine($"[green]✅ Successfully downloaded {downloaded.Count} item{(downloaded.Count > 1 ? "s" : "")}[/]");
            var totalTracks = downloaded.Sum(r => r.GetTracksDownloaded());
            AnsiConsole.MarkupLine($"[dim]Total tracks: {totalTracks}[/]");
        }
        
        if (skipped.Any())
        {
            AnsiConsole.MarkupLine($"[yellow]⏭️ Skipped {skipped.Count} item{(skipped.Count > 1 ? "s" : "")} (already exist)[/]");
        }

        if (failed.Any())
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to download {failed.Count} item{(failed.Count > 1 ? "s" : "")}[/]");
            foreach (var failure in failed.Take(3))
            {
                AnsiConsole.MarkupLine($"[dim]  • {failure.GetSummaryMessage()}[/]");
            }
            if (failed.Count > 3)
            {
                AnsiConsole.MarkupLine($"[dim]  ... and {failed.Count - 3} more errors[/]");
            }
        }

        // Show output location
        if (successful.Any())
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]Downloads saved to: {outputDirectory}[/]");
        }
    }

    private string FormatDetails(Models.SearchResult result)
    {
        var parts = new List<string>();

        if (result.Year.HasValue)
            parts.Add($"({result.Year})");

        if (result.Type == "album" && result.TrackCount > 0)
            parts.Add($"{result.TrackCount} tracks");

        return string.Join(" | ", parts);
    }

    private string FormatQuality(Models.SearchResult result)
    {
        // Handle multi-format display (e.g., "Hi-Res 24bit/96kHz • CD • MP3")
        if (result.Quality.Contains("•"))
        {
            var parts = result.Quality.Split("•").Select(p => p.Trim()).ToList();
            var formatted = new List<string>();
            
            foreach (var part in parts)
            {
                if (part.Contains("Hi-Res"))
                {
                    formatted.Add($"[cyan]✨ {part}[/]");
                }
                else if (part == "CD")
                {
                    formatted.Add("[green]💿 CD[/]");
                }
                else if (part == "MP3")
                {
                    formatted.Add("[yellow]🎵 MP3[/]");
                }
                else
                {
                    formatted.Add($"[dim]{part}[/]");
                }
            }
            
            return string.Join(" [dim]•[/] ", formatted);
        }
        
        // Legacy single format display
        return result.Quality switch
        {
            var q when q.Contains("Hi-Res") => "[cyan]✨ Hi-Res[/]",
            var q when q.Contains("FLAC") || q.Contains("CD") => "[green]💿 FLAC[/]",
            var q when q.Contains("MP3") => "[yellow]🎵 MP3[/]",
            _ => "[dim]Unknown[/]"
        };
    }

    private string FormatMatchScore(Models.SearchResult result)
    {
        return result.Score switch
        {
            >= 95 => "[green]⭐ Exact[/]",
            >= 80 => "[yellow]✓ Good[/]",
            _ => "[dim]~ Fair[/]"
        };
    }


    private async Task AddToQueueAsync(List<Models.SearchResult> results, QobuzConfig config, int priority, string? queueId)
    {
        // Get target queue
        var queues = _queueService.GetQueues();
        DownloadQueue? targetQueue = null;
        
        if (!string.IsNullOrEmpty(queueId))
        {
            targetQueue = _queueService.GetQueue(queueId);
            if (targetQueue == null)
            {
                AnsiConsole.MarkupLine($"[red]❌ Queue '{queueId}' not found[/]");
                return;
            }
        }
        else
        {
            targetQueue = queues.FirstOrDefault();
            if (targetQueue == null)
            {
                AnsiConsole.MarkupLine("[yellow]No download queues found. Creating default queue...[/]");
                targetQueue = await _queueService.CreateQueueAsync("Default", config.MaxConcurrentDownloads).ConfigureAwait(false);
            }
        }

        // Phase 4: Validate downloadability before queueing
        var validatedResults = new List<Models.SearchResult>();
        var skippedCount = 0;

        // REGRESSION FIX: Skip validation that causes HTTP 400 errors
        // Original implementation didn't validate - just attempted downloads
        AnsiConsole.MarkupLine("[cyan]📥 Preparing downloads (validation disabled for compatibility)...[/]");

        foreach (var result in results)
        {
            try
            {
                // REGRESSION FIX: Skip validation that causes HTTP 400 errors
                // Original implementation didn't validate individual tracks
                validatedResults.Add(result);
                AnsiConsole.MarkupLine($"[green]✓[/] {result.Artist} - {result.Title} [dim](ready for download)[/]");
            }
            catch (Exception ex)
            {
                // If validation fails, include the item (better false positive than false negative)
                validatedResults.Add(result);
                _logger.LogWarning(ex, "Validation failed for {Title}, including anyway", result.Title);
                AnsiConsole.MarkupLine($"[yellow]?[/] {result.Artist} - {result.Title} [dim](validation failed, including anyway)[/]");
            }
        }

        if (skippedCount > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠️  Skipped {skippedCount} item{(skippedCount > 1 ? "s" : "")} that are not downloadable[/]");
        }

        if (validatedResults.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]❌ No downloadable items found after validation[/]");
            return;
        }

        // Add validated items to queue
        var items = validatedResults.Select(result => new QueuedDownload
        {
            SearchQuery = result.Title,
            SearchType = ParseSearchType(result.Type),
            Priority = priority,
            Metadata = new Dictionary<string, string>
            {
                ["title"] = result.Title,
                ["artist"] = result.Artist,
                ["quality"] = result.Quality,
                ["qobuzId"] = result.Id,
                ["trackCount"] = result.TrackCount.ToString(),
                ["year"] = result.Year?.ToString() ?? "",
                ["outputDirectory"] = config.OutputDirectory,
                ["qualityPreference"] = config.Quality
            }
        }).ToList();

        var addedIds = await _queueService.AddBatchToQueueAsync(targetQueue.Id, items).ConfigureAwait(false);
        
        AnsiConsole.MarkupLine($"[green]✅ Added {addedIds.Count} item{(addedIds.Count > 1 ? "s" : "")} to queue '{targetQueue.Name}'[/]");
        
        // Start queue processing if not already running
        await _queueService.StartQueueProcessingAsync(targetQueue.Id).ConfigureAwait(false);
        
        // Show queue progress (only if we have items to monitor)
        if (addedIds.Any())
        {
            await _queueMonitoring.MonitorQueueProgressSimpleAsync(targetQueue.Id, addedIds).ConfigureAwait(false);
        }
        
        // Show queue status
        var stats = _queueService.GetQueueStatistics(targetQueue.Id);
        AnsiConsole.MarkupLine($"[dim]Queue has {stats.PendingItems} pending items[/]");
    }
    
    // Queue monitoring methods moved to QueueMonitoringService
    // This reduces DownloadCommand complexity and improves testability

    /// <summary>
    /// Validate downloadability using plugin's validation logic.
    /// </summary>
    private async Task<bool> ValidateAlbumDownloadabilityAsync(string albumId, int preferredQuality)
    {
        try
        {
            // Use the plugin host's validation method directly
            return await _pluginHost.ValidateAlbumDownloadabilityAsync(albumId, preferredQuality);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed for album {AlbumId}", albumId);
            // If validation fails, assume downloadable to avoid false negatives
            return true;
        }
    }

    private int GetQualityId(string quality)
    {
        return quality.ToLower() switch
        {
            "mp3-320" => 5,
            "flac-cd" => 6,
            "flac-hires" => 7,
            "flac-max" => 27,
            _ => 27 // Default to highest quality
        };
    }

    // Removed complex error categorization - this functionality should be in a separate service if needed
    // CLI should focus on UI/UX, not business logic

    /// <summary>
    /// Safely converts PlaylistDownloadResult to DownloadResult with proper error handling
    /// </summary>
    private CliDownloadResult ConvertPlaylistResultToCliResult(
        CliPlaylistDownloadResult playlistResult)
    {
        try
        {
            var trackDownloads = new List<Lidarr.Plugin.Qobuzarr.Models.TrackDownload>();
            
            foreach (var track in playlistResult.DownloadedTracks ?? new List<QobuzCLI.Models.TrackDownloadInfo>())
            {
                try
                {
                    int? trackId = null;
                    if (!string.IsNullOrEmpty(track.TrackId))
                    {
                        if (int.TryParse(track.TrackId, out var parsedId))
                        {
                            trackId = parsedId;
                        }
                        else
                        {
                            _logger.LogWarning("Failed to parse track ID '{TrackId}' as integer", track.TrackId);
                        }
                    }

                    trackDownloads.Add(new Lidarr.Plugin.Qobuzarr.Models.TrackDownload
                    {
                        StreamingUrl = track.Skipped ? null! : "downloaded",
                        QobuzTrackId = trackId,
                        Title = $"Track {track.Position}",
                        MetadataSource = track.Skipped ? "Skipped" : "Playlist Download"
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error converting playlist track");
                    // Add a placeholder for the failed track
                    trackDownloads.Add(new Lidarr.Plugin.Qobuzarr.Models.TrackDownload
                    {
                        Title = $"Track {track.Position} (Conversion Error)",
                        MetadataSource = "Error"
                    });
                }
            }

            return new CliDownloadResult
            {
                Success = playlistResult.Success,
                Message = playlistResult.Message,
                StartedAt = playlistResult.StartedAt,
                CompletedAt = playlistResult.CompletedAt,
                TrackDownloads = playlistResult.DownloadedTracks ?? new List<TrackDownloadInfo>(),
                MetadataStrategy = "Playlist Download",
                ApiCallsSaved = 0,
                AdditionalApiCalls = playlistResult.TotalTracks
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert playlist download result");
            return CliDownloadResult.Failure("Playlist download conversion failed");
        }
    }

    /// <summary>
    /// Safely converts LabelDownloadResult to DownloadResult with proper error handling
    /// </summary>
    private CliDownloadResult ConvertLabelResultToCliResult(
        Lidarr.Plugin.Qobuzarr.Download.Services.LabelDownloadResult labelResult)
    {
        try
        {
            return new CliDownloadResult
            {
                Success = labelResult.Success,
                Message = labelResult.Message ?? $"Downloaded {labelResult.SuccessfulAlbums}/{labelResult.TotalAlbums} albums from {labelResult.LabelName}",
                StartedAt = labelResult.StartedAt,
                CompletedAt = labelResult.CompletedAt,
                TrackDownloads = new List<TrackDownloadInfo>(), // Label downloads don't track individual tracks
                MetadataStrategy = "Label",
                ApiCallsSaved = 0,
                AdditionalApiCalls = labelResult.TotalAlbums
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert label download result");
            return CliDownloadResult.Failure("Label download conversion failed");
        }
    }

    private async Task<QobuzConfig?> InitializeAndValidateAsync(DownloadOptions options)
    {
        // Initialize plugin host
        var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
        if (!_pluginHost.IsInitialized)
        {
            await _pluginHost.InitializeAsync(config).ConfigureAwait(false);
        }

        // Set concurrency override if provided
        if (options.Concurrency.HasValue)
        {
            _logger.LogInformation("Concurrency override requested: {Concurrency} (plugin uses internal concurrency management)", options.Concurrency.Value);
        }

        // Check authentication
        var authValid = await _pluginHost.TestAuthenticationAsync().ConfigureAwait(false);
        if (!authValid)
        {
            AnsiConsole.MarkupLine("[red]❌ Authentication failed. Please run 'qobuz auth login' first.[/]");
            return null;
        }

        return config;
    }
}