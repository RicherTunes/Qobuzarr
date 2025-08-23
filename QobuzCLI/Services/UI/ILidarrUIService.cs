using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace QobuzCLI.Services.UI
{
    /// <summary>
    /// Service responsible for rendering UI elements for Lidarr commands.
    /// Centralizes all display logic to maintain consistency.
    /// </summary>
    public interface ILidarrUIService
    {
        /// <summary>
        /// Displays a summary table of albums.
        /// </summary>
        void DisplayAlbumSummary(IEnumerable<LidarrAlbum> albums, int maxRows = 10);

        /// <summary>
        /// Displays validation results for album downloads.
        /// </summary>
        void DisplayValidationSummary(IList<AlbumDownloadItem> validatedItems, int maxRows = 10);

        /// <summary>
        /// Displays quality profile information.
        /// </summary>
        void DisplayQualityProfileSummary(IList<AlbumDownloadItem> validatedItems);

        /// <summary>
        /// Displays dry-run results.
        /// </summary>
        void DisplayDryRunResults(IList<AlbumDownloadItem> validatedItems, bool immediate, string quality);

        /// <summary>
        /// Displays export summary statistics.
        /// </summary>
        void DisplayExportSummary(List<LidarrAlbum> albums, string format, bool verbose);

        /// <summary>
        /// Displays a progress bar or status.
        /// </summary>
        void ShowProgress(string message, int current, int total);

        /// <summary>
        /// Displays an error message with optional details.
        /// </summary>
        void ShowError(string message, string details = null, bool verbose = false);

        /// <summary>
        /// Displays a success message.
        /// </summary>
        void ShowSuccess(string message);

        /// <summary>
        /// Displays a warning message.
        /// </summary>
        void ShowWarning(string message);

        /// <summary>
        /// Displays an information message.
        /// </summary>
        void ShowInfo(string message);
    }
}