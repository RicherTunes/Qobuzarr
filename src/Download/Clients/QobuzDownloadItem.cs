using System;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Download;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Lidarr.Plugin.Qobuzarr.Download.Clients
{
    public class QobuzDownloadItem
    {
        public string DownloadId { get; set; }
        public string AlbumId { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public DownloadItemStatus Status { get; set; }
        public double Progress { get; set; }
        public long TotalSize { get; set; }
        public long DownloadedSize { get; set; }
        public DateTime StartedAt { get; set; }
        public string OutputPath { get; set; }
        public string Message { get; set; }
        public Task DownloadTask { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }
        public QobuzAlbum Album { get; set; }

        /// <summary>
        /// Calculate download speed in bytes per second
        /// </summary>
        public double GetDownloadSpeed()
        {
            var elapsed = DateTime.UtcNow - StartedAt;
            if (elapsed.TotalSeconds <= 0 || DownloadedSize <= 0)
                return 0;

            return DownloadedSize / elapsed.TotalSeconds;
        }

        /// <summary>
        /// Calculate estimated time remaining
        /// </summary>
        public TimeSpan? GetEstimatedTimeRemaining()
        {
            if (Status != DownloadItemStatus.Downloading || Progress >= 100)
                return null;

            var speed = GetDownloadSpeed();
            if (speed <= 0)
                return null;

            var remainingBytes = TotalSize - DownloadedSize;
            var remainingSeconds = remainingBytes / speed;

            return TimeSpan.FromSeconds(remainingSeconds);
        }

        /// <summary>
        /// Get human-readable status message
        /// </summary>
        public string GetStatusMessage()
        {
            return Status switch
            {
                DownloadItemStatus.Queued => "Queued for download",
                DownloadItemStatus.Downloading => $"Downloading... {Progress:F1}%",
                DownloadItemStatus.Completed => "Download completed",
                DownloadItemStatus.Failed => $"Download failed: {Message}",
                DownloadItemStatus.Warning => $"Download warning: {Message}",
                _ => "Unknown status"
            };
        }

        /// <summary>
        /// Cancel the download if it's in progress
        /// </summary>
        public void Cancel()
        {
            try
            {
                CancellationTokenSource?.Cancel();
                Status = DownloadItemStatus.Failed;
                Message = "Download cancelled by user";
            }
            catch (Exception ex)
            {
                Message = $"Error cancelling download: {ex.Message}";
            }
        }

        /// <summary>
        /// Update progress and downloaded size
        /// </summary>
        public void UpdateProgress(double progress, long downloadedSize = 0)
        {
            Progress = Math.Max(0, Math.Min(100, progress));
            
            if (downloadedSize > 0)
            {
                DownloadedSize = downloadedSize;
            }
            else if (TotalSize > 0)
            {
                // Calculate downloaded size from progress if not provided
                DownloadedSize = (long)(TotalSize * (Progress / 100.0));
            }

            // Update status based on progress
            if (Progress >= 100 && Status == DownloadItemStatus.Downloading)
            {
                Status = DownloadItemStatus.Completed;
                Message = "Download completed successfully";
            }
        }

        /// <summary>
        /// Mark download as failed with error message
        /// </summary>
        public void SetFailed(string errorMessage)
        {
            Status = DownloadItemStatus.Failed;
            Message = errorMessage;
            Progress = 0;
        }

        /// <summary>
        /// Convert to Lidarr's DownloadClientItem format
        /// </summary>
        public DownloadClientItem ToDownloadClientItem(int downloadClientId = 0, string downloadClientName = "Qobuzarr")
        {
            return new DownloadClientItem
            {
                DownloadId = DownloadId ?? "",
                Title = $"{Artist ?? "Unknown Artist"} - {Title ?? "Unknown Album"}",
                TotalSize = TotalSize,
                RemainingSize = Math.Max(0, TotalSize - DownloadedSize),
                RemainingTime = GetEstimatedTimeRemaining(),
                Status = Status,
                Message = GetStatusMessage() ?? "",
                CanMoveFiles = Status == DownloadItemStatus.Completed,
                CanBeRemoved = Status == DownloadItemStatus.Completed || Status == DownloadItemStatus.Failed,
                OutputPath = new NzbDrone.Common.Disk.OsPath(OutputPath ?? ""),
                IsEncrypted = false,
                Category = "",
                SeedRatio = null, // Not applicable for direct downloads
                Removed = false,
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = nameof(QobuzarrDownloadProtocol),
                    Type = "Qobuzarr",
                    Id = downloadClientId, // Use actual download client ID
                    Name = downloadClientName,
                    RemoveCompletedDownloads = false,
                    HasPostImportCategory = false
                }
            };
        }
    }
}