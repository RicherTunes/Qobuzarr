using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Lidarr.Plugin.Common.Collections;
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Security;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Core
{
    /// <summary>
    /// Manages ML optimization and performance tracking for the Qobuz indexer.
    /// Extracted from QobuzIndexer god class to improve maintainability.
    /// </summary>
    public class IndexerMLManager : IIndexerMLManager, IDisposable
    {
        // Maximum number of per-URL metric slots retained before the dictionary
        // clears on overflow. Each slot is a cheap POCO (no timers), so a full
        // clear on overflow is O(n) but negligibly cheap at n=64.
        // Wave 18D-T5: cap enforcement moved from hand-rolled
        // `if (Count >= cap) Clear()` to Common's BoundedConcurrentDictionary.
        // The dictionary is thread-safe internally, but we keep _metricsLock for
        // the compound check-then-update at call sites (the in-place mutation of
        // MLPerformanceMetrics properties is not atomic on its own).
        private const int MetricsCapacity = 64;

        private readonly ISecureMLModelLoader _secureModelLoader;
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;

        // ML Performance tracking
        private readonly BoundedConcurrentDictionary<string, MLPerformanceMetrics> _performanceMetrics = new(MetricsCapacity);
        private readonly object _metricsLock = new object();
        private bool _disposed = false;

        public IndexerMLManager(
            ISecureMLModelLoader secureModelLoader,
            QobuzIndexerSettings settings,
            Logger logger)
        {
            _secureModelLoader = secureModelLoader ?? throw new ArgumentNullException(nameof(secureModelLoader));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IPatternLearningEngine CreateMLOptimizer(Logger logger)
        {
            var modelType = (MLModelType)(_settings?.MLModelType ?? (int)MLModelType.Baseline);

            logger.Info($"Initializing ML optimizer: {modelType}");

            try
            {
                switch (modelType)
                {
                    case MLModelType.Baseline:
                        logger.Info("Using baseline ML model (trained on 500K+ albums)");
                        return new CompiledMLQueryOptimizer(logger);

                    case MLModelType.Personal:
                        logger.Info("Attempting to load personal ML model");
                        var personalModel = TryLoadPersonalModel(logger);
                        if (personalModel != null)
                        {
                            logger.Info("✅ Personal ML model loaded successfully");
                            return personalModel;
                        }
                        logger.Warn("❌ Personal ML model not found, falling back to baseline");
                        return new CompiledMLQueryOptimizer(logger);

                    case MLModelType.Hybrid:
                        logger.Info("Initializing hybrid ML model (baseline + personal)");
                        var baselineModel = new CompiledMLQueryOptimizer(logger);
                        var personalModelForHybrid = TryLoadPersonalModel(logger);

                        if (personalModelForHybrid != null)
                        {
                            logger.Info("✅ Hybrid ML model initialized with both baseline and personal models");
                            return new HybridMLQueryOptimizer(logger, baselineModel, personalModelForHybrid);
                        }
                        logger.Warn("❌ Personal model not available for hybrid mode, using baseline only");
                        return baselineModel;

                    default:
                        logger.Warn($"Unknown ML model type: {modelType}, using baseline");
                        return new CompiledMLQueryOptimizer(logger);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to initialize ML optimizer, falling back to baseline");
                return new CompiledMLQueryOptimizer(logger);
            }
        }

        public int EstimateBaselineApiCalls(string queryUrl, int resultCount)
        {
            try
            {
                var baselineCalls = 1; // Initial search request

                // Additional calls for pagination (rough estimate)
                if (resultCount > 25) // Qobuz typical page size
                {
                    var additionalPages = Math.Max(0, (resultCount - 25) / 25);
                    baselineCalls += additionalPages;
                }

                // Additional calls for metadata enrichment (if applicable)
                if (queryUrl?.Contains("track") == true)
                {
                    baselineCalls += Math.Min(resultCount / 5, 5); // Track detail calls
                }

                lock (_metricsLock)
                {
                    var key = GetMetricsKey(queryUrl);
                    if (!_performanceMetrics.ContainsKey(key))
                    {
                        // BoundedConcurrentDictionary handles overflow internally via
                        // clear-on-cap-reached. No need to check Count or call Clear here.
                        _performanceMetrics[key] = new MLPerformanceMetrics();
                    }
                    _performanceMetrics[key].EstimatedBaselineCalls = baselineCalls;
                }

                return baselineCalls;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error estimating baseline API calls");
                return 1; // Default to single call
            }
        }

        public (int callsSaved, int baselineCalls) CalculateActualApiOptimization(string queryUrl, int resultCount)
        {
            try
            {
                var baselineCalls = EstimateBaselineApiCalls(queryUrl, resultCount);
                var actualCalls = 1; // We made one optimized call
                var callsSaved = Math.Max(0, baselineCalls - actualCalls);

                lock (_metricsLock)
                {
                    var key = GetMetricsKey(queryUrl);
                    if (_performanceMetrics.ContainsKey(key))
                    {
                        _performanceMetrics[key].ActualCalls = actualCalls;
                        _performanceMetrics[key].CallsSaved = callsSaved;
                        _performanceMetrics[key].OptimizationPercentage =
                            baselineCalls > 0 ? (double)callsSaved / baselineCalls * 100.0 : 0.0;
                    }
                }

                return (callsSaved, baselineCalls);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error calculating API optimization");
                return (0, 1);
            }
        }

        public void LogMLPerformanceSummary()
        {
            try
            {
                lock (_metricsLock)
                {
                    if (!_performanceMetrics.Any()) return;

                    var totalBaseline = _performanceMetrics.Values.Sum(m => m.EstimatedBaselineCalls);
                    var totalActual = _performanceMetrics.Values.Sum(m => m.ActualCalls);
                    var totalSaved = _performanceMetrics.Values.Sum(m => m.CallsSaved);

                    var overallOptimization = totalBaseline > 0 ?
                        (double)totalSaved / totalBaseline * 100.0 : 0.0;

                    _logger.Info("🤖 ML Performance Summary - API calls saved: {0}/{1} ({2:F1}%)",
                        totalSaved, totalBaseline, overallOptimization);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error logging ML performance summary");
            }
        }

        public string GetMLPerformanceReport()
        {
            try
            {
                lock (_metricsLock)
                {
                    if (!_performanceMetrics.Any())
                    {
                        return "No ML performance data available";
                    }

                    var report = new System.Text.StringBuilder();
                    report.AppendLine("ML Performance Report");
                    report.AppendLine("====================");

                    var totalBaseline = _performanceMetrics.Values.Sum(m => m.EstimatedBaselineCalls);
                    var totalActual = _performanceMetrics.Values.Sum(m => m.ActualCalls);
                    var totalSaved = _performanceMetrics.Values.Sum(m => m.CallsSaved);
                    var overallOptimization = totalBaseline > 0 ?
                        (double)totalSaved / totalBaseline * 100.0 : 0.0;

                    report.AppendLine($"Overall Optimization: {overallOptimization:F1}%");
                    report.AppendLine($"API Calls Saved: {totalSaved}");
                    report.AppendLine($"Baseline Calls: {totalBaseline}");
                    report.AppendLine($"Actual Calls: {totalActual}");
                    report.AppendLine();

                    report.AppendLine("Per-Query Breakdown:");
                    foreach (var kvp in _performanceMetrics.OrderByDescending(x => x.Value.CallsSaved))
                    {
                        var metrics = kvp.Value;
                        report.AppendLine($"  {kvp.Key}: {metrics.CallsSaved} saved ({metrics.OptimizationPercentage:F1}%)");
                    }

                    return report.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error generating ML performance report");
                return $"Error generating report: {ex.Message}";
            }
        }

        public object GetMLPerformanceMetrics()
        {
            try
            {
                lock (_metricsLock)
                {
                    var totalBaseline = _performanceMetrics.Values.Sum(m => m.EstimatedBaselineCalls);
                    var totalActual = _performanceMetrics.Values.Sum(m => m.ActualCalls);
                    var totalSaved = _performanceMetrics.Values.Sum(m => m.CallsSaved);

                    return new
                    {
                        overallOptimization = totalBaseline > 0 ? (double)totalSaved / totalBaseline * 100.0 : 0.0,
                        totalCallsSaved = totalSaved,
                        totalBaselineCalls = totalBaseline,
                        totalActualCalls = totalActual,
                        queriesOptimized = _performanceMetrics.Count,
                        averageOptimization = _performanceMetrics.Count > 0 ?
                            _performanceMetrics.Values.Average(m => m.OptimizationPercentage) : 0.0
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting ML performance metrics");
                return new { error = ex.Message };
            }
        }

        public object GetMLHealthStatus()
        {
            try
            {
                var securityStats = _secureModelLoader.GetSecurityStats();

                return new
                {
                    status = "healthy",
                    modelLoader = new
                    {
                        totalLoadAttempts = securityStats.TotalLoadAttempts,
                        successfulLoads = securityStats.SuccessfulLoads,
                        failedValidations = securityStats.FailedValidations,
                        successRate = securityStats.TotalLoadAttempts > 0 ?
                            (double)securityStats.SuccessfulLoads / securityStats.TotalLoadAttempts * 100.0 : 100.0
                    },
                    performance = GetMLPerformanceMetrics()
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting ML health status");
                return new { status = "error", error = ex.Message };
            }
        }

        public object GetMLDiagnosticReport()
        {
            try
            {
                return new
                {
                    configuration = new
                    {
                        modelType = _settings?.MLModelType ?? 0,
                        modelTypeName = Enum.GetName(typeof(MLModelType), _settings?.MLModelType ?? 0)
                    },
                    health = GetMLHealthStatus(),
                    performance = GetMLPerformanceMetrics(),
                    detailedReport = GetMLPerformanceReport()
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error generating ML diagnostic report");
                return new { error = ex.Message };
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_metricsLock)
            {
                // The inner MLPerformanceMetrics is a plain POCO (no IDisposable),
                // so we just clear to release references.
                _performanceMetrics.Clear();
            }

            GC.SuppressFinalize(this);
        }

        private IPatternLearningEngine TryLoadPersonalModel(Logger logger)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var possiblePaths = new[]
                {
                    System.IO.Path.Combine(baseDir, "PersonalizedMLQueryOptimizer.dll"),
                    System.IO.Path.Combine(baseDir, "PersonalMLQueryOptimizer.dll"),
                    // Compose plugin path in a cross-platform safe way
                    System.IO.Path.Combine(baseDir, "plugins", QobuzarrConstants.PluginVendor, QobuzarrConstants.PluginName, "PersonalizedMLQueryOptimizer.dll"),
                    System.IO.Path.Combine(baseDir, "ML", "PersonalizedMLQueryOptimizer.dll")
                };

                logger.Info("Attempting to load personal ML model with security validation");

                var externalModel = _secureModelLoader.TryLoadFromPaths(possiblePaths, requireSignature: false);
                if (externalModel != null)
                {
                    logger.Info("Successfully loaded and validated external personal ML model");
                    return externalModel;
                }

                logger.Debug("No valid personal ML model found after security validation");
                return null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to load personal ML model securely");
                return null;
            }
        }

        private string GetMetricsKey(string queryUrl)
        {
            if (string.IsNullOrEmpty(queryUrl)) return "unknown";

            try
            {
                var uri = new Uri(queryUrl);
                var path = uri.AbsolutePath;
                return path.Split('/').LastOrDefault() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private class MLPerformanceMetrics
        {
            public int EstimatedBaselineCalls { get; set; }
            public int ActualCalls { get; set; }
            public int CallsSaved { get; set; }
            public double OptimizationPercentage { get; set; }
        }
    }
}
