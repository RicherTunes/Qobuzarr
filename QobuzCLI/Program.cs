using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using QobuzCLI.Commands;
using QobuzCLI.Services;
using QobuzCLI.Services.UI;
using QobuzCLI.Services.Adapters;
using QobuzCLI.Services.Logging;
using Spectre.Console;
using Lidarr.Plugin.Qobuzarr.Services;
using DotNetEnv;

namespace QobuzCLI;

/// <summary>
/// Main entry point for the QobuzCLI application - a command-line interface for testing and using
/// the Qobuzzarr plugin functionality. This CLI serves as both a testing tool for plugin development
/// and a standalone application for downloading music from Qobuz.
/// </summary>
/// <remarks>
/// The CLI follows the plugin-first architecture principle where all core functionality lives in the plugin
/// (src/) and the CLI only adds command-line interface, console output, and configuration management.
/// The CLI uses the plugin's services directly via project reference.
/// 
/// Available commands:
/// - auth: Authenticate with Qobuz and test API access
/// - search: Search for albums and tracks in the Qobuz catalog
/// - download: Download albums or individual tracks
/// - queue: Manage download queue with batch operations
/// - config: Manage application configuration
/// - history: View download history and statistics
/// </remarks>
class Program
{
    /// <summary>
    /// Application entry point that sets up dependency injection, configures logging,
    /// and dispatches to the appropriate command handler based on command-line arguments.
    /// </summary>
    /// <param name="args">Command-line arguments specifying the operation to perform.</param>
    /// <returns>Exit code: 0 for success, non-zero for errors.</returns>
    static async Task<int> Main(string[] args)
    {
        // Quick test to verify plugin core integration works
        // Plugin core test removed - functionality available through regular commands
        // if (args.Length > 0 && args[0] == "--test-plugin-core")
        // {
        //     await PluginCoreTest.RunBasicTest();
        //     return 0;
        // }
        
        try
        {
            // Load .env file if it exists (for easier credential management)
            LoadEnvironmentVariables();
            
            // Configuration logging simplified for CLI
            
            // Setup dependency injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            var serviceProvider = services.BuildServiceProvider();

            // Initialize state service
            var stateService = serviceProvider.GetRequiredService<IStateService>();
            await stateService.InitializeAsync();
            
            // Initialize queue service
            var queueService = serviceProvider.GetRequiredService<IQueueService>();
            await queueService.InitializeAsync();

            // Create root command
            var rootCommand = CreateRootCommand(serviceProvider);

            // Execute command
            return await rootCommand.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Dashboard state provider for coordinated logging
        services.AddSingleton<IDashboardStateProvider, DashboardStateProvider>();
        
        // Phase 1+: Enhanced logging with dashboard integration
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            
            // Use console logging instead of NLog for CLI
            
            // Add dashboard-aware console logging
            builder.Services.AddSingleton<ILoggerProvider>(serviceProvider =>
            {
                var dashboardState = serviceProvider.GetRequiredService<IDashboardStateProvider>();
                return new DashboardAwareConsoleLoggerProvider(
                    Microsoft.Extensions.Options.Options.Create(new ConsoleLoggerOptions()), 
                    dashboardState);
            });
            
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
        });

        // Dashboard service - register after logging to avoid circular dependency
        services.AddSingleton<Dashboard>(sp => 
        {
            var logger = sp.GetRequiredService<ILogger<Dashboard>>();
            return new Dashboard(logger, sp);
        });
        services.AddSingleton<IDashboard>(sp => sp.GetRequiredService<Dashboard>());

        // Core services
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<ISecureCredentialStorage, SecureCredentialStorage>();
        services.AddSingleton<ISecureConfigService, SecureConfigService>();
        
        // Rate limiter removed - now integrated into plugin's API client
        services.AddSingleton<DownloadProgressTracker>();
        // PluginMetadataService removed - using plugin's QobuzTrackDownloader.ApplyBasicMetadata instead
        services.AddSingleton<DownloadOrchestrator>();
        services.AddSingleton<DownloadErrorAnalyzer>();
        
        // Lidarr integration services
        services.AddSingleton<LidarrCredentialService>();
        
        // Real Lidarr integration service - thin CLI adapter that delegates to plugin services
        // No longer needs HttpClient - plugin services handle all HTTP operations
        services.AddSingleton<Lidarr.Plugin.Qobuzarr.Services.ILidarrIntegrationService, RealLidarrIntegrationService>();
        
