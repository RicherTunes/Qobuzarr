using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Core
{
    /// <summary>
    /// Service for checking existing files and their quality
    /// </summary>
    public interface IQobuzFileService
    {
        /// <summary>
        /// Checks if an album already exists locally with adequate quality
        /// </summary>
        /// <param name="albumId">The album ID to check</param>
        /// <param name="albumDir">The directory where the album would be stored</param>
        /// <param name="requestedQuality">The requested quality level</param>
        /// <returns>Result indicating if the album exists with adequate quality</returns>
        Task<FileExistenceResult> CheckExistingAlbumAsync(string albumId, string albumDir, string requestedQuality);
    }

    public class FileExistenceResult
    {
        public bool AlreadyExists { get; set; }
        public int ExistingTrackCount { get; set; }
        public string Reason { get; set; } = "";
    }
}