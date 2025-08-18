namespace Lidarr.Plugin.Qobuzarr.Download
{
    /// <summary>
    /// Represents the result of a single track download attempt.
    /// Used for tracking success/failure status and reasons for batch downloads.
    /// </summary>
    public class TrackDownloadResult
    {
        /// <summary>
        /// Whether the track download was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The Qobuz track ID that was attempted for download.
        /// </summary>
        public string TrackId { get; set; }

        /// <summary>
        /// If the download failed, the specific reason for failure.
        /// </summary>
        public TrackUnavailableReason? Reason { get; set; }

        /// <summary>
        /// Human-readable message describing the result.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Path to the downloaded file if successful.
        /// </summary>
        public string FilePath { get; set; }
    }
}