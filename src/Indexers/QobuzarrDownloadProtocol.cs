namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Download protocol marker for Qobuzarr plugin
    /// This registers "Qobuzarr" as a valid download protocol in Lidarr's UI
    /// </summary>
    public static class QobuzarrDownloadProtocol
    {
        // This is a constant that identifies our protocol type
        // Lidarr will use this string value to identify our protocol
        public const string Name = "Qobuzarr";
    }
}