using System;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Lidarr.Plugin.Common.HostBridge;

namespace Lidarr.Plugin.Qobuzarr.Download.Clients
{
    /// <summary>
    /// Qobuz-specific download tracker item. Extends <see cref="HostBridgeDownloadItem"/>
    /// (Option A subclass) to inherit thread-safe Status/Progress/CompletedAt fields from
    /// Common, while adding the Qobuz-only extras that don't belong in the shared base:
    /// <list type="bullet">
    ///   <item><see cref="CancellationTokenSource"/> — user cancel path</item>
    ///   <item><see cref="DownloadTask"/> — in-flight Task reference for graceful shutdown</item>
    ///   <item><see cref="Album"/> — Qobuz album model needed by track-download orchestration</item>
    ///   <item><see cref="DownloadedSize"/> — byte counter for speed/ETA calculation</item>
    ///   <item>Quality fallback tracking (count + example string)</item>
    /// </list>
    /// Adopted by Wave 10B; replaces the standalone <c>IDisposable</c> class.
    /// </summary>
    public class QobuzDownloadItem : HostBridgeDownloadItem, IDisposable
    {
        private bool _disposed = false;
        private int _qualityFallbackCount;
        private string? _qualityFallbackExample;

        // Qobuz-only extras ──────────────────────────────────────────────────────────────
        public Task? DownloadTask { get; set; }
        public CancellationTokenSource? CancellationTokenSource { get; set; }
        public QobuzAlbum? Album { get; set; }

        /// <summary>Bytes written to disk so far. Used for speed + ETA calculations.</summary>
        public long DownloadedSize { get; set; }

        /// <summary>Status message (e.g. failure reason, completion note).</summary>
        public string? Message { get; set; }

        // Quality fallback tracking ───────────────────────────────────────────────────────
        public int QualityFallbackCount => _qualityFallbackCount;
        public string? QualityFallbackExample => _qualityFallbackExample;

        // HostBridgeDownloadItem status bridging ─────────────────────────────────────────

        /// <summary>
        /// Gets the Lidarr-host <see cref="DownloadItemStatus"/> by mapping from the
        /// thread-safe Common enum. Call sites that previously accessed <c>.Status</c>
        /// directly now call this helper at the Lidarr boundary.
        /// </summary>
        public DownloadItemStatus GetHostStatus() => GetStatus() switch
        {
            HostBridgeDownloadItemStatus.Queued       => DownloadItemStatus.Queued,
            HostBridgeDownloadItemStatus.Downloading  => DownloadItemStatus.Downloading,
            HostBridgeDownloadItemStatus.Completed    => DownloadItemStatus.Completed,
            HostBridgeDownloadItemStatus.Failed       => DownloadItemStatus.Failed,
            _                                         => DownloadItemStatus.Failed,
        };

        /// <summary>Sets the internal status from a Lidarr-host enum value.</summary>
        public void SetHostStatus(DownloadItemStatus value)
        {
            SetStatus(value switch
            {
                DownloadItemStatus.Queued       => HostBridgeDownloadItemStatus.Queued,
                DownloadItemStatus.Downloading  => HostBridgeDownloadItemStatus.Downloading,
                DownloadItemStatus.Completed    => HostBridgeDownloadItemStatus.Completed,
                DownloadItemStatus.Warning      => HostBridgeDownloadItemStatus.Failed, // no Warning in Common
                _                               => HostBridgeDownloadItemStatus.Failed,
            });

            // Mirror CompletedAt for the retention sweep whenever we reach a terminal state.
            if (value == DownloadItemStatus.Completed || value == DownloadItemStatus.Failed ||
                value == DownloadItemStatus.Warning)
            {
                if (!CompletedAt.HasValue)
                    CompletedAt = DateTime.UtcNow;
            }
        }

        // ────────────────────────────────────────────────────────────────────────────────

        public void RecordQualityFallback(int requestedFormatId, int actualFormatId)
        {
            Interlocked.Increment(ref _qualityFallbackCount);
            Interlocked.CompareExchange(
                ref _qualityFallbackExample,
                QualityFormatter.FormatQualityFallback(actualFormatId, requestedFormatId),
                null);
        }

        /// <summary>Calculate download speed in bytes per second.</summary>
        public double GetDownloadSpeed()
        {
            var elapsed = DateTime.UtcNow - StartedAt;
            if (elapsed.TotalSeconds <= 0 || DownloadedSize <= 0)
                return 0;

            return DownloadedSize / elapsed.TotalSeconds;
        }

