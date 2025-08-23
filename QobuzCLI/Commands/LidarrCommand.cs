using System.CommandLine;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using QobuzCLI.Models.Configuration;
using QobuzCLI.Services;
using QobuzCLI.Services.Logging;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Integration;

namespace QobuzCLI.Commands;

/// <summary>
/// Options for the test-connection subcommand.
/// </summary>
record TestConnectionOptions(
    string? Url,
    string? ApiKey, 
    int Timeout,
    bool Verbose
);

/// <summary>
/// Options for the export subcommand.
/// </summary>
record ExportOptions(
    string? Url,
    string? ApiKey,
    string Output,
    string Format,
    int? Limit,
    string? FilterType,
    int? MinYear,
    int? MaxYear,
    string? Artists,
    bool OptimizeOrder,
    bool IncludeMetadata,
    bool Verbose
);

/// <summary>
/// Options for the download-wanted subcommand.
/// </summary>
record DownloadWantedOptions(
    int? YearFrom,
    int? YearTo, 
    int? LastDays,
    string? Artists,
    string? ArtistsExclude,
    string? AlbumTypes,
    int? MinTracks,
    int? MaxTracks,
    bool Immediate,
    string? Quality,
    string? QualityProfile,
    bool IgnoreProfiles,
    bool ShowProfiles,
    int Limit,
    bool DryRun,
    int Concurrency,
    string? OutputPath,
    bool Verbose
);

/// <summary>
/// Implements the 'lidarr' command for Lidarr integration functionality.
/// This is a thin CLI wrapper around the plugin's LidarrIntegrationService.
/// Follows the plugin-first architecture where CLI only adds UI/UX while delegating core functionality.
/// </summary>
/// <remarks>
/// Architecture principle: CLI wraps plugin functionality, never reimplements it.
/// - Uses plugin's LidarrIntegrationService for all core Lidarr operations
/// - Uses plugin's LidarrApiClient for communication with Lidarr
/// - Adds CLI-specific features: command structure, console output, error handling
/// - Maintains separation: plugin = core logic, CLI = user interface
/// </remarks>
public class LidarrCommand
{
    private readonly IConfigService _configService;
    private readonly LidarrCredentialService _credentialService;
    private readonly IDashboardLogger _logger;
    private readonly ILidarrIntegrationService _lidarrIntegrationService;
    private readonly IQueueService _queueService;
    private readonly Dashboard _dashboard;

    public Command Command { get; }

    public LidarrCommand(
        IConfigService configService,
        LidarrCredentialService credentialService,
        ILogger<LidarrCommand> logger,
        ILidarrIntegrationService lidarrIntegrationService,
        IQueueService queueService,
        Dashboard dashboard)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _credentialService = credentialService ?? throw new ArgumentNullException(nameof(credentialService));
        _lidarrIntegrationService = lidarrIntegrationService ?? throw new ArgumentNullException(nameof(lidarrIntegrationService));
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        _logger = logger as IDashboardLogger ?? 
                  new DashboardLogger<LidarrCommand>(logger, dashboard);
        
