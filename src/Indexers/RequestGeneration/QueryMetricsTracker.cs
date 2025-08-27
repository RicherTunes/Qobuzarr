using System;
using System.Linq;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Indexers.RequestGeneration
{
    /// <summary>
    /// Tracks query optimization metrics.
    /// Extracted from QobuzRequestGenerator to follow Single Responsibility Principle.
    /// </summary>
    public class QueryMetricsTracker : IQueryMetricsTracker
    {
        private readonly Logger _logger;
        private readonly object _metricsLock = new object();
        
        // Metrics tracking
        private int _totalOriginalQueries = 0;
        private int _totalOptimizedQueries = 0;
        private DateTime _lastMetricsReset = DateTime.UtcNow;

        public QueryMetricsTracker(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void UpdateQueryMetrics(int originalCount, int optimizedCount)
        {
            try
            {
                lock (_metricsLock)
                {
                    _totalOriginalQueries += originalCount;
                    _totalOptimizedQueries += optimizedCount;

                    var optimizationPercentage = _totalOriginalQueries > 0 
                        ? (1.0 - (double)_totalOptimizedQueries / _totalOriginalQueries) * 100.0 
                        : 0.0;

                    _logger.Debug("Query optimization: {0} → {1} queries ({2:F1}% reduction)",
                        originalCount, optimizedCount, 
                        originalCount > 0 ? (1.0 - (double)optimizedCount / originalCount) * 100.0 : 0.0);

                    // Log summary periodically
                    var timeSinceLastLog = DateTime.UtcNow - _lastMetricsReset;
                    if (timeSinceLastLog > TimeSpan.FromMinutes(5)) // Log every 5 minutes
                    {
                        _logger.Info("🤖 Query Intelligence Summary - Total optimization: {0:F1}% " +
                                   "({1} → {2} queries over {3:F1} minutes)",
                            optimizationPercentage, _totalOriginalQueries, _totalOptimizedQueries,
                            timeSinceLastLog.TotalMinutes);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating query metrics");
            }
        }

        public int CalculateRelevanceScore(string query, string albumTitle, string artistName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query) || 
                    (string.IsNullOrWhiteSpace(albumTitle) && string.IsNullOrWhiteSpace(artistName)))
                {
                    return 0;
                }

                var score = 0;
                var queryLower = query.ToLowerInvariant();
                var albumLower = albumTitle?.ToLowerInvariant() ?? "";
                var artistLower = artistName?.ToLowerInvariant() ?? "";

                // Exact matches get highest score
                if (queryLower == albumLower || queryLower == artistLower)
                {
                    score += 100;
                }

                // Partial matches
                if (!string.IsNullOrEmpty(albumLower) && albumLower.Contains(queryLower))
                {
                    score += 75;
                }
                else if (!string.IsNullOrEmpty(albumLower) && queryLower.Contains(albumLower))
                {
                    score += 60;
                }

                if (!string.IsNullOrEmpty(artistLower) && artistLower.Contains(queryLower))
                {
                    score += 50;
                }
                else if (!string.IsNullOrEmpty(artistLower) && queryLower.Contains(artistLower))
                {
                    score += 40;
                }

                // Word-level matching for better relevance
                var queryWords = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var albumWords = albumLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var artistWords = artistLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                foreach (var queryWord in queryWords)
                {
                    if (albumWords.Any(w => w.Equals(queryWord, StringComparison.OrdinalIgnoreCase)))
                    {
                        score += 20;
                    }
                    if (artistWords.Any(w => w.Equals(queryWord, StringComparison.OrdinalIgnoreCase)))
                    {
                        score += 15;
                    }
                }

                return Math.Min(score, 100); // Cap at 100
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error calculating relevance score for query: {0}", query);
                return 0;
            }
        }

        public (int totalOriginal, int totalOptimized, double optimizationPercentage) GetOptimizationStats()
        {
            lock (_metricsLock)
            {
                var optimizationPercentage = _totalOriginalQueries > 0 
                    ? (1.0 - (double)_totalOptimizedQueries / _totalOriginalQueries) * 100.0 
                    : 0.0;

                return (_totalOriginalQueries, _totalOptimizedQueries, optimizationPercentage);
            }
        }

        public void ResetMetrics()
        {
            try
            {
                lock (_metricsLock)
                {
                    _logger.Info("Resetting query metrics - Previous stats: {0} → {1} queries", 
                        _totalOriginalQueries, _totalOptimizedQueries);
                    
                    _totalOriginalQueries = 0;
                    _totalOptimizedQueries = 0;
                    _lastMetricsReset = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error resetting metrics");
            }
        }
    }
}