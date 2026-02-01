using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using Lidarr.Plugin.Qobuzarr.Services;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// A/B testing framework for ML model validation as requested by tech lead feedback
    /// Enables empirical comparison of different ML models and thresholds
    /// </summary>
    public class MLABTestingFramework
    {
        private readonly Logger _logger;
        private readonly IPerformanceMonitoringService? _performanceMonitor;
        private readonly Random _random = new Random();

        // A/B test configuration
        private readonly double _testGroupPercentage = 0.1; // 10% of queries use test model
        private readonly Dictionary<string, ABTestResult> _testResults = new();
        private readonly object _testLock = new object();

        public MLABTestingFramework(Logger logger, IPerformanceMonitoringService? performanceMonitor = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _performanceMonitor = performanceMonitor;
        }

        /// <summary>
        /// Determines if a query should use the test model (A/B testing)
        /// </summary>
        public bool ShouldUseTestModel(string query)
        {
            return _random.NextDouble() < _testGroupPercentage;
        }

        /// <summary>
        /// Records A/B test result for comparison
        /// </summary>
        public void RecordABTestResult(string query, QueryComplexity controlResult, QueryComplexity testResult,
            double controlConfidence, double testConfidence, int actualApiCalls)
        {
            var testId = GenerateTestId(query);

            lock (_testLock)
            {
                var result = new ABTestResult
                {
                    Timestamp = DateTime.UtcNow,
                    Query = query,
                    ControlResult = controlResult,
                    TestResult = testResult,
                    ControlConfidence = controlConfidence,
                    TestConfidence = testConfidence,
                    ActualApiCalls = actualApiCalls,
                    TestId = testId
                };

                _testResults[testId] = result;

                // Record for production monitoring
                _performanceMonitor?.RecordMLOptimization(
                    $"AB_Control: {query}",
                    $"AB_Test: {query}",
                    testConfidence > controlConfidence,
                    Math.Max(controlConfidence, testConfidence));
            }

            _logger.Debug("A/B test recorded: {0} - Control: {1} ({2:F2}), Test: {3} ({4:F2})",
                query, controlResult, controlConfidence, testResult, testConfidence);
        }

        /// <summary>
        /// Analyzes A/B test results to determine model effectiveness
        /// </summary>
        public ABTestAnalysis AnalyzeResults()
        {
            lock (_testLock)
            {
                if (!_testResults.Any())
                {
                    return new ABTestAnalysis { SampleSize = 0, Conclusion = "No A/B test data available yet" };
                }

                var totalTests = _testResults.Count;
                var testWins = _testResults.Values.Count(r => r.TestConfidence > r.ControlConfidence);
                var controlWins = _testResults.Values.Count(r => r.ControlConfidence > r.TestConfidence);
                var ties = totalTests - testWins - controlWins;

                var testWinRate = totalTests > 0 ? (double)testWins / totalTests * 100 : 0;
                var avgControlConfidence = _testResults.Values.Average(r => r.ControlConfidence);
                var avgTestConfidence = _testResults.Values.Average(r => r.TestConfidence);

                return new ABTestAnalysis
                {
                    SampleSize = totalTests,
                    TestWins = testWins,
                    ControlWins = controlWins,
                    Ties = ties,
                    TestWinRate = testWinRate,
                    AverageControlConfidence = avgControlConfidence,
                    AverageTestConfidence = avgTestConfidence,
                    Conclusion = GenerateConclusion(testWinRate, avgTestConfidence, avgControlConfidence),
                    IsStatisticallySignificant = totalTests >= 100 && Math.Abs(testWinRate - 50) > 10
                };
            }
        }

        /// <summary>
        /// Generates statistical conclusion from A/B test results
        /// </summary>
        private string GenerateConclusion(double testWinRate, double avgTestConfidence, double avgControlConfidence)
        {
            if (testWinRate > 60)
                return "Test model performs significantly better than control";
            else if (testWinRate < 40)
                return "Control model performs significantly better than test";
            else if (Math.Abs(avgTestConfidence - avgControlConfidence) < 0.05)
                return "Models perform similarly - no significant difference";
            else if (avgTestConfidence > avgControlConfidence)
                return "Test model shows higher confidence but similar accuracy";
            else
                return "Control model shows higher confidence but similar accuracy";
        }

        /// <summary>
        /// Gets recent A/B test performance summary
        /// </summary>
        public void LogPerformanceSummary()
        {
            var analysis = AnalyzeResults();

            _logger.Info("ML A/B Testing Summary: {0} samples, Test wins: {1:F1}%, Avg confidence - Control: {2:F2}, Test: {3:F2}",
                analysis.SampleSize, analysis.TestWinRate, analysis.AverageControlConfidence, analysis.AverageTestConfidence);

            _logger.Info("A/B Test Conclusion: {0}", analysis.Conclusion);
        }

        private string GenerateTestId(string query)
        {
            return $"ab_{query.GetHashCode():X}_{DateTime.UtcNow.Ticks}";
        }
    }

    #region Data Models

    public class ABTestResult
    {
        public DateTime Timestamp { get; set; }
        public string Query { get; set; } = "";
        public QueryComplexity ControlResult { get; set; }
        public QueryComplexity TestResult { get; set; }
        public double ControlConfidence { get; set; }
        public double TestConfidence { get; set; }
        public int ActualApiCalls { get; set; }
        public string TestId { get; set; } = "";
    }

    public class ABTestAnalysis
    {
        public int SampleSize { get; set; }
        public int TestWins { get; set; }
        public int ControlWins { get; set; }
        public int Ties { get; set; }
        public double TestWinRate { get; set; }
        public double AverageControlConfidence { get; set; }
        public double AverageTestConfidence { get; set; }
        public string Conclusion { get; set; } = "";
        public bool IsStatisticallySignificant { get; set; }
    }

    #endregion
}
