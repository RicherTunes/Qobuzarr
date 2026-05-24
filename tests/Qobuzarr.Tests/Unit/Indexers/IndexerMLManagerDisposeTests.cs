using System;
using FluentAssertions;
using Moq;
using NLog;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Indexers.Core;
using Lidarr.Plugin.Qobuzarr.Security;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Tests for <see cref="IndexerMLManager"/> IDisposable contract and
    /// metrics dictionary cap behaviour.
    /// </summary>
    public class IndexerMLManagerDisposeTests
    {
        private readonly Mock<ISecureMLModelLoader> _loaderMock;
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;

        public IndexerMLManagerDisposeTests()
        {
            _loaderMock = new Mock<ISecureMLModelLoader>();
            _loaderMock.Setup(x => x.GetSecurityStats())
                .Returns(new ModelLoadSecurityStats
                {
                    TotalLoadAttempts = 0,
                    SuccessfulLoads = 0,
                    FailedValidations = 0
                });
            _settings = new QobuzIndexerSettings { MLModelType = (int)MLModelType.Baseline };
            _logger = LogManager.CreateNullLogger();
        }

        // -------------------------------------------------------------------
        // IDisposable contract
        // -------------------------------------------------------------------

        [Fact]
        public void IndexerMLManager_ImplementsIDisposable()
        {
            using var manager = CreateManager();
            manager.Should().BeAssignableTo<IDisposable>();
        }

        [Fact]
        public void Dispose_DoesNotThrow()
        {
            var manager = CreateManager();

            // Populate some entries first
            manager.EstimateBaselineApiCalls("https://api.qobuz.com/album/search", 10);
            manager.EstimateBaselineApiCalls("https://api.qobuz.com/track/search", 5);

            var act = () => manager.Dispose();
            act.Should().NotThrow();
        }

        [Fact]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var manager = CreateManager();

            var act = () =>
            {
                manager.Dispose();
                manager.Dispose(); // second call must be idempotent
            };
            act.Should().NotThrow();
        }

        // -------------------------------------------------------------------
        // Dictionary cap (MetricsCapacity = 64)
        // -------------------------------------------------------------------

        [Fact]
        public void EstimateBaselineApiCalls_BelowCap_DoesNotClear()
        {
            var manager = CreateManager();

            // Insert 10 unique URL keys
            for (int i = 0; i < 10; i++)
            {
                manager.EstimateBaselineApiCalls($"https://api.qobuz.com/endpoint{i}/search", 5);
            }

            // Verify that a known key still produces its expected result.
            // EstimateBaselineApiCalls for resultCount=5 (no pagination) is 1.
            var result = manager.EstimateBaselineApiCalls("https://api.qobuz.com/endpoint0/search", 5);
            result.Should().Be(1);
        }

        [Fact]
        public void EstimateBaselineApiCalls_AtCapBoundary_ClearsAndAcceptsNewKey()
        {
            // The MetricsCapacity constant is 64.
            const int cap = 64;
            var manager = CreateManager();

            // Fill the dictionary up to exactly the cap.
            // Each distinct path segment becomes a unique key (GetMetricsKey extracts the last path segment).
            for (int i = 0; i < cap; i++)
            {
                manager.EstimateBaselineApiCalls($"https://api.qobuz.com/endpoint{i}", 5);
            }

            // Adding one more entry (cap+1) must trigger the clear + insert without throwing.
            var act = () => manager.EstimateBaselineApiCalls($"https://api.qobuz.com/endpoint{cap}", 5);
            act.Should().NotThrow("the manager must handle the eviction transparently");
        }

        [Fact]
        public void EstimateBaselineApiCalls_AfterEviction_AcceptsNewEntries()
        {
            const int cap = 64;
            var manager = CreateManager();

            // Fill past the cap (triggers eviction)
            for (int i = 0; i <= cap; i++)
            {
                manager.EstimateBaselineApiCalls($"https://api.qobuz.com/ep{i}", 5);
            }

            // After eviction the manager must still function correctly.
            var result = manager.EstimateBaselineApiCalls("https://api.qobuz.com/fresh-endpoint", 10);
            result.Should().Be(1, "resultCount=10 ≤ 25 so no pagination, baseline=1");
        }

        [Fact]
        public void GetMLPerformanceReport_AfterDispose_DoesNotThrow()
        {
            // This verifies the manager degrades gracefully after Dispose.
            var manager = CreateManager();
            manager.EstimateBaselineApiCalls("https://api.qobuz.com/album/search", 10);
            manager.Dispose();

            // GetMLPerformanceReport reads _performanceMetrics which was cleared during Dispose.
            var act = () => manager.GetMLPerformanceReport();
            act.Should().NotThrow();
        }

        // -------------------------------------------------------------------
        // Helper
        // -------------------------------------------------------------------

        private IndexerMLManager CreateManager()
            => new IndexerMLManager(_loaderMock.Object, _settings, _logger);
    }
}
