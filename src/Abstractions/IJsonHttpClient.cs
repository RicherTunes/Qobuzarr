using System;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Abstractions
{
    /// <summary>
    /// Simplified JSON/bytes/string HTTP helper that both Lidarr and CLI can implement.
    /// This is intentionally separate from the NzbDrone <c>HttpRequest/HttpResponse</c> HTTP abstraction used by the API layer.
    /// </summary>
    public interface IJsonHttpClient
    {
        Task<T> GetJsonAsync<T>(string url, TimeSpan? timeout = null);
        Task<byte[]> GetBytesAsync(string url, IProgress<double>? progress = null);
        Task<string> GetStringAsync(string url);
    }
}
