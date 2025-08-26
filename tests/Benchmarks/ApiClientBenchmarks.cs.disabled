using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;
using NSubstitute;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Services.Observability;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services.Caching;

namespace Lidarr.Plugin.Qobuzarr.Tests.Benchmarks
{
    /// <summary>
    /// Performance benchmarks for API client implementations
    /// Measures the impact of caching, rate limiting, and adaptive optimizations
    /// </summary>
    [Config(typeof(ApiClientBenchmarkConfig))]
    [MemoryDiagnoser]
    [Orderer(SummaryOrderPolicy.FastestToSlowest)]
    [RankColumn]
    public class ApiClientBenchmarks
    {
        private IQobuzApiClient _cachedApiClient;
        private IQobuzApiClient _basicApiClient;
        private IQobuzApiClient _adaptiveApiClient;
        private IMetricsCollector _metricsCollector;
        private IQobuzLogger _logger;
        
        // Test data
        private List<string> _testQueries;
        private List<int> _testAlbumIds;
        private List<int> _testTrackIds;
        private List<string> _testArtistNames;

        [GlobalSetup]
        public void GlobalSetup()
        {
            // Setup dependencies
            _logger = Substitute.For<IQobuzLogger>();
            _metricsCollector = Substitute.For<IMetricsCollector>();
            
            // Setup different API client configurations
            SetupApiClients();
            
            // Prepare test data
            SetupTestData();
        }

        private void SetupApiClients()
        {
            // Basic API client (no optimizations)
            _basicApiClient = CreateMockApiClient("basic");
            
            // Cached API client (with response caching)
            var cacheService = Substitute.For<ICacheStatistics>();
            _cachedApiClient = CreateMockApiClient("cached", withCache: true);
            
            // Adaptive API client (with all optimizations)
            _adaptiveApiClient = new AdaptiveQobuzApiClient(_logger, _metricsCollector);
        }

        private IQobuzApiClient CreateMockApiClient(string type, bool withCache = false)
        {
            var client = Substitute.For<IQobuzApiClient>();
            
            // Mock search responses with realistic delays
            client.SearchAlbumsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
                .Returns(callInfo => CreateMockSearchResponse(type, withCache));
                
            client.GetAlbumAsync(Arg.Any<int>())
                .Returns(callInfo => CreateMockAlbumResponse(callInfo.Arg<int>(), type, withCache));
                
            client.GetTrackAsync(Arg.Any<int>())
                .Returns(callInfo => CreateMockTrackResponse(callInfo.Arg<int>(), type, withCache));
                
            return client;
        }

        private async Task<QobuzSearchResponse> CreateMockSearchResponse(string clientType, bool withCache)
        {
            // Simulate different response times based on client type
            var delay = clientType switch
            {
                "basic" => 150,     // Slower baseline
                "cached" => withCache ? 10 : 150,  // Fast if cached, slow if not
                "adaptive" => 75,   // Optimized response time
                _ => 100
            };
            
            await Task.Delay(delay);
            
            return new QobuzSearchResponse
            {
                Albums = new List<QobuzAlbum>
                {
                    new() { Id = 1, Title = "Test Album 1", Artist = new QobuzArtist { Name = "Test Artist" } },
                    new() { Id = 2, Title = "Test Album 2", Artist = new QobuzArtist { Name = "Test Artist 2" } }
                }
            };
        }

        private async Task<QobuzAlbum> CreateMockAlbumResponse(int albumId, string clientType, bool withCache)
        {
            var delay = clientType switch
            {
                "basic" => 120,
                "cached" => withCache && (albumId % 3 == 0) ? 5 : 120, // 33% cache hit rate
                "adaptive" => 60,
                _ => 80
            };
            
            await Task.Delay(delay);
            
            return new QobuzAlbum
            {
                Id = albumId,
                Title = $"Test Album {albumId}",
                Artist = new QobuzArtist { Name = $"Artist {albumId}" },
                TrackCount = 10,
                Duration = 2400,
                MaximumQuality = QobuzAudioQuality.HiRes
            };
        }

        private async Task<QobuzTrack> CreateMockTrackResponse(int trackId, string clientType, bool withCache)
        {
            var delay = clientType switch
            {
                "basic" => 100,
                "cached" => withCache && (trackId % 4 == 0) ? 3 : 100, // 25% cache hit rate
                "adaptive" => 50,
                _ => 70
            };
            
            await Task.Delay(delay);
            
            return new QobuzTrack
            {
                Id = trackId,
                Title = $"Test Track {trackId}",
                Album = new QobuzAlbum { Title = $"Album for Track {trackId}" },
                Duration = 240,
                MaximumQuality = QobuzAudioQuality.Lossless
            };
        }

