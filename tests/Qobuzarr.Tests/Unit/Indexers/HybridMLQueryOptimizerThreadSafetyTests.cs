using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using NLog;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// HybridMLQueryOptimizer keeps prediction statistics (_statistics dictionary + _totalPredictions /
    /// _baselineUsed / _personalUsed / _hybridUsed counters) in shared mutable fields. A single optimizer
    /// instance is cached by QobuzIndexer (Lazy&lt;IIndexerMLManager&gt;) and PredictComplexity runs on the
    /// indexer search path, which Lidarr can invoke concurrently (RSS sync + interactive/full-library search).
    /// Its polymorphic twin CompiledMLQueryOptimizer guards the identical mutations with lock(_metricsLock);
    /// Hybrid declared the same _metricsLock field but never used it. Unlocked, concurrent callers lose
    /// read-modify-write increments and GetStatistics' `new Dictionary(_statistics)` copy can race the
    /// mutations. This test pins the thread-safety contract.
    /// </summary>
    public class HybridMLQueryOptimizerThreadSafetyTests
    {
        private static IPatternLearningEngine FixedEngine(QueryComplexity complexity, double confidence)
        {
            var mock = new Mock<IPatternLearningEngine>();
            mock.Setup(m => m.PredictComplexity(It.IsAny<string>(), It.IsAny<string>())).Returns(complexity);
            mock.Setup(m => m.GetConfidenceScore(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QueryComplexity>())).Returns(confidence);
            mock.Setup(m => m.GetStatistics()).Returns(new PatternStatistics
            {
                PatternDistribution = new Dictionary<QueryComplexity, int>(),
                HybridStatistics = new Dictionary<string, object>()
            });
            return mock.Object;
        }

        [Fact]
        public async Task PredictComplexity_UnderConcurrentLoad_CountsEveryCallAndNeverThrows()
        {
            var logger = new Mock<Logger>().Object;
            var baseline = FixedEngine(QueryComplexity.Simple, 0.9);
            var personal = FixedEngine(QueryComplexity.Simple, 0.9);
            var optimizer = new HybridMLQueryOptimizer(logger, baseline, personal);

            const int writers = 16;
            const int iterations = 10_000;
            var readerExceptions = new ConcurrentQueue<Exception>();
            using var stop = new CancellationTokenSource();

            // Readers exercise GetStatistics' `new Dictionary(_statistics)` copy while writers mutate the dictionary.
            var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    try { optimizer.GetStatistics(); }
                    catch (Exception ex) { readerExceptions.Enqueue(ex); }
                }
            })).ToArray();

            var writerTasks = Enumerable.Range(0, writers).Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    optimizer.PredictComplexity("Artist", "Album");
                }
            })).ToArray();

            await Task.WhenAll(writerTasks);
            stop.Cancel();
            await Task.WhenAll(readers);

            var stats = optimizer.GetStatistics();

            readerExceptions.Should().BeEmpty(
                "copying the shared statistics dictionary must not race concurrent PredictComplexity mutations");
            stats.TotalPredictions.Should().Be(writers * iterations,
                "every concurrent PredictComplexity call must be counted exactly once (no lost read-modify-write)");
            stats.PatternDistribution.Values.Sum().Should().Be(writers * iterations,
                "the per-complexity tallies must sum to the total with no torn dictionary updates");
        }
    }
}
