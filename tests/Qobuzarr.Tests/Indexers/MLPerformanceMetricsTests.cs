using System;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Indexers;
using NLog;
using Xunit;
using Xunit.Abstractions;

namespace Qobuzarr.Tests.Indexers
{
    public class MLPerformanceMetricsTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly Logger _logger;
        private readonly MLPerformanceMetrics _metrics;

        public MLPerformanceMetricsTests(ITestOutputHelper output)
        {
            _output = output;
            
            // Setup NLog for testing
            var config = new NLog.Config.LoggingConfiguration();
            var consoleTarget = new NLog.Targets.ConsoleTarget("console")
            {
                Layout = "${time} ${level} ${message}"
            };
            config.AddTarget(consoleTarget);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, consoleTarget);
            LogManager.Configuration = config;
            
            _logger = LogManager.GetCurrentClassLogger();
            _metrics = new MLPerformanceMetrics(_logger);
        }

        [Fact]
        public void Constructor_InitializesCorrectly()
        {
            // Act & Assert
            var summary = _metrics.GetPerformanceSummary();
            
            Assert.NotNull(summary);
            Assert.True(summary.GeneratedAt <= DateTime.UtcNow);
            Assert.Equal(0, summary.TotalPredictions);
            Assert.Equal(0.0, summary.CacheHitRatio);
            Assert.Equal(0.0, summary.ApiCallReductionPercentage);
        }

        [Fact]
        public void ModelLoadTiming_RecordsCorrectly()
        {
            // Act
            using (_metrics.StartModelLoadTiming("TestModel"))
            {
                Thread.Sleep(10); // Simulate some work
            }

            // Assert
            var summary = _metrics.GetPerformanceSummary();
            Assert.True(summary.ModelLoadMetrics.Count > 0);
            Assert.True(summary.ModelLoadMetrics.Average >= 8); // At least 8ms due to Sleep(10)
            Assert.Equal("Model Load", summary.ModelLoadMetrics.Operation);
        }

        [Fact]
        public void PredictionTiming_RecordsCorrectly()
        {
            // Act
            using (_metrics.StartPredictionTiming())
            {
                Thread.Sleep(5); // Simulate prediction work
            }
            
            _metrics.RecordPrediction(12.5, true, 0.95);

            // Assert
            var summary = _metrics.GetPerformanceSummary();
            Assert.True(summary.PredictionMetrics.Count >= 1);
            Assert.True(summary.CurrentAccuracy > 0);
            Assert.Equal(1, summary.TotalPredictions);
            Assert.Equal(1, summary.CorrectPredictions);
        }

        [Fact]
        public void CacheMetrics_TrackCorrectly()
        {
            // Act
            _metrics.RecordCacheHit();
            _metrics.RecordCacheHit();
            _metrics.RecordCacheMiss();

            // Assert
            var hitRatio = _metrics.GetCacheHitRatio();
            Assert.Equal(2.0/3.0, hitRatio, 2);

            var summary = _metrics.GetPerformanceSummary();
            Assert.Equal(2, summary.CacheHits);
            Assert.Equal(1, summary.CacheMisses);
        }

        [Fact]
        public void ApiOptimization_TracksCorrectly()
        {
            // Act
            _metrics.RecordApiOptimization(5, 10); // Saved 5 out of 10 calls
            _metrics.RecordApiOptimization(3, 6);  // Saved 3 out of 6 calls

            // Assert
            var reductionPercentage = _metrics.GetApiCallReductionPercentage();
            Assert.Equal(50.0, reductionPercentage, 1); // (5+3)/(10+6) = 8/16 = 50%

            var summary = _metrics.GetPerformanceSummary();
            Assert.Equal(8, summary.TotalApiCallsSaved);
        }

        [Fact]
        public void MemorySnapshots_RecordCorrectly()
        {
            // Act
            _metrics.RecordMemorySnapshot("TestOperation");
            
            // Force some memory allocation
            var largeArray = new byte[1024 * 1024]; // 1MB
            _metrics.RecordMemorySnapshot("AfterAllocation");

            // Assert
            var summary = _metrics.GetPerformanceSummary();
            Assert.True(summary.CurrentMemoryUsage > 0);
            Assert.True(summary.ProcessMemoryUsage > 0);
        }

        [Fact]
        public void RollingMetrics_CalculateCorrectly()
        {
            // Arrange
            for (int i = 0; i < 10; i++)
            {
                _metrics.RecordPrediction(i * 2.0, i % 2 == 0, 0.8 + (i * 0.01));
            }

            // Act
            var rollingMetrics = _metrics.GetRollingMetrics(5);

            // Assert
            Assert.Equal(5, rollingMetrics.WindowMinutes);
            Assert.True(rollingMetrics.AveragePredictionTime >= 0);
            Assert.True(rollingMetrics.RecentAccuracy >= 0);
            Assert.True(rollingMetrics.PredictionThroughput >= 0);
            Assert.True(rollingMetrics.MemoryEfficiency >= 0 && rollingMetrics.MemoryEfficiency <= 1.0);
        }

        [Fact]
        public void PerformanceHealth_AssessesCorrectly()
        {
            // Arrange - Create good performance scenario
            for (int i = 0; i < 200; i++)
            {
                _metrics.RecordPrediction(8.0, true, 0.92); // Good accuracy and timing
            }
            
            for (int i = 0; i < 100; i++)
            {
                _metrics.RecordCacheHit();
            }
            
            _metrics.RecordApiOptimization(50, 100); // 50% reduction

            // Act
            var summary = _metrics.GetPerformanceSummary();
            var health = summary.GetHealthStatus();

            // Assert
            Assert.NotNull(health);
            Assert.True(health.Score >= 80); // Should be healthy with good metrics
            Assert.True(health.IsHealthy || health.HasWarnings); // Should not be critical
            
            _output.WriteLine($"Health Status: {health.Status}, Score: {health.Score}");
            foreach (var issue in health.Issues)
            {
                _output.WriteLine($"Issue: {issue}");
            }
        }

        [Fact]
        public void BenchmarkComparison_WorksCorrectly()
        {
            // Test benchmark comparison functions
            Assert.True(PerformanceBenchmarks.MeetsTarget("accuracy", 0.88));
            Assert.False(PerformanceBenchmarks.MeetsTarget("accuracy", 0.80));
            
            Assert.True(PerformanceBenchmarks.MeetsTarget("api_reduction", 55.0));
            Assert.False(PerformanceBenchmarks.MeetsTarget("api_reduction", 40.0));
            
            Assert.True(PerformanceBenchmarks.MeetsTarget("prediction_time", 8.0));
            Assert.False(PerformanceBenchmarks.MeetsTarget("prediction_time", 60.0));
            
            // Test performance levels
            Assert.Equal("Target", PerformanceBenchmarks.GetPerformanceLevel("accuracy", 0.90));
            Assert.Equal("Warning", PerformanceBenchmarks.GetPerformanceLevel("accuracy", 0.86));
            Assert.Equal("Critical", PerformanceBenchmarks.GetPerformanceLevel("accuracy", 0.82));
            Assert.Equal("Poor", PerformanceBenchmarks.GetPerformanceLevel("accuracy", 0.75));
        }

        [Fact]
        public async Task ConcurrentOperations_HandleCorrectly()
        {
            // Arrange
            var tasks = new Task[50];

            // Act - Simulate concurrent operations
            for (int i = 0; i < tasks.Length; i++)
            {
                int index = i;
                tasks[i] = Task.Run(() =>
                {
                    _metrics.RecordPrediction(index * 0.5, index % 3 == 0, 0.8);
                    _metrics.RecordCacheHit();
                    _metrics.RecordMemorySnapshot($"ConcurrentOp-{index}");
                });
            }

            await Task.WhenAll(tasks);

            // Assert
            var summary = _metrics.GetPerformanceSummary();
            Assert.Equal(tasks.Length, summary.TotalPredictions);
            Assert.Equal(tasks.Length, summary.CacheHits);
        }

        [Fact]
        public void FormattedReport_GeneratesCorrectly()
        {
            // Arrange
            _metrics.RecordPrediction(10.0, true, 0.9);
            _metrics.RecordCacheHit();
            _metrics.RecordApiOptimization(10, 20);

            // Act
            var summary = _metrics.GetPerformanceSummary();
            var report = summary.GetFormattedReport();

            // Assert
            Assert.NotNull(report);
            Assert.Contains("ML Performance Report", report);
            Assert.Contains("ACCURACY METRICS", report);
            Assert.Contains("PERFORMANCE METRICS", report);
            Assert.Contains("CACHE PERFORMANCE", report);
            Assert.Contains("API OPTIMIZATION", report);
            Assert.Contains("MEMORY USAGE", report);
            
            _output.WriteLine("Generated Report:");
            _output.WriteLine(report);
        }

        public void Dispose()
        {
            _metrics?.Dispose();
        }
    }
}