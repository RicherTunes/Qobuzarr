using NLog;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Services.Http;

namespace Lidarr.Plugin.Qobuzarr
{
    /// <summary>
    /// Qobuzarr plugin services and components.
    /// Lidarr automatically discovers and registers classes using DryIoC's AutoAddServices.
    /// Services implementing interfaces are registered as Singletons by default.
    /// </summary>
    /// <remarks>
    /// Plugin discovery is handled automatically by Lidarr:
    /// - QobuzIndexer implements IIndexer (via HttpIndexerBase) and will be auto-registered
    /// - QobuzDownloadClient implements IDownloadClient (via DownloadClientBase) and will be auto-registered
    /// - Other services implementing interfaces are injected via constructor dependency injection
    /// - All plugin types are scanned and registered when the plugin assembly is loaded
    /// </remarks>
    public static class QobuzarrModule
    {
        /// <summary>
        /// Plugin information and metadata
        /// </summary>
        public static class Info
        {
            public const string Name = "Qobuzarr";
            public const string Description = "High-quality music indexer and download client for Qobuz streaming service";
            public const string Author = "RicherTunes";

            /// <summary>
            /// Gets the plugin version from the assembly metadata (single source of truth in csproj)
            /// </summary>
            public static string Version => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        }

        /// <summary>
        /// Releases static plugin-level resources whose lifetimes are tied to the plugin's
        /// AssemblyLoadContext.  Call once during plugin unload to prevent socket-pool leaks
        /// across Lidarr reload cycles.
        /// <list type="bullet">
        ///   <item>SharedSystemHttpClient.Dispose — disposes the Lazy HttpClient + SocketsHttpHandler,
        ///   releasing the underlying socket pool.  After 3-5 reload cycles without this call,
        ///   sockets exhaust (audit finding).</item>
        ///   <item>QobuzApiClient.ResetNativeAuthFailureGate — clears any latched auth failure
        ///   recorded by the Lidarr-native API path before the plugin is reloaded.</item>
        /// </list>
        /// Wave 8A additions should append further static Dispose() calls after the one below.
        /// </summary>
        public static void Dispose()
        {
            QobuzApiClient.ResetNativeAuthFailureGate();
            SharedSystemHttpClient.Dispose();
        }
    }
}
