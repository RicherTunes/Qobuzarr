using System;
using Lidarr.Plugin.Qobuzarr.Download;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// CLI-compatible download result type that bridges to the plugin's TrackDownloadResult.
    /// Provides backward compatibility for CLI without changing core plugin architecture.
    /// </summary>
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string TrackId { get; set; }
        public string Message { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration => CompletedAt - StartedAt;

        /// <summary>
        /// Create from plugin's TrackDownloadResult for CLI compatibility.
        /// </summary>
        public static DownloadResult FromTrackResult(TrackDownloadResult trackResult)
        {
            long fileSize = 0;
            if (!string.IsNullOrEmpty(trackResult.FilePath) && System.IO.File.Exists(trackResult.FilePath))
            {
                fileSize = new System.IO.FileInfo(trackResult.FilePath).Length;
            }

            return new DownloadResult
            {
                Success = trackResult.Success,
                TrackId = trackResult.TrackId,
                Message = trackResult.Message,
                FilePath = trackResult.FilePath,
                FileSize = fileSize,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };
        }
    }

    // Playlist and Label download results moved to Download.Services namespace to avoid ambiguity
}
