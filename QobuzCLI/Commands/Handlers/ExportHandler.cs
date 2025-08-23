using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services;
using QobuzCLI.Services;

namespace QobuzCLI.Commands.Handlers
{
    /// <summary>
    /// Handler for the 'lidarr export' command.
    /// Delegates business logic to plugin services while handling CLI-specific presentation.
    /// </summary>
    public class ExportHandler : ILidarrCommandHandler
    {
        private readonly ILidarrIntegrationService _integrationService;
        private readonly ILidarrExportService _exportService;
        private readonly IConfigService _configService;
        private readonly IDashboard _dashboard;
        private readonly ILogger<ExportHandler> _logger;
        
        private readonly ExportOptions _options;

        public ExportHandler(
            ILidarrIntegrationService integrationService,
            ILidarrExportService exportService,
            IConfigService configService,
            IDashboard dashboard,
            ILogger<ExportHandler> logger,
            ExportOptions options)
        {
            _integrationService = integrationService ?? throw new ArgumentNullException(nameof(integrationService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task ExecuteAsync()
        {
            try
            {
                AnsiConsole.MarkupLine("[blue]📤 Starting Lidarr wanted albums export...[/]");
                AnsiConsole.WriteLine();

                // Load and validate configuration
                var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
                
                var url = _options.UrlOverride ?? config.LidarrUrl;
                var apiKey = _options.ApiKeyOverride ?? config.LidarrApiKey;

                if (!ValidateConfiguration(url, apiKey))
                {
                    return;
                }

                if (!ValidateFormat(_options.Format))
                {
                    return;
                }

                if (_options.Verbose)
                {
                    DisplayExportSettings(url);
                }

                _dashboard.Start("Fetching wanted albums from Lidarr...", _options.Limit ?? 1000);

                try
                {
                    // Fetch wanted albums
                    var albums = await FetchWantedAlbumsAsync();
                    
                    if (!albums.Any())
                    {
                        AnsiConsole.MarkupLine("[yellow]📭 No wanted albums found matching the specified criteria[/]");
                        return;
                    }

                    AnsiConsole.MarkupLine($"[green]✅ Found {albums.Count()} wanted albums[/]");

                    // Apply filters
                    var filteredAlbums = ApplyFilters(albums);
                    
                    if (!filteredAlbums.Any())
                    {
                        AnsiConsole.MarkupLine("[yellow]📭 No albums remain after applying filters[/]");
                        return;
                    }

                    AnsiConsole.MarkupLine($"[green]✅ {filteredAlbums.Count()} albums passed filtering[/]");

                    // Optimize order if requested
                    var finalAlbums = _options.OptimizeOrder 
                        ? _exportService.OptimizeAlbumOrder(filteredAlbums).ToList()
                        : filteredAlbums.ToList();

                    // Apply limit
                    if (_options.Limit.HasValue && finalAlbums.Count > _options.Limit.Value)
                    {
                        finalAlbums = finalAlbums.Take(_options.Limit.Value).ToList();
                        AnsiConsole.MarkupLine($"[yellow]⚠️ Limited to first {_options.Limit.Value} albums[/]");
                    }

                    // Export albums
                    await ExportAlbumsAsync(finalAlbums);

                    // Display summary
                    DisplayExportSummary(finalAlbums);
                }
                finally
                {
                    _dashboard.Stop();
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]❌ Export operation failed: {ex.Message}[/]");
                _logger.LogError(ex, "Export operation failed");
                
                if (_options.Verbose)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.WriteException(ex);
                }
            }
        }

        private bool ValidateConfiguration(string url, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                AnsiConsole.MarkupLine("[red]❌ Lidarr URL not configured[/]");
                AnsiConsole.MarkupLine("[dim]Use: qobuz config set lidarr-url http://your-lidarr-server:8686[/]");
                AnsiConsole.MarkupLine("[dim]Or use: --url http://your-lidarr-server:8686[/]");
                return false;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                AnsiConsole.MarkupLine("[red]❌ Lidarr API key not configured[/]");
                AnsiConsole.MarkupLine("[dim]Use: qobuz config set lidarr-api-key YOUR_API_KEY[/]");
                AnsiConsole.MarkupLine("[dim]Or use: --api-key YOUR_API_KEY[/]");
                return false;
            }

            return true;
        }

        private bool ValidateFormat(string format)
        {
            var validFormats = new[] { "json", "txt", "csv" };
            if (!validFormats.Contains(format.ToLower()))
            {
                AnsiConsole.MarkupLine($"[red]❌ Invalid format '{format}'. Supported formats: json, txt, csv[/]");
                return false;
            }
            return true;
        }

        private void DisplayExportSettings(string url)
        {
            AnsiConsole.MarkupLine($"[dim]Lidarr URL: {url}[/]");
            AnsiConsole.MarkupLine($"[dim]Output file: {_options.OutputFile}[/]");
            AnsiConsole.MarkupLine($"[dim]Format: {_options.Format.ToUpper()}[/]");
            AnsiConsole.WriteLine();
        }

        private async Task<IEnumerable<LidarrAlbum>> FetchWantedAlbumsAsync()
        {
            AnsiConsole.MarkupLine("[cyan]📋 Fetching wanted albums from Lidarr...[/]");
            
            var filterOptions = BuildFilterOptions();
            
            var progressReporter = new Progress<ProgressReport>(progress =>
            {
                _dashboard.UpdateProgress(
                    processed: progress.Completed,
                    success: progress.Completed,
                    failed: 0,
                    currentItem: progress.CurrentItem ?? "",
                    lastSuccessful: progress.Phase == "Fetch Complete" 
                        ? $"Completed {progress.Completed} albums" : ""
                );
            });
            
            return await _integrationService.GetFilteredWantedAlbumsAsync(
                filterOptions, 
                _options.Limit ?? int.MaxValue, 
                progressReporter).ConfigureAwait(false);
        }

        private LidarrFilterOptions BuildFilterOptions()
        {
            var filterOptions = LidarrFilterOptions.ForWantedAlbums();
            
            if (_options.MinYear.HasValue)
            {
                filterOptions.ReleaseDateFrom = new DateTime(_options.MinYear.Value, 1, 1);
            }
            
            if (_options.MaxYear.HasValue)
            {
                filterOptions.ReleaseDateTo = new DateTime(_options.MaxYear.Value, 12, 31);
            }
            
            if (!string.IsNullOrWhiteSpace(_options.FilterType))
            {
                var types = _options.FilterType.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(t => t.Trim().ToLowerInvariant())
                                     .ToList();
                filterOptions.AlbumTypes = types;
            }
            
            return filterOptions;
        }

        private IEnumerable<LidarrAlbum> ApplyFilters(IEnumerable<LidarrAlbum> albums)
        {
            var filtered = albums.AsEnumerable();
            var originalCount = albums.Count();

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

                if (_options.Verbose)
                {
                    var afterCount = filtered.Count();
                    AnsiConsole.MarkupLine($"[dim]Artist filter: {originalCount} → {afterCount} albums[/]");
                }
            }

            return filtered.ToList();
        }

