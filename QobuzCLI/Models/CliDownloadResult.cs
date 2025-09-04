using System;
using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Download.Services;

namespace QobuzCLI.Models
{
    /// <summary>
    /// CLI-specific download result that includes all the fields the CLI expects.
    /// This adapts between the simplified plugin DownloadResult and the richer CLI needs.
    /// </summary>
    public class CliDownloadResult
    {
        // Core properties from plugin's DownloadResult
        public bool Success { get; set; }
        public string TrackId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration => CompletedAt - StartedAt;

        // Extended properties for CLI
        public List<TrackDownloadInfo> TrackDownloads { get; set; } = new();
        public string MetadataStrategy { get; set; } = "Standard";
        public int ApiCallsSaved { get; set; } = 0;
        public int AdditionalApiCalls { get; set; } = 0;
        
        // Compatibility properties
        public bool IsSuccessful => Success;
        public DateTime StartTime => StartedAt;
        public DateTime EndTime => CompletedAt;

        /// <summary>
        /// Create from plugin's simplified DownloadResult
        /// </summary>
        public static CliDownloadResult FromPluginResult(Lidarr.Plugin.Qobuzarr.Services.DownloadResult pluginResult)
        {
            return new CliDownloadResult
            {
                Success = pluginResult.Success,
                TrackId = pluginResult.TrackId,
                Message = pluginResult.Message,
                FilePath = pluginResult.FilePath,
                FileSize = pluginResult.FileSize,
                StartedAt = pluginResult.StartedAt,
                CompletedAt = pluginResult.CompletedAt,
                TrackDownloads = new List<TrackDownloadInfo>
                {
                    // Create a single track download info from the result
                    new TrackDownloadInfo
                    {
                        TrackId = pluginResult.TrackId,
                        StreamingUrl = pluginResult.FilePath,
                        Success = pluginResult.Success,
                        Message = pluginResult.Message
                    }
                }
            };
        }

        /// <summary>
        /// Create from plugin's TrackDownloadResult
        /// </summary>
        public static CliDownloadResult FromTrackResult(TrackDownloadResult trackResult)
        {
            long fileSize = 0;
            if (!string.IsNullOrEmpty(trackResult.FilePath) && System.IO.File.Exists(trackResult.FilePath))
            {
                fileSize = new System.IO.FileInfo(trackResult.FilePath).Length;
            }

            return new CliDownloadResult
            {
                Success = trackResult.Success,
                TrackId = trackResult.TrackId,
                Message = trackResult.Message,
                FilePath = trackResult.FilePath,
                FileSize = fileSize,
                StartedAt = DateTime.UtcNow.AddSeconds(-5), // Estimate
                CompletedAt = DateTime.UtcNow,
                TrackDownloads = new List<TrackDownloadInfo>
                {
                    new TrackDownloadInfo
                    {
                        TrackId = trackResult.TrackId,
                        StreamingUrl = trackResult.FilePath,
                        Success = trackResult.Success,
                        Message = trackResult.Message
                    }
                }
            };
        }

        /// <summary>
        /// Create empty failure result
        /// </summary>
        public static CliDownloadResult Failure(string message)
        {
            return new CliDownloadResult
            {
                Success = false,
                Message = message,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// CLI-specific playlist download result with all expected properties
    /// </summary>
    public class CliPlaylistDownloadResult : PlaylistDownloadResult
    {
        // Additional CLI-specific properties
        public string M3u8FilePath { get; set; } = string.Empty;
        public bool IsSuccessful => Success;
        public DateTime StartTime => StartedAt;
        public DateTime EndTime => CompletedAt;
        public List<TrackDownloadInfo> DownloadedTracks { get; set; } = new();

        /// <summary>
        /// Create from plugin's PlaylistDownloadResult
        /// </summary>
        public static CliPlaylistDownloadResult FromPluginResult(PlaylistDownloadResult pluginResult)
        {
            return new CliPlaylistDownloadResult
            {
                Success = pluginResult.Success,
                PlaylistId = pluginResult.PlaylistId,
                PlaylistName = pluginResult.PlaylistName,
                TotalTracks = pluginResult.TotalTracks,
                SuccessfulTracks = pluginResult.SuccessfulTracks,
                FailedTracks = pluginResult.FailedTracks,
                Message = pluginResult.Message,
                StartedAt = pluginResult.StartedAt,
                CompletedAt = pluginResult.CompletedAt
            };
        }
    }

    /// <summary>
    /// Track download info for CLI
    /// </summary>
    public class TrackDownloadInfo
    {
        public string TrackId { get; set; } = string.Empty;
        public string StreamingUrl { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int Position { get; set; }
        public bool Skipped { get; set; }
    }
}
