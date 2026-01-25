using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Models;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Service for generating and logging download completion summaries.
    /// Extracted from QobuzDownloadClient to reduce god-class complexity.
    /// </summary>
    public class DownloadReportingService : IDownloadReportingService
    {
        private readonly Logger _logger;

        public DownloadReportingService(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public void LogAlbumDownloadSummary(string artistName, string albumTitle, QobuzAlbum album,
            int successful, int skipped, int failed, int total, long bytesDownloaded)
        {
            try
            {
                // Format: ✅ Artist - Album Title (Year) → 12/14 tracks (85%) → 8×📀FLAC-96 + 4×💿FLAC-CD → 847.2MB → 2 preview-only skipped
                var albumYear = album?.ReleaseDate.Year > 1900 ? album.ReleaseDate.Year.ToString() : "";
                var albumInfo = !string.IsNullOrEmpty(albumYear) ? $"{artistName} - {albumTitle} ({albumYear})" : $"{artistName} - {albumTitle}";

                var completionRate = total > 0 ? (int)Math.Round((double)successful / total * 100) : 0;
                var tracksInfo = $"{successful}/{total} tracks ({completionRate}%)";

                var sizeInfo = FormatBytes(bytesDownloaded);

                var summaryParts = new List<string>
                {
                    $"✅ {albumInfo}",
                    tracksInfo
                };

                // Add quality information if we have album data
                if (album?.GetTracks()?.Any() == true)
                {
                    var qualityBreakdown = GetQualityBreakdown(album.GetTracks(), successful);
                    if (!string.IsNullOrEmpty(qualityBreakdown))
                    {
                        summaryParts.Add(qualityBreakdown);
                    }
                }

                summaryParts.Add(sizeInfo);

                // Add issues summary if any
                if (skipped > 0 || failed > 0)
                {
                    var issues = new List<string>();
                    if (skipped > 0) issues.Add($"{skipped} preview-only skipped");
                    if (failed > 0) issues.Add($"{failed} failed");
                    summaryParts.Add(string.Join(", ", issues));
                }

                _logger.Info(string.Join(" → ", summaryParts));
            }
            catch (Exception ex)
            {
                // Fallback to simple summary if enhanced formatting fails
                _logger.Info("✅ Album download completed: {0} successful, {1} skipped, {2} failed out of {3} total tracks",
                    successful, skipped, failed, total);
                _logger.Debug(ex, "Error formatting enhanced album summary");
            }
        }

        private string GetQualityBreakdown(IList<QobuzTrack> tracks, int successfulCount)
        {
            if (tracks == null || !tracks.Any() || successfulCount == 0)
                return "";

            // This is a simplified quality breakdown - in reality we'd need to track what quality each track was downloaded in
            // For now, provide a reasonable estimate based on the tracks available
            var qualityEstimates = new Dictionary<string, int>();

            // Analyze track qualities (this is estimated since we don't track actual download quality here)
            foreach (var track in tracks.Take(successfulCount))
            {
                // This is a placeholder - ideally we'd track actual download quality per track
                var estimatedQuality = EstimateTrackQuality(track);
                if (qualityEstimates.ContainsKey(estimatedQuality))
                    qualityEstimates[estimatedQuality]++;
                else
                    qualityEstimates[estimatedQuality] = 1;
            }

            var qualityParts = qualityEstimates
                .Where(kv => kv.Value > 0)
                .Select(kv => $"{kv.Value}×{GetQualityIcon(kv.Key)}")
                .ToList();

            return qualityParts.Any() ? string.Join(" + ", qualityParts) : "";
        }

        private string EstimateTrackQuality(QobuzTrack track)
        {
            // This is a simplified estimation - in a full implementation we'd track actual download quality
            if (track.MaximumBitDepth >= 24 && track.MaximumSampleRate >= 192000)
                return "FLAC-192";
            else if (track.MaximumBitDepth >= 24 && track.MaximumSampleRate >= 96000)
                return "FLAC-96";
            else if (track.MaximumBitDepth >= 16)
                return "FLAC-CD";
            else
                return "MP3-320";
        }

        private string GetQualityIcon(string quality)
        {
            return quality switch
            {
                "FLAC-192" => "📀FLAC-192",
                "FLAC-96" => "📀FLAC-96",
                "FLAC-CD" => "💿FLAC-CD",
                "MP3-320" => "🎵MP3-320",
                _ => $"🎧{quality}"
            };
        }

        private string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";

            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                size /= 1024;
                order++;
            }

            return $"{size:F1}{sizes[order]}";
        }
    }
}
