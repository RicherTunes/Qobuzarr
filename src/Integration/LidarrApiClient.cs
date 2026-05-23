using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NzbDrone.Common.Http;
using NLog;
using Lidarr.Plugin.Common.Observability;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Configuration;

namespace Lidarr.Plugin.Qobuzarr.Integration
{
    /// <summary>
    /// Implementation of the Lidarr REST API client with comprehensive error handling and response processing.
    /// Provides access to Lidarr's wanted albums, system status, and metadata for plugin integration.
    /// </summary>
    /// <remarks>
    /// Key features:
    /// - Automatic API key authentication
    /// - Comprehensive error handling with typed exceptions
    /// - Response validation and deserialization
    /// - Health check and connectivity testing
    /// - Support for pagination and filtering
    /// </remarks>
    public class LidarrApiClient : ILidarrApiClient
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        private string _baseUrl = string.Empty;
        private string _apiKey = string.Empty;

        /// <summary>
        /// Initializes a new instance of the LidarrApiClient with the required dependencies.
        /// </summary>
        /// <param name="httpClient">The HTTP client for making requests to Lidarr API endpoints.</param>
        /// <param name="logger">The logger for recording API interactions, errors, and performance metrics.</param>
        public LidarrApiClient(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Sets the Lidarr server configuration including URL and API key.
        /// Must be called before making any API requests.
        /// </summary>
        /// <param name="baseUrl">The base URL of the Lidarr instance (e.g., "http://localhost:8686").</param>
        /// <param name="apiKey">The API key for authentication with Lidarr.</param>
        public void SetConfiguration(string baseUrl, string apiKey)
        {
            _baseUrl = baseUrl?.TrimEnd('/');
            _apiKey = apiKey;
            _logger.Debug("Lidarr API client configured for {0}", _baseUrl);
        }

        /// <summary>
        /// Tests the connection to Lidarr and validates the API key.
        /// </summary>
        /// <returns>True if the connection is successful and API key is valid; false otherwise.</returns>
        public async Task<bool> ValidateConnectionAsync()
        {
            try
            {
                var status = await GetSystemStatusAsync().ConfigureAwait(false);
                _logger.Info("Successfully connected to Lidarr {0} at {1}", status.Version, _baseUrl);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to validate connection to Lidarr at {0}", _baseUrl);
                return false;
            }
        }

        /// <summary>
        /// Retrieves system status information from Lidarr.
        /// </summary>
        public async Task<LidarrSystemStatus> GetSystemStatusAsync()
        {
            return await ExecuteRequestAsync<LidarrSystemStatus>("/api/v1/system/status").ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves health check information from Lidarr.
        /// </summary>
        public async Task<List<LidarrHealthCheck>> GetHealthCheckAsync()
        {
            return await ExecuteRequestAsync<List<LidarrHealthCheck>>("/api/v1/health").ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves disk space information for all storage locations.
        /// </summary>
        public async Task<List<LidarrDiskSpace>> GetDiskSpaceAsync()
        {
            return await ExecuteRequestAsync<List<LidarrDiskSpace>>("/api/v1/diskspace").ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves a list of wanted albums based on the specified filter criteria.
        /// </summary>
        public async Task<LidarrPagedResponse<LidarrAlbum>> GetWantedAlbumsAsync(LidarrFilterOptions filterOptions = null)
        {
            var endpoint = "/api/v1/wanted/missing";
            var parameters = filterOptions?.ToQueryParameters();

            return await ExecuteRequestAsync<LidarrPagedResponse<LidarrAlbum>>(endpoint, parameters).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves a specific album by its Lidarr ID.
        /// </summary>
        public async Task<LidarrAlbum> GetAlbumAsync(int albumId, bool includeStatistics = true)
        {
            var endpoint = $"/api/v1/album/{albumId}";
            var parameters = new Dictionary<string, string>();

            if (includeStatistics)
                parameters["includeStatistics"] = "true";

            return await ExecuteRequestAsync<LidarrAlbum>(endpoint, parameters).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves a list of albums for a specific artist.
        /// </summary>
        public async Task<List<LidarrAlbum>> GetAlbumsByArtistAsync(int artistId, bool includeStatistics = true)
        {
            var endpoint = "/api/v1/album";
            var parameters = new Dictionary<string, string>
            {
                ["artistId"] = artistId.ToString()
            };

            if (includeStatistics)
                parameters["includeStatistics"] = "true";

            return await ExecuteRequestAsync<List<LidarrAlbum>>(endpoint, parameters).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves a specific artist by its Lidarr ID.
        /// </summary>
        public async Task<LidarrArtist> GetArtistAsync(int artistId, bool includeStatistics = true)
        {
            var endpoint = $"/api/v1/artist/{artistId}";
            var parameters = new Dictionary<string, string>();

            if (includeStatistics)
                parameters["includeStatistics"] = "true";

            return await ExecuteRequestAsync<LidarrArtist>(endpoint, parameters).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves a list of all artists in Lidarr.
        /// </summary>
        public async Task<List<LidarrArtist>> GetArtistsAsync(bool includeStatistics = false)
        {
            var endpoint = "/api/v1/artist";
            var parameters = new Dictionary<string, string>();

            if (includeStatistics)
                parameters["includeStatistics"] = "true";

            return await ExecuteRequestAsync<List<LidarrArtist>>(endpoint, parameters).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves tracks for a specific album.
        /// </summary>
        public async Task<List<LidarrTrack>> GetTracksAsync(int albumId)
        {
            var endpoint = "/api/v1/track";
            var parameters = new Dictionary<string, string>
            {
                ["albumId"] = albumId.ToString()
            };

            return await ExecuteRequestAsync<List<LidarrTrack>>(endpoint, parameters).ConfigureAwait(false);
        }

        /// <summary>
        /// Searches for albums in Lidarr using the specified search term.
        /// </summary>
        public async Task<List<LidarrAlbum>> SearchAlbumsAsync(string searchTerm, int limit = 50)
        {
            var filterOptions = LidarrFilterOptions.ForSearch(searchTerm);
            filterOptions.PageSize = limit;

            var response = await GetWantedAlbumsAsync(filterOptions).ConfigureAwait(false);
            return response.Records;
        }

        /// <summary>
        /// Searches for artists in Lidarr using the specified search term.
        /// </summary>
        public async Task<List<LidarrArtist>> SearchArtistsAsync(string searchTerm, int limit = 50)
        {
            var endpoint = "/api/v1/artist";
            var parameters = new Dictionary<string, string>
            {
                ["term"] = searchTerm,
                ["pageSize"] = limit.ToString()
            };

            return await ExecuteRequestAsync<List<LidarrArtist>>(endpoint, parameters).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves overall system statistics from Lidarr.
        /// Note: This endpoint may not exist in all Lidarr versions, so it returns computed statistics.
        /// </summary>
        public async Task<LidarrSystemStatistics> GetSystemStatisticsAsync()
        {
            try
            {
                // Try the direct statistics endpoint first (if available)
                return await ExecuteRequestAsync<LidarrSystemStatistics>("/api/v1/statistics").ConfigureAwait(false);
            }
            catch
            {
                // Fallback: compute statistics from artists endpoint
                _logger.Debug("Statistics endpoint not available, computing from artists data");
                var artists = await GetArtistsAsync(includeStatistics: true).ConfigureAwait(false);

                return new LidarrSystemStatistics
                {
                    ArtistCount = artists.Count,
                    AlbumCount = artists.Sum(a => a.Statistics?.AlbumCount ?? 0),
                    TrackFileCount = artists.Sum(a => a.Statistics?.TrackFileCount ?? 0),
                    TrackCount = artists.Sum(a => a.Statistics?.TrackCount ?? 0),
                    TotalSize = artists.Sum(a => a.Statistics?.SizeOnDisk ?? 0)
                };
            }
        }

        /// <summary>
        /// Checks if the Lidarr instance is currently available and responding.
        /// </summary>
        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                var endpoint = "/api/v1/system/status";
                var url = $"{_baseUrl}{endpoint}";

                var request = new HttpRequestBuilder(url)
                    .SetHeader("X-Api-Key", _apiKey)
                    .SetHeader("Accept", "application/json")
                    .Build();

                var response = await _httpClient.ExecuteAsync(request).ConfigureAwait(false);
                return response.StatusCode == System.Net.HttpStatusCode.OK;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Retrieves all quality profiles configured in Lidarr.
        /// </summary>
        public async Task<List<LidarrQualityProfile>> GetQualityProfilesAsync()
        {
            return await ExecuteRequestAsync<List<LidarrQualityProfile>>("/api/v1/qualityprofile").ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves a specific quality profile by its ID.
        /// </summary>
        public async Task<LidarrQualityProfile> GetQualityProfileAsync(int profileId)
        {
            var endpoint = $"/api/v1/qualityprofile/{profileId}";
            return await ExecuteRequestAsync<LidarrQualityProfile>(endpoint).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves all quality definitions available in Lidarr.
        /// </summary>
        public async Task<List<LidarrQuality>> GetQualityDefinitionsAsync()
        {
            return await ExecuteRequestAsync<List<LidarrQuality>>("/api/v1/qualitydefinition").ConfigureAwait(false);
        }

        private async Task<T> ExecuteRequestAsync<T>(string endpoint, Dictionary<string, string> parameters = null)
        {
            ValidateConfiguration();

            try
            {
                var url = $"{_baseUrl}{endpoint}";

                var requestBuilder = new HttpRequestBuilder(url)
                    .SetHeader("X-Api-Key", _apiKey)
                    .SetHeader("Accept", "application/json");

                // Add query parameters
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        if (!string.IsNullOrWhiteSpace(param.Value))
                        {
                            requestBuilder.AddQueryParam(param.Key, param.Value);
                        }
                    }
                }

                var request = requestBuilder.Build();

                _logger.Debug("Making request to Lidarr: {0}", endpoint);

                var response = await _httpClient.ExecuteAsync(request).ConfigureAwait(false);

                if (response.HasHttpError)
                {
                    HandleErrorResponse(response, endpoint);
                }

                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    throw new LidarrApiException($"Empty response from Lidarr endpoint: {endpoint}", (int)response.StatusCode, "EmptyResponse");
                }

                try
                {
                    var result = JsonConvert.DeserializeObject<T>(response.Content);
                    _logger.Debug("Successfully processed response from {0}", endpoint);
                    return result;
                }
                catch (JsonException ex)
                {
                    // Response body on a deserialize failure may include Lidarr
                    // API-key echoes or auth header fragments; redact before logging.
                    _logger.Error(ex, "Failed to deserialize response from {0}: {1}", endpoint, LogRedactor.Redact(response.Content));
                    throw new LidarrApiException($"Invalid JSON response from Lidarr: {ex.Message}", (int)response.StatusCode, "InvalidJson");
                }
            }
            catch (LidarrApiException)
            {
                throw; // Re-throw our custom exceptions
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error calling Lidarr endpoint {0}", endpoint);
                throw new LidarrApiException($"Unexpected error calling Lidarr: {ex.Message}", 0, "UnexpectedError");
            }
        }

        private void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(_baseUrl))
                throw new InvalidOperationException("Lidarr base URL is not configured. Call SetConfiguration first.");

            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("Lidarr API key is not configured. Call SetConfiguration first.");
        }

        private void HandleErrorResponse(HttpResponse response, string endpoint)
        {
            var statusCode = (int)response.StatusCode;
            var content = response.Content ?? "";

            var message = statusCode switch
            {
                401 => "Invalid API key or authentication failed",
                403 => "Access forbidden - check API key permissions",
                404 => $"Endpoint not found: {endpoint}",
                429 => "Rate limit exceeded",
                >= 500 => "Lidarr server error",
                _ => $"HTTP {statusCode}: {content}"
            };

            var errorType = statusCode switch
            {
                401 => "AuthenticationFailed",
                403 => "AccessForbidden",
                404 => "NotFound",
                429 => "RateLimited",
                >= 500 => "ServerError",
                _ => "HttpError"
            };

            _logger.Error("Lidarr API error {0} for endpoint {1}: {2}", statusCode, endpoint, message);
            throw new LidarrApiException(message, statusCode, errorType);
        }
    }

    /// <summary>
    /// Exception thrown when the Lidarr API returns an error response or when API communication fails.
    /// Provides structured access to HTTP status codes and categorized error types for proper error handling.
    /// </summary>
    public class LidarrApiException : Exception
    {
        /// <summary>
        /// Gets the HTTP status code returned by the Lidarr API.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Gets the categorized error type for programmatic error handling.
        /// Common values: AuthenticationFailed, AccessForbidden, NotFound, RateLimited, ServerError, HttpError.
        /// </summary>
        public string ErrorType { get; }

        /// <summary>
        /// Initializes a new instance of LidarrApiException with detailed error information.
        /// </summary>
        /// <param name="message">The error message describing what went wrong.</param>
        /// <param name="statusCode">The HTTP status code returned by the API.</param>
        /// <param name="errorType">The categorized error type for programmatic handling.</param>
        public LidarrApiException(string message, int statusCode, string errorType) : base(message)
        {
            StatusCode = statusCode;
            ErrorType = errorType;
        }
    }
}
