using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    public class QobuzTestHelper
    {
        private readonly IQobuzAuthenticationService _authService;
        private readonly IQobuzApiClient _apiClient;
        private readonly Logger _logger;

        public QobuzTestHelper(IQobuzAuthenticationService authService, IQobuzApiClient apiClient, Logger logger)
        {
            _authService = authService;
            _apiClient = apiClient;
            _logger = logger;
        }

        /// <summary>
        /// Test authentication with provided credentials
        /// </summary>
        public async Task<TestResult> TestAuthenticationAsync(QobuzCredentials credentials)
        {
            try
            {
                _logger.Info("Testing Qobuz authentication...");

                var session = await _authService.AuthenticateAsync(credentials).ConfigureAwait(false);

                if (session?.IsValid() == true)
                {
                    return new TestResult
                    {
                        Success = true,
                        Message = "Authentication successful",
                        Details = new
                        {
                            UserId = session.UserId,
                            SubscriptionTier = session.Subscription?.GetTierDescription(),
                            ExpiresAt = session.ExpiresAt,
                            AppId = session.AppId
                        }
                    };
                }

                return new TestResult
                {
                    Success = false,
                    Message = "Authentication failed - invalid session"
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Authentication test failed");
                return new TestResult
                {
                    Success = false,
                    Message = $"Authentication failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        /// <summary>
        /// Test album search functionality
        /// </summary>
        public async Task<TestResult> TestSearchAsync(string artistName, string albumName)
        {
            try
            {
                _logger.Info("Testing Qobuz search: {0} - {1}", artistName, albumName);

                var searchParams = new System.Collections.Generic.Dictionary<string, string>
                {
                    {"query", $"{artistName} {albumName}"},
                    {"limit", "10"}
                };

                var searchResponse = await _apiClient.GetAsync<QobuzAlbumSearchResponse>("/album/search", searchParams).ConfigureAwait(false);

                if (searchResponse?.IsSuccess == true)
                {
                    return new TestResult
                    {
                        Success = true,
                        Message = $"Search successful - found {searchResponse.Albums?.Total ?? 0} results",
                        Details = new
                        {
                            Query = $"{artistName} {albumName}",
                            ResultCount = searchResponse.Albums?.Total ?? 0,
                            FirstResults = searchResponse.Albums?.Items?.Take(3).Select(a => new
                            {
                                Id = a.Id,
                                Title = a.GetFullTitle(),
                                Artist = a.GetArtistName(),
                                Year = a.ReleaseDate.Year,
                                Quality = a.HasHiResQuality() ? "Hi-Res" : "Standard"
                            })
                        }
                    };
                }

                return new TestResult
                {
                    Success = false,
                    Message = "Search failed - no valid response"
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Search test failed");
                return new TestResult
                {
                    Success = false,
                    Message = $"Search failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        /// <summary>
        /// Test album details retrieval
        /// </summary>
        public async Task<TestResult> TestAlbumDetailsAsync(string albumId)
        {
            try
            {
                _logger.Info("Testing album details retrieval for ID: {0}", albumId);

                var albumParams = new System.Collections.Generic.Dictionary<string, string>
                {
                    {"album_id", albumId},
                    {"extra", "track_ids"}
                };

                var album = await _apiClient.GetAsync<QobuzAlbum>("/album/get", albumParams).ConfigureAwait(false);

                if (album != null && !string.IsNullOrEmpty(album.Id))
                {
                    return new TestResult
                    {
                        Success = true,
                        Message = "Album details retrieved successfully",
                        Details = new
                        {
                            Id = album.Id,
                            Title = album.GetFullTitle(),
                            Artist = album.GetArtistName(),
                            TrackCount = album.TracksCount,
                            Duration = album.Duration.ToString(@"hh\:mm\:ss"),
                            Quality = album.HasHiResQuality() ? "Hi-Res" : "Standard",
                            MaxSampleRate = album.MaximumSampleRate,
                            MaxBitDepth = album.MaximumBitDepth,
                            Streamable = album.Streamable,
                            Downloadable = album.Downloadable
                        }
                    };
                }

                return new TestResult
                {
                    Success = false,
                    Message = "Failed to retrieve album details"
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Album details test failed");
                return new TestResult
                {
                    Success = false,
                    Message = $"Album details test failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        /// <summary>
        /// Run comprehensive tests
        /// </summary>
        public async Task<TestResult> RunFullTestSuiteAsync(QobuzCredentials credentials)
        {
            var results = new System.Collections.Generic.List<TestResult>();

            // Test authentication
            var authResult = await TestAuthenticationAsync(credentials).ConfigureAwait(false);
            results.Add(authResult);

            if (!authResult.Success)
            {
                return new TestResult
                {
                    Success = false,
                    Message = "Full test suite failed - authentication failed",
                    Details = results
                };
            }

            // Set session for subsequent tests
            var session = _authService.GetCachedSession();
            _apiClient.SetSession(session);

            // Test search
            var searchResult = await TestSearchAsync("Miles Davis", "Kind of Blue").ConfigureAwait(false);
            results.Add(searchResult);

            // If search found results, test album details
            if (searchResult.Success && searchResult.Details is { } searchDetails)
            {
                var firstAlbumId = searchDetails.GetType().GetProperty("FirstResults")?.GetValue(searchDetails)
                    as System.Collections.Generic.IEnumerable<dynamic>;

                var firstAlbum = firstAlbumId?.FirstOrDefault();
                if (firstAlbum?.Id != null)
                {
                    var detailsResult = await TestAlbumDetailsAsync(firstAlbum.Id.ToString()).ConfigureAwait(false);
                    results.Add(detailsResult);
                }
            }

            var allSuccessful = results.All(r => r.Success);

            return new TestResult
            {
                Success = allSuccessful,
                Message = allSuccessful ? "All tests passed" : "Some tests failed",
                Details = results
            };
        }

        public class TestResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public object Details { get; set; }
            public Exception Exception { get; set; }
        }
    }
}