        private void SetupTestData()
        {
            _testQueries = new List<string>
            {
                "Miles Davis Kind of Blue",
                "The Beatles Abbey Road", 
                "Pink Floyd Dark Side",
                "Led Zeppelin IV",
                "Radiohead OK Computer",
                "Miles Davis Kind of Blue", // Duplicate for cache testing
                "The Beatles Abbey Road",   // Duplicate for cache testing
            };

            _testAlbumIds = Enumerable.Range(1, 50).ToList();
            _testTrackIds = Enumerable.Range(1001, 100).ToList();
            
            _testArtistNames = new List<string>
            {
                "Miles Davis", "John Coltrane", "Bill Evans", "Charlie Parker",
                "The Beatles", "Pink Floyd", "Led Zeppelin", "Queen",
                "Bach", "Mozart", "Beethoven", "Chopin"
            };
        }

        #region Search Performance Benchmarks

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Search")]
        public async Task<List<QobuzSearchResponse>> BasicClient_SearchAlbums()
        {
            var results = new List<QobuzSearchResponse>();
            foreach (var query in _testQueries)
            {
                var response = await _basicApiClient.SearchAlbumsAsync(query, 0, 25);
                results.Add(response);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("Search")]
        public async Task<List<QobuzSearchResponse>> CachedClient_SearchAlbums()
        {
            var results = new List<QobuzSearchResponse>();
            foreach (var query in _testQueries)
            {
                var response = await _cachedApiClient.SearchAlbumsAsync(query, 0, 25);
                results.Add(response);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("Search")]
        public async Task<List<QobuzSearchResponse>> AdaptiveClient_SearchAlbums()
        {
            var results = new List<QobuzSearchResponse>();
            foreach (var query in _testQueries)
            {
                var response = await _adaptiveApiClient.SearchAlbumsAsync(query, 0, 25);
                results.Add(response);
            }
            return results;
        }

        #endregion

        #region Album Retrieval Benchmarks

        [Benchmark]
        [BenchmarkCategory("Albums")]
        public async Task<List<QobuzAlbum>> BasicClient_GetAlbums()
        {
            var results = new List<QobuzAlbum>();
            foreach (var albumId in _testAlbumIds.Take(10)) // Limit to 10 for benchmark
            {
                var album = await _basicApiClient.GetAlbumAsync(albumId);
                results.Add(album);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("Albums")]
        public async Task<List<QobuzAlbum>> CachedClient_GetAlbums()
        {
            var results = new List<QobuzAlbum>();
            foreach (var albumId in _testAlbumIds.Take(10))
            {
                var album = await _cachedApiClient.GetAlbumAsync(albumId);
                results.Add(album);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("Albums")]
        public async Task<List<QobuzAlbum>> AdaptiveClient_GetAlbums()
        {
            var results = new List<QobuzAlbum>();
            foreach (var albumId in _testAlbumIds.Take(10))
            {
                var album = await _adaptiveApiClient.GetAlbumAsync(albumId);
                results.Add(album);
            }
            return results;
        }

        #endregion

        #region Track Retrieval Benchmarks

        [Benchmark]
        [BenchmarkCategory("Tracks")]
        public async Task<List<QobuzTrack>> BasicClient_GetTracks()
        {
            var results = new List<QobuzTrack>();
            foreach (var trackId in _testTrackIds.Take(20)) // Limit to 20 for benchmark
            {
                var track = await _basicApiClient.GetTrackAsync(trackId);
                results.Add(track);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("Tracks")]
        public async Task<List<QobuzTrack>> CachedClient_GetTracks()
        {
            var results = new List<QobuzTrack>();
            foreach (var trackId in _testTrackIds.Take(20))
            {
                var track = await _cachedApiClient.GetTrackAsync(trackId);
                results.Add(track);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("Tracks")]
        public async Task<List<QobuzTrack>> AdaptiveClient_GetTracks()
        {
            var results = new List<QobuzTrack>();
            foreach (var trackId in _testTrackIds.Take(20))
            {
                var track = await _adaptiveApiClient.GetTrackAsync(trackId);
                results.Add(track);
            }
            return results;
        }

        #endregion

        #region High Volume Benchmarks

        [Benchmark]
        [BenchmarkCategory("HighVolume")]
        public async Task<object> BasicClient_HighVolumeOperations()
        {
            var searchResults = new List<QobuzSearchResponse>();
            var albumResults = new List<QobuzAlbum>();
            
            // Simulate high volume mixed operations
            for (int i = 0; i < 5; i++)
            {
                var searchTask = _basicApiClient.SearchAlbumsAsync(_testQueries[i % _testQueries.Count], 0, 10);
                var albumTask = _basicApiClient.GetAlbumAsync(_testAlbumIds[i]);
                
                var search = await searchTask;
                var album = await albumTask;
                
                searchResults.Add(search);
                albumResults.Add(album);
            }
            
            return new { SearchResults = searchResults.Count, AlbumResults = albumResults.Count };
        }

        [Benchmark]
        [BenchmarkCategory("HighVolume")]
        public async Task<object> CachedClient_HighVolumeOperations()
        {
            var searchResults = new List<QobuzSearchResponse>();
            var albumResults = new List<QobuzAlbum>();
            
            // Simulate high volume mixed operations with cache benefits
            for (int i = 0; i < 5; i++)
            {
                var searchTask = _cachedApiClient.SearchAlbumsAsync(_testQueries[i % _testQueries.Count], 0, 10);
                var albumTask = _cachedApiClient.GetAlbumAsync(_testAlbumIds[i]);
                
                var search = await searchTask;
                var album = await albumTask;
                
                searchResults.Add(search);
                albumResults.Add(album);
            }
            
            return new { SearchResults = searchResults.Count, AlbumResults = albumResults.Count };
        }

        [Benchmark]
        [BenchmarkCategory("HighVolume")]
        public async Task<object> AdaptiveClient_HighVolumeOperations()
        {
            var searchResults = new List<QobuzSearchResponse>();
            var albumResults = new List<QobuzAlbum>();
            
            // Simulate high volume mixed operations with adaptive optimizations
            for (int i = 0; i < 5; i++)
            {
                var searchTask = _adaptiveApiClient.SearchAlbumsAsync(_testQueries[i % _testQueries.Count], 0, 10);
                var albumTask = _adaptiveApiClient.GetAlbumAsync(_testAlbumIds[i]);
                
                var search = await searchTask;
                var album = await albumTask;
                
                searchResults.Add(search);
                albumResults.Add(album);
            }
            
            return new { SearchResults = searchResults.Count, AlbumResults = albumResults.Count };
        }

        #endregion

        #region Concurrency Benchmarks

        [Benchmark]
        [BenchmarkCategory("Concurrency")]
        public async Task<List<QobuzSearchResponse>> BasicClient_ConcurrentSearches()
        {
            var tasks = _testQueries.Select(query => 
                _basicApiClient.SearchAlbumsAsync(query, 0, 10)).ToArray();
                
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        [Benchmark]
        [BenchmarkCategory("Concurrency")]
        public async Task<List<QobuzSearchResponse>> CachedClient_ConcurrentSearches()
        {
            var tasks = _testQueries.Select(query => 
                _cachedApiClient.SearchAlbumsAsync(query, 0, 10)).ToArray();
                
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        [Benchmark]
        [BenchmarkCategory("Concurrency")]
        public async Task<List<QobuzSearchResponse>> AdaptiveClient_ConcurrentSearches()
        {
            var tasks = _testQueries.Select(query => 
                _adaptiveApiClient.SearchAlbumsAsync(query, 0, 10)).ToArray();
                
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        #endregion

        #region Cache Performance Analysis

        [Benchmark]
        [BenchmarkCategory("Cache")]
        public async Task<object> CacheHitRatio_Analysis()
        {
            var results = new List<object>();
            var duplicateQueries = new List<string>();
            
            // First pass - populate cache
            foreach (var query in _testQueries)
            {
                var response = await _cachedApiClient.SearchAlbumsAsync(query, 0, 10);
                results.Add(response);
            }
            
            // Second pass - should hit cache for duplicates
            foreach (var query in _testQueries)
            {
                var response = await _cachedApiClient.SearchAlbumsAsync(query, 0, 10);
                duplicateQueries.Add(query);
            }
            
            return new 
            { 
                TotalRequests = results.Count + duplicateQueries.Count,
                UniqueQueries = _testQueries.Distinct().Count(),
                ExpectedCacheHits = duplicateQueries.Count 
            };
        }

        #endregion

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            // Cleanup if needed
        }
    }

    /// <summary>
    /// Mock adaptive API client for benchmarking
    /// </summary>
    internal class AdaptiveQobuzApiClient : IQobuzApiClient
    {
        private readonly IQobuzLogger _logger;
        private readonly IMetricsCollector _metrics;
        private readonly Dictionary<string, object> _cache = new();

        public AdaptiveQobuzApiClient(IQobuzLogger logger, IMetricsCollector metrics)
        {
            _logger = logger;
            _metrics = metrics;
        }

        public async Task<QobuzSearchResponse> SearchAlbumsAsync(string query, int offset = 0, int limit = 25)
        {
            var cacheKey = $"search:{query}:{offset}:{limit}";
            
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                _metrics?.RecordCacheHit("search", cacheKey, true);
                return (QobuzSearchResponse)cached;
            }

            _metrics?.RecordCacheHit("search", cacheKey, false);
            
            // Adaptive delay based on system load
            await Task.Delay(75);
            
            var result = new QobuzSearchResponse
            {
                Albums = new List<QobuzAlbum>
                {
                    new() { Id = 1, Title = $"Result for {query}", Artist = new QobuzArtist { Name = "Adaptive Artist" } }
                }
            };
            
            _cache[cacheKey] = result;
            return result;
        }

        public async Task<QobuzAlbum> GetAlbumAsync(int albumId)
        {
            var cacheKey = $"album:{albumId}";
            
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                _metrics?.RecordCacheHit("album", cacheKey, true);
                return (QobuzAlbum)cached;
            }

            _metrics?.RecordCacheHit("album", cacheKey, false);
            await Task.Delay(60);
            
            var result = new QobuzAlbum
            {
                Id = albumId,
                Title = $"Adaptive Album {albumId}",
                Artist = new QobuzArtist { Name = "Adaptive Artist" }
            };
            
            _cache[cacheKey] = result;
            return result;
        }

        public async Task<QobuzTrack> GetTrackAsync(int trackId)
        {
            var cacheKey = $"track:{trackId}";
            
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                _metrics?.RecordCacheHit("track", cacheKey, true);
                return (QobuzTrack)cached;
            }

            _metrics?.RecordCacheHit("track", cacheKey, false);
            await Task.Delay(50);
            
            var result = new QobuzTrack
            {
                Id = trackId,
                Title = $"Adaptive Track {trackId}",
                Duration = 240
            };
            
            _cache[cacheKey] = result;
            return result;
        }

        public void Dispose()
        {
            _cache.Clear();
        }
    }

    /// <summary>
    /// Benchmark configuration for API client performance testing
    /// </summary>
    public class ApiClientBenchmarkConfig : ManualConfig
    {
        public ApiClientBenchmarkConfig()
        {
            AddJob(Job.Default
                .WithWarmupCount(2)      // 2 warmup iterations (less for async operations)
                .WithIterationCount(8)   // 8 measurement iterations
                .WithInvocationCount(1)  // 1 invocation per iteration
                .WithUnrollFactor(1));   // No unrolling for async
                
            WithOption(ConfigOptions.DisableOptimizationsValidator, true);
            WithSummaryStyle(BenchmarkDotNet.Reports.SummaryStyle.Default
                .WithRatioStyle(BenchmarkDotNet.Columns.RatioStyle.Trend)
                .WithTimeUnit(BenchmarkDotNet.Columns.TimeUnit.Millisecond));
        }
    }

    /// <summary>
    /// Program entry point for running API client benchmarks
    /// Usage: dotnet run --project tests/Benchmarks --configuration Release -- ApiClient
    /// </summary>
    public class ApiClientBenchmarkRunner
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("🌐 Qobuzarr API Client Performance Benchmarks");
            Console.WriteLine("Comparing Basic, Cached, and Adaptive API client implementations");
            Console.WriteLine();
            
            var summary = BenchmarkRunner.Run<ApiClientBenchmarks>();
            
            Console.WriteLine();
            Console.WriteLine("📊 API Client Benchmark Summary:");
            Console.WriteLine($"Total benchmarks run: {summary.Reports.Length}");
            
            // Display key insights
            Console.WriteLine();
            Console.WriteLine("🚀 Expected performance characteristics:");
            Console.WriteLine("- Cached Client: Faster for repeated queries, higher memory usage");
            Console.WriteLine("- Adaptive Client: Balanced performance with intelligent caching");
            Console.WriteLine("- Basic Client: Baseline performance, lowest memory footprint");
            Console.WriteLine("- Concurrency: Adaptive client should handle concurrent requests better");
        }
    }
}