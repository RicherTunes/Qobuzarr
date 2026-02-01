using System;
using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Models.Lidarr
{
    /// <summary>
    /// Represents system status and health information from Lidarr.
    /// Used for health checks and determining system availability.
    /// </summary>
    public class LidarrSystemStatus
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("buildTime")]
        public DateTime BuildTime { get; set; }

        [JsonProperty("isDebug")]
        public bool IsDebug { get; set; }

        [JsonProperty("isProduction")]
        public bool IsProduction { get; set; }

        [JsonProperty("isAdmin")]
        public bool IsAdmin { get; set; }

        [JsonProperty("isUserInteractive")]
        public bool IsUserInteractive { get; set; }

        [JsonProperty("startupPath")]
        public string StartupPath { get; set; }

        [JsonProperty("appData")]
        public string AppData { get; set; }

        [JsonProperty("osName")]
        public string OsName { get; set; }

        [JsonProperty("osVersion")]
        public string OsVersion { get; set; }

        [JsonProperty("isNetCore")]
        public bool IsNetCore { get; set; }

        [JsonProperty("isMono")]
        public bool IsMono { get; set; }

        [JsonProperty("isLinux")]
        public bool IsLinux { get; set; }

        [JsonProperty("isOsx")]
        public bool IsOsx { get; set; }

        [JsonProperty("isWindows")]
        public bool IsWindows { get; set; }

        [JsonProperty("mode")]
        public string Mode { get; set; }

        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("authentication")]
        public string Authentication { get; set; }

        [JsonProperty("sqliteVersion")]
        public string SqliteVersion { get; set; }

        [JsonProperty("urlBase")]
        public string UrlBase { get; set; }

        [JsonProperty("runtimeVersion")]
        public string RuntimeVersion { get; set; }

        [JsonProperty("runtimeName")]
        public string RuntimeName { get; set; }

        [JsonProperty("startTime")]
        public DateTime StartTime { get; set; }

        [JsonProperty("packageVersion")]
        public string PackageVersion { get; set; }

        [JsonProperty("packageAuthor")]
        public string PackageAuthor { get; set; }

        [JsonProperty("packageUpdateMechanism")]
        public string PackageUpdateMechanism { get; set; }

        /// <summary>
        /// Get system uptime
        /// </summary>
        public TimeSpan GetUptime()
        {
            return DateTime.UtcNow - StartTime;
        }

        /// <summary>
        /// Check if Lidarr is running in development mode
        /// </summary>
        public bool IsDevelopmentMode()
        {
            return IsDebug || !IsProduction;
        }

        /// <summary>
        /// Get formatted version string
        /// </summary>
        public string GetVersionString()
        {
            if (!string.IsNullOrEmpty(PackageVersion))
                return $"{Version} ({PackageVersion})";

            return Version;
        }

        /// <summary>
        /// Check if authentication is enabled
        /// </summary>
        public bool IsAuthenticationEnabled()
        {
            return !string.IsNullOrEmpty(Authentication) && Authentication != "none";
        }
    }

    /// <summary>
    /// Represents health check information from Lidarr
    /// </summary>
    public class LidarrHealthCheck
    {
        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("wikiUrl")]
        public string WikiUrl { get; set; }

        /// <summary>
        /// Check if this is a critical health issue
        /// </summary>
        public bool IsCritical()
        {
            return Type?.ToLower() == "error";
        }

        /// <summary>
        /// Check if this is a warning
        /// </summary>
        public bool IsWarning()
        {
            return Type?.ToLower() == "warning";
        }

        /// <summary>
        /// Check if this health check passed
        /// </summary>
        public bool IsHealthy()
        {
            return Type?.ToLower() == "ok";
        }
    }

    /// <summary>
    /// Represents system disk space information
    /// </summary>
    public class LidarrDiskSpace
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("freeSpace")]
        public long FreeSpace { get; set; }

        [JsonProperty("totalSpace")]
        public long TotalSpace { get; set; }

        /// <summary>
        /// Get used space in bytes
        /// </summary>
        public long GetUsedSpace()
        {
            return TotalSpace - FreeSpace;
        }

        /// <summary>
        /// Get percentage of disk space used
        /// </summary>
        public decimal GetUsedPercentage()
        {
            if (TotalSpace == 0)
                return 0;

            return (decimal)GetUsedSpace() / TotalSpace * 100;
        }

        /// <summary>
        /// Get free space in GB
        /// </summary>
        public decimal GetFreeSpaceGB()
        {
            return (decimal)FreeSpace / (1024 * 1024 * 1024);
        }

        /// <summary>
        /// Get total space in GB
        /// </summary>
        public decimal GetTotalSpaceGB()
        {
            return (decimal)TotalSpace / (1024 * 1024 * 1024);
        }

        /// <summary>
        /// Check if disk space is getting low (less than 10% free)
        /// </summary>
        public bool IsLowSpace()
        {
            return GetUsedPercentage() > 90;
        }
    }

    /// <summary>
    /// Represents overall system statistics and metrics
    /// </summary>
    public class LidarrSystemStatistics
    {
        [JsonProperty("artistCount")]
        public int ArtistCount { get; set; }

        [JsonProperty("albumCount")]
        public int AlbumCount { get; set; }

        [JsonProperty("trackFileCount")]
        public int TrackFileCount { get; set; }

        [JsonProperty("trackCount")]
        public int TrackCount { get; set; }

        [JsonProperty("totalSize")]
        public long TotalSize { get; set; }

        /// <summary>
        /// Get total library size in GB
        /// </summary>
        public decimal GetTotalSizeGB()
        {
            return (decimal)TotalSize / (1024 * 1024 * 1024);
        }

        /// <summary>
        /// Get average file size in MB
        /// </summary>
        public decimal GetAverageFileSizeMB()
        {
            if (TrackFileCount == 0)
                return 0;

            return (decimal)TotalSize / TrackFileCount / (1024 * 1024);
        }

        /// <summary>
        /// Get percentage of tracks that have files
        /// </summary>
        public decimal GetAvailabilityPercentage()
        {
            if (TrackCount == 0)
                return 0;

            return (decimal)TrackFileCount / TrackCount * 100;
        }

        /// <summary>
        /// Get average albums per artist
        /// </summary>
        public decimal GetAverageAlbumsPerArtist()
        {
            if (ArtistCount == 0)
                return 0;

            return (decimal)AlbumCount / ArtistCount;
        }

        /// <summary>
        /// Get average tracks per album
        /// </summary>
        public decimal GetAverageTracksPerAlbum()
        {
            if (AlbumCount == 0)
                return 0;

            return (decimal)TrackCount / AlbumCount;
        }
    }
}
