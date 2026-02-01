using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Interface for orchestrating complete track download workflows
    /// </summary>
    public interface ITrackDownloadOrchestrator
    {
        /// <summary>
        /// Downloads a single track with metadata and quality fallback
        /// </summary>
        Task<string> DownloadTrackAsync(
            QobuzTrack track,
            QobuzAlbum album,
            string outputPath,
            int preferredQuality,
            IProgress<double> progress,
            CancellationToken cancellationToken);

        /// <summary>
        /// Downloads a complete album with intelligent metadata optimization
        /// </summary>
        Task<List<string>> DownloadAlbumWithIntelligentMetadataAsync(
            QobuzAlbum qobuzAlbum,
            LidarrAlbum lidarrAlbum,
            string outputPath,
            int preferredQuality,
            IProgress<double> progress,
            CancellationToken cancellationToken);
    }
}
