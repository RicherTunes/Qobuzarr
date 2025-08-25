namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Quality format definition with immutable properties.
    /// </summary>
    public class QualityFormat
    {
        public int Id { get; init; }
        public string Name { get; init; }
        public string DisplayName { get; init; }
        public int BitRate { get; init; }
        public bool IsLossless { get; init; }
        public int Priority { get; init; }
    }
}