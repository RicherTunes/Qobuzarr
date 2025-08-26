using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Http;
using Lidarr.Plugin.Common.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Example implementation showing how to use the shared StreamingApiRequestBuilder
    /// for Qobuz API calls. Demonstrates the fluent interface and common patterns.
    /// </summary>
    public class QobuzHttpServiceExample
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "https://www.qobuz.com/api.json/0.2";
        
        public QobuzHttpServiceExample(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Example: Search for albums using the shared request builder.
        /// </summary>
        public async Task<string> SearchAlbumsAsync(string query, string authToken, int limit = 50)
        {
            var request = new StreamingApiRequestBuilder(_baseUrl)
                .Endpoint("album/search")
                .Get()
                .BearerToken(authToken)
                .Query("query", query)
                .Query("limit", limit)
                .Query("offset", 0)
                .WithStreamingDefaults("Qobuzarr/1.0")
                .Build();

            // Log the request for debugging (sensitive data is masked)
            var requestInfo = new StreamingApiRequestBuilder(_baseUrl)
                .Endpoint("album/search")
                .Get()
                .BearerToken(authToken)
                .Query("query", query)
                .Query("limit", limit)
                .Query("offset", 0)
                .BuildForLogging();

            System.Diagnostics.Debug.WriteLine($"Qobuz API Request:\n{requestInfo}");

            // Execute with retry and error handling
            var response = await _httpClient.ExecuteWithRetryAsync(request, maxRetries: 3);
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadContentSafelyAsync();
        }

        /// <summary>
        /// Example: Authenticate with Qobuz using form data.
        /// </summary>
        public async Task<string> AuthenticateAsync(string email, string password, string appId)
        {
            var formData = new Dictionary<string, string>
            {
                ["email"] = email,
                ["password"] = password,
                ["app_id"] = appId
            };

            var request = new StreamingApiRequestBuilder(_baseUrl)
                .Endpoint("user/login")
                .Post()
                .FormBody(formData)
                .WithStreamingDefaults("Qobuzarr/1.0")
                .NoCache() // Don't cache authentication requests
                .Timeout(TimeSpan.FromSeconds(30))
                .Build();

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadContentSafelyAsync();
        }

        /// <summary>
        /// Example: Get track stream URL with signature authentication.
        /// </summary>
        public async Task<string> GetStreamUrlAsync(string trackId, string formatId, string authToken, Dictionary<string, string> signatureParams)
        {
            var builder = new StreamingApiRequestBuilder(_baseUrl)
                .Endpoint("track/getFileUrl")
                .Get()
                .BearerToken(authToken)
                .Query("track_id", trackId)
                .Query("format_id", formatId)
                .WithStreamingDefaults("Qobuzarr/1.0");

            // Add signature parameters
            builder.QueryParams(signatureParams);

            var request = builder.Build();

            // Execute with timing measurement
            var (response, duration) = await _httpClient.ExecuteWithTimingAsync(request);
            
            System.Diagnostics.Debug.WriteLine($"Qobuz stream URL request took {duration.TotalMilliseconds}ms");
            
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadContentSafelyAsync();
        }

        /// <summary>
        /// Example: Using JSON body for more complex requests.
        /// </summary>
        public async Task<string> CreatePlaylistAsync(string authToken, object playlistData)
        {
            var request = new StreamingApiRequestBuilder(_baseUrl)
                .Endpoint("playlist/create")
                .Post()
                .BearerToken(authToken)
                .JsonBody(playlistData)
                .WithStreamingDefaults("Qobuzarr/1.0")
                .Build();

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadContentSafelyAsync();
        }

        /// <summary>
        /// Example: Using custom headers for specific Qobuz requirements.
        /// </summary>
        public async Task<string> GetUserInfoAsync(string authToken, string countryCode)
        {
            var customHeaders = new Dictionary<string, string>
            {
                ["X-Country-Code"] = countryCode,
                ["X-App-Id"] = "your_app_id_here"
            };

            var request = new StreamingApiRequestBuilder(_baseUrl)
                .Endpoint("user/login")
                .Get()
                .BearerToken(authToken)
                .Headers(customHeaders)
                .WithStreamingDefaults("Qobuzarr/1.0")
                .Build();

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadContentSafelyAsync();
        }

        /// <summary>
        /// Example: Bulk operation with shared retry utilities.
        /// </summary>
        public async Task<List<string>> GetMultipleAlbumsAsync(List<string> albumIds, string authToken)
        {
            var results = new List<string>();

            foreach (var albumId in albumIds)
            {
                var albumData = await RetryUtilities.ExecuteWithRetryAsync(
                    async () =>
                    {
                        var request = new StreamingApiRequestBuilder(_baseUrl)
                            .Endpoint("album/get")
                            .Get()
                            .BearerToken(authToken)
                            .Query("album_id", albumId)
                            .WithStreamingDefaults("Qobuzarr/1.0")
                            .Build();

                        var response = await _httpClient.SendAsync(request);
                        response.EnsureSuccessStatusCode();
                        return await response.Content.ReadContentSafelyAsync();
                    },
                    maxRetries: 3,
                    initialDelayMs: 1000,
                    $"Get album {albumId}");

                results.Add(albumData);
            }

            return results;
        }
    }
}