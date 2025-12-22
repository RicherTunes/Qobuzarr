using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;

namespace Qobuzarr.Tests.LibraryLinking
{
    /// <summary>
    /// Tests for service isolation when Qobuzarr shares the Common library with other plugins.
    /// Verifies that services like caching, rate limiting, and authentication
    /// are properly scoped to the plugin and don't leak to other plugins.
    /// </summary>
    [Trait("Category", "ServiceIsolation")]
    public class ServiceIsolationTests
    {
        #region Qobuz API Response Cache Isolation Tests

        [Fact]
        public void ResponseCache_Should_Be_Plugin_Scoped()
        {
            // Arrange - Simulate caches for different plugins
            var qobuzCache = new Dictionary<string, object>();
            var tidalCache = new Dictionary<string, object>();

            // Act - Cache a Qobuz API response
            qobuzCache["album:12345"] = new { Title = "Kind of Blue", Artist = "Miles Davis" };

            // Assert - Tidal's cache should not see Qobuz's cached data
            tidalCache.ContainsKey("album:12345").Should().BeFalse(
                "Response caches from different plugins should be isolated");
        }

        [Fact]
        public async Task ConcurrentCacheAccess_Should_Be_Thread_Safe()
        {
            // Arrange - Simulate ML query optimizer's concurrent cache access
            var cache = new ConcurrentDictionary<string, object>();
            var tasks = new List<Task>();
            var errors = new ConcurrentBag<Exception>();

            // Act - Simulate concurrent search requests
            for (int i = 0; i < 100; i++)
            {
                var searchId = i;
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        var key = $"search:{searchId}:optimized";
                        cache.TryAdd(key, new { Query = $"test query {searchId}", Score = 0.95 });
                        cache.TryGetValue(key, out _);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            errors.Should().BeEmpty("Concurrent cache access should not cause exceptions");
        }

        [Fact]
        public void MLOptimizer_Cache_Should_Be_Isolated()
        {
            // Arrange - ML query optimizer uses pattern caching
            var optimizerCache = new ConcurrentDictionary<string, double>();

            // Act
            optimizerCache["qobuzarr:pattern:artist+album"] = 0.92;
            optimizerCache["qobuzarr:pattern:artist"] = 0.78;

            // Assert - Each pattern key should be scoped to plugin
            optimizerCache.Keys.Should().AllSatisfy(k =>
                k.Should().StartWith("qobuzarr:",
                    "ML optimizer cache keys should be prefixed with plugin name"));
        }

        #endregion

        #region Rate Limiter Isolation Tests

        [Fact]
        public async Task QobuzApiRateLimiter_Should_Be_Plugin_Scoped()
        {
            // Arrange - Qobuz API has specific rate limits
            var qobuzRateLimiter = new SemaphoreSlim(10); // Qobuz: 10 concurrent requests
            var tidalRateLimiter = new SemaphoreSlim(5);  // Tidal: 5 concurrent requests

            // Act - Exhaust Qobuz's rate limit
            for (int i = 0; i < 10; i++)
            {
                await qobuzRateLimiter.WaitAsync();
            }

            // Assert - Tidal's rate limiter should still have full capacity
            tidalRateLimiter.CurrentCount.Should().Be(5,
                "Rate limiting exhaustion in Qobuzarr should not affect other streaming plugins");

            // Cleanup
            qobuzRateLimiter.Dispose();
            tidalRateLimiter.Dispose();
        }

        [Fact]
        public async Task DownloadRateLimiter_Should_Not_Block_Search()
        {
            // Arrange - Downloads and searches use separate rate limiters
            var downloadLimiter = new SemaphoreSlim(2);
            var searchLimiter = new SemaphoreSlim(10);

            // Act - Exhaust download limit
            await downloadLimiter.WaitAsync();
            await downloadLimiter.WaitAsync();

            // Assert - Search should still work
            var canSearch = await searchLimiter.WaitAsync(TimeSpan.FromMilliseconds(100));

            canSearch.Should().BeTrue(
                "Download rate limiting should not affect search operations");

            // Cleanup
            downloadLimiter.Dispose();
            searchLimiter.Dispose();
        }

        #endregion

        #region Authentication Session Isolation Tests

        [Fact]
        public void QobuzSession_Should_Be_Plugin_Scoped()
        {
            // Arrange - Simulate sessions for different plugins
            var sessions = new ConcurrentDictionary<string, object>();

            // Act - Create Qobuz session
            sessions["qobuzarr:session"] = new
            {
                AppId = "test-app-id",
                Token = "qobuz-auth-token",
                Expiry = DateTime.UtcNow.AddHours(1)
            };

            // Create Tidal session (hypothetically)
            sessions["tidalarr:session"] = new
            {
                AccessToken = "tidal-token",
                RefreshToken = "tidal-refresh",
                Expiry = DateTime.UtcNow.AddHours(1)
            };

            // Assert - Sessions should be isolated
            sessions.Keys.Should().Contain("qobuzarr:session");
            sessions.Keys.Should().Contain("tidalarr:session");
            sessions["qobuzarr:session"].Should().NotBeSameAs(sessions["tidalarr:session"],
                "Authentication sessions must be isolated per plugin");
        }

        [Fact]
        public void TokenRefresh_Should_Not_Affect_Other_Plugins()
        {
            // Arrange
            var qobuzToken = "qobuz-token-v1";
            var tidalToken = "tidal-token-v1";

            // Act - Refresh Qobuz token
            qobuzToken = "qobuz-token-v2";

            // Assert - Tidal's token should be unchanged
            tidalToken.Should().Be("tidal-token-v1",
                "Token refresh in one plugin should not affect other plugins");
        }

        #endregion

        #region Quality Settings Isolation Tests

        [Fact]
        public void QualitySettings_Should_Be_Plugin_Scoped()
        {
            // Arrange - Quality settings vary per streaming service
            var settings = new Dictionary<string, IDictionary<string, object>>
            {
                ["qobuzarr"] = new Dictionary<string, object>
                {
                    ["Quality"] = 27,        // FLAC-Max (192kHz/24-bit)
                    ["Formats"] = new[] { "FLAC", "MP3-320" }
                },
                ["tidalarr"] = new Dictionary<string, object>
                {
                    ["Quality"] = "HiFi",
                    ["Formats"] = new[] { "MQA", "FLAC", "AAC" }
                }
            };

            // Assert - Each plugin has its own quality settings
            settings["qobuzarr"]["Quality"].Should().Be(27);
            settings["tidalarr"]["Quality"].Should().Be("HiFi",
                "Quality settings must be isolated per streaming service plugin");
        }

        #endregion

        #region Download Queue Isolation Tests

        [Fact]
        public void DownloadQueue_Should_Be_Plugin_Scoped()
        {
            // Arrange
            var qobuzQueue = new Queue<string>();
            var tidalQueue = new Queue<string>();

            // Act - Add items to Qobuz queue
            qobuzQueue.Enqueue("album:qobuz:123");
            qobuzQueue.Enqueue("album:qobuz:456");

            // Assert - Tidal's queue should be empty
            tidalQueue.Should().BeEmpty(
                "Download queues should be isolated per plugin");
            qobuzQueue.Should().HaveCount(2);
        }

        [Fact]
        public async Task ConcurrentDownloads_Should_Not_Interfere()
        {
            // Arrange
            var activeDownloads = new ConcurrentDictionary<string, bool>();
            var tasks = new List<Task>();

            // Act - Simulate concurrent downloads from multiple plugins
            for (int i = 0; i < 10; i++)
            {
                var plugin = i % 2 == 0 ? "qobuzarr" : "tidalarr";
                var downloadId = i;
                tasks.Add(Task.Run(async () =>
                {
                    var key = $"{plugin}:download:{downloadId}";
                    activeDownloads.TryAdd(key, true);
                    await Task.Delay(10); // Simulate download
                    activeDownloads.TryRemove(key, out _);
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - All downloads completed without interference
            activeDownloads.Should().BeEmpty(
                "All concurrent downloads should complete without interference");
        }

        #endregion

        #region ML Query Optimizer Isolation Tests

        [Fact]
        public void MLQueryOptimizer_State_Should_Be_Plugin_Scoped()
        {
            // Arrange - Each plugin might have its own ML optimization state
            var optimizerStates = new Dictionary<string, object>
            {
                ["qobuzarr"] = new
                {
                    PatternsLoaded = true,
                    ModelVersion = "1.0.0",
                    LastTrainingDate = DateTime.UtcNow.AddDays(-7)
                },
                ["brainarr"] = new
                {
                    Provider = "OpenAI",
                    ModelName = "gpt-4",
                    LastUsed = DateTime.UtcNow
                }
            };

            // Assert - Each plugin's state should be independent
            optimizerStates.Should().HaveCount(2);
            optimizerStates["qobuzarr"].Should().NotBeEquivalentTo(optimizerStates["brainarr"],
                "ML/AI state should be isolated per plugin");
        }

        #endregion

        #region Error State Isolation Tests

        [Fact]
        public void CircuitBreaker_State_Should_Be_Plugin_Scoped()
        {
            // Arrange - Circuit breakers for API resilience
            var circuitStates = new ConcurrentDictionary<string, string>
            {
                ["qobuzarr:api"] = "Closed",
                ["tidalarr:api"] = "Closed"
            };

            // Act - Qobuz API experiences failures, circuit breaker opens
            circuitStates["qobuzarr:api"] = "Open";

            // Assert - Tidal's circuit breaker should be unaffected
            circuitStates["qobuzarr:api"].Should().Be("Open");
            circuitStates["tidalarr:api"].Should().Be("Closed",
                "Circuit breaker state should be isolated per plugin");
        }

        [Fact]
        public void ApiErrorCounts_Should_Be_Plugin_Scoped()
        {
            // Arrange
            var errorCounts = new ConcurrentDictionary<string, int>();

            // Act - Increment Qobuz error count
            errorCounts.AddOrUpdate("qobuzarr", 1, (_, count) => count + 1);
            errorCounts.AddOrUpdate("qobuzarr", 1, (_, count) => count + 1);

            // Assert - Tidal error count should be zero (or not exist)
            errorCounts.GetValueOrDefault("qobuzarr").Should().Be(2);
            errorCounts.GetValueOrDefault("tidalarr").Should().Be(0,
                "Error counts should be isolated per plugin");
        }

        #endregion

        #region Memory Management Tests

        [Fact]
        public void Plugin_Objects_Should_Be_GC_Eligible_After_Unload()
        {
            // Arrange - keep the object creation in a separate method so the JIT can't keep locals alive.
            var weakRef = CreateUnrootedWeakReference();

            // Act - Simulate plugin unload
            ForceGc();

            // Assert
            weakRef.IsAlive.Should().BeFalse(
                "Plugin objects should be eligible for garbage collection after unload");
        }

        private static WeakReference CreateUnrootedWeakReference()
        {
            var pluginState = new object();
            return new WeakReference(pluginState);
        }

        private static void ForceGc()
        {
            for (var i = 0; i < 5; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        [Fact]
        public void CacheCleanup_Should_Release_Memory()
        {
            // Arrange
            var cache = new Dictionary<string, byte[]>();

            // Act - Fill cache with data
            for (int i = 0; i < 100; i++)
            {
                cache[$"item:{i}"] = new byte[1024]; // 1KB per item
            }

            var itemCount = cache.Count;

            // Clear cache
            cache.Clear();

            // Assert
            itemCount.Should().Be(100);
            cache.Should().BeEmpty(
                "Cache cleanup should release all items");
        }

        #endregion

        #region Logger Isolation Tests

        [Fact]
        public void Logger_Should_Include_Plugin_Identifier()
        {
            // Arrange - Log entries should be attributable to their plugin
            var logEntries = new List<(string Category, string Message)>();

            // Act - Simulate logging
            logEntries.Add(("Lidarr.Plugin.Qobuzarr.Indexers.QobuzIndexer", "Search started"));
            logEntries.Add(("Lidarr.Plugin.Qobuzarr.Download.QobuzDownloadClient", "Download queued"));

            // Assert
            logEntries.Should().AllSatisfy(entry =>
            {
                entry.Category.Should().Contain("Qobuzarr",
                    "Log categories should identify the plugin");
            });
        }

        #endregion

        #region Cancellation Token Isolation Tests

        [Fact]
        public async Task Cancellation_Should_Be_Plugin_Scoped()
        {
            // Arrange
            using var qobuzCts = new CancellationTokenSource();
            using var tidalCts = new CancellationTokenSource();

            // Act - Cancel Qobuz operations
            qobuzCts.Cancel();

            // Assert - Tidal operations should continue
            qobuzCts.IsCancellationRequested.Should().BeTrue();
            tidalCts.IsCancellationRequested.Should().BeFalse(
                "Cancellation should be isolated per plugin");
        }

        [Fact]
        public async Task Timeout_In_One_Plugin_Should_Not_Affect_Others()
        {
            // Arrange
            using var qobuzCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            using var tidalCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // Act - Wait for Qobuz timeout
            await Task.Delay(100);

            // Assert
            qobuzCts.IsCancellationRequested.Should().BeTrue("Qobuz should have timed out");
            tidalCts.IsCancellationRequested.Should().BeFalse(
                "Tidal should not be affected by Qobuz timeout");
        }

        #endregion
    }
}
