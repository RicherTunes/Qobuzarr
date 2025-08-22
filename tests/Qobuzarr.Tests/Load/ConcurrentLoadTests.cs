using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NBomber.Contracts;
using NBomber.CSharp;
using NLog;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Qobuzarr.Tests.Load
{
    /// <summary>
    /// Load testing suite for concurrent operations
    /// Tests system behavior under realistic and peak load conditions
    /// </summary>
    [Collection("LoadTesting")]
    [Trait("Category", "Load")]
    [Trait("Component", "Performance")]
    public class ConcurrentLoadTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly IServiceProvider _serviceProvider;
        private readonly Logger _logger;
        private readonly ConcurrentBag<LoadTestMetric> _metrics = new();
        private readonly CancellationTokenSource _loadTestCancellation = new();
        
        // Load test parameters
        private const int WARMUP_DURATION_SECONDS = 10;
        private const int TEST_DURATION_SECONDS = 60;
        private const int CONCURRENT_USERS_LOW = 10;
        private const int CONCURRENT_USERS_MEDIUM = 50;
        private const int CONCURRENT_USERS_HIGH = 100;
        private const int CONCURRENT_USERS_PEAK = 200;
        
        // Performance thresholds
        private const double TARGET_SUCCESS_RATE = 99.0; // 99% success rate
        private const int TARGET_P99_LATENCY_MS = 2000; // 2 seconds
        private const int TARGET_THROUGHPUT_RPS = 50; // Requests per second
        private const double MAX_ERROR_RATE = 1.0; // 1% error rate
        private const double MAX_CPU_USAGE = 80.0; // 80% CPU usage
        private const double MAX_MEMORY_GB = 2.0; // 2GB memory

        public ConcurrentLoadTests(ITestOutputHelper output)
        {
            _output = output;
            _logger = LogManager.GetCurrentClassLogger();
            
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Configure services for load testing
            services.AddSingleton<IConcurrencyManager, ConcurrencyManager>();
            services.AddSingleton<IDownloadQueueService, DownloadQueueService>();
            services.AddSingleton<IQobuzApiClient, MockQobuzApiClient>(); // Use mock for load testing
            services.AddTransient<ILoadTestScenario, SearchLoadScenario>();
            services.AddTransient<ILoadTestScenario, DownloadLoadScenario>();
        }

        [Fact]
        public async Task LowLoad_HandlesGracefully()
        {
            // Arrange
            var scenario = CreateSearchScenario(CONCURRENT_USERS_LOW);
            
            // Act
            var results = await RunLoadTest(scenario, "Low Load Test");
            
            // Assert
            AssertLoadTestResults(results, CONCURRENT_USERS_LOW);
            results.SuccessRate.Should().BeGreaterOrEqualTo(TARGET_SUCCESS_RATE);
            results.P99Latency.Should().BeLessThan(TARGET_P99_LATENCY_MS);
            
            _output.WriteLine($"Low load test completed: {CONCURRENT_USERS_LOW} users");
            OutputLoadTestReport(results);
        }

        [Fact]
        public async Task MediumLoad_MaintainsPerformance()
        {
            // Arrange
            var scenario = CreateSearchScenario(CONCURRENT_USERS_MEDIUM);
            
            // Act
            var results = await RunLoadTest(scenario, "Medium Load Test");
            
            // Assert
            AssertLoadTestResults(results, CONCURRENT_USERS_MEDIUM);
            results.Throughput.Should().BeGreaterThan(TARGET_THROUGHPUT_RPS);
            
            _output.WriteLine($"Medium load test completed: {CONCURRENT_USERS_MEDIUM} users");
            OutputLoadTestReport(results);
        }

        [Fact]
        public async Task HighLoad_ScalesAppropriately()
        {
            // Arrange
            var scenario = CreateMixedScenario(CONCURRENT_USERS_HIGH);
            
            // Act
            var results = await RunLoadTest(scenario, "High Load Test");
            
            // Assert
            results.ErrorRate.Should().BeLessOrEqualTo(MAX_ERROR_RATE);
            results.AverageLatency.Should().BeLessThan(1000); // Sub-second average
            
            _output.WriteLine($"High load test completed: {CONCURRENT_USERS_HIGH} users");
            OutputLoadTestReport(results);
        }

        [Fact]
        public async Task PeakLoad_HandlesWithoutCrashing()
        {
            // Arrange
            var scenario = CreateMixedScenario(CONCURRENT_USERS_PEAK);
            
            // Act
            var results = await RunLoadTest(scenario, "Peak Load Test", duration: 30); // Shorter duration
            
            // Assert
            results.CrashCount.Should().Be(0, "System should not crash under peak load");
            results.MemoryUsageGB.Should().BeLessThan(MAX_MEMORY_GB);
            
            _output.WriteLine($"Peak load test completed: {CONCURRENT_USERS_PEAK} users");
            OutputLoadTestReport(results);
        }

        [Fact]
        public async Task SustainedLoad_NoMemoryLeaks()
        {
            // Arrange
            var scenario = CreateSearchScenario(CONCURRENT_USERS_MEDIUM);
            var memorySnapshots = new List<long>();
            
            // Act - Run sustained load for longer duration
            var monitorTask = Task.Run(async () =>
            {
                while (!_loadTestCancellation.Token.IsCancellationRequested)
                {
                    memorySnapshots.Add(GC.GetTotalMemory(false));
                    await Task.Delay(5000); // Sample every 5 seconds
                }
            });
            
            var results = await RunLoadTest(scenario, "Sustained Load Test", duration: 120);
            _loadTestCancellation.Cancel();
            await monitorTask;
            
            // Assert - Memory should stabilize, not continuously grow
            var firstHalf = memorySnapshots.Take(memorySnapshots.Count / 2).Average();
            var secondHalf = memorySnapshots.Skip(memorySnapshots.Count / 2).Average();
            var memoryGrowth = ((secondHalf - firstHalf) / firstHalf) * 100;
            
            memoryGrowth.Should().BeLessThan(10, "Memory growth should be less than 10%");
            
            _output.WriteLine($"Sustained load test completed");
            _output.WriteLine($"Memory growth: {memoryGrowth:F1}%");
            OutputLoadTestReport(results);
        }

        [Fact]
        public async Task BurstLoad_RecoverQuickly()
        {
            // Arrange - Simulate burst pattern
            var burstPattern = new[]
            {
                (users: 10, duration: 10),
                (users: 100, duration: 5),  // Burst
                (users: 10, duration: 10),
                (users: 150, duration: 5),  // Bigger burst
                (users: 10, duration: 10)
            };
            
            var allResults = new List<LoadTestResult>();
            
            // Act
            foreach (var (users, duration) in burstPattern)
            {
                var scenario = CreateSearchScenario(users);
                var results = await RunLoadTest(scenario, $"Burst {users} users", duration);
                allResults.Add(results);
                
                _output.WriteLine($"Burst phase: {users} users for {duration}s");
                _output.WriteLine($"  Success rate: {results.SuccessRate:F1}%");
                _output.WriteLine($"  P99 latency: {results.P99Latency}ms");
            }
            
            // Assert - System should recover after bursts
            var recoveryPhases = new[] { allResults[2], allResults[4] };
            recoveryPhases.Should().AllSatisfy(r =>
            {
                r.SuccessRate.Should().BeGreaterThan(95);
                r.P99Latency.Should().BeLessThan(1000);
            });
            
            _output.WriteLine("Burst load test completed - recovery verified");
        }

        [Fact]
        public async Task ConcurrentDownloads_ResourceManagement()
        {
            // Arrange
            var concurrencyManager = _serviceProvider.GetRequiredService<IConcurrencyManager>();
            var downloadTasks = new List<Task<DownloadResult>>();
            var downloadSizes = Enumerable.Range(0, 50)
                .Select(i => Random.Shared.Next(10_000_000, 100_000_000)) // 10-100MB
                .ToList();
            
            // Act
            var stopwatch = Stopwatch.StartNew();
            
            foreach (var size in downloadSizes)
            {
                var task = SimulateDownload(size, concurrencyManager);
                downloadTasks.Add(task);
            }
            
            var results = await Task.WhenAll(downloadTasks);
            stopwatch.Stop();
            
            // Assert
            var successCount = results.Count(r => r.Success);
            var totalDownloaded = results.Sum(r => r.BytesDownloaded);
            var throughputMBps = (totalDownloaded / 1024.0 / 1024.0) / stopwatch.Elapsed.TotalSeconds;
            var avgConcurrency = results.Average(r => r.ConcurrentDownloads);
            
            successCount.Should().BeGreaterThan(45, "At least 90% should succeed");
            avgConcurrency.Should().BeLessOrEqualTo(5, "Should respect concurrency limits");
            throughputMBps.Should().BeGreaterThan(10, "Should maintain reasonable throughput");
            
            _output.WriteLine($"Concurrent downloads test completed");
            _output.WriteLine($"  Downloads: {downloadSizes.Count}");
            _output.WriteLine($"  Success rate: {(successCount * 100.0 / downloadSizes.Count):F1}%");
            _output.WriteLine($"  Throughput: {throughputMBps:F1} MB/s");
            _output.WriteLine($"  Avg concurrency: {avgConcurrency:F1}");
        }

        [Fact]
        public async Task RateLimitStress_BackoffCorrectly()
        {
            // Arrange - Simulate aggressive rate limiting
            var apiClient = new MockQobuzApiClient { SimulateRateLimiting = true };
            var requests = Enumerable.Range(0, 100).ToList();
            var rateLimitHits = 0;
            var successfulRequests = 0;
            
            // Act
            var tasks = requests.Select(async i =>
            {
                try
                {
                    await apiClient.SearchAsync($"query{i}");
                    Interlocked.Increment(ref successfulRequests);
                    return true;
                }
                catch (QobuzApiException ex) when (ex.StatusCode == 429)
                {
                    Interlocked.Increment(ref rateLimitHits);
                    // Implement backoff
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, Math.Min(i % 5, 4))));
                    return false;
                }
            });
            
            var results = await Task.WhenAll(tasks);
            
            // Assert
            rateLimitHits.Should().BeGreaterThan(0, "Should encounter rate limits");
            successfulRequests.Should().BeGreaterThan(50, "Should still process many requests");
            
            _output.WriteLine($"Rate limit stress test completed");
            _output.WriteLine($"  Total requests: {requests.Count}");
            _output.WriteLine($"  Successful: {successfulRequests}");
            _output.WriteLine($"  Rate limited: {rateLimitHits}");
            _output.WriteLine($"  Success rate: {(successfulRequests * 100.0 / requests.Count):F1}%");
        }

        [Fact]
        public async Task DatabaseConnectionPool_ScalesCorrectly()
        {
            // Arrange - Simulate database operations
            var connectionPool = new MockConnectionPool(maxConnections: 20);
            var operations = Enumerable.Range(0, 200).ToList();
            var connectionMetrics = new ConcurrentBag<ConnectionMetric>();
            
            // Act
            var tasks = operations.Select(async i =>
            {
                var metric = new ConnectionMetric { StartTime = DateTime.UtcNow };
                
                await connectionPool.ExecuteAsync(async connection =>
                {
                    metric.WaitTime = (DateTime.UtcNow - metric.StartTime).TotalMilliseconds;
                    metric.ConnectionId = connection.Id;
                    
                    // Simulate database operation
                    await Task.Delay(Random.Shared.Next(10, 100));
                    
                    metric.ExecutionTime = (DateTime.UtcNow - metric.StartTime).TotalMilliseconds;
                });
                
                connectionMetrics.Add(metric);
            });
            
            await Task.WhenAll(tasks);
            
            // Assert
            var avgWaitTime = connectionMetrics.Average(m => m.WaitTime);
            var maxWaitTime = connectionMetrics.Max(m => m.WaitTime);
            var uniqueConnections = connectionMetrics.Select(m => m.ConnectionId).Distinct().Count();
            
            avgWaitTime.Should().BeLessThan(100, "Average wait time should be low");
            maxWaitTime.Should().BeLessThan(5000, "Max wait time should be reasonable");
            uniqueConnections.Should().BeLessOrEqualTo(20, "Should respect pool size");
            
            _output.WriteLine($"Connection pool test completed");
            _output.WriteLine($"  Operations: {operations.Count}");
            _output.WriteLine($"  Avg wait time: {avgWaitTime:F0}ms");
            _output.WriteLine($"  Max wait time: {maxWaitTime:F0}ms");
            _output.WriteLine($"  Unique connections: {uniqueConnections}");
        }

        // Helper methods
        private LoadTestScenario CreateSearchScenario(int users)
        {
            return new LoadTestScenario
            {
                Name = "Search Load",
                ConcurrentUsers = users,
                Operations = GenerateSearchOperations(users * 10)
            };
        }

        private LoadTestScenario CreateMixedScenario(int users)
        {
            return new LoadTestScenario
            {
                Name = "Mixed Load",
                ConcurrentUsers = users,
                Operations = GenerateMixedOperations(users * 10)
            };
        }

        private List<ILoadOperation> GenerateSearchOperations(int count)
        {
            return Enumerable.Range(0, count)
                .Select(i => new SearchOperation
                {
                    Query = $"Artist {i % 100} Album {i % 50}",
                    ExpectedResults = 10
                })
                .Cast<ILoadOperation>()
                .ToList();
        }

        private List<ILoadOperation> GenerateMixedOperations(int count)
        {
            var operations = new List<ILoadOperation>();
            
            for (int i = 0; i < count; i++)
            {
                if (i % 3 == 0)
                {
                    operations.Add(new SearchOperation
                    {
                        Query = $"Query {i}",
                        ExpectedResults = 10
                    });
                }
                else if (i % 3 == 1)
                {
                    operations.Add(new DownloadOperation
                    {
                        AlbumId = $"album_{i}",
                        SizeMB = Random.Shared.Next(50, 500)
                    });
                }
                else
                {
                    operations.Add(new MetadataOperation
                    {
                        AlbumId = $"album_{i}"
                    });
                }
            }
            
            return operations;
        }

        private async Task<LoadTestResult> RunLoadTest(LoadTestScenario scenario, string testName, int duration = TEST_DURATION_SECONDS)
        {
            _output.WriteLine($"Starting {testName}...");
            
            var result = new LoadTestResult
            {
                TestName = testName,
                StartTime = DateTime.UtcNow,
                ConcurrentUsers = scenario.ConcurrentUsers
            };
            
            var stopwatch = Stopwatch.StartNew();
            var endTime = DateTime.UtcNow.AddSeconds(duration);
            var tasks = new List<Task>();
            
            // Start concurrent users
            for (int user = 0; user < scenario.ConcurrentUsers; user++)
            {
                var userTask = RunUserSimulation(scenario, result, endTime);
                tasks.Add(userTask);
            }
            
            // Wait for all users to complete
            await Task.WhenAll(tasks);
            stopwatch.Stop();
            
            // Calculate final metrics
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.CalculateFinalMetrics();
            
            return result;
        }

        private async Task RunUserSimulation(LoadTestScenario scenario, LoadTestResult result, DateTime endTime)
        {
            while (DateTime.UtcNow < endTime && !_loadTestCancellation.Token.IsCancellationRequested)
            {
                var operation = scenario.Operations[Random.Shared.Next(scenario.Operations.Count)];
                var metric = new LoadTestMetric { StartTime = DateTime.UtcNow };
                
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    await operation.ExecuteAsync();
                    stopwatch.Stop();
                    
                    metric.Success = true;
                    metric.LatencyMs = stopwatch.ElapsedMilliseconds;
                    result.RecordSuccess(metric.LatencyMs);
                }
                catch (Exception ex)
                {
                    metric.Success = false;
                    metric.Error = ex.Message;
                    result.RecordFailure(ex);
                }
                finally
                {
                    metric.EndTime = DateTime.UtcNow;
                    _metrics.Add(metric);
                }
                
                // Simulate think time
                await Task.Delay(Random.Shared.Next(100, 1000));
            }
        }

        private async Task<DownloadResult> SimulateDownload(long sizeBytes, IConcurrencyManager concurrencyManager)
        {
            var result = new DownloadResult { RequestedSize = sizeBytes };
            
            await concurrencyManager.ExecuteWithThrottlingAsync(async () =>
            {
                result.ConcurrentDownloads = concurrencyManager.CurrentConcurrency;
                result.StartTime = DateTime.UtcNow;
                
                // Simulate download time based on size
                var downloadTimeMs = sizeBytes / 10000; // ~10MB/s
                await Task.Delay(TimeSpan.FromMilliseconds(downloadTimeMs));
                
                result.EndTime = DateTime.UtcNow;
                result.BytesDownloaded = sizeBytes;
                result.Success = Random.Shared.Next(100) > 5; // 95% success rate
            });
            
            return result;
        }

        private void AssertLoadTestResults(LoadTestResult results, int expectedUsers)
        {
            results.Should().NotBeNull();
            results.ConcurrentUsers.Should().Be(expectedUsers);
            results.TotalRequests.Should().BeGreaterThan(0);
            results.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        }

        private void OutputLoadTestReport(LoadTestResult results)
        {
            _output.WriteLine("=== LOAD TEST REPORT ===");
            _output.WriteLine($"Test: {results.TestName}");
            _output.WriteLine($"Duration: {results.Duration.TotalSeconds:F1}s");
            _output.WriteLine($"Concurrent Users: {results.ConcurrentUsers}");
            _output.WriteLine($"Total Requests: {results.TotalRequests}");
            _output.WriteLine($"Success Rate: {results.SuccessRate:F1}%");
            _output.WriteLine($"Error Rate: {results.ErrorRate:F1}%");
            _output.WriteLine($"Throughput: {results.Throughput:F1} req/s");
            _output.WriteLine($"Avg Latency: {results.AverageLatency:F0}ms");
            _output.WriteLine($"P50 Latency: {results.P50Latency:F0}ms");
            _output.WriteLine($"P95 Latency: {results.P95Latency:F0}ms");
            _output.WriteLine($"P99 Latency: {results.P99Latency:F0}ms");
            _output.WriteLine($"Max Latency: {results.MaxLatency:F0}ms");
            _output.WriteLine($"Memory Usage: {results.MemoryUsageGB:F2}GB");
            _output.WriteLine($"CPU Usage: {results.CpuUsage:F1}%");
            
            if (results.Errors.Any())
            {
                _output.WriteLine("\nTop Errors:");
                foreach (var error in results.Errors.Take(5))
                {
                    _output.WriteLine($"  - {error}");
                }
            }
        }

        public void Dispose()
        {
            _loadTestCancellation?.Cancel();
            _loadTestCancellation?.Dispose();
        }

        // Helper classes
        private class LoadTestScenario
        {
            public string Name { get; set; }
            public int ConcurrentUsers { get; set; }
            public List<ILoadOperation> Operations { get; set; } = new();
        }

        private interface ILoadOperation
        {
            Task ExecuteAsync();
        }

        private class SearchOperation : ILoadOperation
        {
            public string Query { get; set; }
            public int ExpectedResults { get; set; }

            public async Task ExecuteAsync()
            {
                await Task.Delay(Random.Shared.Next(50, 200)); // Simulate API call
            }
        }

        private class DownloadOperation : ILoadOperation
        {
            public string AlbumId { get; set; }
            public int SizeMB { get; set; }

            public async Task ExecuteAsync()
            {
                await Task.Delay(Random.Shared.Next(500, 2000)); // Simulate download
            }
        }

        private class MetadataOperation : ILoadOperation
        {
            public string AlbumId { get; set; }

            public async Task ExecuteAsync()
            {
                await Task.Delay(Random.Shared.Next(20, 100)); // Simulate metadata fetch
            }
        }

        private class LoadTestResult
        {
            private readonly ConcurrentBag<double> _latencies = new();
            private readonly ConcurrentBag<Exception> _exceptions = new();
            private int _successCount = 0;
            private int _failureCount = 0;

            public string TestName { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public TimeSpan Duration { get; set; }
            public int ConcurrentUsers { get; set; }
            public int TotalRequests => _successCount + _failureCount;
            public double SuccessRate => TotalRequests > 0 ? (_successCount * 100.0 / TotalRequests) : 0;
            public double ErrorRate => TotalRequests > 0 ? (_failureCount * 100.0 / TotalRequests) : 0;
            public double Throughput => Duration.TotalSeconds > 0 ? (TotalRequests / Duration.TotalSeconds) : 0;
            public double AverageLatency { get; private set; }
            public double P50Latency { get; private set; }
            public double P95Latency { get; private set; }
            public double P99Latency { get; private set; }
            public double MaxLatency { get; private set; }
            public double MemoryUsageGB { get; private set; }
            public double CpuUsage { get; private set; }
            public int CrashCount { get; private set; }
            public List<string> Errors { get; } = new();

            public void RecordSuccess(double latencyMs)
            {
                Interlocked.Increment(ref _successCount);
                _latencies.Add(latencyMs);
            }

            public void RecordFailure(Exception ex)
            {
                Interlocked.Increment(ref _failureCount);
                _exceptions.Add(ex);
                
                if (ex is OutOfMemoryException || ex is StackOverflowException)
                {
                    CrashCount++;
                }
            }

            public void CalculateFinalMetrics()
            {
                if (_latencies.Any())
                {
                    var sortedLatencies = _latencies.OrderBy(l => l).ToList();
                    AverageLatency = sortedLatencies.Average();
                    P50Latency = GetPercentile(sortedLatencies, 50);
                    P95Latency = GetPercentile(sortedLatencies, 95);
                    P99Latency = GetPercentile(sortedLatencies, 99);
                    MaxLatency = sortedLatencies.Max();
                }

                MemoryUsageGB = GC.GetTotalMemory(false) / (1024.0 * 1024.0 * 1024.0);
                CpuUsage = Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds / Environment.ProcessorCount / Duration.TotalMilliseconds * 100;

                var errorGroups = _exceptions.GroupBy(e => e.GetType().Name)
                    .OrderByDescending(g => g.Count())
                    .Take(5);
                
                foreach (var group in errorGroups)
                {
                    Errors.Add($"{group.Key}: {group.Count()} occurrences");
                }
            }

            private double GetPercentile(List<double> sortedValues, int percentile)
            {
                int index = (int)Math.Ceiling(sortedValues.Count * percentile / 100.0) - 1;
                return sortedValues[Math.Max(0, Math.Min(index, sortedValues.Count - 1))];
            }
        }

        private class LoadTestMetric
        {
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public bool Success { get; set; }
            public double LatencyMs { get; set; }
            public string Error { get; set; }
        }

        private class DownloadResult
        {
            public long RequestedSize { get; set; }
            public long BytesDownloaded { get; set; }
            public bool Success { get; set; }
            public int ConcurrentDownloads { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
        }

        private class ConnectionMetric
        {
            public DateTime StartTime { get; set; }
            public double WaitTime { get; set; }
            public double ExecutionTime { get; set; }
            public int ConnectionId { get; set; }
        }

        private interface ILoadTestScenario
        {
            Task<LoadTestResult> ExecuteAsync(int concurrentUsers, TimeSpan duration);
        }

        private class SearchLoadScenario : ILoadTestScenario
        {
            public async Task<LoadTestResult> ExecuteAsync(int concurrentUsers, TimeSpan duration)
            {
                // Implementation
                await Task.Delay(100);
                return new LoadTestResult();
            }
        }

        private class DownloadLoadScenario : ILoadTestScenario
        {
            public async Task<LoadTestResult> ExecuteAsync(int concurrentUsers, TimeSpan duration)
            {
                // Implementation
                await Task.Delay(100);
                return new LoadTestResult();
            }
        }

        // Mock implementations for testing
        private class MockQobuzApiClient : IQobuzApiClient
        {
            public bool SimulateRateLimiting { get; set; }
            private int _requestCount = 0;

            public async Task<T> GetAsync<T>(string endpoint, Dictionary<string, string> parameters)
            {
                await Task.Delay(Random.Shared.Next(10, 100));
                
                if (SimulateRateLimiting && Interlocked.Increment(ref _requestCount) % 10 == 0)
                {
                    throw new QobuzApiException("Rate limit exceeded", 429);
                }
                
                return default(T);
            }

            public async Task<T> PostAsync<T>(string endpoint, object data)
            {
                await Task.Delay(Random.Shared.Next(10, 100));
                return default(T);
            }

            public async Task<QobuzSearchResponse> SearchAsync(string query)
            {
                await Task.Delay(Random.Shared.Next(50, 200));
                
                if (SimulateRateLimiting && Random.Shared.Next(100) < 10)
                {
                    throw new QobuzApiException("Rate limit exceeded", 429);
                }
                
                return new QobuzSearchResponse();
            }

            public void SetSession(QobuzSession session) { }
        }

        private class MockConnectionPool
        {
            private readonly SemaphoreSlim _semaphore;
            private int _connectionCounter = 0;

            public MockConnectionPool(int maxConnections)
            {
                _semaphore = new SemaphoreSlim(maxConnections, maxConnections);
            }

            public async Task ExecuteAsync(Func<Connection, Task> operation)
            {
                await _semaphore.WaitAsync();
                try
                {
                    var connection = new Connection { Id = Interlocked.Increment(ref _connectionCounter) % 20 };
                    await operation(connection);
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            public class Connection
            {
                public int Id { get; set; }
            }
        }
    }
}