using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Services.Metadata
{
    /// <summary>
    /// Strategy for combining different metadata sources
    /// </summary>
    public interface IMetadataStrategy
    {
        /// <summary>
        /// Strategy name for logging and diagnostics
        /// </summary>
        string StrategyName { get; }

        /// <summary>
        /// Determines if this strategy can handle the given album pair
        /// </summary>
        bool CanHandle(QobuzAlbum qobuzAlbum, LidarrAlbum lidarrAlbum);

        /// <summary>
        /// Downloads album using the strategy's metadata approach
        /// </summary>
        Task<MetadataDownloadResult> DownloadAlbumAsync(QobuzAlbum qobuzAlbum, LidarrAlbum lidarrAlbum = null);
    }

    /// <summary>
    /// Result of a metadata download operation
    /// </summary>
    public class MetadataDownloadResult
    {
        public List<TrackDownload> TrackDownloads { get; set; } = new();
        public string MetadataStrategy { get; set; }
        public int ApiCallsSaved { get; set; }
        public int AdditionalApiCalls { get; set; }
        public bool IsSuccessful => TrackDownloads.Any();
        public TimeSpan TotalDuration => TimeSpan.FromSeconds(TrackDownloads.Sum(t => t.Duration?.TotalSeconds ?? 0));
    }
}