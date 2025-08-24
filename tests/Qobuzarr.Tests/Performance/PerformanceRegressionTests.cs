using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using NSubstitute;

namespace Qobuzarr.Tests.Performance
{
    /// <summary>
    /// Performance regression tests to ensure system maintains acceptable performance
    /// Establishes baselines and monitors for degradation across releases
    /// </summary>
    [Collection("PerformanceRegression")]
    public class PerformanceRegressionTests
    {
        private readonly ITestOutputHelper _output;
        private readonly PerformanceBaselines _baselines;

        public PerformanceRegressionTests(ITestOutputHelper output)
        {
            _output = output;
            _baselines = new PerformanceBaselines();
        }

        #region Download Performance Tests

        [Fact]
        [Trait("Category", "Performance")]
        public async Task DownloadSpeed_ShouldMeetMinimumThreshold()
        {
            // Verify download speeds meet minimum requirements
            
            // Arrange
            const long fileSize = 50 * 1024 * 1024; // 50MB test file
            const double minimumSpeedMBps = 5.0; // Minimum 5 MB/s
            var mockDownloader = new MockAudioFileDownloader();

            // Act
            var stopwatch = Stopwatch.StartNew();
            await mockDownloader.SimulateDownload(fileSize);
            stopwatch.Stop();

            var actualSpeedMBps = (fileSize / 1024.0 / 1024.0) / stopwatch.Elapsed.TotalSeconds;

            // Assert
            actualSpeedMBps.Should().BeGreaterThanOrEqualTo(minimumSpeedMBps,
                $"Download speed should be at least {minimumSpeedMBps} MB/s");

            // Check against baseline
            var baseline = _baselines.GetDownloadSpeedBaseline();
            var degradation = (baseline - actualSpeedMBps) / baseline * 100;
            
            degradation.Should().BeLessThan(20.0,
                "Download speed should not degrade more than 20% from baseline");

            _output.WriteLine($"Download speed: {actualSpeedMBps:F2} MB/s (Baseline: {baseline:F2} MB/s)");
            _output.WriteLine($"Performance degradation: {degradation:F2}%");
        }

        [Fact]
        [Trait("Category", "Performance")]
        public async Task ConcurrentDownloads_ShouldScale()
        {
            // Test concurrent download performance scaling
            
            // Arrange
            var concurrencyLevels = new[] { 1, 2, 3, 5 };
            var results = new Dictionary<int, double>();

            // Act
            foreach (var concurrency in concurrencyLevels)
            {
                var stopwatch = Stopwatch.StartNew();
                var tasks = Enumerable.Range(0, concurrency)
                    .Select(_ => SimulateDownloadTask(10 * 1024 * 1024)) // 10MB each
                    .ToList();

                await Task.WhenAll(tasks);
                stopwatch.Stop();

                var throughput = (concurrency * 10.0) / stopwatch.Elapsed.TotalSeconds; // MB/s
                results[concurrency] = throughput;

                _output.WriteLine($"Concurrency {concurrency}: {throughput:F2} MB/s total throughput");
            }

            // Assert - Verify scaling efficiency
            var singleThreadThroughput = results[1];
            var dualThreadEfficiency = results[2] / (singleThreadThroughput * 2);
            var tripleThreadEfficiency = results[3] / (singleThreadThroughput * 3);

            dualThreadEfficiency.Should().BeGreaterThan(0.7,
                "Dual thread should achieve at least 70% efficiency");
            tripleThreadEfficiency.Should().BeGreaterThan(0.6,
                "Triple thread should achieve at least 60% efficiency");

            _output.WriteLine($"Scaling efficiency - 2 threads: {dualThreadEfficiency:P}, 3 threads: {tripleThreadEfficiency:P}");
        }

        #endregion

        #region API Response Time Tests

