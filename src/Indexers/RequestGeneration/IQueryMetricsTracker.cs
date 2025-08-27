namespace Lidarr.Plugin.Qobuzarr.Indexers.RequestGeneration
{
    /// <summary>
    /// Tracks query optimization metrics.
    /// Extracted from QobuzRequestGenerator to follow Single Responsibility Principle.
    /// </summary>
    public interface IQueryMetricsTracker
    {
        /// <summary>
        /// Updates query optimization metrics.
        /// </summary>
        void UpdateQueryMetrics(int originalCount, int optimizedCount);

        /// <summary>
        /// Calculates relevance score for a query result.
        /// </summary>
        int CalculateRelevanceScore(string query, string albumTitle, string artistName);

        /// <summary>
        /// Gets current optimization statistics.
        /// </summary>
        (int totalOriginal, int totalOptimized, double optimizationPercentage) GetOptimizationStats();

        /// <summary>
        /// Resets all metrics.
        /// </summary>
        void ResetMetrics();
    }
}