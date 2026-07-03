using System;
using System.Threading;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Common.TestKit.Helpers;
using NLog;
using Xunit;

namespace Qobuzarr.Tests.Indexers
{
    /// <summary>
    /// Verifies the periodic-aggregation log guard introduced to prevent "ML Performance Summary"
    /// spam (121+ entries / 6 minutes) on idle Lidarr Docker instances where all metrics are zero.
    /// </summary>
    public class MLPerformanceMetricsLogGateTests : IDisposable
    {
        private readonly Logger _logger;
        private readonly MLPerformanceMetrics _metrics;

        public MLPerformanceMetricsLogGateTests()
        {
            // Isolated logger — never mutate the process-global LogManager.Configuration.
            // Assigning/nulling it here raced parallel log-capture tests (e.g.
            // QobuzAppSecretLogScrubTests) that read the shared "testMemory" target,
            // deterministically wiping their capture. This test asserts only on
            // GetPerformanceSummary() values, never on captured log output.
            _logger = NLogTestLogger.CreateNullLogger();
            _metrics = new MLPerformanceMetrics(_logger);
        }

        [Fact]
        public void GetPerformanceSummary_WithZeroData_HasZeroMetrics()
        {
            // Just a sanity-check that a fresh instance really has all-zero values.
            var summary = _metrics.GetPerformanceSummary();

            summary.PredictionMetrics.Average.Should().Be(0.0);
            summary.CurrentAccuracy.Should().Be(0.0);
            summary.CacheHitRatio.Should().Be(0.0);
            summary.ApiCallReductionPercentage.Should().Be(0.0);
        }

        [Fact]
        public void GetPerformanceSummary_AfterRecordingPredictions_HasNonZeroMetrics()
        {
            // Arrange
            _metrics.RecordPrediction(5.0, true, 0.9);

            // Act
            var summary = _metrics.GetPerformanceSummary();

            // Assert — at least one metric is non-zero so the guard allows logging.
            var hasData = summary.PredictionMetrics.Average > 0
                || summary.CurrentAccuracy > 0
                || summary.CacheHitRatio > 0
                || summary.ApiCallReductionPercentage > 0;

            hasData.Should().BeTrue("after recording a prediction the metrics must be non-zero");
        }

        [Fact]
        public void GetPerformanceSummary_AfterCacheHit_HasNonZeroCacheRatio()
        {
            // Arrange
            _metrics.RecordCacheHit();

            // Act
            var summary = _metrics.GetPerformanceSummary();

            // Assert
            summary.CacheHitRatio.Should().BeGreaterThan(0.0);
        }

        [Fact]
        public void GetPerformanceSummary_AfterApiOptimization_HasNonZeroReduction()
        {
            // Arrange
            _metrics.RecordApiOptimization(3, 6);

            // Act
            var summary = _metrics.GetPerformanceSummary();

            // Assert
            summary.ApiCallReductionPercentage.Should().BeGreaterThan(0.0);
        }

        /// <summary>
        /// Verifies that the log guard condition is consistent with what the
        /// PerformAggregation callback checks — so we can trust that a fresh
        /// idle instance suppresses the "ML Performance Summary" log line.
        /// </summary>
        [Fact]
        public void LogGuard_ZeroSummary_EvaluatesToFalse()
        {
            var summary = _metrics.GetPerformanceSummary();

            // This mirrors the guard in PerformAggregation:
            bool wouldLog = summary.PredictionMetrics.Average > 0
                || summary.CurrentAccuracy > 0
                || summary.CacheHitRatio > 0
                || summary.ApiCallReductionPercentage > 0;

            wouldLog.Should().BeFalse(
                "an idle instance with all-zero metrics must not emit the periodic summary log");
        }

        [Fact]
        public void LogGuard_NonZeroSummary_EvaluatesToTrue()
        {
            _metrics.RecordPrediction(10.0, true, 0.8);
            var summary = _metrics.GetPerformanceSummary();

            bool wouldLog = summary.PredictionMetrics.Average > 0
                || summary.CurrentAccuracy > 0
                || summary.CacheHitRatio > 0
                || summary.ApiCallReductionPercentage > 0;

            wouldLog.Should().BeTrue(
                "after recording a prediction the guard must allow the summary to be logged");
        }

        public void Dispose()
        {
            _metrics?.Dispose();
        }
    }
}
