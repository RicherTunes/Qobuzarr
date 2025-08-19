using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Handles download progress reporting and summary generation
    /// </summary>
    public interface IDownloadReporter
    {
        /// <summary>
        /// Logs a comprehensive download summary for an album
        /// </summary>
        void LogAlbumDownloadSummary(
            string artistName, 
            string albumTitle, 
            QobuzAlbum album,
            int successfulCount, 
            int totalCount, 
            long totalBytesDownloaded, 
            System.TimeSpan elapsed);
        
        /// <summary>
        /// Gets quality breakdown for tracks
        /// </summary>
        string GetQualityBreakdown(IList<QobuzTrack> tracks, int successfulCount);
        
        /// <summary>
        /// Estimates track quality based on metadata
        /// </summary>
        string EstimateTrackQuality(QobuzTrack track);
        
        /// <summary>
        /// Gets quality icon for display
        /// </summary>
        string GetQualityIcon(string quality);
        
        /// <summary>
        /// Formats bytes for human-readable display
        /// </summary>
        string FormatBytes(long bytes);
    }
}