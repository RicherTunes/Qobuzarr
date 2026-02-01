using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Constants;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Parsing
{
    /// <summary>
    /// Implementation of album matching and filtering logic.
    /// Extracted from QobuzParser god class to follow Single Responsibility Principle.
    /// </summary>
    internal class AlbumMatcher : IAlbumMatcher
    {
        private readonly Logger _logger;

        public AlbumMatcher(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public NzbDrone.Core.Music.Album FindBestMatchingAlbum(QobuzAlbum qobuzAlbum, List<NzbDrone.Core.Music.Album> lidarrAlbums, int qobuzYear)
        {
            if (lidarrAlbums == null || !lidarrAlbums.Any())
                return null;

            // CRITICAL FIX: Don't use context for unrelated albums
            // We should only use the Lidarr title if the Qobuz album actually matches
            // the search criteria, not for ALL albums returned by Qobuz

            var qobuzTitle = qobuzAlbum.GetFullTitle().ToLowerInvariant();
            var qobuzIsLive = IsLiveAlbum(qobuzAlbum.GetFullTitle());

            // Step 1: Try to find exact title match first (case insensitive)
            var titleMatches = lidarrAlbums.Where(a =>
            {
                if (a.Title == null) return false;
                var lidarrTitle = a.Title.ToLowerInvariant();

                // Check for substantial overlap in titles
                return CalculateTitleSimilarity(qobuzTitle, lidarrTitle) > 0.7;
            }).ToList();

            if (titleMatches.Count == 1)
            {
                _logger.Debug("Found title match for Qobuz '{0}': using Lidarr '{1}'",
                    qobuzAlbum.GetFullTitle(), titleMatches.First().Title);
                return titleMatches.First();
            }

            // Step 2: If we have multiple title matches, disambiguate by year
            if (titleMatches.Count > 1)
            {
                var yearAndTitleMatches = titleMatches.Where(a =>
                    a.ReleaseDate?.Year == qobuzYear ||
                    (qobuzYear == 0 && a.ReleaseDate == null)).ToList();

                if (yearAndTitleMatches.Count == 1)
                {
                    _logger.Debug("Found year+title match for Qobuz '{0}': using Lidarr '{1}'",
                        qobuzAlbum.GetFullTitle(), yearAndTitleMatches.First().Title);
                    return yearAndTitleMatches.First();
                }
                else if (yearAndTitleMatches.Any())
                {
                    // Multiple matches, pick the one with best title similarity
                    var bestMatch = yearAndTitleMatches
                        .OrderByDescending(a => CalculateTitleSimilarity(qobuzTitle, a.Title?.ToLowerInvariant() ?? ""))
                        .First();
                    _logger.Debug("Found best match for Qobuz '{0}': using Lidarr '{1}'",
                        qobuzAlbum.GetFullTitle(), bestMatch.Title);
                    return bestMatch;
                }
            }

            // Step 3: Only apply live album matching if titles are similar
            if (qobuzIsLive)
            {
                var liveMatches = lidarrAlbums.Where(a =>
                    IsLiveAlbum(a.Title) &&
                    CalculateTitleSimilarity(qobuzTitle, a.Title?.ToLowerInvariant() ?? "") > 0.5).ToList();

                if (liveMatches.Count == 1)
                {
                    _logger.Debug("Found live album match for Qobuz '{0}': using Lidarr '{1}'",
                        qobuzAlbum.GetFullTitle(), liveMatches.First().Title);
                    return liveMatches.First();
                }
            }

            // Step 4: NO FALLBACK - if we can't find a good match, don't use context
            // This prevents the bug where all albums get the same title
            _logger.Debug("No matching Lidarr album found for Qobuz '{0}', using original title",
                qobuzAlbum.GetFullTitle());
            return null; // Return null to indicate no match found
        }

        public double CalculateTitleSimilarity(string title1, string title2)
        {
            if (string.IsNullOrWhiteSpace(title1) || string.IsNullOrWhiteSpace(title2))
                return 0;

            // Remove common words and punctuation for comparison
            var cleanTitle1 = CleanTitleForComparison(title1);
            var cleanTitle2 = CleanTitleForComparison(title2);

            if (cleanTitle1 == cleanTitle2)
                return 1.0;

            // Check if one title contains the other (for live albums, special editions, etc.)
            if (cleanTitle1.Contains(cleanTitle2) || cleanTitle2.Contains(cleanTitle1))
                return 0.8;

            // Calculate word overlap
            var words1 = cleanTitle1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var words2 = cleanTitle2.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words1.Length == 0 || words2.Length == 0)
                return 0;

            var commonWords = words1.Intersect(words2).Count();
            var totalWords = Math.Max(words1.Length, words2.Length);

            return (double)commonWords / totalWords;
        }

        public string CleanTitleForComparison(string title)
        {
            // Remove punctuation and convert to lowercase
            var cleaned = Regex.Replace(title.ToLowerInvariant(), @"[^\w\s]", " ");

            // Remove common words that don't help with matching
            var stopWords = new[] { "the", "a", "an", "and", "or", "of", "in", "on", "at", "to", "for" };
            var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !stopWords.Contains(w))
                .ToArray();

            return string.Join(" ", words);
        }

        public bool ShouldIncludeAlbum(QobuzAlbum album, QobuzIndexerSettings settings)
        {
            // Check if album is streamable
            if (!album.Streamable)
            {
                return false;
            }

            // Filter singles if not included
            if (!settings.IncludeSingles && IsLikelySingle(album))
            {
                return false;
            }

            // Filter compilations if not included
            if (!settings.IncludeCompilations && IsLikelyCompilation(album))
            {
                return false;
            }

            return true;
        }

        public bool IsLikelySingle(QobuzAlbum album)
        {
            // Consider it a single if it has few tracks and is short duration
            return album.TracksCount <= 3 && album.Duration < TimeSpan.FromMinutes(15);
        }

        public bool IsLikelyCompilation(QobuzAlbum album)
        {
            var title = album.GetFullTitle().ToLower();
            var compilationKeywords = new[] { "compilation", "various artists", "best of", "greatest hits", "collection" };

            return compilationKeywords.Any(keyword => title.Contains(keyword)) ||
                   album.GetArtistName().Equals("Various Artists", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsLiveAlbum(string albumTitle)
        {
            if (string.IsNullOrWhiteSpace(albumTitle))
                return false;

            var liveTerms = new[] { " live", "(live)", "[live]", "live at", "live in", "concert", "unplugged" };
            return liveTerms.Any(term => albumTitle.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
    }
}
