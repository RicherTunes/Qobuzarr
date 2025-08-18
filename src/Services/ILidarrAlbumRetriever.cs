using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for retrieving albums from Lidarr with filtering and pagination support.
    /// </summary>
    public interface ILidarrAlbumRetriever
    {
        /// <summary>
        /// Retrieves wanted albums from Lidarr with filtering and resource limits.
        /// </summary>
        /// <param name="filterOptions">Filtering options for album retrieval.</param>
        /// <param name="maxAlbums">Maximum number of albums to retrieve.</param>
        /// <param name="progress">Progress reporting callback.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Collection of filtered wanted albums.</returns>
        Task<IEnumerable<LidarrAlbum>> GetFilteredWantedAlbumsAsync(
            LidarrFilterOptions filterOptions = null,
            int maxAlbums = 500,
            System.IProgress<ProgressReport> progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Searches Qobuz in parallel for multiple Lidarr albums with intelligent concurrency control.
        /// </summary>
        /// <param name="lidarrAlbums">Collection of Lidarr albums to search for.</param>
        /// <param name="maxConcurrency">Maximum concurrent search operations.</param>
        /// <param name="progress">Progress reporting callback.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Dictionary mapping Lidarr albums to their Qobuz matches.</returns>
        Task<Dictionary<LidarrAlbum, QobuzAlbum>> SearchQobuzParallelAsync(
            IEnumerable<LidarrAlbum> lidarrAlbums,
            int maxConcurrency = 0,
            System.IProgress<ProgressReport> progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates albums before download to check availability, quality, and restrictions.
        /// </summary>
        /// <param name="albumMatches">Dictionary of Lidarr to Qobuz album matches.</param>
        /// <param name="preferredQuality">Preferred quality level for downloads.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Collection of validated album download items.</returns>
        Task<IEnumerable<AlbumDownloadItem>> ValidateAlbumsAsync(
            Dictionary<LidarrAlbum, QobuzAlbum> albumMatches,
            int preferredQuality,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears the quality profile cache to force fresh data on next request.
        /// </summary>
        void ClearQualityProfileCache();
    }
}