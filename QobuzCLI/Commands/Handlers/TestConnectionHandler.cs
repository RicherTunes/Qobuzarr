using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Lidarr.Plugin.Qobuzarr.Services;
using QobuzCLI.Services;

namespace QobuzCLI.Commands.Handlers
{
    /// <summary>
    /// Handler for the 'lidarr test-connection' command.
    /// Delegates business logic to plugin services while handling CLI-specific presentation.
    /// </summary>
    public class TestConnectionHandler : ILidarrCommandHandler
    {
        private readonly ILidarrConnectionTestService _connectionTestService;
        private readonly IConfigService _configService;
        private readonly ILogger<TestConnectionHandler> _logger;
        
        private readonly string _urlOverride;
        private readonly string _apiKeyOverride;
        private readonly int _timeout;
        private readonly bool _verbose;

        public TestConnectionHandler(
            ILidarrConnectionTestService connectionTestService,
            IConfigService configService,
            ILogger<TestConnectionHandler> logger,
            string urlOverride = null,
            string apiKeyOverride = null,
            int timeout = 30,
            bool verbose = false)
        {
            _connectionTestService = connectionTestService ?? throw new ArgumentNullException(nameof(connectionTestService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _urlOverride = urlOverride;
            _apiKeyOverride = apiKeyOverride;
            _timeout = timeout;
            _verbose = verbose;
        }

        public async Task ExecuteAsync()
        {
            try
            {
                AnsiConsole.MarkupLine("[blue]🔍 Testing Lidarr connection...[/]");
                AnsiConsole.WriteLine();

                // Load configuration
                var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
                
                // Use overrides or config values
                var url = _urlOverride ?? config.LidarrUrl;
                var apiKey = _apiKeyOverride ?? config.LidarrApiKey;

                // Display configuration info if verbose
                if (_verbose)
                {
                    DisplayConfiguration(url, apiKey, _urlOverride != null, _apiKeyOverride != null);
                }

                // Validate configuration
                if (!ValidateConfiguration(url, apiKey))
                {
                    return;
                }

                // Test connection
                AnsiConsole.MarkupLine("[cyan]🌐 Testing connection to Lidarr...[/]");
                
                var connectionResult = await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Connecting to Lidarr...", async ctx =>
                    {
                        return await _connectionTestService.TestConnectionAsync(
                            url, apiKey, _timeout).ConfigureAwait(false);
                    });

                DisplayConnectionResult(connectionResult, url, apiKey);

                if (connectionResult.Success)
                {
                    // Test permissions
                    await TestPermissionsAsync(url, apiKey);
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]❌ Test connection failed: {ex.Message}[/]");
                _logger.LogError(ex, "Test connection failed");
                
                if (_verbose)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.WriteException(ex);
                }
            }
        }

        private void DisplayConfiguration(string url, string apiKey, bool urlOverridden, bool apiKeyOverridden)
        {
            if (urlOverridden)
            {
                AnsiConsole.MarkupLine($"[dim]Using URL override: {url}[/]");
            }
            else if (!string.IsNullOrWhiteSpace(url))
            {
                AnsiConsole.MarkupLine($"[dim]Using configured URL: {url}[/]");
            }

            if (apiKeyOverridden)
            {
                AnsiConsole.MarkupLine("[dim]Using API key override[/]");
            }
            else if (!string.IsNullOrWhiteSpace(apiKey))
            {
                AnsiConsole.MarkupLine("[dim]Using configured API key[/]");
            }
        }

        private bool ValidateConfiguration(string url, string apiKey)
        {
            AnsiConsole.MarkupLine("[cyan]📋 Validating configuration...[/]");
            
            if (string.IsNullOrWhiteSpace(url))
            {
                AnsiConsole.MarkupLine("[red]❌ Lidarr URL not configured[/]");
                AnsiConsole.MarkupLine("[dim]Use: qobuz config set lidarr-url http://your-lidarr-server:8686[/]");
                return false;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                AnsiConsole.MarkupLine("[red]❌ Lidarr API key not configured[/]");
                AnsiConsole.MarkupLine("[dim]Use: qobuz config set lidarr-api-key YOUR_API_KEY[/]");
                return false;
            }

            if (_verbose)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] URL: {url.TrimEnd('/')}");
                AnsiConsole.MarkupLine($"[green]✓[/] API Key: ***{apiKey.Substring(Math.Max(0, apiKey.Length - 4))}");
                AnsiConsole.MarkupLine($"[green]✓[/] Timeout: {_timeout}s");
                AnsiConsole.WriteLine();
            }

            return true;
        }

        private void DisplayConnectionResult(ConnectionTestResult result, string url, string apiKey)
        {
            if (result.Success)
            {
                AnsiConsole.MarkupLine("[green]✅ Successfully connected to Lidarr[/]");
                
                if (_verbose)
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
                    table.AddRow("Response Time", $"{result.ResponseTime.TotalMilliseconds:F0}ms");
                    
                    if (!string.IsNullOrEmpty(result.ServerVersion))
                    {
                        table.AddRow("Server Version", result.ServerVersion);
                    }
                    
                    AnsiConsole.Write(table);
                }
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

        private async Task TestPermissionsAsync(string url, string apiKey)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[cyan]🔐 Testing API permissions...[/]");
            
            try
            {
                var permissionResult = await _connectionTestService.TestPermissionsAsync(
                    url, apiKey).ConfigureAwait(false);
                
                if (permissionResult.Success)
                {
                    AnsiConsole.MarkupLine("[green]✅ API permissions verified[/]");
                    
                    if (permissionResult.WantedAlbumCount.HasValue)
                    {
                        if (permissionResult.WantedAlbumCount.Value > 0)
                        {
                            AnsiConsole.MarkupLine($"[dim]Found {permissionResult.WantedAlbumCount.Value} wanted album(s) in test query[/]");
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[dim]No wanted albums found (this is normal for a fresh Lidarr setup)[/]");
                        }
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]❌ API permission test failed[/]");
                    AnsiConsole.MarkupLine($"[dim]Error: {permissionResult.Message}[/]");
                    
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
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]❌ API permission test failed[/]");
                AnsiConsole.MarkupLine($"[dim]Error: {ex.Message}[/]");
                _logger.LogError(ex, "API permission test failed");
            }
        }
    }
}