        // Register DashboardLogger as IDashboardLogger
        services.AddSingleton<QobuzCLI.Services.Logging.IDashboardLogger>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DashboardLogger>>();
            var dashboard = sp.GetRequiredService<Dashboard>();
            return new QobuzCLI.Services.Logging.DashboardLogger(logger, dashboard, "Default");
        });
        
        // Register HttpClient with proper configuration
        services.AddHttpClient<HttpClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "QobuzCLI/1.0.0");
        }).ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler();
            
            // Use system proxy settings
            handler.UseProxy = true;
            handler.UseDefaultCredentials = true;
            
            return handler;
        });
        
        // Also register as singleton for backward compatibility
        services.AddSingleton<HttpClient>(sp => 
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return factory.CreateClient(nameof(HttpClient));
        });
        
        // Use SimplePluginService (transitional implementation)
        // This replaces the 1,776-line RealQobuzService god object with a focused implementation
        services.AddSingleton<IPluginHost>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<PluginHost>>();
            var httpClient = sp.GetRequiredService<HttpClient>();
            return new PluginHost(logger, httpClient);
        });
        
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IConflictService, ConflictService>();
        services.AddSingleton<IStateService, StateService>();
        // Duplicate checker removed - handled by plugin's download orchestration
        services.AddSingleton<IBatchDownloadService, BatchDownloadService>();
        services.AddSingleton<QueueMonitoringService>();
        services.AddSingleton<IConsoleUI, SpectreConsoleUI>();
        services.AddSingleton<IInteractiveSelectionService, InteractiveSelectionService>();
        
        // Use improved queue service if enabled
        var useImprovedQueue = Environment.GetEnvironmentVariable("QOBUZ_USE_IMPROVED_QUEUE") == "true";
        if (useImprovedQueue)
        {
            services.AddSingleton<IQueueService, ImprovedQueueService>();
        }
        else
        {
            services.AddSingleton<IQueueService, QueueService>();
        }

        // Commands
        services.AddTransient<ConfigCommand>();
        services.AddTransient<AuthCommand>();
        services.AddTransient<SearchCommand>();
        services.AddTransient<BatchSearchCommand>();
        services.AddTransient<DownloadCommand>();
        services.AddTransient<QueueCommand>();
        services.AddTransient<HistoryCommand>();
        services.AddTransient<TestPerformanceCommand>();
        services.AddTransient<TestUtilityPerformanceCommand>();
        services.AddTransient<TestQueueCommand>();
        services.AddTransient<LidarrCommand>();
        // ML features - to be implemented in v0.0.13
        // services.AddTransient<GenerateMLTrainingDataCommand>();
        // services.AddTransient<TestMLCommand>();
    }

    private static RootCommand CreateRootCommand(IServiceProvider serviceProvider)
    {
        var rootCommand = new RootCommand("Qobuz CLI - Comprehensive testing tool for the Qobuz Lidarr Plugin");

        // Add implemented commands
        rootCommand.AddCommand(serviceProvider.GetRequiredService<ConfigCommand>().Command);
        rootCommand.AddCommand(serviceProvider.GetRequiredService<AuthCommand>().Command);
        rootCommand.AddCommand(serviceProvider.GetRequiredService<SearchCommand>().Command);
        rootCommand.AddCommand(serviceProvider.GetRequiredService<BatchSearchCommand>().Command);
        rootCommand.AddCommand(serviceProvider.GetRequiredService<DownloadCommand>().Command);
        rootCommand.AddCommand(serviceProvider.GetRequiredService<QueueCommand>().Command);
        rootCommand.AddCommand(serviceProvider.GetRequiredService<HistoryCommand>().CreateCommand());
        rootCommand.AddCommand(serviceProvider.GetRequiredService<TestPerformanceCommand>().Command);
        rootCommand.AddCommand(serviceProvider.GetRequiredService<TestUtilityPerformanceCommand>().Command);
        rootCommand.AddCommand(serviceProvider.GetRequiredService<TestQueueCommand>().Command);
        rootCommand.AddCommand(serviceProvider.GetRequiredService<LidarrCommand>().Command);
        // ML features - to be implemented in v0.0.13
        // rootCommand.AddCommand(serviceProvider.GetRequiredService<GenerateMLTrainingDataCommand>().Command);
        // rootCommand.AddCommand(serviceProvider.GetRequiredService<TestMLCommand>().Command);

        return rootCommand;
    }
    
    /// <summary>
    /// Load environment variables from .env file if it exists
    /// </summary>
    private static void LoadEnvironmentVariables()
    {
        try
        {
            // Check for .env file in current directory first
            var currentDirEnvFile = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            if (File.Exists(currentDirEnvFile))
            {
                Env.Load(currentDirEnvFile);
                Console.WriteLine($"Loaded environment variables from {currentDirEnvFile}");
                return;
            }
            
            // Check for .env file in project root (for development)
            var projectRoot = FindProjectRoot();
            if (!string.IsNullOrEmpty(projectRoot))
            {
                var projectEnvFile = Path.Combine(projectRoot, ".env");
                if (File.Exists(projectEnvFile))
                {
                    Env.Load(projectEnvFile);
                    Console.WriteLine($"Loaded environment variables from {projectEnvFile}");
                    return;
                }
            }
            
            // No .env file found - that's OK, environment variables might be set directly
        }
        catch (Exception ex)
        {
            // Don't fail if .env loading fails - just log and continue
            Console.WriteLine($"Warning: Could not load .env file: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Find the project root directory (containing .csproj or .sln files)
    /// </summary>
    private static string? FindProjectRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var directory = new DirectoryInfo(currentDir);
        
        while (directory != null)
        {
            if (directory.GetFiles("*.sln").Any() || directory.GetFiles("Qobuzarr.csproj").Any())
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        
        return null;
    }
}

