using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Core
{
    /// <summary>
    /// Manages ML optimization and performance tracking for the Qobuz indexer.
    /// Extracted from QobuzIndexer to follow Single Responsibility Principle.
    /// </summary>
    public interface IIndexerMLManager
    {
        /// <summary>
        /// Creates the appropriate ML optimizer based on settings.
        /// </summary>
        IPatternLearningEngine CreateMLOptimizer(Logger logger);

        /// <summary>
        /// Estimates baseline API calls without ML optimization.
        /// </summary>
        int EstimateBaselineApiCalls(string queryUrl, int resultCount);

        /// <summary>
        /// Calculates actual API optimization achieved by ML.
        /// </summary>
        (int callsSaved, int baselineCalls) CalculateActualApiOptimization(string queryUrl, int resultCount);

        /// <summary>
        /// Logs ML performance summary.
        /// </summary>
        void LogMLPerformanceSummary();

        /// <summary>
        /// Gets a comprehensive ML performance report.
        /// </summary>
        string GetMLPerformanceReport();

        /// <summary>
        /// Gets ML performance metrics for API endpoints.
        /// </summary>
        object GetMLPerformanceMetrics();

        /// <summary>
        /// Gets ML health status.
        /// </summary>
        object GetMLHealthStatus();

        /// <summary>
        /// Gets ML diagnostic report.
        /// </summary>
        object GetMLDiagnosticReport();
    }
}