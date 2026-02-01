using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Interface for pattern learning/optimization engines
    /// </summary>
    public interface IPatternLearningEngine
    {
        QueryComplexity PredictComplexity(string artistName, string albumTitle);
        double GetConfidenceScore(string artistName, string albumTitle, QueryComplexity complexity);
        void RecordResult(string artistName, string albumTitle, QueryComplexity usedComplexity, bool wasSuccessful);
        PatternStatistics GetStatistics();
        List<string> GetOptimizedQueryStrategies(string artistName, string albumTitle);

        // Async methods for compatibility
        Task TrainAsync(IEnumerable<QueryPattern> patterns);
        Task<PredictionResult> PredictOptimalStrategyAsync(string artist, string album);
        Task<ModelMetrics> EvaluateModelAsync();
        Task UpdateModelAsync(QueryResult actualResult);
    }

    /// <summary>
    /// Shared types for query optimization engines
    /// </summary>

    /// <summary>
    /// Query complexity levels based on statistical analysis
    /// </summary>
    public enum QueryComplexity
    {
        Simple,   // 73.7% of queries
        Medium,   // 2.0% of queries  
        Complex   // 24.2% of queries
    }

    /// <summary>
    /// Statistics about pattern recognition performance
    /// </summary>
    public class PatternStatistics
    {
        public int TotalPredictions { get; set; }
        public int CorrectPredictions { get; set; }
        public double Accuracy { get; set; }
        public DateTime LastModelUpdate { get; set; }
        public Dictionary<QueryComplexity, int> PatternDistribution { get; set; }
        public bool IsUsingMLEngine { get; set; }
        public Dictionary<string, object> HybridStatistics { get; set; }
    }

    /// <summary>
    /// Model evaluation metrics
    /// </summary>
    public class ModelMetrics
    {
        public double Accuracy { get; set; }
        public double Precision { get; set; }
        public double Recall { get; set; }
        public double F1Score { get; set; }
        public int[,] ConfusionMatrix { get; set; }
    }

    /// <summary>
    /// Training data pattern for ML model
    /// </summary>
    public class QueryPattern
    {
        public string Artist { get; set; }
        public string Album { get; set; }
        public float[] Features { get; set; }
        public string ActualComplexity { get; set; }
    }

    /// <summary>
    /// Prediction result with confidence and recommendations
    /// </summary>
    public class PredictionResult
    {
        public QueryComplexity PredictedComplexity { get; set; }
        public float Confidence { get; set; }
        public List<string> RecommendedQueries { get; set; }
        public float[] Features { get; set; }
    }

    /// <summary>
    /// Actual query execution result for model feedback
    /// </summary>
    public class QueryResult
    {
        public string Artist { get; set; }
        public string Album { get; set; }
        public QueryComplexity SuccessfulComplexity { get; set; }
        public bool WasSuccessful { get; set; }
        public bool WasPredictionCorrect { get; set; }
        public int ResultCount { get; set; }
        public TimeSpan ResponseTime { get; set; }
    }
}
