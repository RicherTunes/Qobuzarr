using Lidarr.Plugin.Qobuzarr.Services;
using System.Linq;

namespace QobuzCLI.Services.Adapters
{
    /// <summary>
    /// Extension methods for plugin's DownloadResult to provide CLI-friendly interface.
    /// Follows plugin-first architecture from CLAUDE.md.
    /// </summary>
    public static class DownloadResultExtensions
    {
        /// <summary>
        /// Checks if the download was successful (as a method for CLI compatibility)
        /// </summary>
        public static bool IsSuccessful(this DownloadResult result)
        {
            return result.IsSuccessful;
        }

        /// <summary>
        /// Gets the number of successfully downloaded tracks
        /// </summary>
        public static int GetTracksDownloaded(this DownloadResult result)
        {
            return result.TrackDownloads?.Count(t => !string.IsNullOrEmpty(t.StreamingUrl)) ?? 0;
        }

        /// <summary>
        /// Gets a summary message for display
        /// </summary>
        public static string GetSummaryMessage(this DownloadResult result)
        {
            if (result.IsSuccessful)
            {
                var tracksCount = result.GetTracksDownloaded();
                return $"Successfully downloaded {tracksCount} track{(tracksCount != 1 ? "s" : "")} using {result.MetadataStrategy} strategy";
            }
            else
            {
                return $"Download failed - no tracks downloaded";
            }
        }
    }
}