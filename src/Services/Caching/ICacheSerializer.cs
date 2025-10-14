using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    public interface ICacheSerializer<TEntry> where TEntry : class
    {
        string SerializationFormat { get; }
        string SerializeEntries(IEnumerable<TEntry> entries);
        IEnumerable<TEntry> DeserializeEntries(string serializedData);
        string SerializeEntry(TEntry entry);
    }
}

