using System.CommandLine;
using Microsoft.Extensions.Logging;
using QobuzCLI.Models;
using QobuzCLI.Services;
using Spectre.Console;

namespace QobuzCLI.Commands;

public class BatchSearchCommand
{
    private readonly IConfigService _configService;
    private readonly IPluginHost _pluginHost;
    private readonly ISearchService _searchService;
    private readonly IConflictService _conflictService;
    private readonly ILogger<BatchSearchCommand> _logger;

    public Command Command { get; }

    public BatchSearchCommand(
        IConfigService configService, 
        IPluginHost pluginHost, 
        ISearchService searchService,
        IConflictService conflictService,
        ILogger<BatchSearchCommand> logger)
    {
        _configService = configService;
        _pluginHost = pluginHost;
        _searchService = searchService;
        _conflictService = conflictService;
        _logger = logger;
        Command = CreateCommand();
    }

    private Command CreateCommand()
    {
        var batchSearchCommand = new Command("batch-search", "Search multiple queries and resolve conflicts in one session");

        var queriesArg = new Argument<string[]>("queries", "Multiple search queries (space-separated or comma-separated)");
        var fileOption = new Option<string?>("--file", "File containing search queries (one per line)");
        var typeOption = new Option<string?>("--type", "Force search type for all queries: auto, album, artist, track");
        var limitOption = new Option<int>("--limit", () => 10, "Maximum results per query");
        var resolveOption = new Option<bool>("--resolve", () => true, "Enable interactive conflict resolution");

        batchSearchCommand.AddArgument(queriesArg);
        batchSearchCommand.AddOption(fileOption);
        batchSearchCommand.AddOption(typeOption);
        batchSearchCommand.AddOption(limitOption);
        batchSearchCommand.AddOption(resolveOption);

        batchSearchCommand.SetHandler(async (string[] queries, string? file, string? type, int limit, bool resolve) => 
            await HandleBatchSearchAsync(queries, file, type, limit, resolve), 
            queriesArg, fileOption, typeOption, limitOption, resolveOption);

        return batchSearchCommand;
    }

    private async Task HandleBatchSearchAsync(string[] queries, string? file, string? type, int limit, bool resolve)
    {
        try
        {
            // Initialize plugin host
            var config = await _configService.LoadConfigAsync();
            if (!_pluginHost.IsInitialized)
            {
                await _pluginHost.InitializeAsync(config);
            }

            // Collect all queries
            var allQueries = await CollectQueriesAsync(queries, file);
            if (!allQueries.Any())
            {
                AnsiConsole.MarkupLine("[yellow]No search queries provided.[/]");
                return;
            }

            AnsiConsole.MarkupLine($"[blue]🔍 Batch Search: {allQueries.Count} quer{(allQueries.Count > 1 ? "ies" : "y")}[/]");
            AnsiConsole.WriteLine();

            // Parse search type
            var forcedType = ParseSearchType(type);
            var conflictSession = _conflictService.CreateSession();
            var allResults = new List<(string Query, List<Models.SearchResult> Results)>();

            // Search all queries
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
                    var searchTask = ctx.AddTask("Searching queries", maxValue: allQueries.Count);

                    foreach (var query in allQueries)
                    {
                        searchTask.Description = $"Searching: {query}";
                        
                        var detectedType = forcedType == Models.SearchType.Auto 
                            ? _searchService.DetectSearchType(query) 
                            : forcedType;
                        
                        var searchType = detectedType;

                        var rawResults = await _pluginHost.SearchAsync(query, searchType);
                        var scoredResults = _searchService.ScoreResults(rawResults, query);
                        var limitedResults = scoredResults.Take(limit).ToList();

                        allResults.Add((query, limitedResults));

                        // Check for conflicts
                        if (resolve)
                        {
                            var conflict = _conflictService.IdentifyConflict(query, limitedResults, detectedType);
                            if (conflict != null)
                            {
                                _conflictService.AddConflict(conflictSession, conflict);
                            }
                        }

                        searchTask.Increment(1);
                    }
                });

            // Display initial results summary
            DisplayBatchResults(allResults);

            // Handle conflicts if any
            if (resolve && conflictSession.Conflicts.Any())
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[yellow]⚠️  Found {conflictSession.Conflicts.Count} conflict{(conflictSession.Conflicts.Count > 1 ? "s" : "")} requiring resolution.[/]");
                
