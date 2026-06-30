using System;

namespace Lidarr.Plugin.Qobuzarr.Download
{
    /// <summary>
    /// Interface for tracking and reporting download statistics for batch operations
    /// </summary>
    public interface IDownloadSummary
    {
        /// <summary>
        /// Records the result of an album download
        /// </summary>
        void RecordAlbumResult(string artist, string album, int successfulTracks,
            int skippedTracks, int failedTracks, int totalTracks, long bytesDownloaded);

        /// <summary>
        /// Records a download speed measurement
        /// </summary>
        void RecordSpeed(double bytesPerSecond);

        /// <summary>
        /// Generates a summary report of all downloads
        /// </summary>
        string GenerateReport();

        /// <summary>
        /// Gets a compact summary suitable for per-album progress logging.
        /// </summary>
        string GetBriefSummary();

        /// <summary>
        /// Gets the total number of albums processed
        /// </summary>
        int GetTotalAlbums();

        /// <summary>
        /// Gets the total bytes downloaded
        /// </summary>
        long GetTotalBytesDownloaded();

        /// <summary>
        /// Gets the average download speed
        /// </summary>
        double GetAverageSpeed();

        /// <summary>
        /// Resets all statistics
        /// </summary>
        void Reset();
    }
}
