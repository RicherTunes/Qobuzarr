using Microsoft.Extensions.Logging;
using QobuzCLI.Models;
using System.Text.RegularExpressions;

namespace QobuzCLI.Services;

public class SearchService : ISearchService
{
    private readonly ILogger<SearchService> _logger;

    public SearchService(ILogger<SearchService> logger)
    {
        _logger = logger;
    }

    public SearchType DetectSearchType(string query)
    {
        try
        {
            var cleanQuery = query.Trim().ToLower();

            // Pattern: "Artist - Album" or "Album by Artist"
            if (Regex.IsMatch(cleanQuery, @"^.+\s+by\s+.+$"))
            {
                _logger.LogDebug("Detected album search pattern: 'X by Y' in query: {Query}", query);
                return SearchType.Album;
            }

            if (Regex.IsMatch(cleanQuery, @"^.+\s*-\s*.+$"))
            {
                _logger.LogDebug("Detected album search pattern: 'X - Y' in query: {Query}", query);
                return SearchType.Album;
            }

            // Year detection: suggests album search
            if (Regex.IsMatch(cleanQuery, @"\b(19|20)\d{2}\b"))
            {
                _logger.LogDebug("Detected year in query, suggesting album search: {Query}", query);
                return SearchType.Album;
            }

            // Track-like patterns
            var trackIndicators = new[]
            {
                "track", "song", "theme", "theme from", "feat", "ft", "featuring",
                "remix", "live", "acoustic", "radio edit", "extended"
            };

            if (trackIndicators.Any(indicator => cleanQuery.Contains(indicator)))
            {
                _logger.LogDebug("Detected track indicators in query: {Query}", query);
                return SearchType.Track;
            }

            // Album-like patterns
            var albumIndicators = new[]
            {
                "album", "lp", "ep", "soundtrack", "ost", "greatest hits", "best of",
                "collection", "anthology", "deluxe", "remaster", "edition"
            };

            if (albumIndicators.Any(indicator => cleanQuery.Contains(indicator)))
            {
                _logger.LogDebug("Detected album indicators in query: {Query}", query);
                return SearchType.Album;
            }

            // Single word suggests artist
            if (!cleanQuery.Contains(' '))
            {
                _logger.LogDebug("Single word detected, assuming artist search: {Query}", query);
                return SearchType.Artist;
            }

            // Common artist patterns
            var artistIndicators = new[]
            {
                "discography", "complete", "all albums", "band", "group"
            };

            if (artistIndicators.Any(indicator => cleanQuery.Contains(indicator)))
            {
                _logger.LogDebug("Detected artist indicators in query: {Query}", query);
                return SearchType.Artist;
            }

            // Default to album for multi-word queries
            _logger.LogDebug("Defaulting to album search for multi-word query: {Query}", query);
            return SearchType.Album;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error detecting search type for query: {Query}", query);
            return SearchType.Album; // Safe default
        }
    }

    public List<SearchResult> ScoreResults(List<SearchResult> results, string query)
    {
        var scoredResults = results.Select(result => 
        {
            result.Score = CalculateRelevanceScore(result, query);
            return result;
        }).OrderByDescending(r => r.Score).ToList();

        _logger.LogDebug("Scored {Count} results for query: {Query}", results.Count, query);
        return scoredResults;
    }

    public bool IsExactMatch(SearchResult result, string query)
    {
        var score = CalculateRelevanceScore(result, query);
        return score >= 95.0;
    }

    public double CalculateRelevanceScore(SearchResult result, string query)
    {
        try
        {
            var queryLower = query.Trim().ToLower();
            var titleLower = result.Title.ToLower();
            var artistLower = result.Artist.ToLower();

            double score = 0;

            // Exact title match
            if (titleLower == queryLower)
            {
                score += 50;
            }
            // Title contains query
            else if (titleLower.Contains(queryLower))
            {
                score += 30;
            }
            // Query contains title (for shorter titles)
            else if (queryLower.Contains(titleLower) && titleLower.Length > 3)
            {
                score += 25;
            }

            // Exact artist match
            if (artistLower == queryLower)
            {
                score += 50;
            }
            // Artist contains query
            else if (artistLower.Contains(queryLower))
            {
                score += 20;
            }

            // Combined artist + title match (for "artist - album" queries)
            var combinedLower = $"{artistLower} - {titleLower}";
            var reverseCombinedLower = $"{titleLower} - {artistLower}";
            
            if (combinedLower.Contains(queryLower) || reverseCombinedLower.Contains(queryLower))
            {
                score += 15;
            }

            // Word matching
            var queryWords = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var titleWords = titleLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var artistWords = artistLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var matchingTitleWords = queryWords.Count(qw => titleWords.Any(tw => tw.Contains(qw) || qw.Contains(tw)));
            var matchingArtistWords = queryWords.Count(qw => artistWords.Any(aw => aw.Contains(qw) || qw.Contains(aw)));

            if (queryWords.Length > 0)
            {
                score += (matchingTitleWords / (double)queryWords.Length) * 20;
                score += (matchingArtistWords / (double)queryWords.Length) * 15;
            }

            // Fuzzy matching bonus (simple Levenshtein-like)
            score += CalculateFuzzyBonus(titleLower, queryLower);

            // Quality bonus
            if (result.Quality.Contains("Hi-Res"))
                score += 5;
            else if (result.Quality.Contains("FLAC"))
                score += 3;

            // Year matching bonus
            if (result.Year.HasValue)
            {
                var yearMatch = Regex.Match(queryLower, @"\b(19|20)(\d{2})\b");
                if (yearMatch.Success)
                {
                    var queryYear = int.Parse($"{yearMatch.Groups[1].Value}{yearMatch.Groups[2].Value}");
                    if (result.Year.Value == queryYear)
                        score += 10;
                    else if (Math.Abs(result.Year.Value - queryYear) <= 1)
                        score += 5;
                }
            }

            // Track count bonus for albums (more tracks = more complete)
            if (result.Type == "album" && result.TrackCount > 0)
            {
                if (result.TrackCount >= 10)
                    score += 2;
                else if (result.TrackCount >= 5)
                    score += 1;
            }

            return Math.Min(100, Math.Max(0, score));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating relevance score for result: {Title} by {Artist}", result.Title, result.Artist);
            return 0;
        }
    }

    private double CalculateFuzzyBonus(string text, string query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
            return 0;

        // Simple similarity bonus based on common characters
        var textChars = text.ToCharArray().Distinct().ToHashSet();
        var queryChars = query.ToCharArray().Distinct().ToHashSet();
        
        var commonChars = textChars.Intersect(queryChars).Count();
        var totalChars = textChars.Union(queryChars).Count();
        
        if (totalChars == 0) return 0;
        
        var similarity = (double)commonChars / totalChars;
        return similarity * 5; // Max 5 point bonus
    }

    public IEnumerable<SearchResult> ScoreResultsPaged(IEnumerable<SearchResult> results, string query, int pageSize = 100)
    {
        // Process results in batches to avoid loading everything into memory
        var batch = new List<SearchResult>(pageSize);
        
        foreach (var result in results)
        {
            result.Score = CalculateRelevanceScore(result, query);
            batch.Add(result);
            
            if (batch.Count >= pageSize)
            {
                // Sort and yield current batch
                foreach (var scoredResult in batch.OrderByDescending(r => r.Score))
                {
                    yield return scoredResult;
                }
                batch.Clear();
            }
        }
        
        // Process remaining items
        if (batch.Any())
        {
            foreach (var scoredResult in batch.OrderByDescending(r => r.Score))
            {
                yield return scoredResult;
            }
        }
    }
}