using System.Net.Http;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Http;
using Lidarr.Plugin.Common.Utilities;
using Lidarr.Plugin.Spotifyarr.Settings;

namespace Lidarr.Plugin.Spotifyarr.API
{
    /// <summary>
    /// Spotify API client using shared library HTTP patterns.
    /// Focus only on Spotify-specific API integration!
    /// </summary>
    public class SpotifyApiClient
    {
        private readonly SpotifySettings _settings;
        private readonly HttpClient _httpClient;

        public SpotifyApiClient(SpotifySettings settings)
        {
            _settings = settings;
            _httpClient = new HttpClient();
        }

        public async Task<string> SearchAsync(string query)
        {
            // Use shared library HTTP builder (80+ LOC saved)
            var request = new StreamingApiRequestBuilder(_settings.BaseUrl)
                .Endpoint("search/albums")
                .Query("query", query)
                .ApiKey("Authorization", _settings.SpotifyApiKey)
                .WithStreamingDefaults("Spotifyarr/1.0")
                .Build();

            // Use shared retry logic (50+ LOC saved)
            var response = await _httpClient.ExecuteWithRetryAsync(request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadContentSafelyAsync();
        }

        // TODO: Add other Spotify API methods
        // Use shared library patterns throughout!
    }
}
