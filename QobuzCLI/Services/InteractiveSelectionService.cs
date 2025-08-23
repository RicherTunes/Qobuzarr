using Microsoft.Extensions.Logging;
using QobuzCLI.Models;
using QobuzCLI.Services.Logging;
using QobuzCLI.Services.UI;
using Spectre.Console;

namespace QobuzCLI.Services;

/// <summary>
/// Service for handling interactive user selection and UI display logic.
/// Provides clean separation between command orchestration and UI interaction logic.
/// </summary>
public class InteractiveSelectionService : IInteractiveSelectionService
{
    private readonly IDashboardLogger _logger;
    private readonly IConsoleUI _console;

    public InteractiveSelectionService(IDashboardLogger logger, IConsoleUI console)
    {
        _logger = logger;
        _console = console;
    }

    public async Task<List<SearchResult>> SelectDownloadTargetsAsync(
        List<SearchResult> results, 
        string query, 
        bool forceSelect, 
        bool downloadAll)
    {
        if (downloadAll)
        {
            _console.MarkupLine($"[cyan]📥 Will download all {results.Count} results[/]");
            return results;
        }

        // Check for exact matches
        var exactMatches = results.Where(r => r.Score >= 95).ToList();
        
        if (exactMatches.Count == 1 && !forceSelect)
        {
            var match = exactMatches.First();
            _console.MarkupLine($"[green]✨ Found exact match: {match.Title} - {match.Artist}[/]");
            return new List<SearchResult> { match };
        }

        // Check for high-quality single match
        var topResult = results.First();
        if (results.Count == 1 || (topResult.Score >= 85 && !forceSelect))
        {
            _console.MarkupLine($"[green]🎯 Auto-selecting best match: {topResult.Title} - {topResult.Artist}[/]");
            return new List<SearchResult> { topResult };
        }

        // Multiple results - try to show selection UI, fallback to top result if not interactive
        try 
        {
            return await ShowSelectionUIAsync(results, query, exactMatches).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            // Terminal not interactive - auto-select best match
            _console.MarkupLine($"[yellow]⚠️  Terminal not interactive, auto-selecting best match: {topResult.Title} - {topResult.Artist}[/]");
            return new List<SearchResult> { topResult };
        }
    }

    public async Task<List<SearchResult>> ShowSelectionUIAsync(
        List<SearchResult> results, 
        string query,
        List<SearchResult> exactMatches)
    {
        _console.MarkupLine($"[blue]🎯 Found {results.Count} result{(results.Count > 1 ? "s" : "")} for '{query}':[/]");
        _console.WriteLine();

        // Display results table
        var table = _console.CreateTable();
        table.AddColumn("Option");
        table.AddColumn("Title");
        table.AddColumn("Artist");
        table.AddColumn("Details");
        table.AddColumn("Quality");
        table.AddColumn("Match");

        var options = new List<string>();
        for (int i = 0; i < Math.Min(results.Count, 10); i++) // Limit to top 10
        {
            var result = results[i];
            var option = $"{i + 1}";
            options.Add(option);

            var title = result.Title.Length > 25 ? result.Title.Substring(0, 22) + "..." : result.Title;
            var artist = result.Artist.Length > 20 ? result.Artist.Substring(0, 17) + "..." : result.Artist;
            var details = FormatDetails(result);
            var quality = FormatQuality(result);
            var match = FormatMatchScore(result);

            table.AddRow(option, title, artist, details, quality, match);
        }

        _console.Write(table);
        _console.WriteLine();

        // Selection prompt
        var choices = new List<string>(options);
        choices.Add("all");
        choices.Add("none");

        var prompt = _console.CreateSelectionPrompt()
            .Title("Select what to download:");

        // Add numbered choices
        foreach (var choice in choices.Take(options.Count))
        {
            prompt.AddChoice(choice);
        }
        
        // Add action choices
        prompt.AddChoice("all (Download all results)");
        prompt.AddChoice("none (Cancel download)");

        var selection = _console.Prompt(prompt);

        if (selection == "none")
        {
            return new List<SearchResult>();
        }
        
        if (selection == "all")
        {
            return results.Take(10).ToList(); // Limit to top 10 for safety
        }

        if (int.TryParse(selection, out int selectedIndex) && selectedIndex <= results.Count)
        {
            return new List<SearchResult> { results[selectedIndex - 1] };
        }

        return new List<SearchResult>();
    }

    public string FormatDetails(SearchResult result)
    {
        var parts = new List<string>();

        if (result.Year.HasValue)
            parts.Add($"({result.Year})");

        if (result.Type == "album" && result.TrackCount > 0)
            parts.Add($"{result.TrackCount} tracks");

        return string.Join(" | ", parts);
    }

    public string FormatQuality(SearchResult result)
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
                    formatted.Add($"[cyan]✨ {part}[/]");
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

    public string FormatMatchScore(SearchResult result)
    {
        return result.Score switch
        {
            >= 95 => "[green]⭐ Exact[/]",
            >= 80 => "[yellow]✓ Good[/]",
            _ => "[dim]~ Fair[/]"
        };
    }
}