using System;

namespace Lidarr.Plugin.Qobuzarr.Plugin
{
    // Strongly-typed settings for StreamingPlugin binder
    public sealed class QobuzSettings
    {
        public string DownloadPath { get; set; } = string.Empty;
        public bool CreateAlbumFolders { get; set; } = true;
        public int PreferredQuality { get; set; } = 6; // 5=MP3 320, 6=FLAC CD, 7=FLAC 96k
        public int MinimumSuccessRatePercent { get; set; } = 80;
        public bool SkipPreviewTracks { get; set; } = true;

        // Auth (token-based)
        public string UserId { get; set; } = string.Empty;
        public string AuthToken { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty; // optional; falls back to Qobuz default
        public string CountryCode { get; set; } = "US";
        public string? Locale { get; set; } = null;
    }
}

