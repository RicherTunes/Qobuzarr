// NOTE: This file is no longer needed for plugin discovery.
// 
// Lidarr automatically discovers plugins by scanning for classes that implement 
// standard interfaces like:
// - IndexerBase<Settings> (for search functionality) 
// - DownloadClientBase<Settings> (for download functionality)
// - ImportListBase<Settings> (for content discovery)
//
// The QobuzIndexer and QobuzDownloadClient classes in their respective folders
// serve as the actual plugin entry points that Lidarr will discover.
//
// This approach follows Brainarr's successful pattern and eliminates the need
// for non-existent NzbDrone.Core.Plugins interfaces.

namespace Lidarr.Plugin.Qobuzarr
{
    /// <summary>
    /// Plugin metadata constants for internal use
    /// </summary>
    public static class QobuzarrPluginInfo
    {
        public const string Name = "Qobuzarr";
        public const string Description = "High-quality music indexer and download client for Qobuz streaming service";
        public const string Author = "RicherTunes";
        public const string GithubUrl = "https://github.com/richertunes/qobuzarr";
        
        /// <summary>
        /// Gets the plugin version from the assembly metadata (single source of truth in csproj)
        /// </summary>
        public static string Version => typeof(QobuzarrPluginInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    }
}