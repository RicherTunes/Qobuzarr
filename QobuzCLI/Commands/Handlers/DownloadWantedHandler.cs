using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services;
using QobuzCLI.Services;
using QobuzCLI.Services.UI;

namespace QobuzCLI.Commands.Handlers
{
    /// <summary>
    /// Handler for the 'lidarr download-wanted' command.
    /// Delegates business logic to plugin services while handling CLI-specific presentation.
    /// </summary>
    public class DownloadWantedHandler : ILidarrCommandHandler
    {
        private readonly ILidarrIntegrationService _integrationService;
        private readonly IConfigService _configService;
        private readonly IQueueService _queueService;
        private readonly ILidarrUIService _uiService;
        private readonly IDashboard _dashboard;
        private readonly ILogger<DownloadWantedHandler> _logger;
        
        private readonly DownloadWantedOptions _options;

        public DownloadWantedHandler(
            ILidarrIntegrationService integrationService,
            IConfigService configService,
            IQueueService queueService,
            ILidarrUIService uiService,
            IDashboard dashboard,
            ILogger<DownloadWantedHandler> logger,
            DownloadWantedOptions options)
        {
            _integrationService = integrationService ?? throw new ArgumentNullException(nameof(integrationService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
            _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task ExecuteAsync()
        {
            try
            {
                // Load and validate configuration
                var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
                
                if (!ValidateConfiguration(config))
                {
                    return;
                }

                if (!ValidateOptions())
                {
                    return;
                }

                _uiService.ShowInfo("🔍 Starting Lidarr wanted albums download operation...");
                AnsiConsole.WriteLine();

                // Build filter options
                var filterOptions = BuildFilterOptions();
                var preferredQuality = GetQualityId(_options.Quality ?? "flac-max");

                // Handle show-profiles option
                if (_options.ShowProfiles)
                {
                    await ShowQualityProfilesAsync();
                    return;
                }

                _dashboard.Start("Fetching wanted albums from Lidarr...", _options.Limit);

                try
                {
                    // Step 1: Fetch wanted albums
                    var wantedAlbums = await FetchWantedAlbumsAsync(filterOptions);
                    
                    if (!wantedAlbums.Any())
                    {
                        _uiService.ShowWarning("📭 No wanted albums found matching the specified criteria");
                        return;
                    }

                    _uiService.ShowSuccess($"✅ Found {wantedAlbums.Count()} wanted albums");

                    // Step 2: Apply filters
                    var filteredAlbums = ApplyFilters(wantedAlbums);
                    
                    if (!filteredAlbums.Any())
                    {
                        _uiService.ShowWarning("📭 No albums remain after applying filters");
                        return;
                    }

                    _uiService.ShowSuccess($"✅ {filteredAlbums.Count()} albums passed filtering");
                    AnsiConsole.WriteLine();

                    if (_options.Verbose)
                    {
                        _uiService.DisplayAlbumSummary(filteredAlbums);
                    }

                    // Step 3: Search Qobuz
                    var albumMatches = await SearchQobuzAsync(filteredAlbums);
                    
                    if (!albumMatches.Any())
                    {
                        _uiService.ShowWarning("📭 No Qobuz matches found for the wanted albums");
                        return;
                    }

                    // Step 4: Validate albums
                    var validatedItems = await ValidateAlbumsAsync(albumMatches, preferredQuality);
                    
                    if (!validatedItems.Any())
                    {
                        _uiService.ShowWarning("📭 No albums passed validation for download");
                        return;
                    }

                    if (_options.Verbose)
                    {
                        _uiService.DisplayValidationSummary(validatedItems);
                        _uiService.DisplayQualityProfileSummary(validatedItems);
                    }

                    // Step 5: Handle dry-run or proceed
                    if (_options.DryRun)
                    {
                        _uiService.DisplayDryRunResults(validatedItems, _options.Immediate, _options.Quality);
                        return;
                    }

                    AnsiConsole.WriteLine();

                    // Step 6: Download or queue
                    if (_options.Immediate)
                    {
                        await PerformImmediateDownloadsAsync(validatedItems);
                    }
                    else
                    {
                        await AddToQueueAsync(validatedItems);
                    }
                }
                finally
                {
                    _dashboard.Stop();
                }
            }
            catch (Exception ex)
            {
                _uiService.ShowError($"Download-wanted operation failed: {ex.Message}", 
                    ex.ToString(), _options.Verbose);
                _logger.LogError(ex, "Download-wanted operation failed");
            }
        }

        private bool ValidateConfiguration(Models.Configuration.AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.LidarrUrl))
            {
                _uiService.ShowError("Lidarr URL not configured", 
                    "Use: qobuz config set lidarr-url http://your-lidarr-server:8686");
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.LidarrApiKey))
            {
                _uiService.ShowError("Lidarr API key not configured", 
                    "Use: qobuz config set lidarr-api-key YOUR_API_KEY");
                return false;
            }

            return true;
        }

