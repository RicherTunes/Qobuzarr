using Microsoft.Extensions.Logging;
using System;
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
        _console.MarkupLine("[dim]Use ↑/↓ to move, Enter to select, A = all, N/Esc = none[/]");
        _console.WriteLine();

        // Limit results for display
        var displayCount = Math.Min(results.Count, 10);

        // Try interactive in-table selection; fallback to prompt if not supported
        try
        {
            var selected = await RunInteractiveTableSelectionAsync(results, displayCount).ConfigureAwait(false);

            if (selected.All)
            {
                return results.Take(displayCount).ToList();
            }

            if (selected.None)
            {
                return new List<SearchResult>();
            }

            if (selected.Index.HasValue && selected.Index.Value >= 0 && selected.Index.Value < displayCount)
            {
                return new List<SearchResult> { results[selected.Index.Value] };
            }

            // If nothing selected, choose best match without extra prompt
            return new List<SearchResult> { results.First() };
        }
        catch
        {
            // Non-interactive terminal; avoid duplicate menu under table
            _console.MarkupLine("[yellow]⚠️  Terminal not fully interactive; auto-selecting best match[/]");
            return new List<SearchResult> { results.First() };
        }
    }

    private async Task<(int? Index, bool All, bool None)> RunInteractiveTableSelectionAsync(List<SearchResult> results, int displayCount)
    {
        // Use Spectre.Console live rendering to allow in-table cursor movement
        if (!Spectre.Console.AnsiConsole.Profile.Capabilities.Interactive)
            throw new NotSupportedException("Terminal is not interactive");
        var selectedIndex = 0;

        Table BuildTable(int sel)
        {
            var t = _console.CreateTable();
            t.AddColumn("");
            t.AddColumn("Option");
            t.AddColumn("Title");
            t.AddColumn("Artist");
            t.AddColumn("Details");
            t.AddColumn("Quality");
            t.AddColumn("Match");

            for (int i = 0; i < displayCount; i++)
            {
                var r = results[i];
                var option = $"{i + 1}";
                var title = r.Title.Length > 25 ? r.Title.Substring(0, 22) + "..." : r.Title;
                var artist = r.Artist.Length > 20 ? r.Artist.Substring(0, 17) + "..." : r.Artist;
                var details = FormatDetails(r);
                var quality = FormatQuality(r);
                var match = FormatMatchScore(r);

                var pointer = i == sel ? "[green]>[/]" : " ";
                if (i == sel)
                {
                    option = $"[bold]{option}[/]";
                    title = $"[white]{title}[/]";
                }

                t.AddRow(pointer, option, title, artist, details, quality, match);
            }

            return t;
        }

        var modeAll = false;
        var modeNone = false;
        int? picked = null;

        var initial = BuildTable(selectedIndex);
        await Spectre.Console.AnsiConsole.Live(initial)
            .StartAsync(async ctx =>
            {
                // Replace content with updated table on each move
                void Refresh()
                {
                    ctx.UpdateTarget(BuildTable(selectedIndex));
                }

                Refresh();

                while (true)
                {
                    var keyInfo = Spectre.Console.AnsiConsole.Console.Input.ReadKey(true);
                    if (keyInfo == null) { await Task.Delay(10); continue; }
                    var key = keyInfo.Value;
                    if (key.Key == ConsoleKey.UpArrow)
                    {
                        selectedIndex = (selectedIndex - 1 + displayCount) % displayCount;
                        Refresh();
                    }
                    else if (key.Key == ConsoleKey.DownArrow)
                    {
                        selectedIndex = (selectedIndex + 1) % displayCount;
                        Refresh();
                    }
                    else if (key.Key == ConsoleKey.Home)
                    {
                        selectedIndex = 0; Refresh();
                    }
                    else if (key.Key == ConsoleKey.End)
                    {
                        selectedIndex = displayCount - 1; Refresh();
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        picked = selectedIndex;
                        break;
                    }
                    else if (key.KeyChar == 'a' || key.KeyChar == 'A')
                    {
                        modeAll = true;
                        break;
                    }
                    else if (key.KeyChar == 'n' || key.KeyChar == 'N' || key.Key == ConsoleKey.Escape)
                    {
                        modeNone = true;
                        break;
                    }
                    else if (char.IsDigit(key.KeyChar))
                    {
                        var d = key.KeyChar - '0';
                        // Map 1..9, 0 -> 10 (if exists)
                        var idx = d == 0 ? 9 : d - 1;
                        if (idx >= 0 && idx < displayCount)
                        {
                            selectedIndex = idx; Refresh();
                        }
                    }

                    await Task.Yield();
                }
            })
            .ConfigureAwait(false);

        return (picked, modeAll, modeNone);
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
