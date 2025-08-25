using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;
using NSubstitute;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Tests.Benchmarks
{
    /// <summary>
    /// Performance benchmarks comparing consolidated QobuzQualityManager vs original QobuzQualityService
    /// Measures the performance improvements achieved through service consolidation
    /// </summary>
    [Config(typeof(BenchmarkConfig))]
    [MemoryDiagnoser]
    [Orderer(SummaryOrderPolicy.FastestToSlowest)]
    [RankColumn]
    public class QualityServiceBenchmarks
    {
        private IQobuzQualityManager _consolidatedQualityManager;
        private QobuzQualityService _originalQualityService;
        private IQobuzLogger _logger;
        
        // Test data
        private List<QobuzAudioQuality> _testQualities;
        private List<string> _testQualityStrings;
        private List<QobuzTrack> _testTracks;

        [GlobalSetup]
        public void GlobalSetup()
        {
            // Setup dependencies
            _logger = Substitute.For<IQobuzLogger>();
            
            // Setup consolidated quality manager (new implementation)
            _consolidatedQualityManager = new QobuzQualityManager(_logger);
            
            // Setup original quality service (legacy implementation)
            _originalQualityService = new QobuzQualityService(_logger);
            
            // Prepare test data
            SetupTestData();
        }

        private void SetupTestData()
        {
            // Create varied test data representing real-world usage patterns
            _testQualities = new List<QobuzAudioQuality>
            {
                QobuzAudioQuality.Lossless,
                QobuzAudioQuality.HiRes,
                QobuzAudioQuality.Mp3320,
                QobuzAudioQuality.Mp3320, // Duplicates for cache testing
                QobuzAudioQuality.Lossless,
                QobuzAudioQuality.HiRes
            };

            _testQualityStrings = new List<string>
            {
                "FLAC 16-Bit/44.1kHz",
                "FLAC 24-Bit/96kHz", 
                "MP3 320 kbps",
                "FLAC 24-Bit/192kHz",
                "FLAC 16-Bit/44.1kHz", // Cache hit scenario
                "MP3 320 kbps" // Cache hit scenario
            };

            _testTracks = new List<QobuzTrack>();
            for (int i = 0; i < 1000; i++)
            {
                _testTracks.Add(new QobuzTrack
                {
                    Id = i,
                    Title = $"Test Track {i}",
                    MaximumQuality = _testQualities[i % _testQualities.Count]
                });
            }
        }

        #region Quality Detection Benchmarks

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("QualityDetection")]
        public async Task<QobuzAudioQuality> OriginalService_DetectQuality_Single()
        {
            var quality = await _originalQualityService.DetectQualityAsync(_testQualityStrings[0]);
            return quality;
        }

        [Benchmark]
        [BenchmarkCategory("QualityDetection")]
        public async Task<QobuzAudioQuality> ConsolidatedManager_DetectQuality_Single()
        {
            var quality = await _consolidatedQualityManager.DetectQualityAsync(_testQualityStrings[0]);
            return quality;
        }

        [Benchmark]
        [BenchmarkCategory("QualityDetection")]
        public async Task<List<QobuzAudioQuality>> OriginalService_DetectQuality_Batch()
        {
            var results = new List<QobuzAudioQuality>();
            foreach (var qualityString in _testQualityStrings)
            {
                var quality = await _originalQualityService.DetectQualityAsync(qualityString);
                results.Add(quality);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("QualityDetection")]
        public async Task<List<QobuzAudioQuality>> ConsolidatedManager_DetectQuality_Batch()
        {
            var results = new List<QobuzAudioQuality>();
            foreach (var qualityString in _testQualityStrings)
            {
                var quality = await _consolidatedQualityManager.DetectQualityAsync(qualityString);
                results.Add(quality);
            }
            return results;
        }

        #endregion

        #region Quality Mapping Benchmarks

        [Benchmark]
        [BenchmarkCategory("QualityMapping")]
        public int OriginalService_MapToLidarrQuality_Single()
        {
            return _originalQualityService.MapToLidarrQuality(_testQualities[0]);
        }

        [Benchmark]
        [BenchmarkCategory("QualityMapping")]
        public int ConsolidatedManager_MapToLidarrQuality_Single()
        {
            return _consolidatedQualityManager.MapToLidarrQuality(_testQualities[0]);
        }

        [Benchmark]
        [BenchmarkCategory("QualityMapping")]
        public List<int> OriginalService_MapToLidarrQuality_Batch()
        {
            var results = new List<int>();
            foreach (var quality in _testQualities)
            {
                var mapped = _originalQualityService.MapToLidarrQuality(quality);
                results.Add(mapped);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("QualityMapping")]
        public List<int> ConsolidatedManager_MapToLidarrQuality_Batch()
        {
            var results = new List<int>();
            foreach (var quality in _testQualities)
            {
                var mapped = _consolidatedQualityManager.MapToLidarrQuality(quality);
                results.Add(mapped);
            }
            return results;
        }

        #endregion

        #region Quality Fallback Benchmarks

        [Benchmark]
        [BenchmarkCategory("QualityFallback")]
        public async Task<QobuzAudioQuality> OriginalService_GetFallbackQuality_Single()
        {
            var fallback = await _originalQualityService.GetFallbackQualityAsync(_testQualities[0], "track_unavailable");
            return fallback;
        }

        [Benchmark]
        [BenchmarkCategory("QualityFallback")]
        public async Task<QobuzAudioQuality> ConsolidatedManager_GetFallbackQuality_Single()
        {
            var fallback = await _consolidatedQualityManager.GetFallbackQualityAsync(_testQualities[0], "track_unavailable");
            return fallback;
        }

        [Benchmark]
        [BenchmarkCategory("QualityFallback")]
        public async Task<List<QobuzAudioQuality>> OriginalService_GetFallbackQuality_Batch()
        {
            var results = new List<QobuzAudioQuality>();
            foreach (var quality in _testQualities)
            {
                var fallback = await _originalQualityService.GetFallbackQualityAsync(quality, "track_unavailable");
                results.Add(fallback);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("QualityFallback")]
        public async Task<List<QobuzAudioQuality>> ConsolidatedManager_GetFallbackQuality_Batch()
        {
            var results = new List<QobuzAudioQuality>();
            foreach (var quality in _testQualities)
            {
                var fallback = await _consolidatedQualityManager.GetFallbackQualityAsync(quality, "track_unavailable");
                results.Add(fallback);
            }
            return results;
        }

        #endregion

        #region High Volume Benchmarks

        [Benchmark]
        [BenchmarkCategory("HighVolume")]
        public async Task<List<QobuzAudioQuality>> OriginalService_ProcessTracks_1000()
        {
            var results = new List<QobuzAudioQuality>();
            foreach (var track in _testTracks)
            {
                var quality = await _originalQualityService.DetectQualityAsync(track.MaximumQuality.ToString());
                results.Add(quality);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("HighVolume")]
        public async Task<List<QobuzAudioQuality>> ConsolidatedManager_ProcessTracks_1000()
        {
            var results = new List<QobuzAudioQuality>();
            foreach (var track in _testTracks)
            {
                var quality = await _consolidatedQualityManager.DetectQualityAsync(track.MaximumQuality.ToString());
                results.Add(quality);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("HighVolume")]
        public async Task<List<int>> OriginalService_MapTracks_1000()
        {
            var results = new List<int>();
            foreach (var track in _testTracks)
            {
                var mapped = _originalQualityService.MapToLidarrQuality(track.MaximumQuality);
                results.Add(mapped);
            }
            await Task.CompletedTask; // Make async for comparison
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("HighVolume")]
        public async Task<List<int>> ConsolidatedManager_MapTracks_1000()
        {
            var results = new List<int>();
            foreach (var track in _testTracks)
            {
                var mapped = _consolidatedQualityManager.MapToLidarrQuality(track.MaximumQuality);
                results.Add(mapped);
            }
            await Task.CompletedTask; // Make async for comparison
            return results;
        }

        #endregion

        #region Memory Efficiency Benchmarks

        [Benchmark]
        [BenchmarkCategory("Memory")]
        public async Task<object> OriginalService_MemoryUsage_Simulation()
        {
            var results = new List<object>();
            
            // Simulate memory-intensive operations
            for (int i = 0; i < 100; i++)
            {
                var quality = await _originalQualityService.DetectQualityAsync(_testQualityStrings[i % _testQualityStrings.Count]);
                var mapped = _originalQualityService.MapToLidarrQuality(quality);
                var fallback = await _originalQualityService.GetFallbackQualityAsync(quality, "test");
                
                results.Add(new { quality, mapped, fallback });
            }
            
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("Memory")]
        public async Task<object> ConsolidatedManager_MemoryUsage_Simulation()
        {
            var results = new List<object>();
            
            // Simulate memory-intensive operations
            for (int i = 0; i < 100; i++)
            {
                var quality = await _consolidatedQualityManager.DetectQualityAsync(_testQualityStrings[i % _testQualityStrings.Count]);
                var mapped = _consolidatedQualityManager.MapToLidarrQuality(quality);
                var fallback = await _consolidatedQualityManager.GetFallbackQualityAsync(quality, "test");
                
                results.Add(new { quality, mapped, fallback });
            }
            
            return results;
        }

        #endregion

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            // Cleanup resources if needed
            _consolidatedQualityManager?.Dispose();
        }
    }

    /// <summary>
    /// Benchmark configuration for consistent and reliable performance measurements
    /// </summary>
    public class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            AddJob(Job.Default
                .WithWarmupCount(3)      // 3 warmup iterations
                .WithIterationCount(10)  // 10 measurement iterations
                .WithInvocationCount(1)  // 1 invocation per iteration
                .WithUnrollFactor(1));   // No unrolling
                
            WithOption(ConfigOptions.DisableOptimizationsValidator, true);
            WithSummaryStyle(BenchmarkDotNet.Reports.SummaryStyle.Default.WithRatioStyle(BenchmarkDotNet.Columns.RatioStyle.Trend));
        }
    }

    /// <summary>
    /// Program entry point for running quality service benchmarks
    /// Usage: dotnet run --project tests/Benchmarks --configuration Release
    /// </summary>
    public class QualityServiceBenchmarkRunner
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("🏁 Qobuzarr Quality Service Performance Benchmarks");
            Console.WriteLine("Comparing consolidated QobuzQualityManager vs original QobuzQualityService");
            Console.WriteLine();
            
            var summary = BenchmarkRunner.Run<QualityServiceBenchmarks>();
            
            Console.WriteLine();
            Console.WriteLine("📊 Benchmark Summary:");
            Console.WriteLine($"Total benchmarks run: {summary.Reports.Length}");
            Console.WriteLine($"Fastest method: {summary.Reports.OrderBy(r => r.ResultStatistics?.Mean ?? double.MaxValue).FirstOrDefault()?.BenchmarkCase?.DisplayInfo}");
            
            // Display key performance improvements
            Console.WriteLine();
            Console.WriteLine("🚀 Expected improvements with consolidated manager:");
            Console.WriteLine("- Reduced memory allocations through caching");
            Console.WriteLine("- Faster quality detection via optimized algorithms");
            Console.WriteLine("- Improved throughput for batch operations");
            Console.WriteLine("- Lower GC pressure in high-volume scenarios");
        }
    }
}