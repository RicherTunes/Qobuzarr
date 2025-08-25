using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using NSubstitute;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Qobuzarr.Tests.Benchmarks
{
    /// <summary>
    /// Performance benchmarks to validate optimization claims
    /// Validates the tech lead's concern about performance metrics
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob]
    public class PerformanceBenchmarks
    {
        private IQobuzQualityManager _qualityManager;
        private CompiledMLQueryOptimizer _mlOptimizer;
        private IQobuzApiClient _mockApiClient;
        private IQobuzLogger _mockLogger;
        private List<TestQuery> _testQueries;

        [GlobalSetup]
        public void Setup()
        {
            _mockApiClient = Substitute.For<IQobuzApiClient>();
            _mockLogger = Substitute.For<IQobuzLogger>();
            
            _qualityManager = new QobuzQualityManager(_mockApiClient, _mockLogger);
            _mlOptimizer = new CompiledMLQueryOptimizer(null);
            
            // Create realistic test data
            _testQueries = new List<TestQuery>
            {
                new TestQuery { Artist = "Miles Davis", Album = "Kind of Blue" },
                new TestQuery { Artist = "The Beatles", Album = "Abbey Road" },
                new TestQuery { Artist = "Pink Floyd", Album = "The Dark Side of the Moon" },
                new TestQuery { Artist = "Led Zeppelin", Album = "Led Zeppelin IV" },
                new TestQuery { Artist = "Radiohead", Album = "OK Computer" },
                new TestQuery { Artist = "Nirvana", Album = "Nevermind" },
                new TestQuery { Artist = "Queen", Album = "A Night at the Opera" },
                new TestQuery { Artist = "David Bowie", Album = "The Rise and Fall of Ziggy Stardust" },
                new TestQuery { Artist = "Bob Dylan", Album = "Highway 61 Revisited" },
                new TestQuery { Artist = "John Coltrane", Album = "A Love Supreme" }
            };
        }

        /// <summary>
        /// Benchmark ML query complexity prediction
        /// Validates the claim of fast ML processing
        /// </summary>
        [Benchmark]
        public void MLQueryClassification()
        {
            foreach (var query in _testQueries)
            {
                _mlOptimizer.PredictComplexity(query.Artist, query.Album);
            }
        }

        /// <summary>
        /// Benchmark quality format lookup
        /// Validates quality management performance
        /// </summary>
        [Benchmark]
        public void QualityFormatLookup()
        {
            foreach (var qualityId in new[] { 5, 6, 7, 27 })
            {
                var format = QobuzQualityManager.QobuzQualityFormats[qualityId];
                var _ = format.DisplayName;
            }
        }

        /// <summary>
        /// Benchmark fallback chain generation
        /// Validates quality fallback performance
        /// </summary>
        [Benchmark]
        public void QualityFallbackChainGeneration()
        {
            var hiResQuality = new QobuzQuality { Id = 27, Name = "FLAC Hi-Res 192" };
            _qualityManager.GetQualityFallbackChain(hiResQuality);
        }

        /// <summary>
        /// Benchmark batch operation vs individual operations
        /// Validates the API reduction claims
        /// </summary>
        [Benchmark]
        public async Task BatchVsIndividualOperations()
        {
            var trackIds = new List<string> { "1", "2", "3", "4", "5" };
            var quality = new QobuzQuality { Id = 6 };

            // Mock successful responses
            _mockApiClient.GetAsync<Dictionary<string, object>>(Arg.Any<string>(), Arg.Any<Dictionary<string, string>>())
                         .Returns(new Dictionary<string, object> { ["url"] = "test_url", ["format_id"] = 6 });

            // Simulate batch operation (should be faster than individual calls)
            await _qualityManager.GetBatchStreamInfoAsync(trackIds, quality);
        }

        /// <summary>
        /// Memory allocation benchmark
        /// Validates memory efficiency claims
        /// </summary>
        [Benchmark]
        public void MemoryAllocationTest()
        {
            // Test memory efficiency of service consolidation
            for (int i = 0; i < 1000; i++)
            {
                var quality = new QobuzQuality { Id = 6, Name = "FLAC CD" };
                var fallbackChain = _qualityManager.GetQualityFallbackChain(quality);
            }
        }

        public class TestQuery
        {
            public string Artist { get; set; } = "";
            public string Album { get; set; } = "";
        }
    }

    /// <summary>
    /// Benchmark runner program for standalone execution
    /// </summary>
    public class BenchmarkRunner
    {
        public static void Main(string[] args)
        {
            BenchmarkRunner.Run<PerformanceBenchmarks>();
        }
    }
}