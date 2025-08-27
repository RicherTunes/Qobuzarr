using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Services.Consolidated
{
    /// <summary>
    /// Unified interface for all quality-related operations in Qobuz.
    /// Consolidates functionality from multiple quality services into a single cohesive interface.
    /// </summary>
    public interface IQobuzQualityManager
    {
        #region Quality Detection

        /// <summary>
        /// Detects available qualities for a single track.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID to check.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Quality detection result with available formats.</returns>
        Task<QualityDetectionResult> DetectAvailableQualitiesAsync(
            string trackId, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Intelligently detects album-level quality availability using sampling.
        /// Reduces API calls by up to 95% through smart track sampling.
        /// </summary>
        /// <param name="album">The album to analyze.</param>
        /// <param name="preferredQuality">User's preferred quality ID.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Album quality detection result with optimization information.</returns>
        Task<Models.AlbumQualityResult> DetectAlbumQualityAsync(
            QobuzAlbum album, 
            int preferredQuality,
            CancellationToken cancellationToken = default);

        #endregion

        #region Quality Mapping

        /// <summary>
        /// Maps a Lidarr quality profile to the appropriate Qobuz quality.
        /// </summary>
        /// <param name="profile">The Lidarr quality profile to map.</param>
        /// <returns>The corresponding Qobuz quality.</returns>
        QobuzQuality MapLidarrQuality(LidarrQualityProfile profile);

        /// <summary>
        /// Gets the quality fallback chain for a given preferred quality.
        /// </summary>
        /// <param name="preferred">The preferred quality.</param>
        /// <returns>Ordered list of qualities to try, from preferred to lowest acceptable.</returns>
        List<QobuzQuality> GetQualityFallbackChain(QobuzQuality preferred);

        #endregion

        #region Quality Selection

        /// <summary>
        /// Selects the best available quality for a track with automatic fallback.
        /// </summary>
        /// <param name="trackId">The track ID to get quality for.</param>
        /// <param name="preferred">The preferred quality.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Quality selection result with stream information.</returns>
        Task<QualitySelectionResult> SelectBestQualityAsync(
            string trackId, 
            QobuzQuality preferred,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes an operation with automatic quality fallback.
        /// Tries the operation with progressively lower qualities until success.
        /// </summary>
        /// <typeparam name="T">The type of result expected from the operation.</typeparam>
        /// <param name="operation">The operation to execute with a specific quality.</param>
        /// <param name="preferred">The preferred quality to start with.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>The result of the successful operation.</returns>
        Task<T> ExecuteWithQualityFallbackAsync<T>(
            Func<QobuzQuality, Task<T>> operation,
            QobuzQuality preferred = null,
            CancellationToken cancellationToken = default);

        #endregion

        #region Stream URL Management

        /// <summary>
        /// Gets stream information for a track with the specified quality.
        /// </summary>
        /// <param name="trackId">The track ID.</param>
        /// <param name="quality">The desired quality.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Stream information including URL and expiration.</returns>
        Task<StreamInfo> GetStreamInfoAsync(
            string trackId, 
            QobuzQuality quality,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets stream information for multiple tracks in batch.
        /// Optimized for downloading entire albums efficiently.
        /// </summary>
        /// <param name="trackIds">List of track IDs to get stream info for.</param>
        /// <param name="quality">The desired quality for all tracks.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Batch stream result with success/failure counts.</returns>
        Task<BatchStreamResult> GetBatchStreamInfoAsync(
            List<string> trackIds, 
            QobuzQuality quality,
            CancellationToken cancellationToken = default);

        #endregion
    }
}