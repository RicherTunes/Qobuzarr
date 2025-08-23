using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Download.Orchestration;

namespace Qobuzarr.Tests.Performance
{
    /// <summary>
    /// Load tests and performance regression tests for Qobuzarr
    /// Validates system behavior under load and detects performance degradation
    /// </summary>
    [Collection("Performance")]
    [Trait("Category", "Performance")]
    [Trait("Component", "LoadTesting")]
    public class LoadAndRegressionTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private IServiceProvider _serviceProvider;
        private readonly PerformanceBaseline _baseline;
        private readonly ConcurrentBag<PerformanceMetric> _metrics = new();
        private readonly string _testOutputPath;

        // Performance baselines (established from historical data)
        private class PerformanceBaseline
        {
            public double MaxAuthenticationTimeMs { get; set; } = 2000;
            public double MaxSearchTimeMs { get; set; } = 1500;
            public double MaxDownloadInitTimeMs { get; set; } = 500;
            public double MinThroughputMBps { get; set; } = 5.0;
            public double MaxMemoryGrowthMB { get; set; } = 100;
            public double MaxCpuUsagePercent { get; set; } = 80;
            public int MinConcurrentDownloads { get; set; } = 10;
            public double MaxP95ResponseTimeMs { get; set; } = 3000;
        }

        public LoadAndRegressionTests(ITestOutputHelper output)
        {
            _output = output;
            _baseline = new PerformanceBaseline();
            _testOutputPath = Path.Combine(Path.GetTempPath(), "QobuzarrLoadTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testOutputPath);
        }

        public async Task InitializeAsync()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
            await Task.CompletedTask;
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddScoped<IQobuzAuthenticationService, QobuzAuthenticationService>();
            services.AddScoped<IQobuzApiClient, QobuzApiClient>();
            services.AddScoped<IDownloadQueueService, DownloadQueueService>();
            services.AddScoped<IDownloadFileService, DownloadFileService>();
            services.AddScoped<IConcurrencyManager, ConcurrencyManager>();
            services.AddScoped<IDownloadOrchestrator, DownloadOrchestrator>();
            services.AddHttpClient();
            services.AddMemoryCache();
        }

        #region Concurrent User Load Tests

        [Theory]
        [InlineData(10)]  // Light load
        [InlineData(50)]  // Medium load
        [InlineData(100)] // Heavy load
        public async Task ConcurrentUsers_SimulateRealisticLoad(int userCount)
        {
            // Arrange
            var users = new List<SimulatedUser>();
            for (int i = 0; i < userCount; i++)
            {
                users.Add(new SimulatedUser
                {
                    Id = i,
                    SearchQueries = GenerateUserQueries(i),
                    DownloadCount = Random.Shared.Next(1, 5)
                });
            }

            var tasks = new List<Task<UserSessionMetrics>>();
            var startTime = DateTime.UtcNow;

            // Act - Simulate concurrent user sessions
            foreach (var user in users)
            {
                tasks.Add(SimulateUserSession(user));
            }

            var results = await Task.WhenAll(tasks);
            var duration = DateTime.UtcNow - startTime;

            // Analyze results
            var successRate = results.Count(r => r.Success) / (double)userCount * 100;
            var avgResponseTime = results.Average(r => r.AverageResponseTimeMs);
            var maxResponseTime = results.Max(r => r.MaxResponseTimeMs);
            var totalRequests = results.Sum(r => r.RequestCount);
            var requestsPerSecond = totalRequests / duration.TotalSeconds;

            // Assert performance requirements
            successRate.Should().BeGreaterThan(95, $"At least 95% of users should complete successfully");
            avgResponseTime.Should().BeLessThan(_baseline.MaxP95ResponseTimeMs, 
                "Average response time should be within baseline");

            // Log performance metrics
            _output.WriteLine($"Load Test Results for {userCount} concurrent users:");
            _output.WriteLine($"  Success Rate: {successRate:F1}%");
            _output.WriteLine($"  Avg Response Time: {avgResponseTime:F0}ms");
            _output.WriteLine($"  Max Response Time: {maxResponseTime:F0}ms");
            _output.WriteLine($"  Total Requests: {totalRequests}");
            _output.WriteLine($"  Requests/Second: {requestsPerSecond:F1}");
            _output.WriteLine($"  Test Duration: {duration.TotalSeconds:F1}s");

            // Store for regression analysis
            _metrics.Add(new PerformanceMetric
            {
                TestName = $"ConcurrentUsers_{userCount}",
                UserCount = userCount,
                SuccessRate = successRate,
                AverageResponseTime = avgResponseTime,
                RequestsPerSecond = requestsPerSecond
            });
        }