        /// <summary>Calculate estimated time remaining.</summary>
        public TimeSpan? GetEstimatedTimeRemaining()
        {
            if (GetHostStatus() != DownloadItemStatus.Downloading || GetProgress() >= 100)
                return null;

            var speed = GetDownloadSpeed();
            if (speed <= 0)
                return null;

            var remainingBytes = TotalSize - DownloadedSize;
            var remainingSeconds = remainingBytes / speed;

            return TimeSpan.FromSeconds(remainingSeconds);
        }

        /// <summary>Get human-readable status message.</summary>
        public string GetStatusMessage()
        {
            var status = GetHostStatus();
            var progress = GetProgress();
            return status switch
            {
                DownloadItemStatus.Queued       => "Queued for download",
                DownloadItemStatus.Downloading  => $"Downloading... {progress:F1}%",
                DownloadItemStatus.Completed    => QualityFallbackCount > 0
                    ? $"Download completed (quality fallback used for {QualityFallbackCount} track(s){(QualityFallbackExample != null ? $": {QualityFallbackExample}" : "")})"
                    : "Download completed",
                DownloadItemStatus.Failed       => $"Download failed: {Message}",
                DownloadItemStatus.Warning      => $"Download warning: {Message}",
                _                               => "Unknown status"
            };
        }

        /// <summary>Cancel the download if it's in progress.</summary>
        public void Cancel()
        {
            if (_disposed)
            {
                Message = "Cannot cancel - download item already disposed";
                return;
            }

            try
            {
                CancellationTokenSource?.Cancel();
                SetHostStatus(DownloadItemStatus.Failed);
                Message = "Download cancelled by user";
            }
            catch (ObjectDisposedException)
            {
                SetHostStatus(DownloadItemStatus.Failed);
                Message = "Download cancelled (already disposed)";
            }
            catch (Exception ex)
            {
                Message = $"Error cancelling download: {ex.Message}";
            }
        }

        /// <summary>Update progress and downloaded size.</summary>
        public void UpdateProgress(double progress, long downloadedSize = 0)
        {
            var clamped = Math.Max(0, Math.Min(100, progress));
            SetProgress(clamped);

            if (downloadedSize > 0)
            {
                DownloadedSize = downloadedSize;
            }
            else if (TotalSize > 0)
            {
                DownloadedSize = (long)(TotalSize * (clamped / 100.0));
            }

            // Automatically transition to Completed when progress hits 100.
            if (clamped >= 100 && GetStatus() == HostBridgeDownloadItemStatus.Downloading)
            {
                SetHostStatus(DownloadItemStatus.Completed);
                Message = "Download completed successfully";
            }
        }

        /// <summary>Mark download as failed with error message.</summary>
        public void SetFailed(string errorMessage)
        {
            SetHostStatus(DownloadItemStatus.Failed);
            Message = errorMessage;
            SetProgress(0);
        }

        /// <summary>Convert to Lidarr's DownloadClientItem format.</summary>
        public DownloadClientItem ToDownloadClientItem(int downloadClientId = 0, string? downloadClientName = null)
        {
            var status = GetHostStatus();
            var progress = GetProgress();
            return new DownloadClientItem
            {
                DownloadId = DownloadId ?? "",
                Title = $"{Artist ?? "Unknown Artist"} - {Title ?? "Unknown Album"}",
                TotalSize = TotalSize,
                RemainingSize = Math.Max(0, TotalSize - DownloadedSize),
                RemainingTime = GetEstimatedTimeRemaining(),
                Status = status,
                Message = GetStatusMessage() ?? "",
                CanMoveFiles = status == DownloadItemStatus.Completed && !string.IsNullOrEmpty(OutputPath),
                CanBeRemoved = status == DownloadItemStatus.Completed || status == DownloadItemStatus.Failed,
                OutputPath = new NzbDrone.Common.Disk.OsPath(OutputPath ?? ""),
                IsEncrypted = false,
                Category = QobuzarrConstants.DownloadCategory,
                SeedRatio = null,
                Removed = false,
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = nameof(QobuzarrDownloadProtocol),
                    Type = QobuzarrConstants.PluginName,
                    Id = downloadClientId,
                    Name = downloadClientName ?? QobuzarrConstants.PluginName,
                    RemoveCompletedDownloads = true,
                    HasPostImportCategory = true
                }
            };
        }

        // IDisposable ────────────────────────────────────────────────────────────────────

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        CancellationTokenSource?.Cancel();
                        CancellationTokenSource?.Dispose();
                    }
                    catch (ObjectDisposedException) { }
                    catch (Exception ex)
                    {
                        Message = $"Error during disposal: {ex.Message}";
                    }
                }

                CancellationTokenSource = null;
                DownloadTask = null;
                _disposed = true;
            }
        }

        ~QobuzDownloadItem()
        {
            Dispose(false);
        }
    }
}
