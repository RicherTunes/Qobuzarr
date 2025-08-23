using System;
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QobuzCLI.Commands.Handlers;
using QobuzCLI.Services;
using QobuzCLI.Services.UI;
using Lidarr.Plugin.Qobuzarr.Services;

namespace QobuzCLI.Commands
{
    /// <summary>
    /// Refactored Lidarr command implementation following plugin-first architecture.
    /// This class now acts as a thin orchestrator that delegates to specialized handlers.
    /// Each handler is responsible for a single operation, maintaining Single Responsibility Principle.
    /// </summary>
    /// <remarks>
    /// Architecture changes:
    /// - Business logic moved to plugin layer services
    /// - Command parsing and setup remains here
    /// - UI rendering delegated to ILidarrUIService
    /// - Each subcommand has its own handler class
    /// - Progress tracking uses existing Dashboard service
    /// </remarks>
    public class LidarrCommandRefactored
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<LidarrCommandRefactored> _logger;

        public Command Command { get; }

        public LidarrCommandRefactored(
            IServiceProvider serviceProvider,
            ILogger<LidarrCommandRefactored> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            Command = CreateCommand();
        }

        private Command CreateCommand()
        {
            var lidarrCommand = new Command("lidarr", "Integrate with Lidarr to export and download wanted albums");

            // Create subcommands
            lidarrCommand.AddCommand(CreateTestConnectionCommand());
            lidarrCommand.AddCommand(CreateExportCommand());
            lidarrCommand.AddCommand(CreateDownloadWantedCommand());

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
                var url = context.ParseResult.GetValueForOption(urlOption);
                var apiKey = context.ParseResult.GetValueForOption(apiKeyOption);
                var timeout = context.ParseResult.GetValueForOption(timeoutOption);
                var verbose = context.ParseResult.GetValueForOption(verboseOption);
                
                // Create handler with dependencies from DI container
                var handler = new TestConnectionHandler(
                    _serviceProvider.GetRequiredService<ILidarrConnectionTestService>(),
                    _serviceProvider.GetRequiredService<IConfigService>(),
                    _serviceProvider.GetRequiredService<ILogger<TestConnectionHandler>>(),
                    url,
                    apiKey,
                    timeout,
                    verbose
                );
                
                await handler.ExecuteAsync().ConfigureAwait(false);
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
                var options = new ExportOptions
                {
                    UrlOverride = context.ParseResult.GetValueForOption(urlOption),
                    ApiKeyOverride = context.ParseResult.GetValueForOption(apiKeyOption),
                    OutputFile = context.ParseResult.GetValueForOption(outputOption)!,
                    Format = context.ParseResult.GetValueForOption(formatOption)!,
                    Limit = context.ParseResult.GetValueForOption(limitOption),
                    FilterType = context.ParseResult.GetValueForOption(filterTypeOption),
                    MinYear = context.ParseResult.GetValueForOption(minYearOption),
                    MaxYear = context.ParseResult.GetValueForOption(maxYearOption),
                    Artists = context.ParseResult.GetValueForOption(artistsOption),
                    OptimizeOrder = context.ParseResult.GetValueForOption(optimizeOrderOption),
                    IncludeMetadata = context.ParseResult.GetValueForOption(includeMetadataOption),
                    Verbose = context.ParseResult.GetValueForOption(verboseOption)
                };
                
                // Create handler with dependencies from DI container
                var handler = new ExportHandler(
                    _serviceProvider.GetRequiredService<ILidarrIntegrationService>(),
                    _serviceProvider.GetRequiredService<ILidarrExportService>(),
                    _serviceProvider.GetRequiredService<IConfigService>(),
                    _serviceProvider.GetRequiredService<IDashboard>(),
                    _serviceProvider.GetRequiredService<ILogger<ExportHandler>>(),
                    options
                );
                
                await handler.ExecuteAsync().ConfigureAwait(false);
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
                var options = new DownloadWantedOptions
                {
                    YearFrom = context.ParseResult.GetValueForOption(yearFromOption),
                    YearTo = context.ParseResult.GetValueForOption(yearToOption),
                    LastDays = context.ParseResult.GetValueForOption(lastDaysOption),
                    Artists = context.ParseResult.GetValueForOption(artistsOption),
                    ArtistsExclude = context.ParseResult.GetValueForOption(artistsExcludeOption),
                    AlbumTypes = context.ParseResult.GetValueForOption(albumTypesOption),
                    MinTracks = context.ParseResult.GetValueForOption(minTracksOption),
                    MaxTracks = context.ParseResult.GetValueForOption(maxTracksOption),
                    Immediate = context.ParseResult.GetValueForOption(immediateOption),
                    Quality = context.ParseResult.GetValueForOption(qualityOption),
                    QualityProfile = context.ParseResult.GetValueForOption(qualityProfileOption),
                    IgnoreProfiles = context.ParseResult.GetValueForOption(ignoreProfilesOption),
                    ShowProfiles = context.ParseResult.GetValueForOption(showProfilesOption),
                    Limit = context.ParseResult.GetValueForOption(limitOption),
                    DryRun = context.ParseResult.GetValueForOption(dryRunOption),
                    Concurrency = context.ParseResult.GetValueForOption(concurrencyOption),
                    OutputPath = context.ParseResult.GetValueForOption(outputPathOption),
                    Verbose = context.ParseResult.GetValueForOption(verboseOption)
                };
                
                // Create handler with dependencies from DI container
                var handler = new DownloadWantedHandler(
                    _serviceProvider.GetRequiredService<ILidarrIntegrationService>(),
                    _serviceProvider.GetRequiredService<IConfigService>(),
                    _serviceProvider.GetRequiredService<IQueueService>(),
                    _serviceProvider.GetRequiredService<ILidarrUIService>(),
                    _serviceProvider.GetRequiredService<IDashboard>(),
                    _serviceProvider.GetRequiredService<ILogger<DownloadWantedHandler>>(),
                    options
                );
                
                await handler.ExecuteAsync().ConfigureAwait(false);
            });

            return command;
        }
    }
}