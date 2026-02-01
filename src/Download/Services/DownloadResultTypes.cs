using System;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Playlist download result for CLI compatibility.
    /// </summary>
    public class PlaylistDownloadResult
    {
        public bool Success { get; set; }
        public string PlaylistId { get; set; }
        public string PlaylistName { get; set; }
        public int TotalTracks { get; set; }
        public int SuccessfulTracks { get; set; }
        public int FailedTracks { get; set; }
        public string Message { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public double SuccessRate => TotalTracks > 0 ? (double)SuccessfulTracks / TotalTracks : 0;
    }

    /// <summary>
    /// Label download result for CLI compatibility.
    /// </summary>  
    public class LabelDownloadResult
    {
        public bool Success { get; set; }
        public string LabelId { get; set; }
        public string LabelName { get; set; }
        public int TotalAlbums { get; set; }
        public int SuccessfulAlbums { get; set; }
        public int FailedAlbums { get; set; }
        public string Message { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public double SuccessRate => TotalAlbums > 0 ? (double)SuccessfulAlbums / TotalAlbums : 0;
    }
}
