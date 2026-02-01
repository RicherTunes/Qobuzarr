using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Common.Extensions;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Indexers.RequestGeneration
{
    /// <summary>
    /// Builds search queries from Lidarr search criteria.
    /// Extracted from QobuzRequestGenerator to follow Single Responsibility Principle.
    /// </summary>
    public class QueryBuilder : IQueryBuilder
    {
        private readonly Logger _logger;

        // Regex patterns for query processing
        private static readonly Regex YearPattern = new Regex(@"\b(19|20)\d{2}\b", RegexOptions.Compiled);
        private static readonly Regex SpecialCharsPattern = new Regex(@"[^\w\s\-\.\(\)\[\]]", RegexOptions.Compiled);
        private static readonly Regex MultiSpacePattern = new Regex(@"\s+", RegexOptions.Compiled);

        // Common album title suffixes to remove
        private static readonly string[] AlbumSuffixes =
        {
            "(Deluxe Edition)", "(Deluxe)", "(Expanded Edition)", "(Remastered)",
            "(Remaster)", "(Anniversary Edition)", "(Special Edition)", "(Bonus Track Version)",
            "(Collector's Edition)", "(Limited Edition)", "[Deluxe]", "[Remastered]"
        };

        public QueryBuilder(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public List<string> BuildAlbumSearchQueries(AlbumSearchCriteria searchCriteria)
        {
            var queries = new List<string>();

            try
            {
                var artistName = searchCriteria.ArtistQuery?.Trim();
                var albumTitle = searchCriteria.AlbumQuery?.Trim();

                if (string.IsNullOrWhiteSpace(artistName) && string.IsNullOrWhiteSpace(albumTitle))
                {
                    _logger.Warn("Both artist and album are empty in search criteria");
                    return queries;
                }

                // Primary search: Artist + Album
                if (!string.IsNullOrWhiteSpace(artistName) && !string.IsNullOrWhiteSpace(albumTitle))
                {
                    var primaryQuery = $"{CleanQuery(artistName)} {CleanQuery(albumTitle)}";
                    queries.Add(primaryQuery);

                    // Add query with core album title (without suffixes)
                    var coreAlbumTitle = ExtractCoreAlbumTitle(albumTitle);
                    if (coreAlbumTitle != albumTitle)
                    {
                        var coreQuery = $"{CleanQuery(artistName)} {CleanQuery(coreAlbumTitle)}";
                        if (!queries.Contains(coreQuery))
                        {
                            queries.Add(coreQuery);
                        }
                    }

                    // Add title case variants for better matching
                    var titleCaseArtist = ApplyTitleCase(artistName);
                    var titleCaseAlbum = ApplyTitleCase(albumTitle);
                    if (titleCaseArtist != artistName || titleCaseAlbum != albumTitle)
                    {
                        var titleCaseQuery = $"{CleanQuery(titleCaseArtist)} {CleanQuery(titleCaseAlbum)}";
                        if (!queries.Contains(titleCaseQuery))
                        {
                            queries.Add(titleCaseQuery);
                        }
                    }
                }

                // Fallback: Album only
                if (!string.IsNullOrWhiteSpace(albumTitle))
                {
                    var albumOnlyQuery = CleanQuery(albumTitle);
                    if (!queries.Contains(albumOnlyQuery))
                    {
                        queries.Add(albumOnlyQuery);
                    }
                }

                // Fallback: Artist only  
                if (!string.IsNullOrWhiteSpace(artistName))
                {
                    var artistOnlyQuery = CleanQuery(artistName);
                    if (!queries.Contains(artistOnlyQuery))
                    {
                        queries.Add(artistOnlyQuery);
                    }
                }

                _logger.Debug("Generated {0} search queries for album: {1} - {2}",
                    queries.Count, artistName, albumTitle);

                return queries;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error building album search queries");
                return new List<string>();
            }
        }

        public List<string> BuildArtistSearchQueries(ArtistSearchCriteria searchCriteria)
        {
            var queries = new List<string>();

            try
            {
                var artistName = searchCriteria.ArtistQuery?.Trim();

                if (string.IsNullOrWhiteSpace(artistName))
                {
                    _logger.Warn("Artist name is empty in search criteria");
                    return queries;
                }

                // Primary artist query
                var primaryQuery = CleanQuery(artistName);
                queries.Add(primaryQuery);

                // Title case variant
                var titleCaseArtist = ApplyTitleCase(artistName);
                if (titleCaseArtist != artistName)
                {
                    var titleCaseQuery = CleanQuery(titleCaseArtist);
                    if (!queries.Contains(titleCaseQuery))
                    {
                        queries.Add(titleCaseQuery);
                    }
                }

                _logger.Debug("Generated {0} search queries for artist: {1}", queries.Count, artistName);

                return queries;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error building artist search queries");
                return new List<string>();
            }
        }

        public string CleanQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            try
            {
                // Remove special characters except basic punctuation
                var cleaned = SpecialCharsPattern.Replace(query, " ");

                // Normalize multiple spaces to single space
                cleaned = MultiSpacePattern.Replace(cleaned, " ");

                // Trim whitespace
                cleaned = cleaned.Trim();

                return cleaned;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error cleaning query: {0}", query);
                return query; // Return original if cleaning fails
            }
        }

        public string ApplyTitleCase(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            try
            {
                var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var result = new List<string>();

                foreach (var word in words)
                {
                    if (word.Length == 0) continue;

                    // Skip articles and prepositions for title case
                    var lowerWord = word.ToLowerInvariant();
                    if (IsArticleOrPreposition(lowerWord) && result.Count > 0)
                    {
                        result.Add(lowerWord);
                    }
                    else
                    {
                        // Capitalize first letter, lowercase the rest
                        var titleCased = char.ToUpperInvariant(word[0]) +
                            (word.Length > 1 ? word.Substring(1).ToLowerInvariant() : "");
                        result.Add(titleCased);
                    }
                }

                return string.Join(" ", result);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error applying title case to: {0}", text);
                return text; // Return original if processing fails
            }
        }

        public string ExtractCoreAlbumTitle(string albumTitle)
        {
            if (string.IsNullOrWhiteSpace(albumTitle))
                return albumTitle;

            try
            {
                var coreTitle = albumTitle;

                // Remove common album suffixes
                foreach (var suffix in AlbumSuffixes)
                {
                    if (coreTitle.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        coreTitle = coreTitle.Substring(0, coreTitle.Length - suffix.Length).Trim();
                        break; // Only remove one suffix
                    }
                }

                // Remove trailing parentheses content if it looks like edition info
                var parenMatch = Regex.Match(coreTitle, @"\s*\([^)]*(?:edition|deluxe|remaster|bonus)\)[^)]*$", RegexOptions.IgnoreCase);
                if (parenMatch.Success)
                {
                    coreTitle = coreTitle.Substring(0, parenMatch.Index).Trim();
                }

                return string.IsNullOrWhiteSpace(coreTitle) ? albumTitle : coreTitle;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error extracting core album title from: {0}", albumTitle);
                return albumTitle; // Return original if extraction fails
            }
        }

        public bool TryExtractYear(string query, out int year)
        {
            year = 0;

            try
            {
                var match = YearPattern.Match(query);
                if (match.Success && int.TryParse(match.Value, out year))
                {
                    // Validate year is reasonable (1950-2030)
                    if (year >= 1950 && year <= 2030)
                    {
                        return true;
                    }
                }

                year = 0;
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error extracting year from query: {0}", query);
                year = 0;
                return false;
            }
        }

        private bool IsArticleOrPreposition(string word)
        {
            var articlesAndPrepositions = new[]
            {
                "a", "an", "the", "and", "or", "but", "in", "on", "at", "to",
                "for", "of", "with", "by", "from", "up", "about", "into", "through"
            };

            return articlesAndPrepositions.Contains(word);
        }
    }
}
