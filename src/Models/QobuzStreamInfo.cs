namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Stream information returned to CLI-facing adapters after the plugin API client resolves a track URL.
    /// </summary>
    public class QobuzStreamInfo
    {
        public string? Url { get; set; }
        public int FormatId { get; set; }
        public string? MimeType { get; set; }
    }
}
