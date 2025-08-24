using System;

namespace Lidarr.Plugin.Qobuzarr.Constants
{
    /// <summary>
    /// Central constants for the Qobuzarr plugin to eliminate hardcoding
    /// </summary>
    public static class QobuzarrConstants
    {
        /// <summary>
        /// The plugin display name used throughout the application
        /// </summary>
        public const string PluginName = "Qobuzarr";
        
        /// <summary>
        /// The service name for metadata source identification
        /// </summary>
        public const string ServiceName = "Qobuz";
        
        /// <summary>
        /// The plugin folder name used for file system operations
        /// </summary>
        public const string PluginFolderName = "Qobuzarr";
        
        /// <summary>
        /// The protocol identifier used for Lidarr plugin system
        /// </summary>
        public const string ProtocolName = "Qobuzarr";

        /// <summary>
        /// Plugin metadata information (restored from deleted QobuzarrPlugin.cs)
        /// </summary>
        public static class Plugin
        {
            /// <summary>
            /// The plugin name
            /// </summary>
            public const string Name = "Qobuzarr";
            
            /// <summary>
            /// The plugin description
            /// </summary>
            public const string Description = "High-quality music indexer and download client for Qobuz streaming service";
            
            /// <summary>
            /// The plugin author
            /// </summary>
            public const string Author = "RicherTunes";
            
            /// <summary>
            /// The plugin GitHub repository URL
            /// </summary>
            public const string GithubUrl = "https://github.com/richertunes/qobuzarr";
            
            /// <summary>
            /// Gets the plugin version from the assembly metadata (single source of truth in csproj)
            /// </summary>
            public static string Version => typeof(QobuzarrConstants).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        }
    }
}