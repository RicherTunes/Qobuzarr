using System.Collections.Generic;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Models;

namespace Lidarr.Plugin.Common.Services.Intelligence
{
    /// <summary>
    /// Interface for query optimization engines.
    /// Allows plugins to implement ML-powered or rule-based query optimization.
    /// </summary>
    public interface IQueryOptimizer
    {
        /// <summary>
        /// Optimizes a search query to reduce API calls while maintaining accuracy.
        /// </summary>
        /// <param name="originalQuery">The original search query</param>
        /// <param name="context">Additional context for optimization</param>
        /// <returns>Optimized query that should produce better results</returns>
        Task<OptimizedQuery> OptimizeQueryAsync(string originalQuery, QueryContext context = null);

        /// <summary>
        /// Learns from query results to improve future optimizations.
        /// </summary>
        /// <param name="query">The query that was executed</param>
        /// <param name="results">The results that were returned</param>
        /// <param name="userFeedback">Whether the results were satisfactory</param>
        Task LearnFromResultsAsync(string query, QueryResults results, QueryFeedback userFeedback);

        /// <summary>
        /// Gets optimization statistics and performance metrics.
        /// </summary>
        Task<OptimizationMetrics> GetMetricsAsync();

        /// <summary>
        /// Resets the optimization engine to its initial state.
        /// </summary>
        Task ResetAsync();
    }

    /// <summary>
    /// Context information for query optimization.
    /// </summary>
    public class QueryContext
    {
        /// <summary>
        /// The type of content being searched for.
        /// </summary>
        public QueryType Type { get; set; } = QueryType.Album;

        /// <summary>
        /// Country/region code for localized optimization.
        /// </summary>
        public string Country { get; set; }

        /// <summary>
        /// User's preferred quality tier.
        /// </summary>
        public StreamingQualityTier? PreferredQuality { get; set; }

        /// <summary>
        /// Additional context metadata.
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Previous query attempts (for retry scenarios).
        /// </summary>
        public List<string> PreviousAttempts { get; set; } = new List<string>();

        /// <summary>
        /// Search confidence level (0.0 - 1.0).
        /// </summary>
        public double Confidence { get; set; } = 1.0;
    }

    /// <summary>
    /// Types of queries for optimization.
    /// </summary>
    public enum QueryType
    {
        Album,
        Artist,
        Track,
        Playlist,
        Label,
        Genre
    }

    /// <summary>
    /// Result of query optimization.
    /// </summary>
    public class OptimizedQuery
    {
        /// <summary>
        /// The optimized query string.
        /// </summary>
        public string Query { get; set; }

        /// <summary>
        /// Confidence level in this optimization (0.0 - 1.0).
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// Alternative query suggestions.
        /// </summary>
        public List<string> Alternatives { get; set; } = new List<string>();

        /// <summary>
        /// Explanation of what optimizations were applied.
        /// </summary>
        public string OptimizationReason { get; set; }

        /// <summary>
        /// Whether this optimization is experimental.
        /// </summary>
        public bool IsExperimental { get; set; }

        /// <summary>
        /// Additional parameters for the optimized query.
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Performance metrics for this optimization.
        /// </summary>
        public Dictionary<string, double> Metrics { get; set; } = new Dictionary<string, double>();
    }

    /// <summary>
    /// Results from executing a query.
    /// </summary>
    public class QueryResults
    {
        /// <summary>
        /// Number of results returned.
        /// </summary>
        public int ResultCount { get; set; }

        /// <summary>
        /// Time taken to execute the query.
        /// </summary>
        public System.TimeSpan ExecutionTime { get; set; }

        /// <summary>
        /// Whether the query found any results.
        /// </summary>
        public bool HasResults => ResultCount > 0;

        /// <summary>
        /// Quality of results (relevance score).
        /// </summary>
        public double RelevanceScore { get; set; } = 0.0;

        /// <summary>
        /// Additional result metadata.
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// User feedback on query results.
    /// </summary>
    public class QueryFeedback
    {
        /// <summary>
        /// Whether the user was satisfied with the results.
        /// </summary>
        public bool Satisfied { get; set; }

        /// <summary>
        /// Rating of result quality (1-5).
        /// </summary>
        public int Rating { get; set; }

        /// <summary>
        /// Specific issues with the results.
        /// </summary>
        public List<string> Issues { get; set; } = new List<string>();

        /// <summary>
        /// User's preferred result (if any).
        /// </summary>
        public string PreferredResult { get; set; }

        /// <summary>
        /// Additional user comments.
        /// </summary>
        public string Comments { get; set; }
    }

    /// <summary>
    /// Optimization performance metrics.
    /// </summary>
    public class OptimizationMetrics
    {
        /// <summary>
        /// Total queries processed.
        /// </summary>
        public long TotalQueries { get; set; }

        /// <summary>
        /// Number of queries that were optimized.
        /// </summary>
        public long OptimizedQueries { get; set; }

        /// <summary>
        /// Average optimization confidence.
        /// </summary>
        public double AverageConfidence { get; set; }

        /// <summary>
        /// Percentage reduction in API calls achieved.
        /// </summary>
        public double ApiCallReduction { get; set; }

        /// <summary>
        /// Average improvement in result relevance.
        /// </summary>
        public double RelevanceImprovement { get; set; }

        /// <summary>
        /// Time range for these metrics.
        /// </summary>
        public System.TimeSpan TimeRange { get; set; }

        /// <summary>
        /// Additional performance metadata.
        /// </summary>
        public Dictionary<string, double> AdditionalMetrics { get; set; } = new Dictionary<string, double>();
    }
}