        Command = CreateCommand();
    }

    private Command CreateCommand()
    {
        var lidarrCommand = new Command("lidarr", "Integrate with Lidarr to export and download wanted albums");

        // Create subcommands
        var testConnectionCommand = CreateTestConnectionCommand();
        var exportCommand = CreateExportCommand();
        var downloadWantedCommand = CreateDownloadWantedCommand();

        lidarrCommand.AddCommand(testConnectionCommand);
        lidarrCommand.AddCommand(exportCommand);
        lidarrCommand.AddCommand(downloadWantedCommand);

        return lidarrCommand;
    }

    private Command CreateTestConnectionCommand()
    {
        var command = new Command("test-connection", "Test connection to Lidarr and verify configuration");
        
        var urlOption = new Option<string?>("--url", "Lidarr server URL (overrides config)");
        var apiKeyOption = new Option<string?>("--api-key", "Lidarr API key (overrides config)");
        var timeoutOption = new Option<int>("--timeout", () => 30, "Connection timeout in seconds");
        var verboseOption = new Option<bool>("--verbose", "Show detailed connection information");

        command.AddOption(urlOption);
        command.AddOption(apiKeyOption);
        command.AddOption(timeoutOption);
        command.AddOption(verboseOption);

        command.SetHandler(async (context) =>
        {
            var options = new TestConnectionOptions(
                context.ParseResult.GetValueForOption(urlOption),
                context.ParseResult.GetValueForOption(apiKeyOption),
                context.ParseResult.GetValueForOption(timeoutOption),
                context.ParseResult.GetValueForOption(verboseOption)
            );
            
            await HandleTestConnectionAsync(options).ConfigureAwait(false);
        });

        return command;
    }

    private Command CreateExportCommand()
    {
        var command = new Command("export", "Export wanted albums from Lidarr to various formats");
        
        // URL and API key options
        var urlOption = new Option<string?>("--url", "Lidarr server URL (overrides config)");
        var apiKeyOption = new Option<string?>("--api-key", "Lidarr API key (overrides config)");
        
        // Output options
        var outputOption = new Option<string>("--output", () => "lidarr-wanted-albums.json", "Output file path");
        var formatOption = new Option<string>("--format", () => "json", "Output format: json, txt, csv");
        
        // Filter options
        var limitOption = new Option<int?>("--limit", "Limit number of albums to export");
        var filterTypeOption = new Option<string?>("--filter-type", "Filter by album type (album, single, ep)");
        var minYearOption = new Option<int?>("--min-year", "Minimum release year");
        var maxYearOption = new Option<int?>("--max-year", "Maximum release year");
        var artistsOption = new Option<string?>("--artists", "Comma-separated list of artists to include");
        
        // Export behavior options
        var optimizeOrderOption = new Option<bool>("--optimize-order", () => true, "Optimize album order for efficient downloading");
        var includeMetadataOption = new Option<bool>("--include-metadata", () => true, "Include detailed metadata in export");
        var verboseOption = new Option<bool>("--verbose", "Show detailed export information");
        
        command.AddOption(urlOption);
        command.AddOption(apiKeyOption);
        command.AddOption(outputOption);
        command.AddOption(formatOption);
        command.AddOption(limitOption);
        command.AddOption(filterTypeOption);
        command.AddOption(minYearOption);
        command.AddOption(maxYearOption);
        command.AddOption(artistsOption);
        command.AddOption(optimizeOrderOption);
        command.AddOption(includeMetadataOption);
        command.AddOption(verboseOption);

        command.SetHandler(async (context) =>
        {
            var options = new ExportOptions(
                context.ParseResult.GetValueForOption(urlOption),
                context.ParseResult.GetValueForOption(apiKeyOption),
                context.ParseResult.GetValueForOption(outputOption)!,
                context.ParseResult.GetValueForOption(formatOption)!,
                context.ParseResult.GetValueForOption(limitOption),
                context.ParseResult.GetValueForOption(filterTypeOption),
                context.ParseResult.GetValueForOption(minYearOption),
                context.ParseResult.GetValueForOption(maxYearOption),
                context.ParseResult.GetValueForOption(artistsOption),
                context.ParseResult.GetValueForOption(optimizeOrderOption),
                context.ParseResult.GetValueForOption(includeMetadataOption),
                context.ParseResult.GetValueForOption(verboseOption)
            );
            
            await HandleExportAsync(options).ConfigureAwait(false);
        });

        return command;
    }

    private Command CreateDownloadWantedCommand()
    {
        var command = new Command("download-wanted", "Download wanted albums from Lidarr via Qobuz");
        
        // Date filter options
        var yearFromOption = new Option<int?>("--year-from", "Filter albums from this year onwards");
        var yearToOption = new Option<int?>("--year-to", "Filter albums up to this year");
        var lastDaysOption = new Option<int?>("--last-days", "Filter albums added in the last N days");
        
        // Artist filter options
        var artistsOption = new Option<string?>("--artists", "Comma-separated list of artists to include");
        var artistsExcludeOption = new Option<string?>("--artists-exclude", "Comma-separated list of artists to exclude");
        
        // Album filter options
        var albumTypesOption = new Option<string?>("--album-types", "Comma-separated album types (album,ep,single,compilation)");
        var minTracksOption = new Option<int?>("--min-tracks", "Minimum number of tracks per album");
        var maxTracksOption = new Option<int?>("--max-tracks", "Maximum number of tracks per album");
        
        // Download behavior options
        var immediateOption = new Option<bool>("--immediate", "Download immediately instead of queueing");
        var qualityOption = new Option<string?>("--quality", "Override quality setting (mp3-320, flac-cd, flac-hires, flac-max)");
        var qualityProfileOption = new Option<string?>("--quality-profile", "Use specific Lidarr quality profile name or ID");
        var ignoreProfilesOption = new Option<bool>("--ignore-profiles", "Ignore Lidarr quality profiles and use default quality");
        var showProfilesOption = new Option<bool>("--show-profiles", "List available quality profiles and exit");
        var limitOption = new Option<int>("--limit", () => 50, "Maximum number of albums to process");
        var dryRunOption = new Option<bool>("--dry-run", "Show what would be downloaded without downloading");
        var concurrencyOption = new Option<int>("--concurrency", () => 0, "Max concurrent operations (0 = auto)");
        
        // Output options
        var outputPathOption = new Option<string?>("--output", "Output directory for downloads (required for immediate downloads)");
        var verboseOption = new Option<bool>("--verbose", "Show detailed progress information");
        
        command.AddOption(yearFromOption);
        command.AddOption(yearToOption);
        command.AddOption(lastDaysOption);
        command.AddOption(artistsOption);
        command.AddOption(artistsExcludeOption);
        command.AddOption(albumTypesOption);
        command.AddOption(minTracksOption);
        command.AddOption(maxTracksOption);
        command.AddOption(immediateOption);
        command.AddOption(qualityOption);
        command.AddOption(qualityProfileOption);
        command.AddOption(ignoreProfilesOption);
        command.AddOption(showProfilesOption);
        command.AddOption(limitOption);
        command.AddOption(dryRunOption);
        command.AddOption(concurrencyOption);
        command.AddOption(outputPathOption);
        command.AddOption(verboseOption);

        command.SetHandler(async (context) =>
        {
            var options = new DownloadWantedOptions(
                context.ParseResult.GetValueForOption(yearFromOption),
                context.ParseResult.GetValueForOption(yearToOption),
                context.ParseResult.GetValueForOption(lastDaysOption),
                context.ParseResult.GetValueForOption(artistsOption),
                context.ParseResult.GetValueForOption(artistsExcludeOption),
                context.ParseResult.GetValueForOption(albumTypesOption),
                context.ParseResult.GetValueForOption(minTracksOption),
                context.ParseResult.GetValueForOption(maxTracksOption),
                context.ParseResult.GetValueForOption(immediateOption),
                context.ParseResult.GetValueForOption(qualityOption),
                context.ParseResult.GetValueForOption(qualityProfileOption),
                context.ParseResult.GetValueForOption(ignoreProfilesOption),
                context.ParseResult.GetValueForOption(showProfilesOption),
                context.ParseResult.GetValueForOption(limitOption),
                context.ParseResult.GetValueForOption(dryRunOption),
                context.ParseResult.GetValueForOption(concurrencyOption),
                context.ParseResult.GetValueForOption(outputPathOption),
                context.ParseResult.GetValueForOption(verboseOption)
            );
            
            await HandleDownloadWantedAsync(options).ConfigureAwait(false);
        });

        return command;
    }

    private async Task HandleDownloadWantedAsync(DownloadWantedOptions options)
    {
        try
        {
            // Load configuration from unified config system
            var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
            
            if (string.IsNullOrWhiteSpace(config.LidarrUrl))
            {
                AnsiConsole.MarkupLine("[red]❌ Lidarr URL not configured[/]");
                AnsiConsole.MarkupLine("[dim]Use: qobuz config set lidarr-url http://your-lidarr-server:8686[/]");
                return;
            }

            if (string.IsNullOrWhiteSpace(config.LidarrApiKey))
            {
                AnsiConsole.MarkupLine("[red]❌ Lidarr API key not configured[/]");
                AnsiConsole.MarkupLine("[dim]Use: qobuz config set lidarr-api-key YOUR_API_KEY[/]");
                return;
            }

            // Validate output path for immediate downloads
            if (options.Immediate && string.IsNullOrWhiteSpace(options.OutputPath))
            {
                AnsiConsole.MarkupLine("[red]❌ Output path required for immediate downloads[/]");
                AnsiConsole.MarkupLine("[dim]Use --output /path/to/downloads or remove --immediate to queue downloads[/]");
                return;
            }

            if (options.Immediate && !string.IsNullOrWhiteSpace(options.OutputPath) && !Directory.Exists(options.OutputPath))
            {
                AnsiConsole.MarkupLine($"[yellow]⚠️ Creating output directory: {options.OutputPath}[/]");
                Directory.CreateDirectory(options.OutputPath);
            }

            AnsiConsole.MarkupLine("[blue]🔍 Starting Lidarr wanted albums download operation...[/]");
            AnsiConsole.WriteLine();

            // Build filter options
            var filterOptions = BuildFilterOptions(options.YearFrom, options.YearTo, options.LastDays, options.AlbumTypes);
            
            // Parse quality setting
            var preferredQuality = GetQualityId(options.Quality ?? "flac-max");

            // Handle show-profiles option first
            if (options.ShowProfiles)
            {
                AnsiConsole.MarkupLine("[yellow]Quality profile display functionality is not yet implemented in CLI.[/]");
                AnsiConsole.MarkupLine("[dim]Quality profiles are automatically used by the integration service when processing albums.[/]");
                return;
            }

            _dashboard.Start("Fetching wanted albums from Lidarr...", options.Limit);

            try
            {
                // Step 1: Fetch wanted albums from Lidarr
                AnsiConsole.MarkupLine("[cyan]📋 Fetching wanted albums from Lidarr...[/]");
                
                var wantedAlbums = await _lidarrIntegrationService.GetFilteredWantedAlbumsAsync(
                    filterOptions, options.Limit, null, CancellationToken.None).ConfigureAwait(false);

                if (!wantedAlbums.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]📭 No wanted albums found matching the specified criteria[/]");
                    return;
                }

                AnsiConsole.MarkupLine($"[green]✅ Found {wantedAlbums.Count()} wanted albums[/]");

                // Step 2: Apply CLI-specific filters
                var filteredAlbums = ApplyCliFilters(wantedAlbums, options.Artists, options.ArtistsExclude, 
                    options.MinTracks, options.MaxTracks, options.Verbose);

                if (!filteredAlbums.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]📭 No albums remain after applying filters[/]");
                    return;
                }

                AnsiConsole.MarkupLine($"[green]✅ {filteredAlbums.Count()} albums passed filtering[/]");
                AnsiConsole.WriteLine();

                if (options.Verbose)
                {
                    DisplayAlbumSummary(filteredAlbums);
                }

                // Step 3: Search Qobuz for matching albums
                AnsiConsole.MarkupLine("[cyan]🔍 Searching Qobuz for matching albums...[/]");
                
                var progress = new Progress<ProgressReport>(report =>
                {
                    _dashboard.UpdateProgress(report.Completed, report.Completed, 0, report.CurrentItem ?? "Processing...");
                });

                var albumMatches = await _lidarrIntegrationService.SearchQobuzParallelAsync(
                    filteredAlbums, options.Concurrency, progress, CancellationToken.None).ConfigureAwait(false);

                AnsiConsole.MarkupLine($"[green]✅ Found {albumMatches.Count} Qobuz matches out of {filteredAlbums.Count()} albums[/]");

                if (!albumMatches.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]📭 No Qobuz matches found for the wanted albums[/]");
                    return;
                }

                // Step 4: Validate albums for download
                AnsiConsole.MarkupLine("[cyan]🔍 Validating albums for download...[/]");
                
                var validatedItems = await _lidarrIntegrationService.ValidateAlbumsAsync(
                    albumMatches, preferredQuality, CancellationToken.None).ConfigureAwait(false);

                var validatedList = validatedItems.ToList();
                AnsiConsole.MarkupLine($"[green]✅ {validatedList.Count} albums ready for download[/]");

                if (!validatedList.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]📭 No albums passed validation for download[/]");
                    return;
                }

                if (options.Verbose)
                {
                    DisplayValidationSummary(validatedList);
                    DisplayQualityProfileSummary(validatedList);
                }

                // Step 5: Show dry-run results or proceed with download/queue
                if (options.DryRun)
                {
                    DisplayDryRunResults(validatedList, options.Immediate, options.Quality);
                    return;
                }

                AnsiConsole.WriteLine();

                if (options.Immediate)
                {
                    // Step 6a: Download immediately
                    await PerformImmediateDownloads(validatedList, options.OutputPath!, options.Concurrency, options.Verbose);
                }
                else
                {
                    // Step 6b: Add to queue
                    await AddToDownloadQueue(validatedList, options.Quality, options.Verbose);
                }
            }
            finally
            {
                _dashboard.Stop();
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Download-wanted operation failed: {ex.Message}[/]");
            _logger.LogError(ex, "Download-wanted operation failed");
            
            if (options.Verbose)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.WriteException(ex);
            }
        }
    }

    private async Task HandleTestConnectionAsync(TestConnectionOptions options)
    {
        try
        {
            AnsiConsole.MarkupLine("[blue]🔍 Testing Lidarr connection...[/]");
            AnsiConsole.WriteLine();

            // Load configuration from unified config system
            var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
            
            // Use values from unified config or overrides
            var url = options.Url ?? config.LidarrUrl;
            var apiKey = options.ApiKey ?? config.LidarrApiKey;
            var timeoutSeconds = options.Timeout;

            if (!string.IsNullOrWhiteSpace(options.Url) && options.Verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Using URL override: {options.Url}[/]");
            }
            else if (!string.IsNullOrWhiteSpace(config.LidarrUrl) && options.Verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Using configured URL: {config.LidarrUrl}[/]");
            }

            if (!string.IsNullOrWhiteSpace(options.ApiKey) && options.Verbose)
            {
                AnsiConsole.MarkupLine("[dim]Using API key override[/]");
            }
            else if (!string.IsNullOrWhiteSpace(config.LidarrApiKey) && options.Verbose)
            {
                AnsiConsole.MarkupLine("[dim]Using configured API key[/]");
            }

            // Validate basic configuration
            AnsiConsole.MarkupLine("[cyan]📋 Validating configuration...[/]");
            
            if (string.IsNullOrWhiteSpace(url))
            {
                AnsiConsole.MarkupLine("[red]❌ Lidarr URL not configured[/]");
                AnsiConsole.MarkupLine("[dim]Use: qobuz config set lidarr-url http://your-lidarr-server:8686[/]");
                return;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                AnsiConsole.MarkupLine("[red]❌ Lidarr API key not configured[/]");
                AnsiConsole.MarkupLine("[dim]Use: qobuz config set lidarr-api-key YOUR_API_KEY[/]");
                return;
            }

            if (options.Verbose)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] URL: {url.TrimEnd('/')}");
                AnsiConsole.MarkupLine($"[green]✓[/] API Key: ***{apiKey.Substring(Math.Max(0, apiKey.Length - 4))}");
                AnsiConsole.MarkupLine($"[green]✓[/] Timeout: {timeoutSeconds}s");
                AnsiConsole.WriteLine();
            }

            // Note: This CLI command performs basic HTTP connectivity testing.
            // Full integration with the plugin's LidarrApiClient occurs when the plugin is loaded within Lidarr.
            AnsiConsole.MarkupLine("[cyan]🌐 Testing connection to Lidarr...[/]");
            
            // Use service for connection testing - maintains CLI adapter pattern
            bool connectionSuccess = false;
            
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Connecting to Lidarr...", async ctx =>
                {
                    try
                    {
                        // Use the service for connection testing
                        var filterOptions = new Lidarr.Plugin.Qobuzarr.Models.Lidarr.LidarrFilterOptions 
                        { 
                            Page = 1, 
                            PageSize = 1 
                        };
                        var testResponse = await _lidarrIntegrationService.GetFilteredWantedAlbumsAsync(filterOptions, 1);
                        connectionSuccess = testResponse != null;
                        
                        if (connectionSuccess && options.Verbose)
                        {
                            ctx.Status = "Connection validated";
                            await Task.Delay(500).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to connect to Lidarr");
                        connectionSuccess = false;
                    }
                });

            if (connectionSuccess)
            {
                AnsiConsole.MarkupLine("[green]✅ Successfully connected to Lidarr[/]");
                
                if (options.Verbose)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[cyan]📊 Lidarr Connection Information:[/]");
                    
                    var table = new Table();
                    table.Border = TableBorder.Rounded;
                    table.AddColumn("Property");
                    table.AddColumn("Value");
                    
                    table.AddRow("URL", url);
                    table.AddRow("API Key", $"{apiKey[..Math.Min(8, apiKey.Length)]}...");
                    table.AddRow("Status", "Connected");
                    
                    AnsiConsole.Write(table);
                }

                // Test API permissions
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[cyan]🔐 Testing API permissions...[/]");
                
                try
                {
                    // Try to get wanted albums to test read permissions
                    var filterOptions = new Lidarr.Plugin.Qobuzarr.Models.Lidarr.LidarrFilterOptions 
                    { 
                        Page = 1, 
                        PageSize = 1 
                    };
                    var albums = await _lidarrIntegrationService.GetFilteredWantedAlbumsAsync(filterOptions, 1).ConfigureAwait(false);
                    
                    if (albums != null)
                    {
                        var albumCount = albums.Count();
                        AnsiConsole.MarkupLine($"[green]✅ API permissions verified[/]");
                        
                        if (albumCount > 0)
                        {
                            AnsiConsole.MarkupLine($"[dim]Found {albumCount} wanted album(s) in test query[/]");
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[dim]No wanted albums found (this is normal for a fresh Lidarr setup)[/]");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("Failed to retrieve wanted albums from API");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine("[red]❌ API permission test failed[/]");
                    AnsiConsole.MarkupLine($"[dim]Error: {ex.Message}[/]");
                    
                    _logger.LogError(ex, "API permission test failed");
                    
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[yellow]💡 Troubleshooting suggestions:[/]");
                    AnsiConsole.MarkupLine("[dim]• Verify the API key has read permissions[/]");
                    AnsiConsole.MarkupLine("[dim]• Check that Lidarr is running and accessible[/]");
                    AnsiConsole.MarkupLine("[dim]• Ensure the URL includes the correct port (usually 8686)[/]");
                    return;
                }

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[green]🎉 Lidarr connection test completed successfully![/]");
                AnsiConsole.MarkupLine("[dim]You can now use other Lidarr integration commands[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[red]❌ Failed to connect to Lidarr[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]💡 Troubleshooting suggestions:[/]");
                AnsiConsole.MarkupLine("[dim]• Check that Lidarr is running and accessible[/]");
                AnsiConsole.MarkupLine("[dim]• Verify the URL is correct (e.g., http://localhost:8686)[/]");
                AnsiConsole.MarkupLine("[dim]• Ensure the API key is valid and has proper permissions[/]");
                AnsiConsole.MarkupLine("[dim]• Check firewall settings if accessing remotely[/]");
                AnsiConsole.MarkupLine("[dim]• Try increasing the timeout with --timeout if connection is slow[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Test connection failed: {ex.Message}[/]");
            _logger.LogError(ex, "Test connection failed");
            
            if (options.Verbose)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.WriteException(ex);
            }
        }
    }

    private async Task HandleExportAsync(ExportOptions options)
    {
        try
        {
            AnsiConsole.MarkupLine("[blue]📤 Starting Lidarr wanted albums export...[/]");
            AnsiConsole.WriteLine();

            // Load configuration from unified config system
            var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
            
            // Use overrides or config values
            var url = options.Url ?? config.LidarrUrl;
            var apiKey = options.ApiKey ?? config.LidarrApiKey;

            if (string.IsNullOrWhiteSpace(url))
            {
                AnsiConsole.MarkupLine("[red]❌ Lidarr URL not configured[/]");
                AnsiConsole.MarkupLine("[dim]Use: qobuz config set lidarr-url http://your-lidarr-server:8686[/]");
                AnsiConsole.MarkupLine("[dim]Or use: --url http://your-lidarr-server:8686[/]");
                return;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                AnsiConsole.MarkupLine("[red]❌ Lidarr API key not configured[/]");
                AnsiConsole.MarkupLine("[dim]Use: qobuz config set lidarr-api-key YOUR_API_KEY[/]");
                AnsiConsole.MarkupLine("[dim]Or use: --api-key YOUR_API_KEY[/]");
                return;
            }

            // Validate format
            if (!new[] { "json", "txt", "csv" }.Contains(options.Format.ToLower()))
            {
                AnsiConsole.MarkupLine($"[red]❌ Invalid format '{options.Format}'. Supported formats: json, txt, csv[/]");
                return;
            }

            if (options.Verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Lidarr URL: {url}[/]");
                AnsiConsole.MarkupLine($"[dim]Output file: {options.Output}[/]");
                AnsiConsole.MarkupLine($"[dim]Format: {options.Format.ToUpper()}[/]");
                AnsiConsole.WriteLine();
            }

            _dashboard.Start("Fetching wanted albums from Lidarr...", options.Limit ?? 1000);

            try
            {
                // Step 1: Fetch wanted albums from Lidarr
                AnsiConsole.MarkupLine("[cyan]📋 Fetching wanted albums from Lidarr...[/]");
                
                // Build filter options
                var filterOptions = BuildExportFilterOptions(options.FilterType, options.MinYear, options.MaxYear);
                
                // Create progress reporter to update dashboard
                var progressReporter = new Progress<ProgressReport>(progress =>
                {
                    _dashboard.UpdateProgress(
                        processed: progress.Completed,
                        success: progress.Completed,
                        failed: 0,
                        currentItem: progress.CurrentItem ?? "",
                        lastSuccessful: progress.Phase == "Fetch Complete" ? $"Completed {progress.Completed} albums" : ""
                    );
                });
                
                var wantedAlbums = await _lidarrIntegrationService.GetFilteredWantedAlbumsAsync(
                    filterOptions, options.Limit ?? int.MaxValue, progressReporter, CancellationToken.None).ConfigureAwait(false);

                if (!wantedAlbums.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]📭 No wanted albums found matching the specified criteria[/]");
                    return;
                }

                AnsiConsole.MarkupLine($"[green]✅ Found {wantedAlbums.Count()} wanted albums[/]");

                // Step 2: Apply CLI-specific filters
                var filteredAlbums = ApplyExportFilters(wantedAlbums, options.Artists, options.Verbose);

                if (!filteredAlbums.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]📭 No albums remain after applying filters[/]");
                    return;
                }

                AnsiConsole.MarkupLine($"[green]✅ {filteredAlbums.Count()} albums passed filtering[/]");

                // Step 3: Optimize order if requested
                var finalAlbums = options.OptimizeOrder 
                    ? OptimizeExportOrder(filteredAlbums, options.Verbose)
                    : filteredAlbums.ToList();

                // Step 4: Apply limit after optimization
                if (options.Limit.HasValue && finalAlbums.Count > options.Limit.Value)
                {
                    finalAlbums = finalAlbums.Take(options.Limit.Value).ToList();
                    AnsiConsole.MarkupLine($"[yellow]⚠️ Limited to first {options.Limit.Value} albums after optimization[/]");
                }

                // Step 5: Create export data
                AnsiConsole.MarkupLine("[cyan]📄 Creating export data...[/]");
                
                var exportData = CreateExportData(finalAlbums, options.IncludeMetadata);

                // Step 6: Write to file
                AnsiConsole.MarkupLine($"[cyan]💾 Writing to {options.Format.ToUpper()} file...[/]");
                
                await WriteExportFile(exportData, options.Output, options.Format.ToLower()).ConfigureAwait(false);

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[green]✅ Successfully exported {finalAlbums.Count} wanted albums to {options.Output}[/]");

                // Display summary
                DisplayExportSummary(finalAlbums, options.Format, options.Verbose);
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
            
            if (options.Verbose)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.WriteException(ex);
            }
        }
    }

    #region Helper Methods

    private LidarrFilterOptions BuildFilterOptions(int? yearFrom, int? yearTo, int? lastDays, string? albumTypes)
    {
        var filterOptions = LidarrFilterOptions.ForWantedAlbums();
        
        // Apply date filters
        if (yearFrom.HasValue)
        {
            filterOptions.ReleaseDateFrom = new DateTime(yearFrom.Value, 1, 1);
        }
        
        if (yearTo.HasValue)
        {
            filterOptions.ReleaseDateTo = new DateTime(yearTo.Value, 12, 31);
        }
        
        if (lastDays.HasValue && lastDays.Value > 0)
        {
            filterOptions.ReleaseDateFrom = DateTime.UtcNow.AddDays(-lastDays.Value);
        }
        
        // Apply album type filters
        if (!string.IsNullOrWhiteSpace(albumTypes))
        {
            var types = albumTypes.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(t => t.Trim().ToLowerInvariant())
                                 .ToList();
            filterOptions.AlbumTypes = types;
        }
        
        return filterOptions;
    }

    private IEnumerable<LidarrAlbum> ApplyCliFilters(IEnumerable<LidarrAlbum> albums, 
        string? artists, string? artistsExclude, int? minTracks, int? maxTracks, bool verbose)
    {
        var filtered = albums.AsEnumerable();
        var originalCount = albums.Count();

        // Artist inclusion filter
        if (!string.IsNullOrWhiteSpace(artists))
        {
            var artistList = artists.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                   .Select(a => a.Trim().ToLowerInvariant())
                                   .ToHashSet();
            
            filtered = filtered.Where(album => 
            {
                var albumArtist = album.Artist?.ArtistName?.ToLowerInvariant() ?? "";
                return artistList.Any(artist => albumArtist.Contains(artist));
            });

            if (verbose)
            {
                var afterCount = filtered.Count();
                AnsiConsole.MarkupLine($"[dim]Artist inclusion filter: {originalCount} → {afterCount} albums[/]");
            }
        }

        // Artist exclusion filter
        if (!string.IsNullOrWhiteSpace(artistsExclude))
        {
            var excludeList = artistsExclude.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                           .Select(a => a.Trim().ToLowerInvariant())
                                           .ToHashSet();
            
            filtered = filtered.Where(album => 
            {
                var albumArtist = album.Artist?.ArtistName?.ToLowerInvariant() ?? "";
                return !excludeList.Any(artist => albumArtist.Contains(artist));
            });

            if (verbose)
            {
                var afterCount = filtered.Count();
                AnsiConsole.MarkupLine($"[dim]Artist exclusion filter: {filtered.Count()} albums remain[/]");
            }
        }

        // Track count filters
        if (minTracks.HasValue)
        {
            filtered = filtered.Where(album => (album.Statistics?.TrackFileCount ?? 0) >= minTracks.Value);
            
            if (verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Minimum tracks filter ({minTracks}): {filtered.Count()} albums remain[/]");
            }
        }

        if (maxTracks.HasValue)
        {
            filtered = filtered.Where(album => (album.Statistics?.TrackFileCount ?? 0) <= maxTracks.Value);
            
            if (verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Maximum tracks filter ({maxTracks}): {filtered.Count()} albums remain[/]");
            }
        }

        return filtered.ToList();
    }

    private void DisplayAlbumSummary(IEnumerable<LidarrAlbum> albums)
    {
        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("Artist");
        table.AddColumn("Album");
        table.AddColumn("Year");
        table.AddColumn("Tracks");
        table.AddColumn("Type");

        foreach (var album in albums.Take(10)) // Show first 10 albums
        {
            table.AddRow(
                album.Artist?.ArtistName ?? "Unknown",
                album.Title ?? "Unknown",
                album.ReleaseDate?.Year.ToString() ?? "Unknown",
                (album.Statistics?.TrackFileCount ?? 0).ToString(),
                album.AlbumType ?? "Unknown"
            );
        }

        if (albums.Count() > 10)
        {
            table.AddRow("[dim]...[/]", "[dim]...[/]", "[dim]...[/]", "[dim]...[/]", "[dim]...[/]");
            table.AddRow($"[dim]+{albums.Count() - 10} more albums[/]", "", "", "", "");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private void DisplayValidationSummary(IList<AlbumDownloadItem> validatedItems)
    {
        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("Artist");
        table.AddColumn("Album");
        table.AddColumn("Qobuz Quality");
        table.AddColumn("Warnings");

        foreach (var item in validatedItems.Take(10))
        {
            var qobuzAlbum = item.QobuzAlbum;
            var warnings = item.ValidationMessages?.Any() == true 
                ? string.Join("; ", item.ValidationMessages)
                : "None";

            table.AddRow(
                item.LidarrAlbum.Artist?.ArtistName ?? "Unknown",
                item.LidarrAlbum.Title ?? "Unknown",
                GetQualityString(qobuzAlbum),
                warnings
            );
        }

        if (validatedItems.Count > 10)
        {
            table.AddRow("[dim]...[/]", "[dim]...[/]", "[dim]...[/]", "[dim]...[/]");
            table.AddRow($"[dim]+{validatedItems.Count - 10} more albums[/]", "", "", "");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private void DisplayDryRunResults(IList<AlbumDownloadItem> validatedItems, bool immediate, string? quality)
    {
        AnsiConsole.MarkupLine("[yellow]🔍 DRY RUN RESULTS[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine($"[green]✅ Operation: {(immediate ? "Immediate Download" : "Add to Queue")}[/]");
        AnsiConsole.MarkupLine($"[green]✅ Quality: {quality ?? "flac-max"}[/]");
        AnsiConsole.MarkupLine($"[green]✅ Albums to process: {validatedItems.Count}[/]");
        AnsiConsole.WriteLine();

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("Artist");
        table.AddColumn("Album");
        table.AddColumn("Tracks");
        table.AddColumn("Qobuz Quality");

        foreach (var item in validatedItems)
        {
            table.AddRow(
                item.LidarrAlbum.Artist?.ArtistName ?? "Unknown",
                item.LidarrAlbum.Title ?? "Unknown",
                item.QobuzAlbum.TracksCount.ToString(),
                GetQualityString(item.QobuzAlbum)
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Use --dry-run=false to proceed with the operation[/]");
    }

    private async Task PerformImmediateDownloads(IList<AlbumDownloadItem> validatedItems, 
        string outputPath, int concurrency, bool verbose)
    {
        AnsiConsole.MarkupLine("[cyan]🎵 Starting immediate downloads...[/]");
        
        var progress = new Progress<DownloadProgressReport>(report =>
        {
            _dashboard.UpdateProgress(report.Completed, report.SuccessCount, report.FailureCount, report.CurrentAlbum ?? "Downloading...");
        });

        var result = await _lidarrIntegrationService.DownloadLidarrAlbumsAsync(
            validatedItems, outputPath, concurrency, progress, CancellationToken.None).ConfigureAwait(false);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]✅ Download complete: {result.SuccessfulDownloads} successful, {result.FailedDownloads} failed[/]");
        
        if (result.SuccessfulDownloads > 0)
        {
            var avgSpeedMB = result.AverageDownloadSpeed;
            var totalSizeGB = result.TotalBytesDownloaded / 1024.0 / 1024.0 / 1024.0;
            
            AnsiConsole.MarkupLine($"[dim]Total size: {totalSizeGB:F2} GB, Average speed: {avgSpeedMB:F2} MB/s[/]");
        }

        if (verbose && result.FailureItems.Any())
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]Failed downloads:[/]");
            
            foreach (var failure in result.FailureItems.Take(5))
            {
                AnsiConsole.MarkupLine($"[dim]• {failure.OriginalItem.LidarrAlbum.Artist?.ArtistName} - {failure.OriginalItem.LidarrAlbum.Title}: {failure.FailureReason}[/]");
            }
            
            if (result.FailureItems.Count > 5)
            {
                AnsiConsole.MarkupLine($"[dim]... and {result.FailureItems.Count - 5} more failures[/]");
            }
        }
    }

    private async Task AddToDownloadQueue(IList<AlbumDownloadItem> validatedItems, string? quality, bool verbose)
    {
        AnsiConsole.MarkupLine("[cyan]📥 Adding albums to download queue...[/]");
        
        var successCount = 0;
        var failedCount = 0;

        foreach (var item in validatedItems)
        {
            try
            {
                // Convert to CLI queue item format
                var queuedDownload = new QobuzCLI.Models.QueuedDownload
                {
                    Id = Guid.NewGuid().ToString(),
                    SearchQuery = $"{item.LidarrAlbum.Artist?.ArtistName} - {item.LidarrAlbum.Title}",
                    SearchType = QobuzCLI.Models.SearchType.Album,
                    SearchResultId = item.QobuzAlbum.Id,
                    Priority = 0,
                    Status = QobuzCLI.Models.QueueStatus.Pending
                };

                // Add to the default queue
                var queues = _queueService.GetQueues();
                var defaultQueue = queues.FirstOrDefault() ?? await _queueService.CreateQueueAsync("Default").ConfigureAwait(false);
                
                await _queueService.AddToQueueAsync(defaultQueue.Id, queuedDownload).ConfigureAwait(false);
                successCount++;

                if (verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]✅ Queued: {item.LidarrAlbum.Artist?.ArtistName} - {item.LidarrAlbum.Title}[/]");
                }
            }
            catch (Exception ex)
            {
                failedCount++;
                
                if (verbose)
                {
                    AnsiConsole.MarkupLine($"[red]❌ Failed to queue: {item.LidarrAlbum.Artist?.ArtistName} - {item.LidarrAlbum.Title}: {ex.Message}[/]");
                }
                
                _logger.LogError(ex, "Failed to add album to queue: {Artist} - {Album}", 
                    item.LidarrAlbum.Artist?.ArtistName, item.LidarrAlbum.Title);
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]✅ Queue operation complete: {successCount} added, {failedCount} failed[/]");
        
        if (successCount > 0)
        {
            AnsiConsole.MarkupLine("[dim]Use 'qobuz queue start' to begin downloading the queued items[/]");
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

    private string GetQualityString(QobuzAlbum album)
    {
        var maxBitDepth = album.MaximumBitDepth ?? 16;
        var maxSampleRate = album.MaximumSampleRate ?? 44100;
        
        if (maxBitDepth >= 24 && maxSampleRate >= 192000)
        {
            return $"Hi-Res FLAC {maxBitDepth}bit/{maxSampleRate / 1000}kHz";
        }
        else if (maxBitDepth >= 24 && maxSampleRate >= 96000)
        {
            return $"Hi-Res FLAC {maxBitDepth}bit/{maxSampleRate / 1000}kHz";
        }
        else if (maxBitDepth >= 16 && maxSampleRate >= 44100)
        {
            return $"FLAC {maxBitDepth}bit/{maxSampleRate / 1000}kHz";
        }
        else
        {
            return "MP3 320kbps";
        }
    }

    /// <summary>
    /// Displays quality profile information for validated albums.
    /// </summary>
    private void DisplayQualityProfileSummary(IList<AlbumDownloadItem> validatedItems)
    {
        AnsiConsole.MarkupLine("\n[cyan]📊 Quality Profile Summary:[/]");

        // Group by quality profile
        var profileGroups = validatedItems
            .GroupBy(item => item.QualityProfile?.Name ?? "Default")
            .OrderBy(g => g.Key);

        var table = new Table();
        table.AddColumn("Quality Profile");
        table.AddColumn("Albums");
        table.AddColumn("Selected Quality");
        table.AddColumn("Sample Albums");

        foreach (var group in profileGroups)
        {
            var sampleAlbums = group.Take(2).Select(item => $"{item.LidarrAlbum.Artist?.ArtistName} - {item.LidarrAlbum.Title}");
            var selectedQualities = group.Select(item => item.SelectedQobuzQuality).Distinct();

            table.AddRow(
                group.Key,
                group.Count().ToString(),
                string.Join(", ", selectedQualities),
                string.Join("; ", sampleAlbums) + (group.Count() > 2 ? "..." : "")
            );
        }

        AnsiConsole.Write(table);

        // Show quality distribution
        var qualityDistribution = validatedItems
            .GroupBy(item => item.SelectedQobuzQuality ?? "Unknown")
            .OrderByDescending(g => g.Count());

        AnsiConsole.MarkupLine("\n[cyan]📈 Quality Distribution:[/]");
        foreach (var qualityGroup in qualityDistribution)
        {
            var percentage = (double)qualityGroup.Count() / validatedItems.Count * 100;
            AnsiConsole.MarkupLine($"[dim]• {qualityGroup.Key}: {qualityGroup.Count()} albums ({percentage:F1}%)[/]");
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Shows available quality profiles from Lidarr.
    /// </summary>
    private async Task ShowQualityProfilesAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Quality profile display is not yet implemented.[/]");
        AnsiConsole.MarkupLine("[yellow]Please check your quality profiles directly in the Lidarr web interface.[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]This feature will be added in a future release.[/]");
        await Task.CompletedTask;
    }

    private LidarrFilterOptions BuildExportFilterOptions(string? filterType, int? minYear, int? maxYear)
    {
        var filterOptions = LidarrFilterOptions.ForWantedAlbums();
        
        // Apply date filters
        if (minYear.HasValue)
        {
            filterOptions.ReleaseDateFrom = new DateTime(minYear.Value, 1, 1);
        }
        
        if (maxYear.HasValue)
        {
            filterOptions.ReleaseDateTo = new DateTime(maxYear.Value, 12, 31);
        }
        
        // Apply album type filters
        if (!string.IsNullOrWhiteSpace(filterType))
        {
            var types = filterType.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(t => t.Trim().ToLowerInvariant())
                                 .ToList();
            filterOptions.AlbumTypes = types;
        }
        
        return filterOptions;
    }

    private IEnumerable<LidarrAlbum> ApplyExportFilters(IEnumerable<LidarrAlbum> albums, string? artists, bool verbose)
    {
        var filtered = albums.AsEnumerable();
        var originalCount = albums.Count();

        // Artist filter
        if (!string.IsNullOrWhiteSpace(artists))
        {
            var artistList = artists.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                   .Select(a => a.Trim().ToLowerInvariant())
                                   .ToHashSet();
            
            filtered = filtered.Where(album => 
            {
                var albumArtist = album.Artist?.ArtistName?.ToLowerInvariant() ?? "";
                return artistList.Any(artist => albumArtist.Contains(artist));
            });

            if (verbose)
            {
                var afterCount = filtered.Count();
                AnsiConsole.MarkupLine($"[dim]Artist filter: {originalCount} → {afterCount} albums[/]");
            }
        }

        return filtered.ToList();
    }

    private List<LidarrAlbum> OptimizeExportOrder(IEnumerable<LidarrAlbum> albums, bool verbose)
    {
        AnsiConsole.MarkupLine("[cyan]🔄 Optimizing album order for efficient downloading...[/]");
        
        var optimized = albums.OrderBy(album =>
        {
            // Priority order based on Python script logic:
            // 1. Release date (newer first - more likely to be available)
            // 2. Album type (albums before singles/EPs)
            // 3. Artist name (alphabetical for consistency)
            
            // Release date score (newer = lower sort value)
            var releaseYear = album.ReleaseDate?.Year ?? 1900;
            var dateScore = 3000 - releaseYear; // Invert so newer = lower
            
            // Album type score
            var albumType = album.AlbumType?.ToLowerInvariant() ?? "unknown";
            var typeScore = albumType switch
            {
                "album" => 0,
                "ep" => 100,
                "single" => 200,
                "broadcast" => 300,
                _ => 400
            };
            
            // Combine scores for sorting
            var combinedScore = (dateScore * 1000) + typeScore;
            
            return (combinedScore, album.Artist?.ArtistName?.ToLowerInvariant() ?? "");
        }).ToList();

        if (verbose)
        {
            AnsiConsole.MarkupLine($"[dim]Optimized {albums.Count()} albums by release date and type[/]");
        }

        return optimized;
    }

    private Dictionary<string, object> CreateExportData(List<LidarrAlbum> albums, bool includeMetadata)
    {
        var exportData = new Dictionary<string, object>
        {
            ["version"] = "1.0",
            ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ["source"] = "lidarr",
            ["total_albums"] = albums.Count,
            ["albums"] = albums.Select(album => CreateAlbumExportData(album, includeMetadata)).ToList()
        };

        return exportData;
    }

    private Dictionary<string, object?> CreateAlbumExportData(LidarrAlbum album, bool includeMetadata)
    {
        var artist = album.Artist;
        var artistName = artist?.ArtistName ?? "Unknown Artist";
        var albumTitle = album.Title ?? "Unknown Album";
        
        // Clean up common suffixes that might confuse search (like Python script)
        var albumTitleClean = albumTitle;
        var suffixesToRemove = new[] 
        { 
            " (Deluxe Edition)", " (Remastered)", " (Expanded Edition)", 
            " (Special Edition)", " (Anniversary Edition)", " (Collector's Edition)"
        };
        
        foreach (var suffix in suffixesToRemove)
        {
            albumTitleClean = albumTitleClean.Replace(suffix, "", StringComparison.OrdinalIgnoreCase);
        }
        albumTitleClean = albumTitleClean.Trim();

        var basicData = new Dictionary<string, object?>
        {
            ["lidarr_id"] = album.Id,
            ["artist_name"] = artistName,
            ["artist_id"] = artist?.Id,
            ["album_title"] = albumTitle,
            ["album_title_clean"] = albumTitleClean,
            ["album_type"] = album.AlbumType?.ToLowerInvariant() ?? "album",
            ["release_date"] = album.ReleaseDate?.ToString("yyyy-MM-dd"),
            ["release_year"] = album.ReleaseDate?.Year.ToString(),
            ["track_count"] = album.Statistics?.TrackCount ?? 0,
            ["monitored"] = album.Monitored,
            ["search_query"] = $"\"{artistName}\" \"{albumTitleClean}\"",
        };

        if (includeMetadata)
        {
            basicData["disambiguation"] = album.Disambiguation;
            basicData["foreign_album_id"] = album.ForeignAlbumId;
            basicData["genres"] = album.Genres?.ToList() ?? new List<string>();
            basicData["ratings"] = album.Ratings;
            basicData["overview"] = album.Overview;
            basicData["album_id"] = album.Id;
            basicData["artist_metadata_id"] = artist?.ArtistMetadataId;
        }

        return basicData;
    }

    private async Task WriteExportFile(Dictionary<string, object> exportData, string outputFile, string format)
    {
        var directory = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        switch (format)
        {
            case "json":
                await WriteJsonFile(exportData, outputFile).ConfigureAwait(false);
                break;
            case "txt":
                await WriteTxtFile(exportData, outputFile).ConfigureAwait(false);
                break;
            case "csv":
                await WriteCsvFile(exportData, outputFile).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentException($"Unsupported format: {format}");
        }
    }

    private async Task WriteJsonFile(Dictionary<string, object> exportData, string outputFile)
    {
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(exportData, Newtonsoft.Json.Formatting.Indented);
        await File.WriteAllTextAsync(outputFile, json, System.Text.Encoding.UTF8).ConfigureAwait(false);
    }

    private async Task WriteTxtFile(Dictionary<string, object> exportData, string outputFile)
    {
        var lines = new List<string>
        {
            "# Lidarr Wanted Albums Export",
            $"# Created: {exportData["created_at"]}",
            $"# Total albums: {exportData["total_albums"]}",
            "#",
            "# Format: search_query | artist | album | type | year",
            "#" + new string('=', 70),
            ""
        };

        if (exportData["albums"] is List<object> albums)
        {
            foreach (var albumObj in albums)
            {
                if (albumObj is Dictionary<string, object?> album)
                {
                    var line = $"{album["search_query"]} | " +
                              $"{album["artist_name"]} | " +
                              $"{album["album_title"]} | " +
                              $"{album["album_type"]} | " +
                              $"{album["release_year"] ?? "Unknown"}";
                    lines.Add(line);
                }
            }
        }

        await File.WriteAllLinesAsync(outputFile, lines, System.Text.Encoding.UTF8).ConfigureAwait(false);
    }

    private async Task WriteCsvFile(Dictionary<string, object> exportData, string outputFile)
    {
        var lines = new List<string>
        {
            "search_query,artist_name,album_title,album_type,release_year,track_count,lidarr_id"
        };

        if (exportData["albums"] is List<object> albums)
        {
            foreach (var albumObj in albums)
            {
                if (albumObj is Dictionary<string, object?> album)
                {
                    var csvLine = string.Join(",", new[]
                    {
                        EscapeCsvField(album["search_query"]?.ToString() ?? ""),
                        EscapeCsvField(album["artist_name"]?.ToString() ?? ""),
                        EscapeCsvField(album["album_title"]?.ToString() ?? ""),
                        EscapeCsvField(album["album_type"]?.ToString() ?? ""),
                        EscapeCsvField(album["release_year"]?.ToString() ?? ""),
                        EscapeCsvField(album["track_count"]?.ToString() ?? "0"),
                        EscapeCsvField(album["lidarr_id"]?.ToString() ?? "")
                    });
                    lines.Add(csvLine);
                }
            }
        }

        await File.WriteAllLinesAsync(outputFile, lines, System.Text.Encoding.UTF8).ConfigureAwait(false);
    }

    private string EscapeCsvField(string field)
    {
        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
        {
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
        return field;
    }

    private void DisplayExportSummary(List<LidarrAlbum> albums, string format, bool verbose)
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

        if (verbose)
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

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]💡 Usage with Qobuz CLI:[/]");
        
        switch (format.ToLower())
        {
            case "json":
                AnsiConsole.MarkupLine($"[dim]qobuz download --from-file exported-albums.json[/]");
                break;
            case "txt":
                AnsiConsole.MarkupLine($"[dim]Use the text file to manually copy search queries[/]");
                break;
            case "csv":
                AnsiConsole.MarkupLine($"[dim]Import into spreadsheet for analysis and filtering[/]");
                break;
        }
    }

    #endregion

}