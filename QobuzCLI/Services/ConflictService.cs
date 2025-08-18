using Microsoft.Extensions.Logging;
using QobuzCLI.Models;
using Spectre.Console;
using System.Text.Json;

namespace QobuzCLI.Services;

public class ConflictService : IConflictService
{
    private readonly ILogger<ConflictService> _logger;
    private readonly IConfigService _configService;
    private readonly string _sessionsPath;

    public ConflictService(ILogger<ConflictService> logger, IConfigService configService)
    {
        _logger = logger;
        _configService = configService;
        _sessionsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qobuz", "conflict-sessions.json");
    }

    public SearchConflict? IdentifyConflict(string query, List<SearchResult> results, SearchType detectedType)
    {
        try
        {
            if (!results.Any())
                return null;

            var conflict = new SearchConflict
            {
                Query = query,
                DetectedType = detectedType,
                CandidateResults = results
            };

            // Check for multiple exact matches (95+ score)
            var exactMatches = results.Where(r => r.Score >= 95).ToList();
            if (exactMatches.Count > 1)
            {
                conflict.Reason = ConflictReason.MultipleExactMatches;
                conflict.CandidateResults = exactMatches;
                _logger.LogDebug("Conflict detected: Multiple exact matches for query '{Query}'", query);
                return conflict;
            }

            // Check for ambiguous search type (mixed results with similar scores)
            var topResults = results.Take(5).Where(r => r.Score >= 70).ToList();
            var typeGroups = topResults.GroupBy(r => r.Type).ToList();
            if (typeGroups.Count > 1 && typeGroups.All(g => g.Count() >= 2))
            {
                conflict.Reason = ConflictReason.AmbiguousSearchType;
                conflict.CandidateResults = topResults;
                _logger.LogDebug("Conflict detected: Ambiguous search type for query '{Query}'", query);
                return conflict;
            }

            // Check for no high confidence match (all scores below 80)
            var bestScore = results.Max(r => r.Score);
            if (bestScore < 80 && results.Count >= 3)
            {
                conflict.Reason = ConflictReason.NoHighConfidenceMatch;
                conflict.CandidateResults = results.Take(5).ToList();
                _logger.LogDebug("Conflict detected: No high confidence match for query '{Query}'", query);
                return conflict;
            }

            // Check for quality conflicts (same album, different qualities)
            var albumGroups = results
                .Where(r => r.Type == "album")
                .GroupBy(r => new { r.Title, r.Artist })
                .Where(g => g.Count() > 1)
                .ToList();

            if (albumGroups.Any())
            {
                var qualityConflicts = albumGroups.Where(g => 
                    g.Select(r => r.Quality).Distinct().Count() > 1).ToList();
                
                if (qualityConflicts.Any())
                {
                    conflict.Reason = ConflictReason.QualityConflict;
                    conflict.CandidateResults = qualityConflicts.SelectMany(g => g).ToList();
                    _logger.LogDebug("Conflict detected: Quality conflict for query '{Query}'", query);
                    return conflict;
                }
            }

            // Check for artist disambiguation (similar artist names)
            if (detectedType == SearchType.Artist)
            {
                var artistNames = results.Select(r => r.Artist.ToLower()).Distinct().ToList();
                var similarArtists = new List<string>();
                
                foreach (var artist1 in artistNames)
                {
                    foreach (var artist2 in artistNames)
                    {
                        if (artist1 != artist2 && (artist1.Contains(artist2) || artist2.Contains(artist1)))
                        {
                            similarArtists.AddRange(new[] { artist1, artist2 });
                        }
                    }
                }

                if (similarArtists.Any())
                {
                    conflict.Reason = ConflictReason.ArtistDisambiguation;
                    conflict.CandidateResults = results.Where(r => 
                        similarArtists.Contains(r.Artist.ToLower())).ToList();
                    _logger.LogDebug("Conflict detected: Artist disambiguation for query '{Query}'", query);
                    return conflict;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error identifying conflict for query: {Query}", query);
            return null;
        }
    }

    public ConflictSession CreateSession()
    {
        return new ConflictSession();
    }

    public void AddConflict(ConflictSession session, SearchConflict conflict)
    {
        session.Conflicts.Add(conflict);
        _logger.LogDebug("Added conflict to session: {ConflictId} for query '{Query}'", conflict.Id, conflict.Query);
    }

    public async Task<ConflictSession> ResolveConflictsAsync(ConflictSession session)
    {
        try
        {
            if (!session.Conflicts.Any())
            {
                _logger.LogInformation("No conflicts to resolve in session");
                return session;
            }

            AnsiConsole.MarkupLine("[blue]🔧 Conflict Resolution Session[/]");
            AnsiConsole.MarkupLine($"[dim]Found {session.Conflicts.Count} conflict{(session.Conflicts.Count > 1 ? "s" : "")} requiring your attention.[/]");
            AnsiConsole.WriteLine();

            foreach (var conflict in session.Conflicts)
            {
                var resolution = await ResolveConflictAsync(conflict);
                session.Resolutions.Add(resolution);
            }

            AnsiConsole.MarkupLine("[green]✅ All conflicts resolved![/]");
            AnsiConsole.WriteLine();

            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving conflicts in session");
            throw;
        }
    }

    private async Task<ConflictResolution> ResolveConflictAsync(SearchConflict conflict)
    {
        AnsiConsole.MarkupLine($"[yellow]⚠️  Conflict #{conflict.Id[..8]}[/]: [white]{conflict.Query}[/]");
        AnsiConsole.MarkupLine($"[dim]Reason: {GetConflictReasonDescription(conflict.Reason)}[/]");
        AnsiConsole.WriteLine();

        // Display candidate results
        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("Option");
        table.AddColumn("Title");
        table.AddColumn("Artist");
        table.AddColumn("Details");
        table.AddColumn("Quality");
        table.AddColumn("Score");

        var options = new List<string>();
        
        for (int i = 0; i < conflict.CandidateResults.Count; i++)
        {
            var result = conflict.CandidateResults[i];
            var option = $"{i + 1}";
            options.Add(option);

            var title = result.Title.Length > 30 ? result.Title.Substring(0, 27) + "..." : result.Title;
            var artist = result.Artist.Length > 25 ? result.Artist.Substring(0, 22) + "..." : result.Artist;
            var details = FormatDetails(result);
            var quality = FormatQuality(result);
            var score = $"[dim]{result.Score:F0}[/]";

            table.AddRow(option, title, artist, details, quality, score);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Add action options
        options.Add("s"); // Skip
        options.Add("r"); // Refine search
        options.Add("a"); // Download all (for quality conflicts)

        var actionPrompt = new SelectionPrompt<string>()
            .Title("How would you like to resolve this conflict?");
        
        // Add numbered options
        foreach (var option in options.Take(conflict.CandidateResults.Count))
        {
            actionPrompt.AddChoice(option);
        }
        
        // Add action options
        actionPrompt.AddChoice("s (Skip this query)");
        actionPrompt.AddChoice("r (Refine search - enter new query)");

        if (conflict.Reason == ConflictReason.QualityConflict)
        {
            actionPrompt.AddChoice("a (Download all qualities)");
        }

        var choice = AnsiConsole.Prompt(actionPrompt);

        var resolution = new ConflictResolution
        {
            ConflictId = conflict.Id
        };

        if (int.TryParse(choice, out int selectedIndex) && selectedIndex <= conflict.CandidateResults.Count)
        {
            resolution.SelectedResultId = conflict.CandidateResults[selectedIndex - 1].Id;
            resolution.Action = ResolutionAction.SelectResult;
            AnsiConsole.MarkupLine($"[green]✓ Selected option {selectedIndex}[/]");
        }
        else if (choice.StartsWith("s"))
        {
            resolution.Action = ResolutionAction.SkipQuery;
            AnsiConsole.MarkupLine("[yellow]⏭️  Skipped[/]");
        }
        else if (choice.StartsWith("r"))
        {
            var newQuery = AnsiConsole.Ask<string>("Enter refined search query:");
            resolution.Action = ResolutionAction.RefineSearch;
            resolution.UserNote = newQuery;
            AnsiConsole.MarkupLine($"[blue]🔍 Will search for: '{newQuery}'[/]");
        }
        else if (choice.StartsWith("a"))
        {
            resolution.Action = ResolutionAction.DownloadAll;
            AnsiConsole.MarkupLine("[cyan]📥 Will download all quality versions[/]");
        }

        AnsiConsole.WriteLine();
        return resolution;
    }

    public List<SearchResult> ApplyResolutions(ConflictSession session)
    {
        var finalResults = new List<SearchResult>();

        foreach (var resolution in session.Resolutions)
        {
            var conflict = session.Conflicts.FirstOrDefault(c => c.Id == resolution.ConflictId);
            if (conflict == null) continue;

            switch (resolution.Action)
            {
                case ResolutionAction.SelectResult:
                    var selectedResult = conflict.CandidateResults.FirstOrDefault(r => r.Id == resolution.SelectedResultId);
                    if (selectedResult != null)
                        finalResults.Add(selectedResult);
                    break;

                case ResolutionAction.DownloadAll:
                    finalResults.AddRange(conflict.CandidateResults);
                    break;

                case ResolutionAction.SkipQuery:
                    // Skip - don't add any results
                    break;

                case ResolutionAction.RefineSearch:
                    // This would trigger a new search with the refined query
                    // For now, we'll skip as this requires integration with search service
                    _logger.LogInformation("Refined search requested: {NewQuery}", resolution.UserNote);
                    break;
            }
        }

        return finalResults;
    }

    public async Task SaveSessionAsync(ConflictSession session)
    {
        try
        {
            var sessionsDir = Path.GetDirectoryName(_sessionsPath);
            if (!Directory.Exists(sessionsDir))
                Directory.CreateDirectory(sessionsDir!);

            List<ConflictSession> sessions;
            if (File.Exists(_sessionsPath))
            {
                var json = await File.ReadAllTextAsync(_sessionsPath);
                sessions = JsonSerializer.Deserialize<List<ConflictSession>>(json) ?? new List<ConflictSession>();
            }
            else
            {
                sessions = new List<ConflictSession>();
            }

            sessions.Add(session);

            // Keep only the last 50 sessions
            if (sessions.Count > 50)
                sessions = sessions.OrderByDescending(s => s.CreatedAt).Take(50).ToList();

            var updatedJson = JsonSerializer.Serialize(sessions, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_sessionsPath, updatedJson);

            _logger.LogInformation("Saved conflict session with {ConflictCount} conflicts", session.Conflicts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save conflict session");
        }
    }

    public async Task<List<ConflictSession>> LoadRecentSessionsAsync(int count = 10)
    {
        try
        {
            if (!File.Exists(_sessionsPath))
                return new List<ConflictSession>();

            var json = await File.ReadAllTextAsync(_sessionsPath);
            var sessions = JsonSerializer.Deserialize<List<ConflictSession>>(json) ?? new List<ConflictSession>();

            return sessions.OrderByDescending(s => s.CreatedAt).Take(count).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load recent conflict sessions");
            return new List<ConflictSession>();
        }
    }

    private string GetConflictReasonDescription(ConflictReason reason)
    {
        return reason switch
        {
            ConflictReason.MultipleExactMatches => "Multiple exact matches found",
            ConflictReason.AmbiguousSearchType => "Search could match multiple content types",
            ConflictReason.NoHighConfidenceMatch => "No high-confidence matches found",
            ConflictReason.QualityConflict => "Same content available in multiple qualities",
            ConflictReason.ArtistDisambiguation => "Similar artist names require disambiguation",
            _ => "Unknown conflict type"
        };
    }

    private string FormatDetails(Models.SearchResult result)
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