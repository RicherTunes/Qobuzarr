using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.IndexerSearch.Definitions;
using NLog;
using Lidarr.Plugin.Common.Services.Intelligence;

namespace Lidarr.Plugin.Qobuzarr.Indexers.RequestGeneration
{
    /// <summary>
    /// Builds search queries from Lidarr search criteria.
    ///
    /// Delegates all query canonicalization and variant generation to Common's
    /// <see cref="SearchQuerySanitizer"/>. The previous bespoke regex (which mapped a degree sign
    /// to a space — splitting "Record n°V" into "Record n V" so Qobuz's tokenizer couldn't match)
    /// is replaced by the shared sanitizer, which emits BOTH the symbol-removed-adjacent form
    /// ("Record nV") and the spaced separator form ("AC DC"), and guarantees a non-truncated
    /// artist-only fallback tier.
    /// </summary>
    public class QueryBuilder : IQueryBuilder
    {
        private readonly Logger _logger;

        // Emit promo/edition-suffix-stripped fallbacks (replaces the old ExtractCoreAlbumTitle pass)
        // on top of the cross-plugin defaults; the roman/number crosswalk stays off to avoid
        // over-folding real titles.
        private static readonly SanitizerOptions QueryOptions = new SanitizerOptions
        {
            StripVersionSuffix = true,
        };

        public QueryBuilder(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Test entry point: returns the SearchPlan this builder produces for (artist, album).
        /// Exposes the REAL plan-construction path (same options as BuildAlbumSearchQueries) so
        /// SearchTermProvenanceComplianceTestBase and SearchQuerySanitizerParityTestBase can assert
        /// plan-shape without constructing a full generator.
        /// </summary>
        public static SearchPlan BuildPlanForTest(string artist, string album) =>
            SearchQuerySanitizer.BuildPlan(artist, album, QueryOptions);

        public List<string> BuildAlbumSearchQueries(AlbumSearchCriteria searchCriteria)
        {
            var queries = new List<string>();

            try
            {
                var artistName = ResolveArtist(searchCriteria);
                var albumTitle = ResolveAlbum(searchCriteria);

                if (string.IsNullOrWhiteSpace(artistName) && string.IsNullOrWhiteSpace(albumTitle))
                {
                    _logger.Warn("Both artist and album are empty in search criteria");
                    return queries;
                }

                // Ordered fallback tiers: combined -> artist-only -> album-only. The artist-only tier
                // is never truncated, so an over-specific/special-char album query degrades to the
                // band's catalogue (Lidarr matches the wanted album from it).
                var plan = SearchQuerySanitizer.BuildPlan(artistName, albumTitle, QueryOptions);
                foreach (var tier in plan.Tiers)
                {
                    foreach (var variant in tier)
                    {
                        AddQuery(queries, variant);
                    }
                }

                // Always record the artist-only catalogue fallback slot, even when the artist
                // canonicalizes to no usable signal (e.g. a symbol-only band name) — request
                // creation skips blank queries, but downstream callers rely on its presence.
                if (!string.IsNullOrWhiteSpace(artistName))
                {
                    AddQuery(queries, CleanQuery(artistName), allowEmpty: true);
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
                var artistName = (searchCriteria?.ArtistQuery ?? searchCriteria?.Artist?.Name)?.Trim();

                if (string.IsNullOrWhiteSpace(artistName))
                {
                    _logger.Warn("Artist name is empty in search criteria");
                    return queries;
                }

                var sanitized = SearchQuerySanitizer.Sanitize(artistName, QueryOptions);
                foreach (var variant in sanitized.Variants)
                {
                    AddQuery(queries, variant);
                }

                // Preserve the prior contract: the cleaned artist string is always present.
                AddQuery(queries, CleanQuery(artistName), allowEmpty: true);

                _logger.Debug("Generated {0} search queries for artist: {1}", queries.Count, artistName);

                return queries;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error building artist search queries");
                return new List<string>();
            }
        }

        public IReadOnlyList<string> BuildArtistFallbackQueries(string artistName)
        {
            var queries = new List<string>();

            if (string.IsNullOrWhiteSpace(artistName))
            {
                return queries;
            }

            try
            {
                var sanitized = SearchQuerySanitizer.Sanitize(artistName, QueryOptions);
                foreach (var variant in sanitized.Variants)
                {
                    AddQuery(queries, variant);
                }

                // Preserve the prior exact-fallback contract even if a future sanitizer option changes
                // variant ordering or suppresses Original for a corner case.
                AddQuery(queries, sanitized.Original, allowEmpty: true);
                return queries;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error building artist fallback queries: {0}", artistName);
                AddQuery(queries, artistName);
                return queries;
            }
        }

        public string CleanQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            try
            {
                return SearchQuerySanitizer.Sanitize(query, QueryOptions).Original;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error cleaning query: {0}", query);
                return query; // Return original if cleaning fails
            }
        }

        // Lidarr's AlbumSearchCriteria.ArtistQuery/AlbumQuery are backed by the flattened
        // AlbumTitle/Artist.Name; fall back to the Artist/Albums entities so the criteria is read
        // robustly regardless of which surface the caller populated.
        private static string ResolveArtist(AlbumSearchCriteria searchCriteria)
        {
            var artist = searchCriteria?.ArtistQuery;
            if (string.IsNullOrWhiteSpace(artist))
            {
                artist = searchCriteria?.Artist?.Name;
            }

            return artist?.Trim();
        }

        private static string ResolveAlbum(AlbumSearchCriteria searchCriteria)
        {
            var album = searchCriteria?.AlbumQuery;
            if (string.IsNullOrWhiteSpace(album))
            {
                album = searchCriteria?.Albums?.FirstOrDefault()?.Title;
            }

            return album?.Trim();
        }

        private static void AddQuery(List<string> queries, string query, bool allowEmpty = false)
        {
            if (query == null)
                return;

            if (!allowEmpty && string.IsNullOrWhiteSpace(query))
                return;

            if (!queries.Contains(query, StringComparer.OrdinalIgnoreCase))
            {
                queries.Add(query);
            }
        }
    }
}
