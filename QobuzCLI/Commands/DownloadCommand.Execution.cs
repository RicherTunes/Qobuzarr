using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using QobuzCLI.Models;

namespace QobuzCLI.Commands;

public partial class DownloadCommand
{
    private async Task<List<Models.SearchResult>> SelectDownloadTargetsCoreAsync(
        List<Models.SearchResult> results,
        string query,
        bool forceSelect,
        bool downloadAll)
    {
        if (downloadAll)
        {
            AnsiConsole.MarkupLine($"[cyan]📥 Will download all {results.Count} results[/]");
            return results;
        }

        // Check for exact matches
        var exactMatches = results.Where(r => r.Score >= 95).ToList();

        if (exactMatches.Count == 1 && !forceSelect)
        {
            var match = exactMatches.First();
            AnsiConsole.MarkupLine($"[green]✨ Found exact match: {match.Title} - {match.Artist}[/]");
            return new List<Models.SearchResult> { match };
        }

        // Check for high-quality single match
        var topResult = results.First();
        if (results.Count == 1 || (topResult.Score >= 85 && !forceSelect))
        {
            AnsiConsole.MarkupLine($"[green]🎯 Auto-selecting best match: {topResult.Title} - {topResult.Artist}[/]");
            return new List<Models.SearchResult> { topResult };
        }

        // Multiple results - try to show selection UI, fallback to top result if not interactive
        try
        {
            return await ShowSelectionUIAsyncCore(results, query, exactMatches).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            // Terminal not interactive - auto-select best match
            AnsiConsole.MarkupLine($"[yellow]⚠️  Terminal not interactive, auto-selecting best match: {topResult.Title} - {topResult.Artist}[/]");
            return new List<Models.SearchResult> { topResult };
        }
    }

    private Task<List<Models.SearchResult>> ShowSelectionUIAsyncCore(
        List<Models.SearchResult> results,
        string query,
        List<Models.SearchResult> exactMatches)
    {
        var safeQuery = Lidarr.Plugin.Common.Security.Sanitize.DisplayText(query);
        AnsiConsole.MarkupLine($"[blue]🎯 Found {results.Count} result{(results.Count > 1 ? "s" : "")} for '{safeQuery}':[/]");
        AnsiConsole.WriteLine();

        // Display results table
        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("Option");
        table.AddColumn("Title");
        table.AddColumn("Artist");
        table.AddColumn("Details");
        table.AddColumn("Quality");

        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var isExact = exactMatches.Any(m => m.Title == r.Title && m.Artist == r.Artist);
            var details = $"{r.Type} • {r.Year} • {r.Label}";
            var quality = string.IsNullOrWhiteSpace(r.Quality) ? "" : r.Quality;
            table.AddRow(
                $"[yellow]{i + 1}[/]",
                Lidarr.Plugin.Common.Security.Sanitize.DisplayText(r.Title),
                Lidarr.Plugin.Common.Security.Sanitize.DisplayText(r.Artist) + (isExact ? " [green](exact)[/]" : ""),
                details,
                quality
            );
        }

        AnsiConsole.Write(table);

        // Prompt selection
        var selectionPrompt = new SelectionPrompt<string>()
            .Title("Select item(s) to download")
            .PageSize(10)
            .MoreChoicesText("(Move up and down to reveal more items)")
            .AddChoices(results.Select((r, i) => (i + 1).ToString()));

        var choice = AnsiConsole.Prompt(selectionPrompt);
        if (int.TryParse(choice, out var index) && index >= 1 && index <= results.Count)
        {
            return Task.FromResult(new List<Models.SearchResult> { results[index - 1] });
        }

        return Task.FromResult(new List<Models.SearchResult>());
    }

    
}
