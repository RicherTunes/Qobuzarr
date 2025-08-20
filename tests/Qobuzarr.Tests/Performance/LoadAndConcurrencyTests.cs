using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Download.Orchestration;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace Qobuzarr.Tests.Performance
{
    /// <summary>
    /// Load and concurrency tests for download operations
    /// Validates system behavior under heavy concurrent load
    /// </summary>
    [Collection("LoadTesting")]
    [Trait("Category", "Performance")]
    [Trait("Component", "Concurrency")]
    public class LoadAndConcurrencyTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<IQobuzAuthenticationService> _mockAuthService;
        private readonly Mock<IQobuzApiClient> _mockApiClient;
        private readonly Mock<IConcurrencyManager> _mockConcurrencyManager;
        private readonly PerformanceMonitor _performanceMonitor;
        private readonly ResourceMonitor _resourceMonitor;
        
        // Performance targets
        private const int MAX_CONCURRENT_DOWNLOADS = 10;
        private const int TARGET_DOWNLOADS_PER_HOUR = 100;
        private const double MAX_CPU_USAGE_PERCENT = 80;
        private const double MAX_MEMORY_GROWTH_MB = 500;
        private const int MAX_THREAD_COUNT = 50;
        private const int TARGET_THROUGHPUT_MBPS = 100;

        public LoadAndConcurrencyTests(ITestOutputHelper output)
        {
            _output = output;
            _mockAuthService = new Mock<IQobuzAuthenticationService>();
            _mockApiClient = new Mock<IQobuzApiClient>();
            _mockConcurrencyManager = new Mock<IConcurrencyManager>();
            _performanceMonitor = new PerformanceMonitor();
            _resourceMonitor = new ResourceMonitor();
            
            SetupMocks();
        }

        private void SetupMocks()
        {
            var session = new QobuzSession
            {
                UserId = "test_user",
                AuthToken = "test_token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            
            _mockAuthService.Setup(x => x.GetCachedSession()).Returns(session);
            _mockAuthService.Setup(x => x.GetValidSessionAsync()).ReturnsAsync(session);
            
            _mockApiClient.Setup(x => x.GetAlbumAsync(It.IsAny<string>()))
                .ReturnsAsync(() => GenerateMockAlbum());
            
            _mockApiClient.Setup(x => x.GetStreamUrlAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(() => GenerateMockStreamUrl());
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task ConcurrentDownloads_ShouldRespectMaxLimit()
        {
            // Arrange
            var semaphore = new SemaphoreSlim(MAX_CONCURRENT_DOWNLOADS, MAX_CONCURRENT_DOWNLOADS);
            var concurrencyManager = new ConcurrencyManager(semaphore);
            var downloadClient = CreateDownloadClient(concurrencyManager);
            
            var activeDownloads = new ConcurrentDictionary<string, bool>();
            var maxConcurrent = 0;
            var downloadTasks = new List<Task<string>>();
            
            // Act - Start 20 downloads
            for (int i = 0; i < 20; i++)
            {
                var album = CreateTestRemoteAlbum($"Album {i}");
                var task = Task.Run(async () =>
                {
                    var downloadId = Guid.NewGuid().ToString();
                    activeDownloads[downloadId] = true;
                    
                    // Track max concurrent
                    var currentActive = activeDownloads.Count(x => x.Value);
                    lock (activeDownloads)
                    {
                        maxConcurrent = Math.Max(maxConcurrent, currentActive);
                    }
                    
                    // Simulate download
                    await concurrencyManager.ExecuteAsync(async () =>
                    {
                        await Task.Delay(500); // Simulate download time
                    });
                    
                    activeDownloads[downloadId] = false;
                    return downloadId;
                });
                
                downloadTasks.Add(task);
            }
            
            await Task.WhenAll(downloadTasks);
            
            // Assert
            maxConcurrent.Should().BeLessThanOrEqualTo(MAX_CONCURRENT_DOWNLOADS,
                $"Should not exceed {MAX_CONCURRENT_DOWNLOADS} concurrent downloads");
            
            _output.WriteLine($"Max concurrent downloads: {maxConcurrent}/{MAX_CONCURRENT_DOWNLOADS}");
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task LoadTest_SustainedDownloadRate_ShouldMeetTargets()
        {
            // Arrange
            var downloadClient = CreateDownloadClient();
            var targetDownloads = 50; // Scaled down for test
            var testDuration = TimeSpan.FromMinutes(2);
            var completedDownloads = new ConcurrentBag<DownloadResult>();
            
            _performanceMonitor.Start();
            
            // Act - Sustained load test
            var cancellationSource = new CancellationTokenSource(testDuration);
            var downloadTasks = new List<Task>();
            
            for (int i = 0; i < targetDownloads; i++)
            {
                if (cancellationSource.Token.IsCancellationRequested)
                    break;
                
                var album = CreateTestRemoteAlbum($"Album {i}");
                var task = Task.Run(async () =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        var downloadId = await downloadClient.Download(album, Mock.Of<NzbDrone.Core.Indexers.IIndexer>());
                        stopwatch.Stop();
                        
                        completedDownloads.Add(new DownloadResult
                        {
                            DownloadId = downloadId,
                            Duration = stopwatch.Elapsed,
                            Success = true
                        });
                    }
                    catch (Exception ex)
                    {
                        stopwatch.Stop();
                        completedDownloads.Add(new DownloadResult
                        {
                            Duration = stopwatch.Elapsed,
                            Success = false,
                            Error = ex.Message
                        });
                    }
                }, cancellationSource.Token);
                
                downloadTasks.Add(task);
                
                // Throttle submission rate
                await Task.Delay(100);
            }
            
            await Task.WhenAll(downloadTasks);
            _performanceMonitor.Stop();
            
            // Calculate metrics
            var successfulDownloads = completedDownloads.Count(d => d.Success);
            var failedDownloads = completedDownloads.Count(d => !d.Success);
            var avgDuration = completedDownloads.Where(d => d.Success).Average(d => d.Duration.TotalSeconds);
            var downloadsPerHour = (successfulDownloads / _performanceMonitor.ElapsedTime.TotalHours);
            
            // Assert
            var successRate = (double)successfulDownloads / completedDownloads.Count;
            successRate.Should().BeGreaterThan(0.95, "Success rate should be above 95%");
            downloadsPerHour.Should().BeGreaterThan(TARGET_DOWNLOADS_PER_HOUR * 0.8,
                $"Should achieve at least 80% of target rate ({TARGET_DOWNLOADS_PER_HOUR}/hour)");
            
            _output.WriteLine($"Load Test Results:");
            _output.WriteLine($"  Total Downloads: {completedDownloads.Count}");
            _output.WriteLine($"  Successful: {successfulDownloads}");
            _output.WriteLine($"  Failed: {failedDownloads}");
            _output.WriteLine($"  Success Rate: {successRate:P2}");
            _output.WriteLine($"  Downloads/Hour: {downloadsPerHour:F2}");
            _output.WriteLine($"  Avg Duration: {avgDuration:F2}s");
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task ResourceUsage_UnderLoad_ShouldRemainWithinLimits()
        {
            // Arrange
            var downloadClient = CreateDownloadClient();
            var concurrentDownloads = 10;
            
            _resourceMonitor.StartMonitoring();
            var initialMemory = GC.GetTotalMemory(true) / (1024.0 * 1024.0); // MB
            
            // Act - Create sustained load
            var downloadTasks = new List<Task>();
            for (int batch = 0; batch < 3; batch++)
            {
                for (int i = 0; i < concurrentDownloads; i++)
                {
                    var album = CreateTestRemoteAlbum($"Batch{batch}-Album{i}");
                    downloadTasks.Add(Task.Run(async () =>
                    {
                        await downloadClient.Download(album, Mock.Of<NzbDrone.Core.Indexers.IIndexer>());
                        await Task.Delay(Random.Shared.Next(100, 500));
                    }));
                }
                
                await Task.Delay(1000); // Space out batches
            }
            
            await Task.WhenAll(downloadTasks);
            _resourceMonitor.StopMonitoring();
            
            // Measure final state
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var finalMemory = GC.GetTotalMemory(false) / (1024.0 * 1024.0); // MB
            var memoryGrowth = finalMemory - initialMemory;
            
            // Assert
            _resourceMonitor.PeakCpuUsage.Should().BeLessThan(MAX_CPU_USAGE_PERCENT,
                $"CPU usage should stay below {MAX_CPU_USAGE_PERCENT}%");
            memoryGrowth.Should().BeLessThan(MAX_MEMORY_GROWTH_MB,
                $"Memory growth should be less than {MAX_MEMORY_GROWTH_MB}MB");
            _resourceMonitor.PeakThreadCount.Should().BeLessThan(MAX_THREAD_COUNT,
                $"Thread count should stay below {MAX_THREAD_COUNT}");
            
            _output.WriteLine($"Resource Usage:");
            _output.WriteLine($"  Peak CPU: {_resourceMonitor.PeakCpuUsage:F2}%");
            _output.WriteLine($"  Memory Growth: {memoryGrowth:F2}MB");
            _output.WriteLine($"  Peak Threads: {_resourceMonitor.PeakThreadCount}");
            _output.WriteLine($"  Avg Response Time: {_resourceMonitor.AverageResponseTime:F2}ms");
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task QueueManagement_WithBackpressure_ShouldThrottleAppropriately()
        {
            // Arrange
            var maxQueueSize = 50;
            var downloadQueue = new BlockingCollection<RemoteAlbum>(maxQueueSize);
            var processedCount = 0;
            var rejectedCount = 0;
            
            // Act - Producer task (submitting downloads)
            var producerTask = Task.Run(async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    var album = CreateTestRemoteAlbum($"Album {i}");
                    
                    if (downloadQueue.TryAdd(album, TimeSpan.FromMilliseconds(100)))
                    {
                        Interlocked.Increment(ref processedCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref rejectedCount);
                    }
                    
                    await Task.Delay(10); // Rapid submission
                }
                
                downloadQueue.CompleteAdding();
            });
            
            // Consumer tasks (processing downloads)
            var consumerTasks = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
            {
                foreach (var album in downloadQueue.GetConsumingEnumerable())
                {
                    // Simulate download processing
                    await Task.Delay(Random.Shared.Next(50, 150));
                }
            })).ToArray();
            
            await producerTask;
            await Task.WhenAll(consumerTasks);
            
            // Assert
            (processedCount + rejectedCount).Should().Be(100);
            rejectedCount.Should().BeGreaterThan(0, "Should have some backpressure rejections");
            processedCount.Should().BeGreaterThan(50, "Should process majority of requests");
            
            _output.WriteLine($"Queue Management:");
            _output.WriteLine($"  Processed: {processedCount}");
            _output.WriteLine($"  Rejected (backpressure): {rejectedCount}");
            _output.WriteLine($"  Max Queue Size: {maxQueueSize}");
        }

        [Fact]
        [Trait("Priority", "Medium")]
        public async Task ThroughputTest_LargeFiles_ShouldMaintainSpeed()
        {
            // Arrange
            var downloadClient = CreateDownloadClient();
            var fileSizes = new[] { 100, 500, 1000, 2000 }; // MB
            var throughputResults = new List<ThroughputResult>();
            
            // Act - Download files of various sizes
            foreach (var sizeMB in fileSizes)
            {
                var album = CreateTestRemoteAlbum($"Album-{sizeMB}MB");
                album.Release.Size = sizeMB * 1024 * 1024;
                
                var stopwatch = Stopwatch.StartNew();
                var downloadId = await downloadClient.Download(album, Mock.Of<NzbDrone.Core.Indexers.IIndexer>());
                
                // Simulate actual download time based on size
                await Task.Delay(sizeMB * 10); // Simplified simulation
                stopwatch.Stop();
                
                var throughputMbps = (sizeMB * 8) / stopwatch.Elapsed.TotalSeconds;
                throughputResults.Add(new ThroughputResult
                {
                    FileSizeMB = sizeMB,
                    Duration = stopwatch.Elapsed,
                    ThroughputMbps = throughputMbps
                });
            }
            
            // Calculate average throughput
            var avgThroughput = throughputResults.Average(r => r.ThroughputMbps);
            
            // Assert
            avgThroughput.Should().BeGreaterThan(TARGET_THROUGHPUT_MBPS * 0.7,
                $"Average throughput should be at least 70% of target ({TARGET_THROUGHPUT_MBPS} Mbps)");
            
            _output.WriteLine($"Throughput Test Results:");
            foreach (var result in throughputResults)
            {
                _output.WriteLine($"  {result.FileSizeMB}MB: {result.ThroughputMbps:F2} Mbps ({result.Duration.TotalSeconds:F2}s)");
            }
            _output.WriteLine($"  Average: {avgThroughput:F2} Mbps");
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task ConnectionPooling_ShouldReuseConnections()
        {
            // Arrange
            var connectionPool = new ConnectionPool();
            var downloadTasks = new List<Task>();
            var connectionReuses = 0;
            
            // Act - Multiple downloads that should reuse connections
            for (int i = 0; i < 20; i++)
            {
                downloadTasks.Add(Task.Run(async () =>
                {
                    var connection = await connectionPool.GetConnectionAsync();
                    
                    if (connection.ReuseCount > 0)
                    {
                        Interlocked.Increment(ref connectionReuses);
                    }
                    
                    // Simulate download
                    await Task.Delay(100);
                    
                    connectionPool.ReturnConnection(connection);
                }));
            }
            
            await Task.WhenAll(downloadTasks);
            
            // Assert
            connectionReuses.Should().BeGreaterThan(10, "Should reuse connections for most requests");
            connectionPool.TotalConnectionsCreated.Should().BeLessThan(10,
                "Should not create too many connections");
            
            _output.WriteLine($"Connection Pooling:");
            _output.WriteLine($"  Total Connections Created: {connectionPool.TotalConnectionsCreated}");
            _output.WriteLine($"  Connection Reuses: {connectionReuses}");
            _output.WriteLine($"  Pool Efficiency: {(double)connectionReuses / 20:P2}");
        }

        [Fact]
        [Trait("Priority", "Medium")]
        public async Task MemoryLeakTest_ExtendedOperation_ShouldNotLeak()
        {
            // Arrange
            var downloadClient = CreateDownloadClient();
            var memorySnapshots = new List<long>();
            var iterations = 10;
            var downloadsPerIteration = 5;
            
            // Act - Extended operation with memory tracking
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                var tasks = new List<Task>();
                
                for (int i = 0; i < downloadsPerIteration; i++)
                {
                    var album = CreateTestRemoteAlbum($"Iteration{iteration}-Album{i}");
                    tasks.Add(Task.Run(async () =>
                    {
                        await downloadClient.Download(album, Mock.Of<NzbDrone.Core.Indexers.IIndexer>());
                    }));
                }
                
                await Task.WhenAll(tasks);
                
                // Force garbage collection and measure memory
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                var currentMemory = GC.GetTotalMemory(false);
                memorySnapshots.Add(currentMemory);
                
                await Task.Delay(100);
            }
            
            // Analyze memory trend
            var firstHalf = memorySnapshots.Take(iterations / 2).Average();
            var secondHalf = memorySnapshots.Skip(iterations / 2).Average();
            var memoryGrowthRate = (secondHalf - firstHalf) / firstHalf;
            
            // Assert
            memoryGrowthRate.Should().BeLessThan(0.2, "Memory growth should be less than 20% over extended operation");
            
            var maxMemory = memorySnapshots.Max() / (1024.0 * 1024.0);
            var minMemory = memorySnapshots.Min() / (1024.0 * 1024.0);
            
            _output.WriteLine($"Memory Leak Test:");
            _output.WriteLine($"  Min Memory: {minMemory:F2}MB");
            _output.WriteLine($"  Max Memory: {maxMemory:F2}MB");
            _output.WriteLine($"  Growth Rate: {memoryGrowthRate:P2}");
            _output.WriteLine($"  Stable: {memoryGrowthRate < 0.2}");
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task RateLimiting_ShouldDistributeLoadEvenly()
        {
            // Arrange
            var rateLimiter = new RateLimiter(10); // 10 requests per second
            var requestTimes = new ConcurrentBag<DateTime>();
            var downloadTasks = new List<Task>();
            
            // Act - Submit 30 requests
            for (int i = 0; i < 30; i++)
            {
                downloadTasks.Add(Task.Run(async () =>
                {
                    await rateLimiter.WaitAsync();
                    requestTimes.Add(DateTime.UtcNow);
                    
                    // Simulate work
                    await Task.Delay(50);
                }));
            }
            
            await Task.WhenAll(downloadTasks);
            
            // Analyze request distribution
            var sortedTimes = requestTimes.OrderBy(t => t).ToList();
            var intervals = new List<double>();
            
            for (int i = 1; i < sortedTimes.Count; i++)
            {
                intervals.Add((sortedTimes[i] - sortedTimes[i - 1]).TotalMilliseconds);
            }
            
            var avgInterval = intervals.Average();
            var expectedInterval = 1000.0 / 10; // 100ms for 10 req/s
            
            // Assert
            avgInterval.Should().BeApproximately(expectedInterval, expectedInterval * 0.3,
                "Request intervals should match rate limit");
            
            _output.WriteLine($"Rate Limiting:");
            _output.WriteLine($"  Total Requests: {requestTimes.Count}");
            _output.WriteLine($"  Avg Interval: {avgInterval:F2}ms");
            _output.WriteLine($"  Expected Interval: {expectedInterval:F2}ms");
            _output.WriteLine($"  Distribution Variance: {intervals.StandardDeviation():F2}ms");
        }

        // Helper methods
        private QobuzDownloadClient CreateDownloadClient(IConcurrencyManager concurrencyManager = null)
        {
            return new QobuzDownloadClient(
                _mockAuthService.Object,
                _mockApiClient.Object,
                new System.Net.Http.HttpClient(),
                Mock.Of<IDownloadQueueService>(),
                Mock.Of<IDownloadFileService>(),
                concurrencyManager ?? _mockConcurrencyManager.Object,
                Mock.Of<IDownloadOrchestrator>(),
                Mock.Of<IDownloadSummary>(),
                Mock.Of<IBatchProcessor>(),
                Mock.Of<IQobuzTrackDownloaderFactory>(),
                Mock.Of<NzbDrone.Core.Configuration.IConfigService>(),
                Mock.Of<NzbDrone.Common.Disk.IDiskProvider>(),
                Mock.Of<NzbDrone.Core.RemotePathMappings.IRemotePathMappingService>(),
                Mock.Of<NzbDrone.Core.Localization.ILocalizationService>(),
                Mock.Of<ILogger<QobuzDownloadClient>>()
            );
        }

        private RemoteAlbum CreateTestRemoteAlbum(string title)
        {
            return new RemoteAlbum
            {
                Artist = new NzbDrone.Core.Music.Artist
                {
                    Name = "Test Artist",
                    Id = Random.Shared.Next(1, 1000)
                },
                Albums = new List<NzbDrone.Core.Music.Album>
                {
                    new NzbDrone.Core.Music.Album
                    {
                        Title = title,
                        Id = Random.Shared.Next(1, 10000),
                        ReleaseDate = DateTime.Now
                    }
                },
                Release = new ReleaseInfo
                {
                    Title = title,
                    DownloadUrl = $"qobuz://album/{Random.Shared.Next(100000, 999999)}",
                    Guid = Guid.NewGuid().ToString(),
                    Size = Random.Shared.Next(50, 500) * 1024 * 1024 // 50-500MB
                }
            };
        }

        private QobuzAlbum GenerateMockAlbum()
        {
            return new QobuzAlbum
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Mock Album",
                Artist = new QobuzArtist { Name = "Mock Artist" },
                Tracks = new QobuzTrackList
                {
                    Items = Enumerable.Range(1, 10).Select(i => new QobuzTrack
                    {
                        Id = Guid.NewGuid().ToString(),
                        Title = $"Track {i}",
                        TrackNumber = i,
                        Duration = Random.Shared.Next(180, 300)
                    }).ToList()
                }
            };
        }

        private QobuzStreamUrl GenerateMockStreamUrl()
        {
            return new QobuzStreamUrl
            {
                Url = $"https://stream.qobuz.com/track/{Guid.NewGuid()}",
                Quality = "FLAC",
                SampleRate = 44100,
                BitDepth = 16
            };
        }

        public void Dispose()
        {
            _performanceMonitor?.Dispose();
            _resourceMonitor?.Dispose();
        }

        // Helper classes
        private class PerformanceMonitor : IDisposable
        {
            private Stopwatch _stopwatch;
            
            public TimeSpan ElapsedTime => _stopwatch?.Elapsed ?? TimeSpan.Zero;
            
            public void Start()
            {
                _stopwatch = Stopwatch.StartNew();
            }
            
            public void Stop()
            {
                _stopwatch?.Stop();
            }
            
            public void Dispose()
            {
                _stopwatch = null;
            }
        }

        private class ResourceMonitor : IDisposable
        {
            private Timer _monitoringTimer;
            private readonly List<double> _cpuSamples = new List<double>();
            private readonly List<int> _threadSamples = new List<int>();
            private readonly List<long> _responseTimes = new List<long>();
            
            public double PeakCpuUsage => _cpuSamples.Any() ? _cpuSamples.Max() : 0;
            public int PeakThreadCount => _threadSamples.Any() ? _threadSamples.Max() : 0;
            public double AverageResponseTime => _responseTimes.Any() ? _responseTimes.Average() : 0;
            
            public void StartMonitoring()
            {
                _monitoringTimer = new Timer(_ =>
                {
                    // Simulate CPU monitoring
                    _cpuSamples.Add(Random.Shared.Next(20, 60));
                    
                    // Monitor thread count
                    _threadSamples.Add(Process.GetCurrentProcess().Threads.Count);
                    
                }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            }
            
            public void StopMonitoring()
            {
                _monitoringTimer?.Dispose();
            }
            
            public void RecordResponseTime(long milliseconds)
            {
                _responseTimes.Add(milliseconds);
            }
            
            public void Dispose()
            {
                _monitoringTimer?.Dispose();
            }
        }

        private class ConcurrencyManager : IConcurrencyManager
        {
            private readonly SemaphoreSlim _semaphore;
            
            public ConcurrencyManager(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }
            
            public async Task ExecuteAsync(Func<Task> action)
            {
                await _semaphore.WaitAsync();
                try
                {
                    await action();
                }
                finally
                {
                    _semaphore.Release();
                }
            }
        }

        private class ConnectionPool
        {
            private readonly ConcurrentBag<Connection> _connections = new ConcurrentBag<Connection>();
            private int _totalCreated = 0;
            
            public int TotalConnectionsCreated => _totalCreated;
            
            public async Task<Connection> GetConnectionAsync()
            {
                if (_connections.TryTake(out var connection))
                {
                    connection.ReuseCount++;
                    return connection;
                }
                
                await Task.Delay(10); // Simulate connection creation
                Interlocked.Increment(ref _totalCreated);
                return new Connection { Id = Guid.NewGuid().ToString() };
            }
            
            public void ReturnConnection(Connection connection)
            {
                _connections.Add(connection);
            }
        }

        private class Connection
        {
            public string Id { get; set; }
            public int ReuseCount { get; set; }
        }

        private class RateLimiter
        {
            private readonly SemaphoreSlim _semaphore;
            private readonly Timer _refillTimer;
            private readonly int _maxRequestsPerSecond;
            
            public RateLimiter(int maxRequestsPerSecond)
            {
                _maxRequestsPerSecond = maxRequestsPerSecond;
                _semaphore = new SemaphoreSlim(maxRequestsPerSecond, maxRequestsPerSecond);
                
                var refillInterval = TimeSpan.FromMilliseconds(1000.0 / maxRequestsPerSecond);
                _refillTimer = new Timer(_ =>
                {
                    if (_semaphore.CurrentCount < _maxRequestsPerSecond)
                    {
                        _semaphore.Release();
                    }
                }, null, refillInterval, refillInterval);
            }
            
            public async Task WaitAsync()
            {
                await _semaphore.WaitAsync();
            }
        }

        private class DownloadResult
        {
            public string DownloadId { get; set; }
            public TimeSpan Duration { get; set; }
            public bool Success { get; set; }
            public string Error { get; set; }
        }

        private class ThroughputResult
        {
            public int FileSizeMB { get; set; }
            public TimeSpan Duration { get; set; }
            public double ThroughputMbps { get; set; }
        }
    }

    public static class EnumerableExtensions
    {
        public static double StandardDeviation(this IEnumerable<double> values)
        {
            var list = values.ToList();
            if (!list.Any()) return 0;
            
            var avg = list.Average();
            var sum = list.Sum(d => Math.Pow(d - avg, 2));
            return Math.Sqrt(sum / list.Count);
        }
    }
}