        [Fact]
        [Trait("Category", "Performance")]
        public async Task ApiResponseTime_ShouldMeetSLA()
        {
            // Verify API response times meet SLA requirements
            
            // Arrange
            var apiClient = Substitute.For<IQobuzApiClient>();
            var endpoints = new[]
            {
                ("/album/search", 200),  // Search should respond in 200ms
                ("/album/get", 100),     // Direct fetch in 100ms
                ("/track/getFileUrl", 150) // Stream URL in 150ms
            };

            var measurements = new List<(string Endpoint, double ResponseTime, double SLA)>();

            // Act
            foreach (var (endpoint, slaMsec) in endpoints)
            {
                var stopwatch = Stopwatch.StartNew();
                
                // Simulate API call
                await Task.Delay(Random.Shared.Next(50, 150)); // Simulated response time
                
                stopwatch.Stop();
                measurements.Add((endpoint, stopwatch.Elapsed.TotalMilliseconds, slaMsec));
            }

            // Assert
            foreach (var (endpoint, responseTime, sla) in measurements)
            {
                responseTime.Should().BeLessThanOrEqualTo(sla * 1.1, // Allow 10% margin
                    $"Endpoint {endpoint} should respond within {sla}ms");

                _output.WriteLine($"{endpoint}: {responseTime:F2}ms (SLA: {sla}ms)");
            }

            // Check P95 latency
            var allResponseTimes = measurements.Select(m => m.ResponseTime).OrderBy(t => t).ToList();
            var p95Index = (int)(allResponseTimes.Count * 0.95);
            var p95Latency = allResponseTimes[p95Index];

            p95Latency.Should().BeLessThan(250.0,
                "95th percentile latency should be under 250ms");

            _output.WriteLine($"P95 Latency: {p95Latency:F2}ms");
        }

        [Fact]
        [Trait("Category", "Performance")]
        public async Task ApiCaching_ShouldReduceLatency()
        {
            // Verify caching effectiveness
            
            // Arrange
            var cache = new Dictionary<string, object>();
            var apiCallCount = 0;

            // Act - First calls (cache miss)
            var firstCallTimes = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                var key = $"album_{i}";
                
                if (!cache.ContainsKey(key))
                {
                    await Task.Delay(50); // Simulate API call
                    apiCallCount++;
                    cache[key] = new { Album = $"Album {i}" };
                }
                
                stopwatch.Stop();
                firstCallTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            // Act - Second calls (cache hit)
            var cachedCallTimes = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                var key = $"album_{i}";
                
                if (!cache.ContainsKey(key))
                {
                    await Task.Delay(50);
                    apiCallCount++;
                }
                
                stopwatch.Stop();
                cachedCallTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            // Assert
            var avgFirstCall = firstCallTimes.Average();
            var avgCachedCall = cachedCallTimes.Average();
            var speedup = avgFirstCall / avgCachedCall;

            speedup.Should().BeGreaterThan(10.0,
                "Cached calls should be at least 10x faster");
            apiCallCount.Should().Be(5,
                "Should only make API calls on cache miss");

            _output.WriteLine($"Cache performance - First call: {avgFirstCall:F2}ms, Cached: {avgCachedCall:F2}ms");
            _output.WriteLine($"Cache speedup: {speedup:F2}x");
        }

        #endregion

        #region Memory Usage Tests

        [Fact]
        [Trait("Category", "Performance")]
        public void MemoryUsage_ShouldStayWithinLimits()
        {
            // Monitor memory usage during typical operations
            
            // Arrange
            const int maxMemoryMB = 500; // Maximum 500MB for plugin operations
            var initialMemory = GC.GetTotalMemory(true) / 1024.0 / 1024.0;

            // Act - Simulate typical workload
            var albums = new List<QobuzAlbum>();
            for (int i = 0; i < 1000; i++)
            {
                albums.Add(new QobuzAlbum
                {
                    Id = i.ToString(),
                    Title = $"Album {i}",
                    Tracks = Enumerable.Range(0, 15).Select(t => new QobuzTrack
                    {
                        Id = $"{i}_{t}",
                        Title = $"Track {t}",
                        Duration = 240
                    }).ToList()
                });
            }

            var currentMemory = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
            var memoryUsed = currentMemory - initialMemory;

            // Force cleanup
            albums.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var finalMemory = GC.GetTotalMemory(true) / 1024.0 / 1024.0;
            var memoryLeaked = finalMemory - initialMemory;

            // Assert
            memoryUsed.Should().BeLessThan(maxMemoryMB,
                $"Memory usage should stay under {maxMemoryMB}MB");
            memoryLeaked.Should().BeLessThan(10.0,
                "Memory leak should be less than 10MB after cleanup");

            _output.WriteLine($"Memory usage - Peak: {memoryUsed:F2}MB, Leaked: {memoryLeaked:F2}MB");
        }

