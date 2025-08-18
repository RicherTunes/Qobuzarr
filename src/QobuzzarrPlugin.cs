using System;
using NzbDrone.Core.Plugins;

namespace Lidarr.Plugin.Qobuzarr
{
    /// <summary>
    /// Main plugin class for Qobuzarr. This class serves as the entry point for Lidarr plugin discovery.
    /// Lidarr automatically discovers plugins by scanning for classes that implement IPlugin.
    /// </summary>
    public class QobuzarrPlugin : NzbDrone.Core.Plugins.Plugin
    {
        public override string Name => "Qobuzarr";
        public override string Owner => "RicherTunes";
        public override string GithubUrl => "https://github.com/richertunes/qobuzarr";
    }

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
        public static string Version => typeof(QobuzarrPlugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    }
}