        private bool ValidateOptions()
        {
            if (_options.Immediate && string.IsNullOrWhiteSpace(_options.OutputPath))
            {
                _uiService.ShowError("Output path required for immediate downloads", 
                    "Use --output /path/to/downloads or remove --immediate to queue downloads");
                return false;
            }

            if (_options.Immediate && !string.IsNullOrWhiteSpace(_options.OutputPath) && !Directory.Exists(_options.OutputPath))
            {
                _uiService.ShowWarning($"⚠️ Creating output directory: {_options.OutputPath}");
                Directory.CreateDirectory(_options.OutputPath);
            }

            return true;
        }

        private LidarrFilterOptions BuildFilterOptions()
        {
            var filterOptions = LidarrFilterOptions.ForWantedAlbums();
            
            if (_options.YearFrom.HasValue)
            {
                filterOptions.ReleaseDateFrom = new DateTime(_options.YearFrom.Value, 1, 1);
            }
            
            if (_options.YearTo.HasValue)
            {
                filterOptions.ReleaseDateTo = new DateTime(_options.YearTo.Value, 12, 31);
            }
            
            if (_options.LastDays.HasValue && _options.LastDays.Value > 0)
            {
                filterOptions.ReleaseDateFrom = DateTime.UtcNow.AddDays(-_options.LastDays.Value);
            }
            
            if (!string.IsNullOrWhiteSpace(_options.AlbumTypes))
            {
                var types = _options.AlbumTypes.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(t => t.Trim().ToLowerInvariant())
                                     .ToList();
                filterOptions.AlbumTypes = types;
            }
            
            return filterOptions;
        }

        private async Task<IEnumerable<LidarrAlbum>> FetchWantedAlbumsAsync(LidarrFilterOptions filterOptions)
        {
            _uiService.ShowInfo("📋 Fetching wanted albums from Lidarr...");
            
            return await _integrationService.GetFilteredWantedAlbumsAsync(
                filterOptions, _options.Limit, null, CancellationToken.None).ConfigureAwait(false);
        }

        private IEnumerable<LidarrAlbum> ApplyFilters(IEnumerable<LidarrAlbum> albums)
        {
            var filtered = albums.AsEnumerable();

            // Artist inclusion filter
            if (!string.IsNullOrWhiteSpace(_options.Artists))
            {
                var artistList = _options.Artists.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                       .Select(a => a.Trim().ToLowerInvariant())
                                       .ToHashSet();
                
                filtered = filtered.Where(album => 
                {
                    var albumArtist = album.Artist?.ArtistName?.ToLowerInvariant() ?? "";
                    return artistList.Any(artist => albumArtist.Contains(artist));
                });
            }

            // Artist exclusion filter
            if (!string.IsNullOrWhiteSpace(_options.ArtistsExclude))
            {
                var excludeList = _options.ArtistsExclude.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                               .Select(a => a.Trim().ToLowerInvariant())
                                               .ToHashSet();
                
                filtered = filtered.Where(album => 
                {
                    var albumArtist = album.Artist?.ArtistName?.ToLowerInvariant() ?? "";
                    return !excludeList.Any(artist => albumArtist.Contains(artist));
                });
            }

            // Track count filters
            if (_options.MinTracks.HasValue)
            {
                filtered = filtered.Where(album => (album.Statistics?.TrackFileCount ?? 0) >= _options.MinTracks.Value);
            }

            if (_options.MaxTracks.HasValue)
            {
                filtered = filtered.Where(album => (album.Statistics?.TrackFileCount ?? 0) <= _options.MaxTracks.Value);
            }

            return filtered.ToList();
        }

