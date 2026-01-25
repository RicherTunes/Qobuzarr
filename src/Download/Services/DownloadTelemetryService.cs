using System;
using Lidarr.Plugin.Common.Services.Download;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Service for logging download telemetry information.
    /// Extracted from QobuzDownloadClient to reduce god-class complexity.
    /// </summary>
    public class DownloadTelemetryService : IDownloadTelemetryService
    {
        private readonly Logger _logger;

        public DownloadTelemetryService(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public void LogDownloadTelemetry(DownloadTelemetry telemetry)
        {
            try
            {
                var seconds = Math.Max(0.001, telemetry.Elapsed.TotalSeconds);
                var kbPerSecond = (telemetry.BytesPerSecond / 1024.0);

                if (telemetry.Success)
                {
                    _logger.Info(
                        "Download completed: track={0} album={1} bytes={2} elapsed={3:F2}s rate={4:F1}KB/s retries={5} 429s={6}",
                        telemetry.TrackId,
                        telemetry.AlbumId ?? "",
                        telemetry.BytesWritten,
                        seconds,
                        kbPerSecond,
                        telemetry.RetryCount,
                        telemetry.TooManyRequestsCount);
                }
                else
                {
                    _logger.Warn(
                        "Download failed: track={0} album={1} elapsed={2:F2}s retries={3} 429s={4} error={5}",
                        telemetry.TrackId,
                        telemetry.AlbumId ?? "",
                        seconds,
                        telemetry.RetryCount,
                        telemetry.TooManyRequestsCount,
                        telemetry.ErrorMessage ?? "");
                }
            }
            catch
            {
                // best-effort; never break downloads for telemetry
            }
        }
    }
}
