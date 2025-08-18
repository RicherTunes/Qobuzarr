using System;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Abstractions
{
    /// <summary>
    /// Simplified HTTP client interface that both Lidarr and CLI can implement
    /// </summary>
    public interface IQobuzHttpClient
    {
        Task<T> GetJsonAsync<T>(string url, TimeSpan? timeout = null);
        Task<byte[]> GetBytesAsync(string url, IProgress<double>? progress = null);
        Task<string> GetStringAsync(string url);
    }
}