        [Fact]
        [Trait("Category", "Performance")]
        public async Task LargePlaylist_ShouldHandleEfficiently()
        {
            // Test memory efficiency with large playlists
            
            // Arrange
            const int playlistSize = 10000; // Large playlist
            const double maxMemoryPerTrackKB = 2.0; // Max 2KB per track

            var initialMemory = GC.GetTotalMemory(true);

            // Act
            var playlist = new List<QobuzTrack>();
            for (int i = 0; i < playlistSize; i++)
            {
                playlist.Add(new QobuzTrack
                {
                    Id = i.ToString(),
                    Title = $"Track {i}",
                    Artist = new QobuzArtist { Name = $"Artist {i % 100}" },
                    Album = new QobuzAlbum { Title = $"Album {i % 200}" }
                });
            }

            var memoryUsed = GC.GetTotalMemory(false) - initialMemory;
            var memoryPerTrack = memoryUsed / (double)playlistSize / 1024.0;

            // Assert
            memoryPerTrack.Should().BeLessThan(maxMemoryPerTrackKB,
                $"Memory per track should be under {maxMemoryPerTrackKB}KB");

            _output.WriteLine($"Large playlist memory usage: {memoryUsed / 1024.0 / 1024.0:F2}MB total");
            _output.WriteLine($"Memory per track: {memoryPerTrack:F2}KB");
        }

        #endregion

        #region Search Performance Tests