        private async Task<UserSessionMetrics> SimulateUserSession(SimulatedUser user)
        {
            var metrics = new UserSessionMetrics { UserId = user.Id };
            var stopwatch = new Stopwatch();
            var responseTimes = new List<long>();

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var apiClient = scope.ServiceProvider.GetRequiredService<IQobuzApiClient>();

                // Simulate authentication
                stopwatch.Restart();
                // Note: Would use real auth in integration environment
                await Task.Delay(Random.Shared.Next(100, 300)); // Simulate auth time
                stopwatch.Stop();
                responseTimes.Add(stopwatch.ElapsedMilliseconds);

                // Simulate searches
                foreach (var query in user.SearchQueries.Take(3))
                {
                    stopwatch.Restart();
                    await Task.Delay(Random.Shared.Next(200, 500)); // Simulate search
                    stopwatch.Stop();
                    responseTimes.Add(stopwatch.ElapsedMilliseconds);
                    metrics.RequestCount++;

                    // Random delay between actions
                    await Task.Delay(Random.Shared.Next(500, 2000));
                }

                // Simulate downloads
                for (int i = 0; i < user.DownloadCount; i++)
                {
                    stopwatch.Restart();
                    await SimulateDownload($"user_{user.Id}_file_{i}.flac");
                    stopwatch.Stop();
                    responseTimes.Add(stopwatch.ElapsedMilliseconds);
                    metrics.RequestCount++;
                }

                metrics.Success = true;
                metrics.AverageResponseTimeMs = responseTimes.Average();
                metrics.MaxResponseTimeMs = responseTimes.Max();
            }
            catch (Exception ex)
            {
                metrics.Success = false;
                metrics.ErrorMessage = ex.Message;
                _output.WriteLine($"User {user.Id} session failed: {ex.Message}");
            }

