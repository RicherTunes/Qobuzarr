namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Represents the status of a Qobuz download operation
    /// </summary>
    public enum QobuzDownloadStatus
    {
        /// <summary>
        /// Download is queued for processing
        /// </summary>
        Queued = 0,

        /// <summary>
        /// Download is currently in progress
        /// </summary>
        Downloading = 1,

        /// <summary>
        /// Download completed successfully
        /// </summary>
        Completed = 2,

        /// <summary>
        /// Download failed with an error
        /// </summary>
        Failed = 3,

        /// <summary>
        /// Download was cancelled by user
        /// </summary>
        Cancelled = 4,

        /// <summary>
        /// Download is paused
        /// </summary>
        Paused = 5
    }
}