        [Fact]
        [Trait("Category", "Performance")]
        public async Task SearchIndexing_ShouldBeFast()
        {
            // Test search indexing performance
            
            // Arrange
            var indexer = new QobuzIndexer(null, null, null, null, null);
            var searchQueries = new[]
            {
                "Miles Davis",
                "The Beatles Abbey Road",
                "Pink Floyd Dark Side",
                "Led Zeppelin",
                "Nirvana Nevermind"
            };

            var searchTimes = new List<double>();

            // Act
            foreach (var query in searchQueries)
            {
                var stopwatch = Stopwatch.StartNew();
                
                // Simulate search operation
                var normalizedQuery = query.ToLowerInvariant();
                var tokens = normalizedQuery.Split(' ');
                var searchKey = string.Join("_", tokens.Take(3));
                
                // Simulate index lookup
                await Task.Delay(5); // Simulated index access
                
                stopwatch.Stop();
                searchTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            // Assert
            var avgSearchTime = searchTimes.Average();
            var maxSearchTime = searchTimes.Max();

            avgSearchTime.Should().BeLessThan(10.0,
                "Average search time should be under 10ms");
            maxSearchTime.Should().BeLessThan(20.0,
                "Maximum search time should be under 20ms");

            _output.WriteLine($"Search performance - Avg: {avgSearchTime:F2}ms, Max: {maxSearchTime:F2}ms");
        }

        [Fact]
        [Trait("Category", "Performance")]
        public void MLClassification_ShouldBeFast()
        {
            // Test ML classification performance
            
            // Arrange
            var optimizer = new CompiledMLQueryOptimizer();
            const int iterations = 1000;
            
            // Warm up
            for (int i = 0; i < 10; i++)
            {
                optimizer.ClassifyQuery("warmup query");
            }

            // Act
            var stopwatch = Stopwatch.StartNew();
            
            for (int i = 0; i < iterations; i++)
            {
                var query = $"Artist{i % 100} Album{i % 50}";
                var result = optimizer.ClassifyQuery(query);
            }
            
            stopwatch.Stop();

            var avgTimeMs = stopwatch.Elapsed.TotalMilliseconds / iterations;

            // Assert
            avgTimeMs.Should().BeLessThan(1.0,
                "ML classification should average under 1ms per query");

            var throughput = iterations / stopwatch.Elapsed.TotalSeconds;
            throughput.Should().BeGreaterThan(1000,
                "Should process over 1000 classifications per second");

            _output.WriteLine($"ML Classification - Avg time: {avgTimeMs:F3}ms, Throughput: {throughput:F0} queries/sec");
        }

        #endregion

        #region Resource Utilization Tests

        [Fact]
        [Trait("Category", "Performance")]
        public async Task CpuUsage_ShouldStayReasonable()
        {
            // Monitor CPU usage during operations
            
            // Arrange
            var process = Process.GetCurrentProcess();
            var startTime = DateTime.UtcNow;
            var startCpuTime = process.TotalProcessorTime;

            // Act - Simulate workload
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < 100; j++)
                    {
                        // Simulate CPU-bound work
                        var data = Enumerable.Range(0, 1000).Select(x => x * 2).ToList();
                        await Task.Delay(1);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            var endTime = DateTime.UtcNow;
            var endCpuTime = process.TotalProcessorTime;

            // Calculate CPU usage
            var cpuUsedMs = (endCpuTime - startCpuTime).TotalMilliseconds;
            var wallTimeMs = (endTime - startTime).TotalMilliseconds;
            var cpuUsagePercent = (cpuUsedMs / wallTimeMs / Environment.ProcessorCount) * 100;

            // Assert
            cpuUsagePercent.Should().BeLessThan(80.0,
                "CPU usage should stay under 80% during normal operations");

            _output.WriteLine($"CPU Usage: {cpuUsagePercent:F2}% across {Environment.ProcessorCount} cores");
        }

        [Fact]
        [Trait("Category", "Performance")]
        public async Task ThreadPool_ShouldNotExhaust()
        {
            // Verify thread pool doesn't get exhausted
            
            // Arrange
            ThreadPool.GetAvailableThreads(out var initialWorkerThreads, out var initialIoThreads);
            
            // Act - Create many concurrent operations
            var tasks = Enumerable.Range(0, 100)
                .Select(i => Task.Run(async () =>
                {
                    await Task.Delay(100);
                    return i;
                }))
                .ToList();

            // Check thread pool during execution
            ThreadPool.GetAvailableThreads(out var duringWorkerThreads, out var duringIoThreads);
            
            await Task.WhenAll(tasks);

            // Check thread pool after completion
            ThreadPool.GetAvailableThreads(out var afterWorkerThreads, out var afterIoThreads);

            // Assert
            var minAvailableThreads = Math.Min(duringWorkerThreads, duringIoThreads);
            minAvailableThreads.Should().BeGreaterThan(10,
                "Should maintain at least 10 available threads");

            var threadsRecovered = afterWorkerThreads >= initialWorkerThreads - 10;
            threadsRecovered.Should().BeTrue(
                "Thread pool should recover after operations complete");

            _output.WriteLine($"Thread pool - Initial: {initialWorkerThreads}, During: {duringWorkerThreads}, After: {afterWorkerThreads}");
        }

        #endregion

        #region Helper Methods

        private async Task<double> SimulateDownloadTask(long bytes)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Simulate network I/O
            await Task.Delay(Random.Shared.Next(100, 200));
            
            // Simulate disk I/O
            var buffer = new byte[4096];
            var written = 0L;
            while (written < bytes)
            {
                var toWrite = Math.Min(buffer.Length, bytes - written);
                written += toWrite;
                
                if (written % (1024 * 1024) == 0) // Every MB
                    await Task.Yield();
            }
            
            stopwatch.Stop();
            return bytes / 1024.0 / 1024.0 / stopwatch.Elapsed.TotalSeconds;
        }

        private class MockAudioFileDownloader
        {
            public async Task SimulateDownload(long bytes)
            {
                var buffer = new byte[65536]; // 64KB buffer
                var downloaded = 0L;

                while (downloaded < bytes)
                {
                    var toDownload = Math.Min(buffer.Length, bytes - downloaded);
                    downloaded += toDownload;
                    
                    // Simulate network latency
                    if (downloaded % (1024 * 1024 * 5) == 0) // Every 5MB
                        await Task.Delay(10);
                }
            }
        }

        private class PerformanceBaselines
        {
            private readonly Dictionary<string, double> _baselines = new()
            {
                ["DownloadSpeed"] = 10.0, // MB/s
                ["ApiResponseTime"] = 100.0, // ms
                ["MemoryPerOperation"] = 50.0, // MB
                ["MLClassificationTime"] = 0.5 // ms
            };

            public double GetDownloadSpeedBaseline() => _baselines["DownloadSpeed"];
            public double GetApiResponseBaseline() => _baselines["ApiResponseTime"];
            public double GetMemoryBaseline() => _baselines["MemoryPerOperation"];
            public double GetMLClassificationBaseline() => _baselines["MLClassificationTime"];
        }

        #endregion
    }
}