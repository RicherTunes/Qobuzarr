using System;
using System.Net.Http;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Newtonsoft.Json;

namespace QobuzCLI.Services.Adapters
{
    /// <summary>
    /// Adapter that bridges CLI's HttpClient to plugin's IQobuzHttpClient interface.
    /// Follows plugin-first architecture from CLAUDE.md.
    /// </summary>
    public class CliHttpClientAdapter : IQobuzHttpClient
    {
        private readonly HttpClient _httpClient;

        public CliHttpClientAdapter(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<T> GetJsonAsync<T>(string url, TimeSpan? timeout = null)
        {
            if (timeout.HasValue)
            {
                _httpClient.Timeout = timeout.Value;
            }

            var response = await _httpClient.GetStringAsync(url).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<T>(response)!;
        }

        public async Task<byte[]> GetBytesAsync(string url, IProgress<double>? progress = null)
        {
            // Note: Basic implementation without progress reporting
            // Progress reporting would require more complex implementation
            return await _httpClient.GetByteArrayAsync(url).ConfigureAwait(false);
        }

        public async Task<string> GetStringAsync(string url)
        {
            return await _httpClient.GetStringAsync(url).ConfigureAwait(false);
        }
    }
}
