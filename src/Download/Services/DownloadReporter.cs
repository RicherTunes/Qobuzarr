using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Handles download progress reporting and summary generation
    /// </summary>
    public class DownloadReporter : IDownloadReporter
    {
        private readonly Logger _logger;

        public DownloadReporter(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void LogAlbumDownloadSummary(
            string artistName, 
            string albumTitle, 
            QobuzAlbum album,
            int successfulCount, 
            int totalCount, 
            long totalBytesDownloaded, 
            TimeSpan elapsed)
        {
            var successRate = totalCount > 0 ? (successfulCount * 100.0 / totalCount) : 0;
            var downloadSpeed = elapsed.TotalSeconds > 0 ? totalBytesDownloaded / elapsed.TotalSeconds : 0;
            
            var summaryBuilder = new StringBuilder();
            summaryBuilder.AppendLine("╔══════════════════════════════════════════════════════════════════════╗");
            summaryBuilder.AppendLine("║                     ALBUM DOWNLOAD SUMMARY                           ║");
            summaryBuilder.AppendLine("╠══════════════════════════════════════════════════════════════════════╣");
            summaryBuilder.AppendLine($"║ Artist:     {artistName,-57}║");
            summaryBuilder.AppendLine($"║ Album:      {albumTitle,-57}║");
            summaryBuilder.AppendLine($"║ Year:       {album.ReleaseDateOriginal ?? "Unknown",-57}║");
            summaryBuilder.AppendLine($"║ Label:      {album.Label?.Name ?? "Unknown",-57}║");
            summaryBuilder.AppendLine("╠══════════════════════════════════════════════════════════════════════╣");
            summaryBuilder.AppendLine($"║ Tracks:     {$"{successfulCount}/{totalCount} ({successRate:F1}%)",-57}║");
            summaryBuilder.AppendLine($"║ Size:       {FormatBytes(totalBytesDownloaded),-57}║");
            summaryBuilder.AppendLine($"║ Duration:   {$"{elapsed.TotalMinutes:F1} minutes",-57}║");
            summaryBuilder.AppendLine($"║ Speed:      {$"{FormatBytes((long)downloadSpeed)}/s",-57}║");
            summaryBuilder.AppendLine("╠══════════════════════════════════════════════════════════════════════╣");
            
            // Quality breakdown
            var qualityBreakdown = GetQualityBreakdown(album.GetTracks(), successfulCount);
            summaryBuilder.AppendLine($"║ Quality:    {qualityBreakdown,-57}║");
            
            // Success indicator
            if (successfulCount == totalCount)
            {
                summaryBuilder.AppendLine("║ Status:     ✅ Complete - All tracks downloaded successfully         ║");
            }
            else if (successfulCount > 0)
            {
                summaryBuilder.AppendLine($"║ Status:     ⚠️ Partial - {totalCount - successfulCount} tracks failed                              ║");
            }
            else
            {
                summaryBuilder.AppendLine("║ Status:     ❌ Failed - No tracks downloaded                         ║");
            }
            
            summaryBuilder.AppendLine("╚══════════════════════════════════════════════════════════════════════╝");
            
            _logger.Info(summaryBuilder.ToString());
        }

        public string GetQualityBreakdown(IList<QobuzTrack> tracks, int successfulCount)
        {
            if (tracks == null || !tracks.Any())
                return "Unknown";
                
            var qualityGroups = tracks
                .Where(t => t != null)
                .GroupBy(t => EstimateTrackQuality(t))
                .OrderByDescending(g => GetQualityPriority(g.Key))
                .Select(g => $"{GetQualityIcon(g.Key)} {g.Key}: {g.Count()}")
                .ToList();
                
            if (!qualityGroups.Any())
                return "Unknown";
                
            return string.Join(" | ", qualityGroups);
        }

        public string EstimateTrackQuality(QobuzTrack track)
        {
            if (track == null)
                return "Unknown";
                
            // Estimate based on bit depth and sample rate
            if (track.MaximumBitDepth >= 24 && track.MaximumSampleRate >= 96000)
                return "Hi-Res 96/24";
            else if (track.MaximumBitDepth >= 24 && track.MaximumSampleRate >= 48000)
                return "Hi-Res 48/24";
            else if (track.MaximumBitDepth >= 16 && track.MaximumSampleRate >= 44100)
                return "FLAC CD";
            else
                return "MP3 320";
        }

        public string GetQualityIcon(string quality)
        {
            return quality switch
            {
                "Hi-Res 192/24" => "🎵",
                "Hi-Res 96/24" => "🎵",
                "Hi-Res 48/24" => "🎶",
                "FLAC CD" => "💿",
                "MP3 320" => "🎧",
                _ => "📁"
            };
        }

        public string FormatBytes(long bytes)
        {
            if (bytes < 0)
                return "Unknown";
                
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            
            return $"{len:0.##} {sizes[order]}";
        }

        private int GetQualityPriority(string quality)
        {
            return quality switch
            {
                "Hi-Res 192/24" => 5,
                "Hi-Res 96/24" => 4,
                "Hi-Res 48/24" => 3,
                "FLAC CD" => 2,
                "MP3 320" => 1,
                _ => 0
            };
        }
    }
}