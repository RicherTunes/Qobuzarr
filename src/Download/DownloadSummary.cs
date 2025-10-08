using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Download
{
    /// <summary>
    /// Tracks and reports download statistics for batch operations
    /// </summary>
    public class DownloadSummary : IDownloadSummary
    {
        private readonly DateTime _startTime;
        private readonly IClock _clock = new SystemClock();
        private readonly List<AlbumDownloadResult> _albumResults;
        private long _totalBytesDownloaded;
        private readonly List<double> _downloadSpeeds;

        public DownloadSummary()
        {
            _startTime = _clock.UtcNow;
            _albumResults = new List<AlbumDownloadResult>();
            _downloadSpeeds = new List<double>();
        }

        /// <summary>
        /// Records the result of an album download
        /// </summary>
        public void RecordAlbumResult(string artist, string album, int successfulTracks, 
            int skippedTracks, int failedTracks, int totalTracks, long bytesDownloaded)
        {
            _albumResults.Add(new AlbumDownloadResult
            {
                Artist = artist,
                Album = album,
                SuccessfulTracks = successfulTracks,
                SkippedTracks = skippedTracks,
                FailedTracks = failedTracks,
                TotalTracks = totalTracks,
                BytesDownloaded = bytesDownloaded,
                CompletedAt = _clock.UtcNow
            });

            _totalBytesDownloaded += bytesDownloaded;
        }

        /// <summary>
        /// Records a download speed measurement
        /// </summary>
        public void RecordSpeed(double bytesPerSecond)
        {
            _downloadSpeeds.Add(bytesPerSecond);
        }

        /// <summary>
        /// Generates a formatted summary report
        /// </summary>
        public string GenerateReport()
        {
            var elapsed = _clock.UtcNow - _startTime;
            var sb = new StringBuilder();

            // Header
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("           Download Summary            ");
            sb.AppendLine("═══════════════════════════════════════");

            // Album Statistics
            var completedAlbums = _albumResults.Count(a => a.FailedTracks == 0);
            var partialAlbums = _albumResults.Count(a => a.FailedTracks > 0 && a.SuccessfulTracks > 0);
            var failedAlbums = _albumResults.Count(a => a.SuccessfulTracks == 0 && a.FailedTracks > 0);

            sb.AppendLine();
            sb.AppendLine($"✅ Completed: {completedAlbums} album{(completedAlbums != 1 ? "s" : "")} ({FormatBytes(_albumResults.Where(a => a.FailedTracks == 0).Sum(a => a.BytesDownloaded))})");
            
            if (partialAlbums > 0)
            {
                var partialResults = _albumResults.Where(a => a.FailedTracks > 0 && a.SuccessfulTracks > 0).ToList();
                var totalPartialTracks = partialResults.Sum(a => a.SuccessfulTracks);
                var totalPartialTotal = partialResults.Sum(a => a.TotalTracks);
                sb.AppendLine($"⚠️  Partial: {partialAlbums} album{(partialAlbums != 1 ? "s" : "")} ({totalPartialTracks}/{totalPartialTotal} tracks)");
            }
            
            if (failedAlbums > 0)
            {
                sb.AppendLine($"❌ Failed: {failedAlbums} album{(failedAlbums != 1 ? "s" : "")}");
            }

            // Track Statistics
            var totalSuccessfulTracks = _albumResults.Sum(a => a.SuccessfulTracks);
            var totalSkippedTracks = _albumResults.Sum(a => a.SkippedTracks);
            var totalFailedTracks = _albumResults.Sum(a => a.FailedTracks);
            var totalTracks = _albumResults.Sum(a => a.TotalTracks);

            sb.AppendLine();
            sb.AppendLine("Track Statistics:");
            sb.AppendLine($"  • Downloaded: {totalSuccessfulTracks} tracks");
            if (totalSkippedTracks > 0)
                sb.AppendLine($"  • Skipped: {totalSkippedTracks} tracks (already exist)");
            if (totalFailedTracks > 0)
                sb.AppendLine($"  • Failed: {totalFailedTracks} tracks");

            // Performance Statistics
            sb.AppendLine();
            sb.AppendLine("Performance:");
            sb.AppendLine($"⏱️  Total time: {FormatDuration(elapsed)}");
            sb.AppendLine($"💾 Total size: {FormatBytes(_totalBytesDownloaded)}");
            
            if (_downloadSpeeds.Any())
            {
                var avgSpeed = _downloadSpeeds.Average();
                var maxSpeed = _downloadSpeeds.Max();
                sb.AppendLine($"📊 Avg speed: {FormatBytes((long)avgSpeed)}/s");
                sb.AppendLine($"🚀 Peak speed: {FormatBytes((long)maxSpeed)}/s");
            }
            else if (_totalBytesDownloaded > 0 && elapsed.TotalSeconds > 0)
            {
                var avgSpeed = _totalBytesDownloaded / elapsed.TotalSeconds;
                sb.AppendLine($"📊 Avg speed: {FormatBytes((long)avgSpeed)}/s");
            }

            // Success Rate
            if (totalTracks > 0)
            {
                var successRate = (double)totalSuccessfulTracks / totalTracks * 100;
                sb.AppendLine($"📈 Success rate: {successRate:F1}%");
            }

            // Failed Album Details (if any)
            if (partialAlbums > 0 || failedAlbums > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Issues encountered:");
                
                foreach (var album in _albumResults.Where(a => a.FailedTracks > 0).Take(5))
                {
                    sb.AppendLine($"  • {album.Artist} - {album.Album}");
                    sb.AppendLine($"    Failed: {album.FailedTracks}/{album.TotalTracks} tracks");
                }

                var moreIssues = _albumResults.Count(a => a.FailedTracks > 0) - 5;
                if (moreIssues > 0)
                {
                    sb.AppendLine($"  ... and {moreIssues} more album{(moreIssues != 1 ? "s" : "")} with issues");
                }
            }

            sb.AppendLine("═══════════════════════════════════════");

            return sb.ToString();
        }

        /// <summary>
        /// Gets a brief inline summary for logging
        /// </summary>
        public string GetBriefSummary()
        {
            var elapsed = _clock.UtcNow - _startTime;
            var completedAlbums = _albumResults.Count(a => a.FailedTracks == 0);
            var totalAlbums = _albumResults.Count;
            
            return $"Downloaded {completedAlbums}/{totalAlbums} albums in {FormatDuration(elapsed)} ({FormatBytes(_totalBytesDownloaded)})";
        }

        /// <summary>
        /// Gets the total number of albums processed
        /// </summary>
        public int GetTotalAlbums()
        {
            return _albumResults.Count;
        }

        /// <summary>
        /// Gets the total bytes downloaded
        /// </summary>
        public long GetTotalBytesDownloaded()
        {
            return _totalBytesDownloaded;
        }

        /// <summary>
        /// Gets the average download speed
        /// </summary>
        public double GetAverageSpeed()
        {
            if (_downloadSpeeds.Any())
            {
                return _downloadSpeeds.Average();
            }
            
            var elapsed = DateTime.UtcNow - _startTime;
            if (_totalBytesDownloaded > 0 && elapsed.TotalSeconds > 0)
            {
                return _totalBytesDownloaded / elapsed.TotalSeconds;
            }
            
            return 0;
        }

        /// <summary>
        /// Resets all statistics
        /// </summary>
        public void Reset()
        {
            _albumResults.Clear();
            _downloadSpeeds.Clear();
            _totalBytesDownloaded = 0;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1048576)
                return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1073741824)
                return $"{bytes / 1048576.0:F1} MB";
            return $"{bytes / 1073741824.0:F2} GB";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
            if (duration.TotalMinutes >= 1)
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            return $"{duration.TotalSeconds:F1}s";
        }

        private class AlbumDownloadResult
        {
            public string Artist { get; set; }
            public string Album { get; set; }
            public int SuccessfulTracks { get; set; }
            public int SkippedTracks { get; set; }
            public int FailedTracks { get; set; }
            public int TotalTracks { get; set; }
            public long BytesDownloaded { get; set; }
            public DateTime CompletedAt { get; set; }
        }
    }
}
