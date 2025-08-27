using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services.Quality
{
    /// <summary>
    /// Service interface for managing stream URLs and information.
    /// Extracted from QobuzQualityManager to follow Single Responsibility Principle.
    /// </summary>
    public interface IStreamInfoService
    {
        /// <summary>
        /// Gets stream information for a track with the specified quality.
        /// </summary>
        Task<StreamInfo> GetStreamInfoAsync(string trackId, QobuzQuality quality, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets stream information for multiple tracks in batch.
        /// </summary>
        Task<BatchStreamResult> GetBatchStreamInfoAsync(
            List<string> trackIds, 
            QobuzQuality quality,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Selects the best available quality for a track with automatic fallback.
        /// </summary>
        Task<QualitySelectionResult> SelectBestQualityAsync(
            string trackId, 
            QobuzQuality preferred,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes an operation with automatic quality fallback.
        /// </summary>
        Task<T> ExecuteWithQualityFallbackAsync<T>(
            System.Func<QobuzQuality, Task<T>> operation,
            QobuzQuality preferred = null,
            CancellationToken cancellationToken = default);
    }
}