                var shouldResolve = AnsiConsole.Confirm("Would you like to resolve these conflicts now?");
                if (shouldResolve)
                {
                    conflictSession = await _conflictService.ResolveConflictsAsync(conflictSession);
                    await _conflictService.SaveSessionAsync(conflictSession);

                    // Display final resolved results
                    var resolvedResults = _conflictService.ApplyResolutions(conflictSession);
                    if (resolvedResults.Any())
                    {
                        AnsiConsole.WriteLine();
                        DisplayResolvedResults(resolvedResults);
                    }
                }
            }
            else if (resolve)
            {
                AnsiConsole.MarkupLine("[green]✅ No conflicts detected - all searches returned clear results.[/]");
            }

            // Show usage hints
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]💡 Tips:[/]");
            AnsiConsole.MarkupLine("[dim]  • Use [blue]qobuz download \"query\"[/] to download any result[/]");
            AnsiConsole.MarkupLine("[dim]  • Use [blue]--no-resolve[/] to skip conflict resolution[/]");
            AnsiConsole.MarkupLine("[dim]  • Use [blue]--file queries.txt[/] to batch search from a file[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Batch search failed: {ex.Message}[/]");
            _logger.LogError(ex, "Batch search failed");
        }
    }

    private async Task<List<string>> CollectQueriesAsync(string[] queries, string? file)
    {
        var allQueries = new List<string>();

        // Add command line queries
        if (queries.Any())
        {
            foreach (var query in queries)
            {
                // Handle comma-separated queries
                var splitQueries = query.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(q => q.Trim())
                    .Where(q => !string.IsNullOrEmpty(q));
                allQueries.AddRange(splitQueries);
            }
        }

        // Add file queries
        if (!string.IsNullOrEmpty(file))
        {
            if (File.Exists(file))
            {
                var fileQueries = await File.ReadAllLinesAsync(file);
                var validQueries = fileQueries
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrEmpty(line) && !line.StartsWith('#'));
                allQueries.AddRange(validQueries);
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: File '{file}' not found.[/]");
            }
        }

        return allQueries.Distinct().ToList();
    }

    private Models.SearchType ParseSearchType(string? type)
    {
        if (string.IsNullOrEmpty(type))
            return Models.SearchType.Auto;

        return type.ToLower() switch
        {
            "album" => Models.SearchType.Album,
            "artist" => Models.SearchType.Artist,
            "track" => Models.SearchType.Track,
            "auto" => Models.SearchType.Auto,
            _ => SearchType.Auto
        };
    }

    private void DisplayBatchResults(List<(string Query, List<Models.SearchResult> Results)> allResults)
    {
        AnsiConsole.MarkupLine("[blue]📊 Batch Search Results:[/]");
        AnsiConsole.WriteLine();

        var summaryTable = new Table();
        summaryTable.Border = TableBorder.Rounded;
        summaryTable.AddColumn("Query");
        summaryTable.AddColumn("Results");
        summaryTable.AddColumn("Best Match");
        summaryTable.AddColumn("Score");

        foreach (var (query, results) in allResults)
        {
            var resultCount = results.Count.ToString();
            var bestMatch = results.FirstOrDefault();
            var bestMatchText = bestMatch != null 
                ? $"{bestMatch.Title} - {bestMatch.Artist}".Truncate(40)
                : "[dim]No results[/]";
            var scoreText = bestMatch != null 
                ? FormatScore(bestMatch.Score)
                : "[dim]N/A[/]";

            summaryTable.AddRow(
                query.Truncate(25),
                resultCount,
                bestMatchText,
                scoreText
            );
        }

        AnsiConsole.Write(summaryTable);

        // Show detailed results for queries with good matches
        var goodMatches = allResults.Where(r => r.Results.Any() && r.Results.First().Score >= 80).ToList();
        if (goodMatches.Any())
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]✅ {goodMatches.Count} quer{(goodMatches.Count > 1 ? "ies" : "y")} with high-confidence matches[/]");
        }

        var poorMatches = allResults.Where(r => !r.Results.Any() || r.Results.First().Score < 60).ToList();
        if (poorMatches.Any())
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[yellow]⚠️  {poorMatches.Count} quer{(poorMatches.Count > 1 ? "ies" : "y")} with poor or no matches[/]");
        }
    }

    private void DisplayResolvedResults(List<Models.SearchResult> results)
    {
        AnsiConsole.MarkupLine("[green]🎯 Final Resolved Results:[/]");
        AnsiConsole.WriteLine();

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("Title");
        table.AddColumn("Artist");
        table.AddColumn("Type");
        table.AddColumn("Quality");
        table.AddColumn("Score");

        foreach (var result in results)
        {
            table.AddRow(
                result.Title.Truncate(30),
                result.Artist.Truncate(25),
                result.Type,
                FormatQuality(result),
                FormatScore(result.Score)
            );
        }

        AnsiConsole.Write(table);
    }

    private string FormatScore(double score)
    {
        return score switch
        {
            >= 95 => $"[green]{score:F0}⭐[/]",
            >= 80 => $"[yellow]{score:F0}[/]",
            _ => $"[dim]{score:F0}[/]"
        };
    }

    private string FormatQuality(Models.SearchResult result)
    {
        return result.Quality switch
        {
            var q when q.Contains("Hi-Res") => "[cyan]✨ Hi-Res[/]",
            var q when q.Contains("FLAC") => "[green]💿 FLAC[/]",
            var q when q.Contains("MP3") => "[yellow]🎵 MP3[/]",
            _ => "[dim]Unknown[/]"
        };
    }
}

public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength - 3) + "...";
    }
}