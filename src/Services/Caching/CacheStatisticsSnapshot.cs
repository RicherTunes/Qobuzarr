using System;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    /// <summary>
    /// Snapshot of cache statistics for reporting and monitoring
    /// </summary>
    public class CacheStatisticsSnapshot
    {
        public int TotalEntries { get; set; }
        public int TotalHits { get; set; }
        public int TotalMisses { get; set; }
        public int UniqueArtists { get; set; }
        public int UniqueAlbums { get; set; }
        public double AverageHitsPerEntry { get; set; }
        public long CacheSizeBytes { get; set; }
        public double HitRate { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

