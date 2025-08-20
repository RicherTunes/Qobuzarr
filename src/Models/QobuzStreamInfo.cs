namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Stream information from Qobuz API
    /// </summary>
    public class QobuzStreamInfo
    {
        public string? Url { get; set; }
        public int FormatId { get; set; }
        public string? MimeType { get; set; }
    }
}