using Lidarr.Plugin.Common.Services.Download;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Service for logging download telemetry information.
    /// Provides formatted logging for download performance and error tracking.
    /// </summary>
    public interface IDownloadTelemetryService
    {
        /// <summary>
        /// Logs telemetry data for a download operation.
        /// </summary>
        /// <param name="telemetry">Telemetry data including success status, bytes transferred, timing, and error information</param>
        void LogDownloadTelemetry(DownloadTelemetry telemetry);
    }
}
