using System;
using System.Net.Http;
using System.Threading;
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
        private readonly TimeProvider _timeProvider;

        public CliHttpClientAdapter(HttpClient httpClient)
            : this(httpClient, TimeProvider.System)
        {
        }

        internal CliHttpClientAdapter(HttpClient httpClient, TimeProvider timeProvider)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        public async Task<T> GetJsonAsync<T>(string url, TimeSpan? timeout = null)
        {
            // Preserve HttpClient's accepted timeout range, without mutating a
            // caller-owned client that may already be serving other requests.
            if (timeout.HasValue && timeout.Value != Timeout.InfiniteTimeSpan &&
                (timeout.Value <= TimeSpan.Zero || timeout.Value > TimeSpan.FromMilliseconds(int.MaxValue)))
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout,
                    "Timeout must be positive and within HttpClient's supported range, or infinite.");
            }

            // A per-call deadline can shorten, but never disable or extend, the
            // owning client's timeout. Null/infinite adds no additional timer.
            using var deadline = timeout.HasValue && timeout.Value != Timeout.InfiniteTimeSpan
                ? new CancellationTokenSource(timeout.Value, _timeProvider)
                : null;
            var response = await _httpClient.GetStringAsync(url, deadline?.Token ?? CancellationToken.None).ConfigureAwait(false);
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
