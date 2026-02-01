using System.ComponentModel;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Query optimization modes for reducing API calls
    /// </summary>
    public enum QueryOptimizationMode
    {
        /// <summary>
        /// No optimization - all queries are executed
        /// </summary>
        [Description("Disabled - Execute all search queries")]
        Disabled = 0,

        /// <summary>
        /// Pattern-based query intelligence
        /// Analyzes artist/album patterns to skip unnecessary queries
        /// Typically saves ~35% of API calls
        /// </summary>
        [Description("Query Intelligence - Pattern analysis (~35% API reduction)")]
        QueryIntelligence = 1,

        /// <summary>
        /// Machine learning based predictions
        /// Uses pre-trained models to predict optimal search strategy
        /// Typically saves ~49% of API calls
        /// </summary>
        [Description("ML Prediction - Machine learning (~49% API reduction)")]
        MLPrediction = 2
    }
}
