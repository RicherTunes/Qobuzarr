using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QobuzCLI.Models;
using QobuzCLI.Services;
using Spectre.Console;

namespace QobuzCLI.Commands;

public class TestPerformanceCommand
{
    private readonly IConfigService _configService;
    private readonly IPluginHost _pluginHost;
    private readonly ILogger<TestPerformanceCommand> _logger;

    public Command Command { get; }

    public TestPerformanceCommand(IConfigService configService, IPluginHost pluginHost, ILogger<TestPerformanceCommand> logger)
    {
        _configService = configService;
        _pluginHost = pluginHost;
        _logger = logger;
        Command = CreateCommand();
    }

    private Command CreateCommand()
    {
        var testCommand = new Command("test-performance", "Test adaptive rate limiting and search performance");

        var countOption = new Option<int>("--count", () => 20, "Number of searches to perform");
        var concurrentOption = new Option<bool>("--concurrent", () => false, "Run searches concurrently");
        var concurrencyOption = new Option<int>("--concurrency", () => Environment.ProcessorCount, "Maximum concurrent operations (default: processor count)");
        var typeOption = new Option<string>("--type", () => "album", "Search type: album, artist, track");

        testCommand.AddOption(countOption);
        testCommand.AddOption(concurrentOption);
        testCommand.AddOption(concurrencyOption);
        testCommand.AddOption(typeOption);

        testCommand.SetHandler(async (int count, bool concurrent, int concurrency, string type) =>
            await HandleTestPerformanceAsync(count, concurrent, concurrency, type), countOption, concurrentOption, concurrencyOption, typeOption);

        return testCommand;
    }

    private async Task HandleTestPerformanceAsync(int count, bool concurrent, int concurrency, string type)
    {
        try
        {
            // Initialize plugin
            var config = await _configService.LoadConfigAsync();
            if (!_pluginHost.IsInitialized)
            {
                await _pluginHost.InitializeAsync(config);
            }

            var searchType = type.ToLower() switch
            {
                "album" => SearchType.Album,
                "artist" => SearchType.Artist,
                "track" => SearchType.Track,
                _ => SearchType.Album
            };

            AnsiConsole.MarkupLine($"[blue]🚀 Performance Test: {count} {type} searches ({(concurrent ? "concurrent" : "sequential")})[/]");
            AnsiConsole.WriteLine();

            // Generate test queries
            var queries = GenerateTestQueries(count);
            var stopwatch = Stopwatch.StartNew();
            var results = new List<SearchResult>();
            var errors = 0;

            if (concurrent)
            {
                // Concurrent execution
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
                        var searchTask = ctx.AddTask("Concurrent searches", maxValue: count);

                        using var semaphore = new SemaphoreSlim(concurrency); // Configurable concurrent limit
                        var tasks = queries.Select(async (query, index) =>
                        {
                            await semaphore.WaitAsync();
                            try
                            {
                                searchTask.Description = $"Searching: {query}";
                                var searchResults = await _pluginHost.SearchAsync(query, searchType);
                                searchTask.Increment(1);
                                return searchResults;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Search failed for: {Query}", query);
                                Interlocked.Increment(ref errors);
                                searchTask.Increment(1);
                                return new List<SearchResult>();
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        });

                        var allResults = await Task.WhenAll(tasks);
                        results = allResults.SelectMany(r => r).ToList();
                    });
            }
            else
            {
                // Sequential execution
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
                        var searchTask = ctx.AddTask("Sequential searches", maxValue: count);

                        foreach (var query in queries)
                        {
                            searchTask.Description = $"Searching: {query}";
                            try
                            {
                                var searchResults = await _pluginHost.SearchAsync(query, searchType);
                                results.AddRange(searchResults);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Search failed for: {Query}", query);
                                errors++;
                            }
                            searchTask.Increment(1);
                        }
                    });
            }

            stopwatch.Stop();

            // Display results
            AnsiConsole.WriteLine();
            var table = new Table();
            table.Border = TableBorder.Rounded;
            table.AddColumn("Metric");
            table.AddColumn("Value");

            table.AddRow("Total Searches", count.ToString());
            table.AddRow("Successful", (count - errors).ToString());
            table.AddRow("Failed", errors.ToString());
            table.AddRow("Total Results", results.Count.ToString());
            table.AddRow("Time Elapsed", $"{stopwatch.Elapsed.TotalSeconds:F2} seconds");
            table.AddRow("Search Rate", $"{count / stopwatch.Elapsed.TotalMinutes:F2} searches/minute");
            table.AddRow("Success Rate", $"{(count - errors) * 100.0 / count:F1}%");

            AnsiConsole.Write(table);

            // Show performance comparison
            AnsiConsole.WriteLine();
            var baselineRate = 4.76; // Your current rate
            var actualRate = count / stopwatch.Elapsed.TotalMinutes;
            var improvement = actualRate / baselineRate;

            if (improvement > 1)
            {
                AnsiConsole.MarkupLine($"[green]✨ Performance improvement: {improvement:F1}x faster than baseline![/]");
            }

            // Extrapolate to large batch
            var targetSize = 23324;
            var estimatedHours = targetSize / actualRate / 60;
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[blue]📊 Projected time for 23,324 albums at this rate: {estimatedHours:F2} hours[/]");

            // Log performance metrics from the service
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]📈 Service Performance Statistics:[/]");

            _pluginHost.LogPerformanceMetrics();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Performance test failed: {ex.Message}[/]");
            _logger.LogError(ex, "Performance test failed");
        }
    }

    private List<string> GenerateTestQueries(int count)
    {
        var artists = new[]
        {
            "Pink Floyd", "Led Zeppelin", "The Beatles", "Queen", "David Bowie",
            "Miles Davis", "John Coltrane", "Bill Evans", "Kraftwerk", "Daft Punk",
            "Radiohead", "Nirvana", "Pearl Jam", "The Rolling Stones", "Bob Dylan",
            "Jimi Hendrix", "The Who", "Black Sabbath", "Metallica", "AC/DC"
        };

        var albums = new[]
        {
            "Dark Side", "Physical Graffiti", "Abbey Road", "Night at the Opera",
            "Ziggy Stardust", "Kind of Blue", "Giant Steps", "Waltz for Debby",
            "Computer World", "Discovery", "OK Computer", "Nevermind", "Ten",
            "Exile on Main St", "Highway 61", "Electric Ladyland", "Tommy",
            "Paranoid", "Master of Puppets", "Back in Black"
        };

        var queries = new List<string>();
        var random = new Random();

        for (int i = 0; i < count; i++)
        {
            // Mix of artist searches and album searches
            if (i % 3 == 0)
            {
                // Artist only
                queries.Add(artists[random.Next(artists.Length)]);
            }
            else if (i % 3 == 1)
            {
                // Album only
                queries.Add(albums[random.Next(albums.Length)]);
            }
            else
            {
                // Artist + Album
                queries.Add($"{artists[random.Next(artists.Length)]} {albums[random.Next(albums.Length)]}");
            }
        }

        return queries;
    }
}