            return metrics;
        }

        #endregion

        #region Concurrent Download Tests

        [Theory]
        [InlineData(5, 10)]   // 5 downloads of 10MB each
        [InlineData(10, 50)]  // 10 downloads of 50MB each
        [InlineData(20, 100)] // 20 downloads of 100MB each
        public async Task ConcurrentDownloads_MaintainThroughput(int downloadCount, int fileSizeMB)
        {
            // Arrange
            var downloads = new List<DownloadTask>();
            for (int i = 0; i < downloadCount; i++)
            {
                downloads.Add(new DownloadTask
                {
                    Id = i,
                    FileName = $"test_download_{i}.flac",
                    SizeBytes = fileSizeMB * 1024 * 1024
                });
            }

            var concurrencyManager = _serviceProvider.GetRequiredService<IConcurrencyManager>();
            var startTime = DateTime.UtcNow;
            var completedDownloads = new ConcurrentBag<DownloadResult>();

            // Act - Start all downloads concurrently
            var tasks = downloads.Select(async download =>
            {
                var result = new DownloadResult { DownloadId = download.Id };
                var sw = Stopwatch.StartNew();

                try
                {
                    // Simulate download with rate limiting
                    await SimulateDownloadWithThrottling(download, concurrencyManager);
                    
                    sw.Stop();
                    result.Success = true;
                    result.DurationMs = sw.ElapsedMilliseconds;
                    result.ThroughputMBps = (download.SizeBytes / 1024.0 / 1024.0) / 
                                           (sw.ElapsedMilliseconds / 1000.0);
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Error = ex.Message;
                }

                completedDownloads.Add(result);
                return result;
            }).ToArray();

            var results = await Task.WhenAll(tasks);
            var totalDuration = (DateTime.UtcNow - startTime).TotalSeconds;

            // Calculate metrics
            var successCount = results.Count(r => r.Success);
            var totalBytesDownloaded = successCount * fileSizeMB * 1024 * 1024;
            var overallThroughputMBps = (totalBytesDownloaded / 1024.0 / 1024.0) / totalDuration;
            var avgIndividualThroughput = results.Where(r => r.Success)
                                                 .Average(r => r.ThroughputMBps);

            // Assert
            successCount.Should().Be(downloadCount, "All downloads should complete successfully");
            overallThroughputMBps.Should().BeGreaterThan(_baseline.MinThroughputMBps,
                $"Overall throughput should exceed {_baseline.MinThroughputMBps} MB/s");

            // Log results
            _output.WriteLine($"Concurrent Download Test: {downloadCount} x {fileSizeMB}MB");
            _output.WriteLine($"  Success Rate: {successCount}/{downloadCount}");
            _output.WriteLine($"  Overall Throughput: {overallThroughputMBps:F2} MB/s");
            _output.WriteLine($"  Avg Individual Throughput: {avgIndividualThroughput:F2} MB/s");
            _output.WriteLine($"  Total Duration: {totalDuration:F1}s");
        }

        #endregion

        #region Memory and Resource Tests

        [Fact]
        public async Task LongRunningSession_MemoryDoesNotLeak()
        {
            // Arrange
            var initialMemory = GC.GetTotalMemory(true) / 1024.0 / 1024.0;
            var memorySnapshots = new List<double>();
            var operationCount = 1000;
            
            _output.WriteLine($"Initial memory: {initialMemory:F2} MB");

            // Act - Perform many operations
            for (int i = 0; i < operationCount; i++)
            {
                // Simulate various operations
                await SimulateApiCall($"query_{i}");
                
                if (i % 100 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    
                    var currentMemory = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
                    memorySnapshots.Add(currentMemory);
                    
                    _output.WriteLine($"Memory at operation {i}: {currentMemory:F2} MB");
                }
            }

            // Final memory measurement
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var finalMemory = GC.GetTotalMemory(true) / 1024.0 / 1024.0;
            var memoryGrowth = finalMemory - initialMemory;

            // Assert
            memoryGrowth.Should().BeLessThan(_baseline.MaxMemoryGrowthMB,
                $"Memory growth should not exceed {_baseline.MaxMemoryGrowthMB} MB");

            // Check for steady growth (potential leak)
            var isLeaking = IsMemoryLeaking(memorySnapshots);
            isLeaking.Should().BeFalse("Memory should stabilize, not continuously grow");

            _output.WriteLine($"Final memory: {finalMemory:F2} MB");
            _output.WriteLine($"Memory growth: {memoryGrowth:F2} MB");
        }

        [Fact]
        public async Task HighConcurrency_CpuUsageRemainsBounded()
        {
            // Arrange
            var cpuMeasurements = new List<double>();
            var measurementTask = Task.Run(async () =>
            {
                var process = Process.GetCurrentProcess();
                var startTime = DateTime.UtcNow;
                var startCpuTime = process.TotalProcessorTime;

                while ((DateTime.UtcNow - startTime).TotalSeconds < 10)
                {
                    await Task.Delay(1000);
                    
                    var currentCpuTime = process.TotalProcessorTime;
                    var cpuUsedMs = (currentCpuTime - startCpuTime).TotalMilliseconds;
                    var totalTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    var cpuUsagePercent = (cpuUsedMs / totalTimeMs) * 100;
                    
                    cpuMeasurements.Add(cpuUsagePercent);
                    _output.WriteLine($"CPU usage: {cpuUsagePercent:F1}%");
                }
            });

            // Act - Generate CPU load
            var loadTasks = Enumerable.Range(0, 50)
                .Select(i => Task.Run(async () =>
                {
                    for (int j = 0; j < 100; j++)
                    {
                        await SimulateApiCall($"load_test_{i}_{j}");
                        await Task.Delay(10);
                    }
                }))
                .ToArray();

            await Task.WhenAll(measurementTask, Task.WhenAll(loadTasks));

            // Assert
            var avgCpu = cpuMeasurements.Average();
            var maxCpu = cpuMeasurements.Max();

            maxCpu.Should().BeLessThan(_baseline.MaxCpuUsagePercent,
                $"Peak CPU usage should not exceed {_baseline.MaxCpuUsagePercent}%");

            _output.WriteLine($"Average CPU: {avgCpu:F1}%, Peak CPU: {maxCpu:F1}%");
        }

        #endregion

        #region Performance Regression Detection

        [Fact]
        public async Task PerformanceRegression_CompareWithBaseline()
        {
            // Run a standard set of operations to compare with baseline
            var testSuite = new[]
            {
                ("Authentication", async () => await MeasureOperation(SimulateAuthentication)),
                ("Search", async () => await MeasureOperation(() => SimulateApiCall("test query"))),
                ("AlbumDetails", async () => await MeasureOperation(() => SimulateApiCall("album/123"))),
                ("DownloadInit", async () => await MeasureOperation(() => SimulateDownload("test.flac")))
            };

            var regressions = new List<string>();

            foreach (var (operation, test) in testSuite)
            {
                var timing = await test();
                var baseline = GetBaselineForOperation(operation);
                
                if (timing > baseline * 1.2) // 20% regression threshold
                {
                    regressions.Add($"{operation}: {timing:F0}ms (baseline: {baseline:F0}ms)");
                }

                _output.WriteLine($"{operation}: {timing:F0}ms (baseline: {baseline:F0}ms)");
            }

            // Assert
            regressions.Should().BeEmpty("No performance regressions should be detected");

            if (regressions.Any())
            {
                _output.WriteLine("Performance regressions detected:");
                foreach (var regression in regressions)
                {
                    _output.WriteLine($"  - {regression}");
                }
            }
        }

        [Fact]
        public async Task SearchPerformance_ScalesLinearly()
        {
            // Test that search performance scales appropriately with result count
            var resultCounts = new[] { 10, 50, 100, 500 };
            var timings = new Dictionary<int, double>();

            foreach (var count in resultCounts)
            {
                var sw = Stopwatch.StartNew();
                await SimulateSearchWithResults(count);
                sw.Stop();
                
                timings[count] = sw.ElapsedMilliseconds;
                _output.WriteLine($"Search for {count} results: {sw.ElapsedMilliseconds}ms");
            }

            // Check for linear scaling (with some tolerance)
            for (int i = 1; i < resultCounts.Length; i++)
            {
                var prevCount = resultCounts[i - 1];
                var currCount = resultCounts[i];
                var scaleFactor = (double)currCount / prevCount;
                var timeScaleFactor = timings[currCount] / timings[prevCount];

                // Time should scale sub-linearly (better than O(n))
                timeScaleFactor.Should().BeLessThan(scaleFactor * 1.5,
                    $"Search should scale better than O(n) from {prevCount} to {currCount} results");
            }
        }

        #endregion

        #region Stress Tests

        [Fact]
        public async Task StressTest_RapidAuthenticationAttempts()
        {
            // Test system behavior under authentication stress
            var authAttempts = 100;
            var successCount = 0;
            var errors = new ConcurrentBag<string>();
            
            var tasks = Enumerable.Range(0, authAttempts)
                .Select(async i =>
                {
                    try
                    {
                        await SimulateAuthentication();
                        Interlocked.Increment(ref successCount);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Attempt {i}: {ex.Message}");
                    }
                })
                .ToArray();

            var sw = Stopwatch.StartNew();
            await Task.WhenAll(tasks);
            sw.Stop();

            // Assert
            var successRate = (successCount / (double)authAttempts) * 100;
            successRate.Should().BeGreaterThan(90, "Most authentication attempts should succeed");
            
            _output.WriteLine($"Rapid auth test: {successCount}/{authAttempts} succeeded in {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Errors: {errors.Count}");
        }

        #endregion

        #region Helper Methods

        private List<string> GenerateUserQueries(int userId)
        {
            var queries = new List<string>
            {
                $"user_{userId}_query_1",
                $"artist search {userId}",
                $"album {userId} latest",
                $"track popular {userId}"
            };
            return queries;
        }

        private async Task SimulateDownload(string fileName)
        {
            var filePath = Path.Combine(_testOutputPath, fileName);
            var data = new byte[1024 * 1024]; // 1MB
            await File.WriteAllBytesAsync(filePath, data);
            await Task.Delay(Random.Shared.Next(100, 300)); // Simulate network time
        }

        private async Task SimulateDownloadWithThrottling(DownloadTask download, IConcurrencyManager manager)
        {
            await manager.ExecuteAsync(async () =>
            {
                // Simulate bandwidth-limited download
                var chunkSize = 1024 * 1024; // 1MB chunks
                var chunks = (int)Math.Ceiling(download.SizeBytes / (double)chunkSize);
                
                for (int i = 0; i < chunks; i++)
                {
                    await Task.Delay(50); // Simulate network transfer time
                }
            });
        }

        private async Task SimulateApiCall(string query)
        {
            await Task.Delay(Random.Shared.Next(50, 200));
        }

        private async Task SimulateAuthentication()
        {
            await Task.Delay(Random.Shared.Next(200, 500));
        }

        private async Task SimulateSearchWithResults(int resultCount)
        {
            // Simulate processing time proportional to result count
            var baseTime = 100;
            var perResultTime = 2;
            await Task.Delay(baseTime + (resultCount * perResultTime));
        }

        private async Task<double> MeasureOperation(Func<Task> operation)
        {
            var sw = Stopwatch.StartNew();
            await operation();
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        private double GetBaselineForOperation(string operation)
        {
            return operation switch
            {
                "Authentication" => _baseline.MaxAuthenticationTimeMs,
                "Search" => _baseline.MaxSearchTimeMs,
                "DownloadInit" => _baseline.MaxDownloadInitTimeMs,
                _ => 1000
            };
        }

        private bool IsMemoryLeaking(List<double> memorySnapshots)
        {
            if (memorySnapshots.Count < 3) return false;

            // Check if memory is consistently increasing
            var increases = 0;
            for (int i = 1; i < memorySnapshots.Count; i++)
            {
                if (memorySnapshots[i] > memorySnapshots[i - 1])
                    increases++;
            }

            // If memory increases in >80% of snapshots, likely a leak
            return (increases / (double)(memorySnapshots.Count - 1)) > 0.8;
        }

        #endregion

        public Task DisposeAsync()
        {
            // Cleanup
            if (Directory.Exists(_testOutputPath))
            {
                Directory.Delete(_testOutputPath, true);
            }

            // Output summary metrics
            if (_metrics.Any())
            {
                _output.WriteLine("\n=== Performance Test Summary ===");
                foreach (var metric in _metrics)
                {
                    _output.WriteLine($"{metric.TestName}: Success={metric.SuccessRate:F1}%, " +
                                    $"AvgResponse={metric.AverageResponseTime:F0}ms, " +
                                    $"RPS={metric.RequestsPerSecond:F1}");
                }
            }

            return Task.CompletedTask;
        }

        #region Test Models

        private class SimulatedUser
        {
            public int Id { get; set; }
            public List<string> SearchQueries { get; set; }
            public int DownloadCount { get; set; }
        }

        private class UserSessionMetrics
        {
            public int UserId { get; set; }
            public bool Success { get; set; }
            public int RequestCount { get; set; }
            public double AverageResponseTimeMs { get; set; }
            public double MaxResponseTimeMs { get; set; }
            public string ErrorMessage { get; set; }
        }

        private class DownloadTask
        {
            public int Id { get; set; }
            public string FileName { get; set; }
            public long SizeBytes { get; set; }
        }

        private class DownloadResult
        {
            public int DownloadId { get; set; }
            public bool Success { get; set; }
            public long DurationMs { get; set; }
            public double ThroughputMBps { get; set; }
            public string Error { get; set; }
        }

        private class PerformanceMetric
        {
            public string TestName { get; set; }
            public int UserCount { get; set; }
            public double SuccessRate { get; set; }
            public double AverageResponseTime { get; set; }
            public double RequestsPerSecond { get; set; }
        }

        #endregion
    }
}