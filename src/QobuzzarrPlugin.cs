/*
 * Qobuzarr - Lidarr Plugin for Qobuz
 * Copyright (C) 2025 RicherTunes
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 * 
 * IMPORTANT LEGAL NOTICE:
 * This software is provided for educational and personal use only.
 * Users are solely responsible for ensuring compliance with all
 * applicable laws and service terms in their jurisdiction.
 * The authors assume no liability for misuse of this software.
 * 
 * By using this software, you acknowledge that you must:
 * - Comply with Qobuz's Terms of Service
 * - Have valid subscription/rights to access content
 * - Verify the legality of your use in your jurisdiction
 * 
 * See LEGAL_DISCLAIMER.md for complete terms and conditions.
 */

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