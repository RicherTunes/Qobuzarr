using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    public interface ICacheEvictionStrategy<TEntry> where TEntry : class
    {
        string StrategyName { get; }
        double EvictionPercentage { get; set; }
        IEnumerable<TEntry> SelectEntriesForEviction(IEnumerable<TEntry> allEntries, int maxCacheSize, int currentSize);
    }
}

