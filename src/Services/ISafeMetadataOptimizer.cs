using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Interface for safe metadata optimization that gracefully handles errors
    /// </summary>
    public interface ISafeMetadataOptimizer
    {
        /// <summary>
        /// Safely optimizes metadata for a track, returning original if optimization fails
        /// </summary>
        Task<QobuzTrack> OptimizeTrackMetadataAsync(QobuzTrack track, QobuzAlbum album = null);

        /// <summary>
        /// Safely optimizes metadata for an album, returning original if optimization fails
        /// </summary>
        Task<QobuzAlbum> OptimizeAlbumMetadataAsync(QobuzAlbum album);

        /// <summary>
        /// Downloads album using safe metadata optimization with comprehensive validation
        /// </summary>
        Task<DownloadResult> DownloadAlbumSafelyAsync(QobuzAlbum qobuzAlbum, LidarrAlbum lidarrAlbum = null);

        /// <summary>
        /// Logs current optimization effectiveness statistics
        /// </summary>
        void LogOptimizationStatistics();

        /// <summary>
        /// Checks if metadata optimization is available
        /// </summary>
        bool IsOptimizationAvailable { get; }

        /// <summary>
        /// Gets statistics about optimization success rate
        /// </summary>
        OptimizationStatistics GetStatistics();
    }

    /// <summary>
    /// Statistics about metadata optimization
    /// </summary>
    public class OptimizationStatistics
    {
        public int SuccessfulOptimizations { get; set; }
        public int FailedOptimizations { get; set; }
        public double SuccessRate => SuccessfulOptimizations + FailedOptimizations > 0 
            ? (double)SuccessfulOptimizations / (SuccessfulOptimizations + FailedOptimizations) 
            : 0;
    }
}