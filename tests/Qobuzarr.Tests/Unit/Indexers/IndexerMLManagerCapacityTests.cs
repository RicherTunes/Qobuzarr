using System.Linq;
using System.Reflection;
using FluentAssertions;
using NLog;
using Moq;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Indexers.Core;
using Lidarr.Plugin.Qobuzarr.Security;

namespace Qobuzarr.Tests.Unit.Indexers;

/// <summary>
/// Characterization tests for IndexerMLManager's bounded-capacity behavior on
/// `_performanceMetrics`. The dictionary is capped at <c>MetricsCapacity = 64</c>;
/// previously enforced by a hand-rolled "Count &gt;= cap → Clear()" block at the
/// call site. After the wave-18D-T5 refactor the cap is enforced by Common's
/// <c>BoundedConcurrentDictionary</c>; behavior must remain identical from the
/// caller's perspective: after inserting more than `cap` distinct query URLs,
/// `queriesOptimized` (reported by <c>GetMLPerformanceMetrics</c>) must stay
/// ≤ cap.
/// </summary>
public class IndexerMLManagerCapacityTests
{
    private const int MetricsCapacity = 64;

    [Fact]
    public void EstimateBaselineApiCalls_BeyondCapacity_DictionaryStaysAtOrBelowCap()
    {
        var loaderMock = new Mock<ISecureMLModelLoader>();
        loaderMock.Setup(x => x.GetSecurityStats()).Returns(new ModelLoadSecurityStats());
        var settings = new QobuzIndexerSettings { MLModelType = (int)MLModelType.Baseline };
        var logger = LogManager.CreateNullLogger();

        var manager = new IndexerMLManager(loaderMock.Object, settings, logger);

        // Insert MetricsCapacity + 20 distinct keys. GetMetricsKey extracts the URL's
        // last path segment, so unique trailing tokens guarantee distinct keys.
        for (int i = 0; i < MetricsCapacity + 20; i++)
        {
            manager.EstimateBaselineApiCalls($"https://www.qobuz.com/api.json/0.2/track/get/{i}", 25);
        }

        var metricsObj = manager.GetMLPerformanceMetrics();
        var queriesOptimizedProp = metricsObj.GetType().GetProperty("queriesOptimized");
        queriesOptimizedProp.Should().NotBeNull(
            "GetMLPerformanceMetrics is documented to return an anonymous object with a queriesOptimized field");

        int queriesOptimized = (int)queriesOptimizedProp!.GetValue(metricsObj)!;

        // Cap is 64; overflowing inserts must not blow past it. Allow the post-cap
        // "1 new entry after a Clear" to also be valid (BoundedConcurrentDictionary
        // clears all then admits the next insert) — the invariant is no unbounded growth.
        queriesOptimized.Should().BeLessThanOrEqualTo(MetricsCapacity);
    }

    [Fact]
    public void EstimateBaselineApiCalls_DistinctUrls_AllUnderCap_PreservesAllEntries()
    {
        var loaderMock = new Mock<ISecureMLModelLoader>();
        loaderMock.Setup(x => x.GetSecurityStats()).Returns(new ModelLoadSecurityStats());
        var settings = new QobuzIndexerSettings { MLModelType = (int)MLModelType.Baseline };
        var logger = LogManager.CreateNullLogger();

        var manager = new IndexerMLManager(loaderMock.Object, settings, logger);

        // Insert exactly half the capacity — no overflow expected.
        const int targetEntries = MetricsCapacity / 2;
        for (int i = 0; i < targetEntries; i++)
        {
            manager.EstimateBaselineApiCalls($"https://www.qobuz.com/api.json/0.2/track/get/{i}", 25);
        }

        var metricsObj = manager.GetMLPerformanceMetrics();
        int queriesOptimized = (int)metricsObj.GetType().GetProperty("queriesOptimized")!.GetValue(metricsObj)!;

        queriesOptimized.Should().Be(targetEntries);
    }
}
