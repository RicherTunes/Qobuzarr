using System.CommandLine;
using Microsoft.Extensions.Logging;
using QobuzCLI.Models;
using QobuzCLI.Services;
using Spectre.Console;

namespace QobuzCLI.Commands;

public class SearchCommand
{
    private readonly IConfigService _configService;
    private readonly IPluginHost _pluginHost;
    private readonly ISearchService _searchService;
    private readonly ILogger<SearchCommand> _logger;

    public Command Command { get; }

    public SearchCommand(IConfigService configService, IPluginHost pluginHost, ISearchService searchService, ILogger<SearchCommand> logger)
    {
        _configService = configService;
        _pluginHost = pluginHost;
        _searchService = searchService;
        _logger = logger;
        Command = CreateCommand();
    }

    private Command CreateCommand()
    {
        var searchCommand = new Command("search", "Search for music on Qobuz");

        var queryArg = new Argument<string>("query", "Search query (album, artist, or track name)");
        var typeOption = new Option<string?>("--type", "Search type: auto, album, artist, track, playlist, label") { IsRequired = false };
        var limitOption = new Option<int>("--limit", () => 20, "Maximum number of results to show");

        searchCommand.AddArgument(queryArg);
        searchCommand.AddOption(typeOption);
        searchCommand.AddOption(limitOption);

        searchCommand.SetHandler(async (string query, string? type, int limit) => 
            await HandleSearchAsync(query, type, limit).ConfigureAwait(false), queryArg, typeOption, limitOption);

        return searchCommand;
    }

    private async Task HandleSearchAsync(string query, string? type, int limit)
    {
        try
        {
            // Initialize plugin host if needed
            var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
            if (!_pluginHost.IsInitialized)
            {
                await _pluginHost.InitializeAsync(config).ConfigureAwait(false);
            }

            // Parse search type
            var searchType = ParseSearchType(type);
            if (searchType == SearchType.Auto)
            {
                searchType = _searchService.DetectSearchType(query);
                AnsiConsole.MarkupLine($"[dim]Auto-detected search type: {searchType.ToString().ToLower()}[/]");
            }

            // Perform search
            List<SearchResult> results = new();
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Searching Qobuz for '{query}'...", async ctx =>
                {
                    var rawResults = await _pluginHost.SearchAsync(query, searchType).ConfigureAwait(false);
                    results = _searchService.ScoreResults(rawResults, query);
                    results = results.Take(limit).ToList();
                });

            if (!results.Any())
            {
                AnsiConsole.MarkupLine($"[yellow]No results found for '{query}'[/]");
                AnsiConsole.MarkupLine("[dim]Try a different search term or check your spelling.[/]");
                return;
            }

            // Display results
            DisplaySearchResults(query, results, searchType);

            // Show usage hint
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]💡 To download any of these results, use:[/] [blue]qobuz download \"{query}\"[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Search failed: {ex.Message}[/]");
            _logger.LogError(ex, "Search failed for query: {Query}", query);
        }
    }

    private SearchType ParseSearchType(string? type)
    {
        if (string.IsNullOrEmpty(type))
            return SearchType.Auto;

        return type.ToLower() switch
        {
            "album" => SearchType.Album,
            "artist" => SearchType.Artist,
            "track" => SearchType.Track,
            "playlist" => SearchType.Playlist,
            "label" => SearchType.Label,
            "auto" => SearchType.Auto,
            _ => SearchType.Auto
        };
    }

    private void DisplaySearchResults(string query, List<SearchResult> results, SearchType searchType)
    {
        AnsiConsole.MarkupLine($"[blue]🔍 Search results for '{query}' ({searchType.ToString().ToLower()}):[/]");
        AnsiConsole.WriteLine();

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn(new TableColumn("").RightAligned());
        table.AddColumn("Title");
        table.AddColumn("Artist");
        table.AddColumn("Details");
        table.AddColumn("Quality");

        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            var index = $"[dim]{i + 1}.[/]";
            var title = FormatTitle(result);
            var artist = FormatArtist(result);
            var details = FormatDetails(result);
            var quality = FormatQuality(result);

            table.AddRow(index, title, artist, details, quality);
        }

        AnsiConsole.Write(table);

        // Show match quality indicators
        var exactMatches = results.Where(r => r.Score >= 95).Count();
        var goodMatches = results.Where(r => r.Score >= 80 && r.Score < 95).Count();
        
        if (exactMatches > 0 || goodMatches > 0)
        {
            AnsiConsole.WriteLine();
            if (exactMatches > 0)
                AnsiConsole.MarkupLine($"[green]✓ {exactMatches} exact match{(exactMatches > 1 ? "es" : "")} found[/]");
            if (goodMatches > 0)
                AnsiConsole.MarkupLine($"[yellow]~ {goodMatches} close match{(goodMatches > 1 ? "es" : "")} found[/]");
        }
    }

    private string FormatTitle(SearchResult result)
    {
        var title = result.Title;
        
        // Highlight exact matches
        if (result.Score >= 95)
            return $"[green]{title}[/] ⭐";
        else if (result.Score >= 80)
            return $"[yellow]{title}[/]";
        else
            return title;
    }

    private string FormatArtist(SearchResult result)
    {
        return result.Artist.Length > 30 
            ? result.Artist.Substring(0, 27) + "..." 
            : result.Artist;
    }

    private string FormatDetails(SearchResult result)
    {
        var parts = new List<string>();

        if (result.Year.HasValue)
            parts.Add($"({result.Year})");

        if (result.Type == "album" && result.TrackCount > 0)
            parts.Add($"{result.TrackCount} tracks");
        else if (result.Type == "artist" && result.TrackCount > 0)
            parts.Add($"{result.TrackCount} total tracks");

        return string.Join(" | ", parts);
    }

    private string FormatQuality(SearchResult result)
    {
        // Handle multi-format display (e.g., "Hi-Res 24bit/96kHz • CD • MP3")
        if (result.Quality.Contains("•"))
        {
            var parts = result.Quality.Split("•").Select(p => p.Trim()).ToList();
            var formatted = new List<string>();
            
            foreach (var part in parts)
            {
                if (part.Contains("Hi-Res"))
                {
                    // Extract bit depth and sample rate if available
                    var match = System.Text.RegularExpressions.Regex.Match(part, @"(\d+)bit/(\d+(?:\.\d+)?)kHz");
                    if (match.Success)
                    {
                        formatted.Add($"[cyan]✨ {match.Groups[1]}bit/{match.Groups[2]}kHz[/]");
                    }
                    else
                    {
                        formatted.Add("[cyan]✨ Hi-Res[/]");
                    }
                }
                else if (part == "CD")
                {
                    formatted.Add("[green]💿 CD[/]");
                }
                else if (part == "MP3")
                {
                    formatted.Add("[yellow]🎵 MP3[/]");
                }
                else
                {
                    formatted.Add($"[dim]{part}[/]");
                }
            }
            
            return string.Join(" [dim]•[/] ", formatted);
        }
        
        // Legacy single format display
        return result.Quality switch
        {
            var q when q.Contains("Hi-Res") => "[cyan]✨ Hi-Res[/]",
            var q when q.Contains("FLAC") || q.Contains("CD") => "[green]💿 FLAC[/]",
            var q when q.Contains("MP3") => "[yellow]🎵 MP3[/]",
            _ => "[dim]Unknown[/]"
        };
    }
}