        private async Task<Dictionary<LidarrAlbum, QobuzAlbum>> SearchQobuzAsync(IEnumerable<LidarrAlbum> albums)
        {
            _uiService.ShowInfo("🔍 Searching Qobuz for matching albums...");
            
            var progress = new Progress<ProgressReport>(report =>
            {
                _dashboard.UpdateProgress(report.Completed, report.Completed, 0, 
                    report.CurrentItem ?? "Processing...");
            });

            return await _integrationService.SearchQobuzParallelAsync(
                albums, _options.Concurrency, progress, CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<IList<AlbumDownloadItem>> ValidateAlbumsAsync(
            Dictionary<LidarrAlbum, QobuzAlbum> albumMatches, int preferredQuality)
        {
            _uiService.ShowInfo("🔍 Validating albums for download...");
            
            var validated = await _integrationService.ValidateAlbumsAsync(
                albumMatches, preferredQuality, CancellationToken.None).ConfigureAwait(false);
            
            return validated.ToList();
        }

        private async Task PerformImmediateDownloadsAsync(IList<AlbumDownloadItem> validatedItems)
        {
            _uiService.ShowInfo("🎵 Starting immediate downloads...");
            
            var progress = new Progress<DownloadProgressReport>(report =>
            {
                _dashboard.UpdateProgress(report.Completed, report.SuccessCount, 
                    report.FailureCount, report.CurrentAlbum ?? "Downloading...");
            });

            var result = await _integrationService.DownloadLidarrAlbumsAsync(
                validatedItems, _options.OutputPath!, _options.Concurrency, 
                progress, CancellationToken.None).ConfigureAwait(false);

            AnsiConsole.WriteLine();
            _uiService.ShowSuccess($"✅ Download complete: {result.SuccessfulDownloads} successful, {result.FailedDownloads} failed");
            
            if (result.SuccessfulDownloads > 0)
            {
                var avgSpeedMB = result.AverageDownloadSpeed;
                var totalSizeGB = result.TotalBytesDownloaded / 1024.0 / 1024.0 / 1024.0;
                
                AnsiConsole.MarkupLine($"[dim]Total size: {totalSizeGB:F2} GB, Average speed: {avgSpeedMB:F2} MB/s[/]");
            }

            if (_options.Verbose && result.FailureItems.Any())
            {
                DisplayFailures(result.FailureItems);
            }
        }

        private async Task AddToQueueAsync(IList<AlbumDownloadItem> validatedItems)
        {
            _uiService.ShowInfo("📥 Adding albums to download queue...");
            
            var successCount = 0;
            var failedCount = 0;

            foreach (var item in validatedItems)
            {
                try
                {
                    var queuedDownload = new QobuzCLI.Models.QueuedDownload
                    {
                        Id = Guid.NewGuid().ToString(),
                        SearchQuery = $"{item.LidarrAlbum.Artist?.ArtistName} - {item.LidarrAlbum.Title}",
                        SearchType = QobuzCLI.Models.SearchType.Album,
                        SearchResultId = item.QobuzAlbum.Id,
                        Priority = 0,
                        Status = QobuzCLI.Models.QueueStatus.Pending
                    };

                    var queues = _queueService.GetQueues();
                    var defaultQueue = queues.FirstOrDefault() ?? 
                        await _queueService.CreateQueueAsync("Default").ConfigureAwait(false);
                    
                    await _queueService.AddToQueueAsync(defaultQueue.Id, queuedDownload).ConfigureAwait(false);
                    successCount++;

                    if (_options.Verbose)
                    {
                        AnsiConsole.MarkupLine($"[dim]✅ Queued: {item.LidarrAlbum.Artist?.ArtistName} - {item.LidarrAlbum.Title}[/]");
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    
                    if (_options.Verbose)
                    {
                        _uiService.ShowError($"Failed to queue: {item.LidarrAlbum.Artist?.ArtistName} - {item.LidarrAlbum.Title}: {ex.Message}");
                    }
                    
                    _logger.LogError(ex, "Failed to add album to queue: {Artist} - {Album}", 
                        item.LidarrAlbum.Artist?.ArtistName, item.LidarrAlbum.Title);
                }
            }

            AnsiConsole.WriteLine();
            _uiService.ShowSuccess($"✅ Queue operation complete: {successCount} added, {failedCount} failed");
            
            if (successCount > 0)
            {
                AnsiConsole.MarkupLine("[dim]Use 'qobuz queue start' to begin downloading the queued items[/]");
            }
        }

        private void DisplayFailures(IList<DownloadFailureItem> failures)
        {
            AnsiConsole.WriteLine();
            _uiService.ShowError("Failed downloads:");
            
            foreach (var failure in failures.Take(5))
            {
                AnsiConsole.MarkupLine($"[dim]• {failure.OriginalItem.LidarrAlbum.Artist?.ArtistName} - " +
                    $"{failure.OriginalItem.LidarrAlbum.Title}: {failure.FailureReason}[/]");
            }
            
            if (failures.Count > 5)
            {
                AnsiConsole.MarkupLine($"[dim]... and {failures.Count - 5} more failures[/]");
            }
        }

        private async Task ShowQualityProfilesAsync()
        {
            _uiService.ShowWarning("Quality profile display functionality is not yet implemented in CLI.");
            AnsiConsole.MarkupLine("[dim]Quality profiles are automatically used by the integration service when processing albums.[/]");
            await Task.CompletedTask;
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
    }

    /// <summary>
    /// Options for the download-wanted command.
    /// </summary>
    public class DownloadWantedOptions
    {
        public int? YearFrom { get; set; }
        public int? YearTo { get; set; }
        public int? LastDays { get; set; }
        public string Artists { get; set; }
        public string ArtistsExclude { get; set; }
        public string AlbumTypes { get; set; }
        public int? MinTracks { get; set; }
        public int? MaxTracks { get; set; }
        public bool Immediate { get; set; }
        public string Quality { get; set; }
        public string QualityProfile { get; set; }
        public bool IgnoreProfiles { get; set; }
        public bool ShowProfiles { get; set; }
        public int Limit { get; set; } = 50;
        public bool DryRun { get; set; }
        public int Concurrency { get; set; } = 0;
        public string OutputPath { get; set; }
        public bool Verbose { get; set; }
    }
}