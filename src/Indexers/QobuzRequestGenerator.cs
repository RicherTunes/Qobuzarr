using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Indexers;
using NLog;
using Lidarr.Plugin.Common.Services.Intelligence;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Indexers.RequestGeneration;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Refactored Qobuz search request generator with decomposed responsibilities.
    /// Reduced from 909 lines to ~150 lines by extracting specialized services:
    /// - QueryBuilder: Constructs search queries from criteria
    /// - RequestFactory: Creates HTTP requests for API endpoints  
    /// - QueryMetricsTracker: Tracks optimization performance
    /// 
    /// Maintains all original functionality including ML optimization and Query Intelligence.
    /// </summary>
    public class QobuzRequestGenerator : IIndexerRequestGenerator
    {
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;
        private readonly Func<QobuzSession> _getSession;

        // Decomposed service dependencies
        private readonly IQueryBuilder _queryBuilder;
        private readonly IRequestFactory _requestFactory;
        private readonly IQueryMetricsTracker _metricsTracker;

        // ML optimization services (preserved from original)
        private readonly SmartQueryStrategy _smartQueryStrategy;
        private readonly SemanticQueryStrategy _semanticQueryStrategy;

        // Context for parser integration
        private SearchCriteriaBase _currentSearchCriteria;

        public QobuzRequestGenerator(
            QobuzIndexerSettings settings,
            Logger logger,
            Func<QobuzSession> getSession = null,
            IPatternLearningEngine patternLearningEngine = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _getSession = getSession;

            // Initialize decomposed services
            _queryBuilder = new QueryBuilder(logger);
            _requestFactory = new RequestFactory(settings, logger);
            _metricsTracker = new QueryMetricsTracker(logger);

            // Initialize ML optimization services (preserved functionality)
            var useMLPredictions = patternLearningEngine != null && (settings?.IsMLPredictionEnabled() ?? false);
            _smartQueryStrategy = new SmartQueryStrategy(logger, patternLearningEngine, useMLPredictions);
            _semanticQueryStrategy = new SemanticQueryStrategy(logger);
        }

        public SearchCriteriaBase GetCurrentSearchCriteria()
        {
            return _currentSearchCriteria;
        }

        public IndexerPageableRequestChain GetSearchRequests(AlbumSearchCriteria searchCriteria)
        {
            try
            {
                _currentSearchCriteria = searchCriteria;
                _logger.Debug("Generating album search requests for: {0} - {1}",
                    searchCriteria.ArtistQuery, searchCriteria.AlbumQuery);

                // Build search queries using decomposed service
                var queries = _queryBuilder.BuildAlbumSearchQueries(searchCriteria);
                if (!queries.Any())
                {
                    _logger.Warn("No search queries generated for criteria");
                    return new IndexerPageableRequestChain();
                }

                // Apply ML optimization
                var originalQueryCount = queries.Count;
                queries = ApplyMLOptimization(queries, searchCriteria);

                // Update metrics using decomposed service
                _metricsTracker.UpdateQueryMetrics(originalQueryCount, queries.Count);

                // Create requests using decomposed service
                var requests = CreateIndexerRequests(queries, searchCriteria);
                var chain = new IndexerPageableRequestChain();
                if (requests.Any())
                {
                    chain.Add(requests);
                }
                return chain;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error generating album search requests");
                return new IndexerPageableRequestChain();
            }
        }

        public IndexerPageableRequestChain GetSearchRequests(ArtistSearchCriteria searchCriteria)
        {
            try
            {
                _currentSearchCriteria = searchCriteria;
                _logger.Debug("Generating artist search requests for: {0}", searchCriteria.ArtistQuery);

                // Build search queries using decomposed service
                var queries = _queryBuilder.BuildArtistSearchQueries(searchCriteria);
                if (!queries.Any())
                {
                    _logger.Warn("No artist search queries generated");
                    return new IndexerPageableRequestChain();
                }

                // Apply ML optimization
                var originalQueryCount = queries.Count;
                queries = ApplyMLOptimization(queries, searchCriteria);

                // Update metrics using decomposed service
                _metricsTracker.UpdateQueryMetrics(originalQueryCount, queries.Count);

                // Create requests using decomposed service
                var requests = CreateIndexerRequests(queries, searchCriteria);
                var chain = new IndexerPageableRequestChain();
                if (requests.Any())
                {
                    chain.Add(requests);
                }
                return chain;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error generating artist search requests");
                return new IndexerPageableRequestChain();
            }
        }

        public IndexerPageableRequestChain GetRecentRequests()
        {
            try
            {
                _logger.Debug("Generating recent releases requests");

                // Qobuz doesn't have a direct "recent releases" endpoint, so use a broad search
                var recentQueries = new[] { "2024", "2023", "new releases" };

                var session = _getSession?.Invoke();
                var requests = new List<IndexerRequest>();

                foreach (var query in recentQueries)
                {
                    var request = _requestFactory.CreateSearchRequest(query, null, session);
                    requests.Add(request);
                }

                var chain = new IndexerPageableRequestChain();
                if (requests.Any())
                {
                    chain.Add(requests);
                }
                return chain;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error generating recent requests");
                return new IndexerPageableRequestChain();
            }
        }

        private List<string> ApplyMLOptimization(List<string> queries, SearchCriteriaBase searchCriteria)
        {
            try
            {
                if (!(_settings?.IsQueryIntelligenceEnabled() ?? true))
                {
                    _logger.Debug("Query Intelligence disabled, using all queries");
                    return queries;
                }

                // Apply smart query strategy optimization
                var optimizedQueries = queries;
                if (_smartQueryStrategy != null && searchCriteria is AlbumSearchCriteria albumCriteria)
                {
                    optimizedQueries = _smartQueryStrategy.BuildOptimizedQueries(albumCriteria.ArtistQuery, albumCriteria.AlbumQuery, queries);
                }
                else if (_smartQueryStrategy != null && searchCriteria is ArtistSearchCriteria artistCriteria)
                {
                    // For artist searches, use artist name as both parameters
                    optimizedQueries = _smartQueryStrategy.BuildOptimizedQueries(artistCriteria.ArtistQuery, "", queries);
                }

                // Note: Semantic query strategy would need artist/album context to be useful
                // Skipping semantic optimization for now since it requires specific context

                _logger.Debug("ML optimization: {0} → {1} queries", queries.Count, optimizedQueries.Count);
                return optimizedQueries.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error applying ML optimization, using original queries");
                return queries;
            }
        }

        // Cap on the over-specific (combined / album-only) queries issued per search, to keep API
        // calls bounded. The artist-only catalogue fallback is issued IN ADDITION to this cap (never
        // truncated away) so a special-char/over-specific query can always degrade to the band's
        // catalogue.
        private const int MaxOverSpecificRequests = 3;

        private List<IndexerRequest> CreateIndexerRequests(List<string> queries, SearchCriteriaBase searchCriteria)
        {
            var requests = new List<IndexerRequest>();
            var session = _getSession?.Invoke();

            // Guarantee the artist-only fallback is always sent for album searches — the shipped
            // "Bleu Jeans Bleu - Record n°V" bug was a special-char album query returning 0 results
            // while the artist-only fallback had been truncated away by the request cap.
            IReadOnlyList<string> artistOnlyFallbacks = Array.Empty<string>();
            if (searchCriteria is AlbumSearchCriteria albumCriteria)
            {
                var artistName = albumCriteria.ArtistQuery;
                if (string.IsNullOrWhiteSpace(artistName))
                {
                    artistName = albumCriteria.Artist?.Name;
                }

                artistOnlyFallbacks = _queryBuilder.BuildArtistFallbackQueries(artistName);
            }

            // Cap the over-specific queries (deduped, blank-dropped, best-first) but always preserve the
            // artist-only fallback tier. Shared cross-plugin policy: Common's CappedSearchChain — see its
            // fallback-survival tests for the Bleu Jeans Bleu regression coverage.
            var selected = CappedSearchChain.Build(queries, artistOnlyFallbacks, MaxOverSpecificRequests);

            foreach (var query in selected)
            {
                try
                {
                    var request = _requestFactory.CreateSearchRequest(query, searchCriteria, session);
                    requests.Add(request);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error creating request for query: {0}", query);
                }
            }

            return requests;
        }

        public int CalculateRelevanceScore(string query, string albumTitle, string artistName)
        {
            return _metricsTracker.CalculateRelevanceScore(query, albumTitle, artistName);
        }

        public (int totalOriginal, int totalOptimized, double optimizationPercentage) GetOptimizationStats()
        {
            return _metricsTracker.GetOptimizationStats();
        }

        public void ResetMetrics()
        {
            _metricsTracker.ResetMetrics();
        }
    }
}
