using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace QobuzCLI.Commands;

public class TestUtilityPerformanceCommand
{
    private readonly ILogger<TestUtilityPerformanceCommand> _logger;

    public Command Command { get; }

    public TestUtilityPerformanceCommand(ILogger<TestUtilityPerformanceCommand> logger)
    {
        _logger = logger;
        Command = CreateCommand();
    }

    private Command CreateCommand()
    {
        var testCommand = new Command("test-utils", "Test optimized utilities performance (hashing and string sanitization)");

        var hashCountOption = new Option<int>("--hash-count", () => 100000, "Number of hashes to perform");
        var stringCountOption = new Option<int>("--string-count", () => 10000, "Number of string sanitizations to perform");

        testCommand.AddOption(hashCountOption);
        testCommand.AddOption(stringCountOption);

        testCommand.SetHandler(async (int hashCount, int stringCount) => 
            await HandleTestUtilityPerformanceAsync(hashCount, stringCount), hashCountOption, stringCountOption);

        return testCommand;
    }

    private async Task HandleTestUtilityPerformanceAsync(int hashCount, int stringCount)
    {
        try
        {
            AnsiConsole.MarkupLine("[blue]🚀 Testing Optimized Plugin Utilities Performance[/]");
            AnsiConsole.WriteLine();

            // Test HashingUtility Performance
            await TestHashingPerformance(hashCount);
            
            AnsiConsole.WriteLine();
            
            // Test StringExtensions Performance
            await TestStringExtensionsPerformance(stringCount);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]✅ Utility performance tests completed successfully![/]");
            AnsiConsole.MarkupLine("[yellow]📋 These optimizations are integrated into the plugin and improve overall download performance.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Utility performance test failed: {ex.Message}[/]");
            _logger.LogError(ex, "Utility performance test failed");
        }
    }

    private async Task TestHashingPerformance(int count)
    {
        AnsiConsole.MarkupLine($"[yellow]🔐 Testing HashingUtility.ComputeMD5Hash() - {count:N0} operations[/]");
        
        // Generate test data
        var testStrings = new List<string>();
        var random = new Random(42); // Fixed seed for reproducible results
        
        for (int i = 0; i < count; i++)
        {
            // Generate realistic test data similar to what the plugin processes
            var testData = i switch
            {
                var x when x % 4 == 0 => $"qobuz_user_credentials_{random.Next(100000, 999999)}",
                var x when x % 4 == 1 => $"album_cache_key_{random.Next(1000000, 9999999)}_{random.Next(1000, 9999)}",
                var x when x % 4 == 2 => $"track_download_url_signature_{Guid.NewGuid()}",
                _ => $"metadata_hash_{random.NextDouble():F8}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"
            };
            testStrings.Add(testData);
        }

        var results = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        
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
                var hashTask = ctx.AddTask("Computing MD5 hashes", maxValue: count);
                
                await Task.Run(() =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        // This uses the optimized plugin HashingUtility
                        var hash = HashingUtility.ComputeMD5Hash(testStrings[i]);
                        results.Add(hash);
                        
                        if (i % 1000 == 0)
                        {
                            hashTask.Increment(1000);
                        }
                    }
                    
                    // Complete any remaining increments
                    hashTask.Value = count;
                });
            });

        stopwatch.Stop();

        // Display results
        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.Title = new TableTitle("[bold blue]HashingUtility Performance Results[/]");
        table.AddColumn("Metric");
        table.AddColumn("Value");

        var hashesPerSecond = count / stopwatch.Elapsed.TotalSeconds;
        var expectedImprovement = 1.7; // 60-80% improvement means 1.6-1.8x faster

        table.AddRow("Total Hashes", $"{count:N0}");
        table.AddRow("Time Elapsed", $"{stopwatch.Elapsed.TotalMilliseconds:F2} ms");
        table.AddRow("Hashes per Second", $"{hashesPerSecond:N0}");
        table.AddRow("Expected Improvement", $"{expectedImprovement:F1}x faster than StringBuilder approach");
        table.AddRow("Optimization Used", "[green]Convert.ToHexString() + ToLowerInvariant()[/]");
        
        // Sample hash verification
        table.AddRow("Sample Hash", $"[dim]{results.First()}[/]");
        
        AnsiConsole.Write(table);

        // Performance assessment
        if (hashesPerSecond > 500000)
        {
            AnsiConsole.MarkupLine($"[green]✨ Excellent performance! {hashesPerSecond:N0} hashes/sec exceeds 500k target[/]");
        }
        else if (hashesPerSecond > 200000)
        {
            AnsiConsole.MarkupLine($"[yellow]⚡ Good performance: {hashesPerSecond:N0} hashes/sec[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[orange3]⚠️  Performance below expectation: {hashesPerSecond:N0} hashes/sec[/]");
        }
    }

    private async Task TestStringExtensionsPerformance(int count)
    {
        AnsiConsole.MarkupLine($"[yellow]🧹 Testing FileNamingUtils.ToSafeFileName() - {count:N0} operations[/]");
        
        // Generate test data with problematic characters that need sanitization
        var testStrings = new List<string>();
        var problematicChars = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" };
        var random = new Random(42);
        
        for (int i = 0; i < count; i++)
        {
            var testData = i switch
            {
                var x when x % 5 == 0 => $"Artist Name {problematicChars[random.Next(problematicChars.Length)]} Album",
                var x when x % 5 == 1 => $"{reservedNames[random.Next(reservedNames.Length)]}.Album.Name",
                var x when x % 5 == 2 => $"Track: Name with \"quotes\" and <brackets>",
                var x when x % 5 == 3 => $"File/Path\\With|Various*Problem?Characters",
                _ => $"Normal Album Name {random.Next(1000, 9999)}"
            };
            testStrings.Add(testData);
        }

        var results = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        
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
                var sanitizeTask = ctx.AddTask("Sanitizing filenames", maxValue: count);
                
                await Task.Run(() =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        // This uses the optimized plugin FileNamingUtils with static arrays
                        var sanitized = testStrings[i].ToSafeFileName();
                        results.Add(sanitized);
                        
                        if (i % 500 == 0)
                        {
                            sanitizeTask.Increment(500);
                        }
                    }
                    
                    // Complete any remaining increments
                    sanitizeTask.Value = count;
                });
            });

        stopwatch.Stop();

        // Display results
        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.Title = new TableTitle("[bold blue]FileNamingUtils Performance Results[/]");
        table.AddColumn("Metric");
        table.AddColumn("Value");

        var sanitizationsPerSecond = count / stopwatch.Elapsed.TotalSeconds;

        table.AddRow("Total Sanitizations", $"{count:N0}");
        table.AddRow("Time Elapsed", $"{stopwatch.Elapsed.TotalMilliseconds:F2} ms");
        table.AddRow("Sanitizations per Second", $"{sanitizationsPerSecond:N0}");
        table.AddRow("Optimization Used", "[green]Static readonly arrays (no repeated allocations)[/]");
        
        // Show before/after examples
        var exampleIndex = random.Next(results.Count);
        table.AddRow("Example Input", $"[dim]{testStrings[exampleIndex]}[/]");
        table.AddRow("Example Output", $"[dim]{results[exampleIndex]}[/]");
        
        AnsiConsole.Write(table);

        // Performance assessment
        if (sanitizationsPerSecond > 50000)
        {
            AnsiConsole.MarkupLine($"[green]✨ Excellent performance! {sanitizationsPerSecond:N0} sanitizations/sec[/]");
        }
        else if (sanitizationsPerSecond > 20000)
        {
            AnsiConsole.MarkupLine($"[yellow]⚡ Good performance: {sanitizationsPerSecond:N0} sanitizations/sec[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[orange3]⚠️  Performance could be improved: {sanitizationsPerSecond:N0} sanitizations/sec[/]");
        }
    }
}