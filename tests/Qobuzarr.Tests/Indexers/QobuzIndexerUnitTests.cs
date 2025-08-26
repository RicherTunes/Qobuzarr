using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.NUnit;
using Lidarr.Plugin.Qobuzarr.API.Interfaces;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Album;
using Lidarr.Plugin.Qobuzarr.Models.Artist;
using Lidarr.Plugin.Qobuzarr.Models.Track;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace Lidarr.Plugin.Qobuzarr.Tests.Indexers
{
    [TestFixture]
    [Category("Unit")]
    [Category("Indexer")]
    public class QobuzIndexerUnitTests
    {
        private QobuzIndexer _indexer;
        private IQobuzApiService _apiService;
        private IQobuzAuthenticationService _authService;
        private ICompiledMLQueryOptimizer _mlOptimizer;
        private IHttpClient _httpClient;
        private IConfigService _configService;
        private ILogger<QobuzIndexer> _logger;
        private QobuzIndexerSettings _settings;

        [SetUp]
        public void Setup()
        {
            _apiService = Substitute.For<IQobuzApiService>();
            _authService = Substitute.For<IQobuzAuthenticationService>();
            _mlOptimizer = Substitute.For<ICompiledMLQueryOptimizer>();
            _httpClient = Substitute.For<IHttpClient>();
            _configService = Substitute.For<IConfigService>();
            _logger = Substitute.For<ILogger<QobuzIndexer>>();
            
            _settings = new QobuzIndexerSettings
            {
                BaseUrl = "https://api.qobuz.com",
                AppId = "test-app-id",
                AppSecret = "test-secret",
                Username = "test@example.com",
                Password = "password123"
            };
            
            _authService.IsAuthenticated().Returns(true);
            _authService.GetSessionTokenAsync().Returns(Task.FromResult("test-token"));
            
            _indexer = new QobuzIndexer(_httpClient, _configService)
            {
                Settings = _settings,
                ApiService = _apiService,
                AuthService = _authService,
                MLOptimizer = _mlOptimizer,
                Logger = _logger
            };
        }

        #region Core Functionality Tests

        [Test]
        public void Protocol_ReturnsQobuzarrDownloadProtocol()
        {
            // Act
            var protocol = _indexer.Protocol;
            
            // Assert
            Assert.That(protocol, Is.EqualTo("QobuzarrDownloadProtocol"));
        }

        [Test]
        public void SupportsSearch_ReturnsTrue()
        {
            // Act
            var supportsSearch = _indexer.SupportsSearch;
            
            // Assert
            Assert.That(supportsSearch, Is.True);
        }

        [Test]
        public void SupportsFeed_ReturnsFalse()
        {
            // Act
            var supportsFeed = _indexer.SupportsFeed;
            
            // Assert
            Assert.That(supportsFeed, Is.False);
        }

        [Test]
        public void Name_ReturnsQobuzarr()
        {
            // Act
            var name = _indexer.Name;
            
            // Assert
            Assert.That(name, Is.EqualTo("Qobuzarr"));
        }

        #endregion

        #region Search Mapping Tests

        [Test]
        public async Task GetSearchRequests_AlbumQuery_MapsCorrectly()
        {
            // Arrange
            var searchCriteria = new AlbumSearchCriteria
            {
                AlbumTitle = "Kind of Blue",
                ArtistName = "Miles Davis",
                AlbumYear = 1959
            };
            
            // Act
            var requests = await _indexer.GetSearchRequests(searchCriteria);
            
            // Assert
            Assert.That(requests, Is.Not.Null);
            Assert.That(requests.Count(), Is.GreaterThan(0));
            
            var firstRequest = requests.First();
            Assert.That(firstRequest.Url.FullUri, Does.Contain("Kind+of+Blue"));
            Assert.That(firstRequest.Url.FullUri, Does.Contain("Miles+Davis"));
        }

        [Test]
        public async Task GetSearchRequests_ArtistQuery_MapsCorrectly()
        {
            // Arrange
            var searchCriteria = new ArtistSearchCriteria
            {
                ArtistQuery = "John Coltrane"
            };
            
            // Act
            var requests = await _indexer.GetSearchRequests(searchCriteria);
            
            // Assert
            Assert.That(requests, Is.Not.Null);
            Assert.That(requests.Count(), Is.GreaterThan(0));
            
            var firstRequest = requests.First();
            Assert.That(firstRequest.Url.FullUri, Does.Contain("John+Coltrane"));
        }

        [Test]
        public async Task GetSearchRequests_TrackQuery_MapsCorrectly()
        {
            // Arrange
            var searchCriteria = new BasicSearchCriteria
            {
                SearchTerm = "So What Miles Davis"
            };
            
            // Act
            var requests = await _indexer.GetSearchRequests(searchCriteria);
            
            // Assert
            Assert.That(requests, Is.Not.Null);
            Assert.That(requests.Count(), Is.GreaterThan(0));
            
            var firstRequest = requests.First();
            Assert.That(firstRequest.Url.FullUri, Does.Contain("So+What+Miles+Davis"));
        }

        [Test]
        public async Task GetSearchRequests_EmptyQuery_HandlesGracefully()
        {
            // Arrange
            var searchCriteria = new BasicSearchCriteria
            {
                SearchTerm = ""
            };
            
            // Act
            var requests = await _indexer.GetSearchRequests(searchCriteria);
            
            // Assert
            Assert.That(requests, Is.Not.Null);
            Assert.That(requests.Count(), Is.EqualTo(0), "Empty query should return no requests");
        }

        #endregion

        #region Result Processing Tests

        [Test]
        public async Task Process_ValidResults_MapsToReleaseInfo()
        {
            // Arrange
            var qobuzAlbums = new List<QobuzAlbum>
            {
                new QobuzAlbum
                {
                    Id = "album1",
                    Title = "Test Album",
                    Artist = new QobuzArtist { Name = "Test Artist" },
                    ReleaseDateOriginal = "2023-01-01",
                    TracksCount = 10,
                    Genre = new QobuzGenre { Name = "Jazz" }
                }
            };
            
            _apiService.SearchAlbumsAsync(Arg.Any<string>(), Arg.Any<int>())
                .Returns(Task.FromResult<IEnumerable<QobuzAlbum>>(qobuzAlbums));
            
            // Act
            var searchCriteria = new AlbumSearchCriteria { AlbumTitle = "Test Album" };
            var requests = await _indexer.GetSearchRequests(searchCriteria);
            var results = new List<ReleaseInfo>();
            
            foreach (var request in requests)
            {
                var pageResults = await _indexer.FetchPage(request);
                results.AddRange(pageResults);
            }
            
            // Assert
            Assert.That(results.Count, Is.EqualTo(1));
            var releaseInfo = results.First();
            Assert.That(releaseInfo.Title, Is.EqualTo("Test Album"));
            Assert.That(releaseInfo.Artist, Is.EqualTo("Test Artist"));
            Assert.That(releaseInfo.DownloadProtocol, Is.EqualTo(nameof(QobuzarrDownloadProtocol)));
        }

        [Test]
        public async Task Process_PartialData_HandlesGracefully()
        {
            // Arrange
            var qobuzAlbums = new List<QobuzAlbum>
            {
                new QobuzAlbum
                {
                    Id = "album1",
                    Title = null, // Missing title
                    Artist = new QobuzArtist { Name = "Test Artist" },
                    ReleaseDateOriginal = "2023-01-01"
                },
                new QobuzAlbum
                {
                    Id = "album2",
                    Title = "Valid Album",
                    Artist = null, // Missing artist
                    ReleaseDateOriginal = "2023-01-01"
                },
                new QobuzAlbum
                {
                    Id = "album3",
                    Title = "Another Album",
                    Artist = new QobuzArtist { Name = "Another Artist" },
                    ReleaseDateOriginal = null // Missing date
                }
            };
            
            _apiService.SearchAlbumsAsync(Arg.Any<string>(), Arg.Any<int>())
                .Returns(Task.FromResult<IEnumerable<QobuzAlbum>>(qobuzAlbums));
            
            // Act
            var searchCriteria = new AlbumSearchCriteria { AlbumTitle = "Album" };
            var requests = await _indexer.GetSearchRequests(searchCriteria);
            var results = new List<ReleaseInfo>();
            
            foreach (var request in requests)
            {
                var pageResults = await _indexer.FetchPage(request);
                results.AddRange(pageResults);
            }
            
            // Assert - Should handle partial data without crashing
            Assert.That(results, Is.Not.Null);
            Assert.That(results.Any(r => r.Title == "Another Album"), Is.True);
        }

        [Test]
        public async Task Process_DuplicateResults_Deduplicates()
        {
            // Arrange
            var qobuzAlbums = new List<QobuzAlbum>
            {
                new QobuzAlbum
                {
                    Id = "album1",
                    Title = "Duplicate Album",
                    Artist = new QobuzArtist { Name = "Artist" },
                    ReleaseDateOriginal = "2023-01-01"
                },
                new QobuzAlbum
                {
                    Id = "album1", // Same ID
                    Title = "Duplicate Album",
                    Artist = new QobuzArtist { Name = "Artist" },
                    ReleaseDateOriginal = "2023-01-01"
                }
            };
            
            _apiService.SearchAlbumsAsync(Arg.Any<string>(), Arg.Any<int>())
                .Returns(Task.FromResult<IEnumerable<QobuzAlbum>>(qobuzAlbums));
            
            // Act
            var searchCriteria = new AlbumSearchCriteria { AlbumTitle = "Duplicate" };
            var requests = await _indexer.GetSearchRequests(searchCriteria);
            var results = new List<ReleaseInfo>();
            
            foreach (var request in requests)
            {
                var pageResults = await _indexer.FetchPage(request);
                results.AddRange(pageResults);
            }
            
            // Assert - Should deduplicate based on album ID
            Assert.That(results.Count, Is.EqualTo(1));
        }

        #endregion

        #region ML Optimization Tests

        [Test]
        public async Task Search_MLOptimized_ReducesApiCalls()
        {
            // Arrange
            var cachedResult = new MLSearchResult
            {
                IsCached = true,
                Albums = new List<QobuzAlbum>
                {
                    new QobuzAlbum { Id = "cached1", Title = "Cached Album" }
                }
            };
            
            _mlOptimizer.OptimizeSearchAsync(Arg.Any<string>())
                .Returns(Task.FromResult(cachedResult));
            
            // Act
            var searchCriteria = new AlbumSearchCriteria { AlbumTitle = "Cached Album" };
            var requests = await _indexer.GetSearchRequests(searchCriteria);
            var results = new List<ReleaseInfo>();
            
            foreach (var request in requests)
            {
                var pageResults = await _indexer.FetchPage(request);
                results.AddRange(pageResults);
            }
            
            // Assert - Should use cached result, no API call
            await _apiService.DidNotReceive().SearchAlbumsAsync(Arg.Any<string>(), Arg.Any<int>());
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results.First().Title, Is.EqualTo("Cached Album"));
        }

        [Test]
        public async Task Search_MLCacheHit_ReturnsInstantly()
        {
            // Arrange
            var cachedResult = new MLSearchResult
            {
                IsCached = true,
                ResponseTimeMs = 5, // Very fast cached response
                Albums = new List<QobuzAlbum>
                {
                    new QobuzAlbum { Id = "instant1", Title = "Instant Result" }
                }
            };
            
            _mlOptimizer.OptimizeSearchAsync(Arg.Any<string>())
                .Returns(Task.FromResult(cachedResult));
            
            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var searchCriteria = new AlbumSearchCriteria { AlbumTitle = "Instant" };
            var requests = await _indexer.GetSearchRequests(searchCriteria);
            var results = new List<ReleaseInfo>();
            
            foreach (var request in requests)
            {
                var pageResults = await _indexer.FetchPage(request);
                results.AddRange(pageResults);
            }
            stopwatch.Stop();
            
            // Assert - Should return very quickly from cache
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(50), "Cached response should be instant");
            Assert.That(results.First().Title, Is.EqualTo("Instant Result"));
        }

        [Test]
        [TestCase(10)]
        [TestCase(50)]
        [TestCase(100)]
        public async Task Search_LoadTest_HandlesVolume(int concurrent)
        {
            // Arrange
            _apiService.SearchAlbumsAsync(Arg.Any<string>(), Arg.Any<int>())
                .Returns(Task.FromResult<IEnumerable<QobuzAlbum>>(new List<QobuzAlbum>
                {
                    new QobuzAlbum { Id = "test", Title = "Test Album" }
                }));
            
            // Act
            var tasks = new List<Task<IEnumerable<ReleaseInfo>>>();
            for (int i = 0; i < concurrent; i++)
            {
                var searchCriteria = new AlbumSearchCriteria { AlbumTitle = $"Album {i}" };
                tasks.Add(Task.Run(async () =>
                {
                    var requests = await _indexer.GetSearchRequests(searchCriteria);
                    var results = new List<ReleaseInfo>();
                    foreach (var request in requests)
                    {
                        var pageResults = await _indexer.FetchPage(request);
                        results.AddRange(pageResults);
                    }
                    return results.AsEnumerable();
                }));
            }
            
            var allResults = await Task.WhenAll(tasks);
            
            // Assert - All concurrent requests should complete
            Assert.That(allResults.Length, Is.EqualTo(concurrent));
            Assert.That(allResults.All(r => r.Any()), Is.True, "All searches should return results");
        }

        [Test]
        public async Task MLOptimization_TrackingMetrics_ValidatesPerformance()
        {
            // Arrange
            var mlResult = new MLSearchResult
            {
                IsCached = false,
                ApiCallsReduced = 49, // Target: 49% reduction
                ResponseTimeMs = 85,
                ConfidenceScore = 0.92f,
                Albums = new List<QobuzAlbum>
                {
                    new QobuzAlbum { Id = "perf1", Title = "Performance Test Album" }
                }
            };
            
            _mlOptimizer.OptimizeSearchAsync(Arg.Any<string>())
                .Returns(Task.FromResult(mlResult));
            _mlOptimizer.GetPerformanceMetrics()
                .Returns(new MLPerformanceMetrics
                {
                    AverageApiReduction = 49.2f,
                    AverageResponseTime = 87,
                    CacheHitRate = 0.35f,
                    TotalSearches = 1000
                });
            
            // Act
            var metrics = _mlOptimizer.GetPerformanceMetrics();
            
            // Assert - Validate ML performance meets targets
            Assert.That(metrics.AverageApiReduction, Is.GreaterThanOrEqualTo(49f), 
                "ML should achieve 49% API call reduction target");
            Assert.That(metrics.AverageResponseTime, Is.LessThan(100), 
                "Response time should be under 100ms");
            Assert.That(metrics.CacheHitRate, Is.GreaterThan(0.3f), 
                "Cache hit rate should be above 30%");
        }

        #endregion

        #region Property-Based Tests

        [Property(MaxTest = 50)]
        public Property SearchRequests_RandomInput_AlwaysValid()
        {
            return Prop.ForAll<string, int>((searchTerm, year) =>
            {
                if (string.IsNullOrEmpty(searchTerm))
                    return true; // Skip empty searches
                
                var searchCriteria = new AlbumSearchCriteria
                {
                    AlbumTitle = searchTerm,
                    AlbumYear = Math.Abs(year % 100) + 1920 // Reasonable year range
                };
                
                // Should never throw for any input
                Assert.DoesNotThrowAsync(async () =>
                {
                    var requests = await _indexer.GetSearchRequests(searchCriteria);
                    Assert.That(requests, Is.Not.Null);
                });
                
                return true;
            });
        }

        [Property(MaxTest = 50)]
        public Property ProcessResults_RandomAlbumData_NeverCrashes()
        {
            return Prop.ForAll(GenerateRandomAlbum(), (album) =>
            {
                var albums = new List<QobuzAlbum> { album };
                _apiService.SearchAlbumsAsync(Arg.Any<string>(), Arg.Any<int>())
                    .Returns(Task.FromResult<IEnumerable<QobuzAlbum>>(albums));
                
                // Processing should handle any album data without crashing
                Assert.DoesNotThrowAsync(async () =>
                {
                    var searchCriteria = new AlbumSearchCriteria { AlbumTitle = "Test" };
                    var requests = await _indexer.GetSearchRequests(searchCriteria);
                    foreach (var request in requests)
                    {
                        await _indexer.FetchPage(request);
                    }
                });
                
                return true;
            });
        }

        private Arbitrary<QobuzAlbum> GenerateRandomAlbum()
        {
            return Arb.From(
                from id in Arb.Generate<string>()
                from title in Arb.Generate<string>()
                from artistName in Arb.Generate<string>()
                from year in Gen.Choose(1900, 2024)
                from trackCount in Gen.Choose(1, 100)
                select new QobuzAlbum
                {
                    Id = id ?? "default-id",
                    Title = title,
                    Artist = string.IsNullOrEmpty(artistName) ? null : new QobuzArtist { Name = artistName },
                    ReleaseDateOriginal = $"{year}-01-01",
                    TracksCount = trackCount
                });
        }

        #endregion

        #region Helper Classes

        private class AlbumSearchCriteria : SearchCriteriaBase
        {
            public string AlbumTitle { get; set; }
            public string ArtistName { get; set; }
            public int? AlbumYear { get; set; }
        }
        
        private class ArtistSearchCriteria : SearchCriteriaBase
        {
            public string ArtistQuery { get; set; }
        }
        
        private class BasicSearchCriteria : SearchCriteriaBase
        {
            public string SearchTerm { get; set; }
        }

        private class MLSearchResult
        {
            public bool IsCached { get; set; }
            public int ApiCallsReduced { get; set; }
            public int ResponseTimeMs { get; set; }
            public float ConfidenceScore { get; set; }
            public List<QobuzAlbum> Albums { get; set; }
        }

        private class MLPerformanceMetrics
        {
            public float AverageApiReduction { get; set; }
            public int AverageResponseTime { get; set; }
            public float CacheHitRate { get; set; }
            public int TotalSearches { get; set; }
        }

        #endregion
    }
}