        private async Task ExportAlbumsAsync(List<LidarrAlbum> albums)
        {
            AnsiConsole.MarkupLine("[cyan]📄 Creating export data...[/]");
            
            var format = Enum.Parse<ExportFormat>(_options.Format, ignoreCase: true);
            var exportData = await _exportService.ExportAlbumsAsync(
                albums, format, _options.IncludeMetadata).ConfigureAwait(false);

            AnsiConsole.MarkupLine($"[cyan]💾 Writing to {_options.Format.ToUpper()} file...[/]");
            
            var directory = Path.GetDirectoryName(_options.OutputFile);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(_options.OutputFile, exportData).ConfigureAwait(false);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]✅ Successfully exported {albums.Count} wanted albums to {_options.OutputFile}[/]");
        }

        private void DisplayExportSummary(List<LidarrAlbum> albums)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[cyan]📊 Export Summary:[/]");

            // Summary by album type
            var typeGroups = albums.GroupBy(a => a.AlbumType?.ToLowerInvariant() ?? "unknown")
                                   .OrderByDescending(g => g.Count());

            var table = new Table();
            table.Border = TableBorder.Rounded;
            table.AddColumn("Album Type");
            table.AddColumn("Count");

            foreach (var group in typeGroups)
            {
                table.AddRow(group.Key, group.Count().ToString());
            }

            AnsiConsole.Write(table);

            if (_options.Verbose)
            {
                DisplayYearDistribution(albums);
            }

            DisplayUsageInstructions();
        }

        private void DisplayYearDistribution(List<LidarrAlbum> albums)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[cyan]📅 Release Year Distribution:[/]");
            
            var yearGroups = albums.Where(a => a.ReleaseDate.HasValue)
                                  .GroupBy(a => a.ReleaseDate!.Value.Year / 10 * 10) // Group by decade
                                  .OrderBy(g => g.Key);

            foreach (var group in yearGroups.Take(5))
            {
                var decade = $"{group.Key}s";
                AnsiConsole.MarkupLine($"[dim]• {decade}: {group.Count()} albums[/]");
            }
        }

        private void DisplayUsageInstructions()
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]💡 Usage with Qobuz CLI:[/]");
            
            switch (_options.Format.ToLower())
            {
                case "json":
                    AnsiConsole.MarkupLine($"[dim]qobuz download --from-file {_options.OutputFile}[/]");
                    break;
                case "txt":
                    AnsiConsole.MarkupLine("[dim]Use the text file to manually copy search queries[/]");
                    break;
                case "csv":
                    AnsiConsole.MarkupLine("[dim]Import into spreadsheet for analysis and filtering[/]");
                    break;
            }
        }
    }

    /// <summary>
    /// Options for the export command.
    /// </summary>
    public class ExportOptions
    {
        public string UrlOverride { get; set; }
        public string ApiKeyOverride { get; set; }
        public string OutputFile { get; set; } = "lidarr-wanted-albums.json";
        public string Format { get; set; } = "json";
        public int? Limit { get; set; }
        public string FilterType { get; set; }
        public int? MinYear { get; set; }
        public int? MaxYear { get; set; }
        public string Artists { get; set; }
        public bool OptimizeOrder { get; set; } = true;
        public bool IncludeMetadata { get; set; } = true;
        public bool Verbose { get; set; }
    }
}