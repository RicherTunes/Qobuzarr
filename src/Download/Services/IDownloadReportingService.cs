using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Service for generating and logging download completion summaries.
    /// Provides formatted output for album download results including
    /// track counts, quality breakdown, and file size information.
    /// </summary>
    public interface IDownloadReportingService
    {
        /// <summary>
        /// Logs a formatted summary of an album download operation.
        /// </summary>
        /// <param name="artistName">Artist name</param>
        /// <param name="albumTitle">Album title</param>
        /// <param name="album">Album metadata including tracks and release date</param>
        /// <param name="successful">Number of successfully downloaded tracks</param>
        /// <param name="skipped">Number of skipped tracks (preview-only, etc.)</param>
        /// <param name="failed">Number of failed tracks</param>
        /// <param name="total">Total number of tracks</param>
        /// <param name="bytesDownloaded">Total bytes downloaded</param>
        void LogAlbumDownloadSummary(string artistName, string albumTitle, QobuzAlbum album,
            int successful, int skipped, int failed, int total, long bytesDownloaded);
    }
}
