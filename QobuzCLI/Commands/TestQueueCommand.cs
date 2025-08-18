using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QobuzCLI.Models;
using QobuzCLI.Services;
using Spectre.Console;

namespace QobuzCLI.Commands;

public class TestQueueCommand
{
    private readonly IConfigService _configService;
    private readonly IPluginHost _pluginHost;
    private readonly IQueueService _queueService;
    private readonly ISearchService _searchService;
    private readonly ILogger<TestQueueCommand> _logger;

    public Command Command { get; }

    public TestQueueCommand(
        IConfigService configService,
        IPluginHost pluginHost,
        IQueueService queueService,
        ISearchService searchService,
        ILogger<TestQueueCommand> logger)
    {
        _configService = configService;
        _pluginHost = pluginHost;
        _queueService = queueService;
        _searchService = searchService;
        _logger = logger;
        Command = CreateCommand();
    }

    private Command CreateCommand()
    {
        var testCommand = new Command("test-queue", "Test improved queue processing performance");

        var countOption = new Option<int>("--count", () => 10, "Number of albums to queue for download");
        var concurrentOption = new Option<int>("--concurrent", () => 4, "Maximum concurrent downloads");
        var searchFirstOption = new Option<bool>("--search-first", () => true, "Search for albums before queueing");

        testCommand.AddOption(countOption);
        testCommand.AddOption(concurrentOption);
        testCommand.AddOption(searchFirstOption);

        testCommand.SetHandler(async (int count, int concurrent, bool searchFirst) => 
            await HandleTestQueueAsync(count, concurrent, searchFirst), countOption, concurrentOption, searchFirstOption);

        return testCommand;
    }

