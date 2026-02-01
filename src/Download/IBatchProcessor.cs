using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Download
{
    /// <summary>
    /// Interface for memory-efficient batch processing of large download operations
    /// </summary>
    public interface IBatchProcessor
    {
        /// <summary>
        /// Processes albums in memory-efficient batches
        /// </summary>
        Task<List<T>> ProcessBatchAsync<T>(
            IEnumerable<QobuzAlbum> albums,
            Func<QobuzAlbum, Task<T>> processFunc,
            IProgress<BatchProgress> progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Process tracks in memory-efficient batches
        /// </summary>
        Task<List<T>> ProcessTracksAsync<T>(
            IEnumerable<QobuzTrack> tracks,
            Func<QobuzTrack, Task<T>> processFunc,
            IProgress<BatchProgress> progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if memory usage is within acceptable limits
        /// </summary>
        bool CanProcessBatch(int itemCount, long estimatedSizePerItem);

        /// <summary>
        /// Reports current memory usage
        /// </summary>
        long GetCurrentMemoryUsage();
    }
}
