using System;
using System.IO;
using System.Threading.Tasks;
using NzbDrone.Common.Http;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Integration
{
    /// <summary>
    /// Adapts Lidarr's IHttpClient to our simple interface
    /// </summary>
    public class LidarrHttpClientAdapter : IQobuzHttpClient
    {
        private readonly IHttpClient _httpClient;

        public LidarrHttpClientAdapter(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<T> GetJsonAsync<T>(string url, TimeSpan? timeout = null)
        {
            var request = new HttpRequest(url);
            if (timeout.HasValue)
            {
                request.RequestTimeout = timeout.Value;
            }

            var response = await _httpClient.ExecuteAsync(request);
            return JsonConvert.DeserializeObject<T>(response.Content);
        }

        public async Task<byte[]> GetBytesAsync(string url, IProgress<double>? progress = null)
        {
            var request = new HttpRequest(url);
            var response = await _httpClient.ExecuteAsync(request);
            return response.ResponseData;
        }

        public async Task<string> GetStringAsync(string url)
        {
            var request = new HttpRequest(url);
            var response = await _httpClient.ExecuteAsync(request);
            return response.Content;
        }
    }
}