    private async Task HandleTestQueueAsync(int count, int concurrent, bool searchFirst)
    {
        try
        {
            // Initialize plugin
            var config = await _configService.LoadConfigAsync();
            if (!_pluginHost.IsInitialized)
            {
                await _pluginHost.InitializeAsync(config);
            }

            AnsiConsole.MarkupLine($"[blue]🚀 Queue Performance Test: {count} albums, {concurrent} concurrent downloads[/]");
            AnsiConsole.WriteLine();

            // Create or get test queue
            var queues = _queueService.GetQueues();
            var testQueue = queues.FirstOrDefault(q => q.Name == "Performance Test") 
                ?? await _queueService.CreateQueueAsync("Performance Test", concurrent);

            // Clear any existing items
            await _queueService.ClearCompletedAsync(testQueue.Id);

            var queuedItems = new List<QueuedDownload>();
            var stopwatch = Stopwatch.StartNew();

            if (searchFirst)
            {
                // Search for real albums to download
                AnsiConsole.MarkupLine("[yellow]🔍 Searching for albums to queue...[/]");
                
                await AnsiConsole.Progress()
                    .Columns(new ProgressColumn[] 
                    {
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new SpinnerColumn()
                    })
                    .StartAsync(async ctx =>
                    {
                        var searchTask = ctx.AddTask("Searching for albums", maxValue: count);
                        var queries = GeneratePopularAlbums().Take(count).ToList();
                        
                        foreach (var query in queries)
                        {
                            searchTask.Description = $"Searching: {query}";
                            
                            try
                            {
                                var results = await _pluginHost.SearchAsync(query, SearchType.Album);
                                if (results.Any())
                                {
                                    var bestMatch = results.First();
                                    var queueItem = new QueuedDownload
                                    {
                                        SearchQuery = query,
                                        SearchType = SearchType.Album,
                                        Priority = 0,
                                        Metadata = new Dictionary<string, string>
                                        {
                                            ["title"] = bestMatch.Title,
                                            ["artist"] = bestMatch.Artist,
                                            ["qobuzId"] = bestMatch.Id,
                                            ["qualityPreference"] = "flac-max",
                                            ["outputDirectory"] = "./TestDownloads"
                                        }
                                    };
                                    queuedItems.Add(queueItem);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to search for: {Query}", query);
                            }
                            
                            searchTask.Increment(1);
                        }
                    });
            }
            else
            {
                // Create dummy queue items for testing
                for (int i = 0; i < count; i++)
                {
                    var albums = GeneratePopularAlbums().ToList();
                    var album = albums[i % albums.Count];
                    
                    queuedItems.Add(new QueuedDownload
                    {
                        SearchQuery = album,
                        SearchType = SearchType.Album,
                        Priority = 0,
                        Metadata = new Dictionary<string, string>
                        {
                            ["title"] = album,
                            ["artist"] = "Test Artist",
                            ["qobuzId"] = $"test-{i}",
                            ["qualityPreference"] = "flac-max",
                            ["outputDirectory"] = "./TestDownloads"
                        }
                    });
                }
            }

            // Add items to queue
            AnsiConsole.MarkupLine($"[yellow]📥 Adding {queuedItems.Count} items to queue...[/]");
            await _queueService.AddBatchToQueueAsync(testQueue.Id, queuedItems);

            // Start queue processing
            stopwatch.Restart();
            AnsiConsole.MarkupLine($"[green]▶️ Starting queue processing with {concurrent} concurrent downloads...[/]");
            await _queueService.StartQueueProcessingAsync(testQueue.Id);

            // Monitor progress
            var lastStats = new DownloadQueueStatistics();
            var table = new Table();
            table.Border = TableBorder.Rounded;
            table.AddColumn("Metric");
            table.AddColumn("Value");

            await AnsiConsole.Live(table)
                .AutoClear(false)
                .StartAsync(async ctx =>
                {
                    while (true)
                    {
                        var stats = _queueService.GetQueueStatistics(testQueue.Id);
                        
                        if (stats.PendingItems == 0 && stats.ActiveDownloads == 0)
                        {
                            break; // All done
                        }

                        // Update table
                        table.Rows.Clear();
                        table.AddRow("Total Items", stats.TotalItems.ToString());
                        table.AddRow("Pending", stats.PendingItems.ToString());
                        table.AddRow("Active Downloads", $"{stats.ActiveDownloads}/{concurrent}");
                        table.AddRow("Completed", $"[green]{stats.CompletedItems}[/]");
                        table.AddRow("Failed", stats.FailedItems > 0 ? $"[red]{stats.FailedItems}[/]" : "0");
                        table.AddRow("Progress", $"{(double)stats.CompletedItems / stats.TotalItems:P}");
                        
                        if (stats.EstimatedTimeRemaining.HasValue)
                        {
                            table.AddRow("Est. Time Remaining", $"{stats.EstimatedTimeRemaining.Value:hh\\:mm\\:ss}");
                        }
                        
                        if (stats.AverageDownloadSpeed > 0)
                        {
                            table.AddRow("Download Rate", $"{stats.AverageDownloadSpeed:F1} items/min");
                        }

                        ctx.Refresh();
                        lastStats = stats;
                        
                        await Task.Delay(1000);
                    }
                });

            stopwatch.Stop();

            // Display final results
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]✅ Queue processing complete![/]");
            
            var finalTable = new Table();
            finalTable.Border = TableBorder.Rounded;
            finalTable.AddColumn("Final Results");
            finalTable.AddColumn("Value");
            
            finalTable.AddRow("Total Processed", lastStats.TotalItems.ToString());
            finalTable.AddRow("Successful", $"[green]{lastStats.CompletedItems}[/]");
            finalTable.AddRow("Failed", lastStats.FailedItems > 0 ? $"[red]{lastStats.FailedItems}[/]" : "0");
            finalTable.AddRow("Success Rate", $"{(double)lastStats.CompletedItems / lastStats.TotalItems:P}");
            finalTable.AddRow("Time Elapsed", $"{stopwatch.Elapsed:hh\\:mm\\:ss}");
            finalTable.AddRow("Average Rate", $"{lastStats.TotalItems / stopwatch.Elapsed.TotalMinutes:F1} downloads/minute");

            AnsiConsole.Write(finalTable);

            // Extrapolate to large batch
            if (lastStats.CompletedItems > 0)
            {
                var avgTimePerItem = stopwatch.Elapsed.TotalSeconds / lastStats.CompletedItems;
                var projectedTime = TimeSpan.FromSeconds(23324 * avgTimePerItem);
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[blue]📊 Projected time for 23,324 albums: {projectedTime.TotalHours:F2} hours[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Queue test failed: {ex.Message}[/]");
            _logger.LogError(ex, "Queue test failed");
        }
    }

    private IEnumerable<string> GeneratePopularAlbums()
    {
        // Return popular albums that are likely to be found on Qobuz
        return new[]
        {
            "Dark Side of the Moon",
            "Abbey Road", 
            "Led Zeppelin IV",
            "Rumours",
            "Hotel California",
            "Back in Black",
            "The Wall",
            "Thriller",
            "Born to Run",
            "Nevermind",
            "OK Computer",
            "In Rainbows",
            "Kid A",
            "Wish You Were Here",
            "Animals",
            "Physical Graffiti",
            "Houses of the Holy",
            "Who's Next",
            "Quadrophenia",
            "The Joshua Tree",
            "Achtung Baby",
            "Automatic for the People",
            "Out of Time",
            "Ten",
            "Vs.",
            "Master of Puppets",
            "Ride the Lightning",
            "Black Album",
            "Kind of Blue",
            "Bitches Brew",
            "A Love Supreme",
            "Giant Steps",
            "Blue Train",
            "Discovery",
            "Random Access Memories",
            "Homework"
        };
    }
}