using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    public interface ICacheStatistics
    {
        int TotalHits { get; }
        int TotalMisses { get; }
        double HitRate { get; }
        void RecordHit(string key);
        void RecordMiss(string key);
        int GetHitCount(string key);
        int GetMissCount(string key);
        IEnumerable<string> GetHitKeys();
        IEnumerable<string> GetMissKeys();
        CacheStatisticsSnapshot GetStatistics(int totalEntries, int uniqueArtists, int uniqueAlbums);
        void RemoveKey(string key);
        void Clear();
    }
}

