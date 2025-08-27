using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services.Quality
{
    /// <summary>
    /// Service interface for detecting available qualities for tracks and albums.
    /// Extracted from QobuzQualityManager to follow Single Responsibility Principle.
    /// </summary>
    public interface IQualityDetectionService
    {
        /// <summary>
        /// Detects available qualities for a single track.
        /// </summary>
        Task<QualityDetectionResult> DetectAvailableQualitiesAsync(string trackId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Intelligently detects album-level quality availability using sampling.
        /// </summary>
        Task<Models.AlbumQualityResult> DetectAlbumQualityAsync(
            QobuzAlbum album, 
            int preferredQuality,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates if a stream URL is a full track (not preview/sample).
        /// </summary>
        bool IsValidStreamUrl(string url);

        /// <summary>
        /// Selects representative tracks from an album for quality sampling.
        /// </summary>
        List<QobuzTrack> SelectRepresentativeTracks(List<QobuzTrack> tracks, int count);
    }
}