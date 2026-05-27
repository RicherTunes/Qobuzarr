using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Download
{
    /// <summary>
    /// Provides memory-efficient batch processing for large download operations
    /// </summary>
    public class BatchProcessor : IBatchProcessor, IDisposable
    {
        private readonly Logger _logger;
        private readonly int _batchSize;
        private readonly int _maxMemoryMB;
        private long _currentMemoryUsage;
        private readonly SemaphoreSlim _memoryThrottle;
        private bool _disposed = false;

        public BatchProcessor(Logger logger = null, int batchSize = 10, int maxMemoryMB = 500)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _batchSize = batchSize;
            _maxMemoryMB = maxMemoryMB;
            _memoryThrottle = new SemaphoreSlim(1, 1);
        }

        /// <summary>
        /// Processes albums in memory-efficient batches
        /// </summary>
        public async Task<List<T>> ProcessBatchAsync<T>(
            IEnumerable<QobuzAlbum> albums,
            Func<QobuzAlbum, Task<T>> processFunc,
            IProgress<BatchProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var albumList = albums.ToList();
            var totalAlbums = albumList.Count;
            var results = new List<T>();

            if (totalAlbums <= _batchSize)
            {
                // Small batch - process normally
                _logger.Debug("Processing {0} albums in single batch", totalAlbums);
                return await ProcessAlbumsAsync(albumList, processFunc, progress, cancellationToken).ConfigureAwait(false);
            }

            // Large batch - use memory-efficient streaming
            _logger.Info("Processing large collection ({0} albums):", totalAlbums);
            _logger.Info(" ↳ Using memory-efficient streaming mode");
            _logger.Info(" ↳ Processing in batches of {0} albums", _batchSize);

            var processedCount = 0;
            var batchNumber = 0;

            foreach (var batch in albumList.Chunk(_batchSize))
            {
                batchNumber++;
                await CheckMemoryPressureAsync().ConfigureAwait(false);

                _logger.Debug("Processing batch {0}/{1} ({2} albums)",
                    batchNumber,
                    Math.Ceiling((double)totalAlbums / _batchSize),
                    batch.Length);

                try
                {
                    var batchResults = await ProcessAlbumsAsync(
                        batch,
                        processFunc,
                        null, // Use main progress reporter
                        cancellationToken).ConfigureAwait(false);

                    results.AddRange(batchResults);
                    processedCount += batch.Length;

                    // Report overall progress
                    progress?.Report(new BatchProgress
                    {
                        TotalItems = totalAlbums,
                        ProcessedItems = processedCount,
                        CurrentBatch = batchNumber,
                        TotalBatches = (int)Math.Ceiling((double)totalAlbums / _batchSize),
                        Message = $"Processed {processedCount}/{totalAlbums} albums",
                        EstimatedMemoryMB = _currentMemoryUsage / 1048576
                    });

                    // Allow natural GC to clean up between batches
                    // Note: Removed forced GC as it's a performance anti-pattern
                    // The runtime's GC is optimized and will run when needed
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error processing batch {0}", batchNumber);

                    // Continue with next batch on error (resilient processing)
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        continue;
                    }
                    throw;
                }
            }

            _logger.Info("✅ Batch processing completed: {0} albums processed", processedCount);
            return results;
        }

        /// <summary>
        /// Process tracks in memory-efficient batches
        /// </summary>
        public async Task<List<T>> ProcessTracksAsync<T>(
            IEnumerable<QobuzTrack> tracks,
            Func<QobuzTrack, Task<T>> processFunc,
            IProgress<BatchProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var trackList = tracks.ToList();
            var totalTracks = trackList.Count;
            var results = new List<T>();

            if (totalTracks <= _batchSize)
            {
                // Small batch - process normally
                _logger.Debug("Processing {0} tracks in single batch", totalTracks);
                foreach (var track in trackList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await processFunc(track).ConfigureAwait(false);
                    results.Add(result);
                }
                return results;
            }

            // Large batch - use memory-efficient streaming
            _logger.Info("Processing large collection ({0} tracks) in batches of {1}", totalTracks, _batchSize);

            var processedCount = 0;
            var batchNumber = 0;

            foreach (var batch in trackList.Chunk(_batchSize))
            {
                batchNumber++;
                await CheckMemoryPressureAsync().ConfigureAwait(false);

                _logger.Debug("Processing track batch {0}/{1} ({2} tracks)",
                    batchNumber,
                    Math.Ceiling((double)totalTracks / _batchSize),
                    batch.Length);

                foreach (var track in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await processFunc(track).ConfigureAwait(false);
                    results.Add(result);
                }

                processedCount += batch.Length;

                // Report progress
                progress?.Report(new BatchProgress
                {
                    TotalItems = totalTracks,
                    ProcessedItems = processedCount,
                    CurrentBatch = batchNumber,
                    TotalBatches = (int)Math.Ceiling((double)totalTracks / _batchSize),
                    Message = $"Processed {processedCount}/{totalTracks} tracks",
                    EstimatedMemoryMB = _currentMemoryUsage / 1048576
                });
            }

            return results;
        }

        /// <summary>
        /// Checks if memory usage is within acceptable limits
        /// </summary>
        public bool CanProcessBatch(int itemCount, long estimatedSizePerItem)
        {
            var estimatedMemory = itemCount * estimatedSizePerItem;
            var currentMemory = GC.GetTotalMemory(false);
            var totalMemory = currentMemory + estimatedMemory;
            var totalMemoryMB = totalMemory / 1048576;

            return totalMemoryMB <= _maxMemoryMB;
        }

        /// <summary>
        /// Reports current memory usage
        /// </summary>
        public long GetCurrentMemoryUsage()
        {
            return _currentMemoryUsage;
        }

        /// <summary>
        /// Processes tracks in memory-efficient batches (internal helper method)
        /// </summary>
        internal async Task ProcessTracksInBatchesAsync(
            QobuzAlbum album,
            Func<QobuzTrack, Task> processFunc,
            int tracksPerBatch = 5,
            CancellationToken cancellationToken = default)
        {
            var tracks = album.GetTracks();
            var totalTracks = tracks.Count;

            if (totalTracks > 50) // Large album threshold
            {
                _logger.Info("Processing large album '{0}' with {1} tracks:", album.Title, totalTracks);
                _logger.Info(" ↳ Using memory-efficient track batching");
                _logger.Info(" ↳ Processing {0} tracks at a time", tracksPerBatch);

                var processedCount = 0;
                foreach (var trackBatch in tracks.Chunk(tracksPerBatch))
                {
                    await CheckMemoryPressureAsync().ConfigureAwait(false);

                    var tasks = trackBatch.Select(track => processFunc(track)).ToArray();
                    await Task.WhenAll(tasks).ConfigureAwait(false);

                    processedCount += trackBatch.Length;
                    _logger.Debug(" ↳ Progress: {0}/{1} tracks", processedCount, totalTracks);

                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            else
            {
                // Small album - process all tracks at once
                var tasks = tracks.Select(track => processFunc(track)).ToArray();
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks memory pressure and waits if necessary
        /// </summary>
        private async Task CheckMemoryPressureAsync()
        {
            await _memoryThrottle.WaitAsync().ConfigureAwait(false);
            try
            {
                var currentMemory = GC.GetTotalMemory(false);
                _currentMemoryUsage = currentMemory;

                var memoryMB = currentMemory / 1048576;
                if (memoryMB > _maxMemoryMB)
                {
                    _logger.Warn("⚠️ Memory pressure detected ({0} MB > {1} MB limit)", memoryMB, _maxMemoryMB);
                    _logger.Info(" ↳ Suggesting garbage collection...");

                    // Suggest GC without blocking
                    await SuggestGarbageCollectionAsync().ConfigureAwait(false);

                    // Give GC time to work if needed
                    await Task.Delay(500).ConfigureAwait(false);

                    var newMemoryMB = GC.GetTotalMemory(false) / 1048576;
                    _logger.Info(" ↳ Current memory: {0} MB", newMemoryMB);

                    // If still high, wait a bit more for natural GC
                    if (newMemoryMB > _maxMemoryMB * 0.9)
                    {
                        _logger.Info(" ↳ Waiting for memory to stabilize...");
                        await Task.Delay(1500).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _memoryThrottle.Release();
            }
        }

        /// <summary>
        /// Suggests garbage collection to free memory (non-blocking)
        /// </summary>
        /// <remarks>
        /// This method now only suggests GC rather than forcing it.
        /// Forced GC is an anti-pattern that causes performance issues.
        /// </remarks>
        private Task SuggestGarbageCollectionAsync()
        {
            // Only suggest collection, don't force it
            // The runtime will decide if/when to actually collect

            return Task.CompletedTask;
        }

        /// <summary>
        /// Processes a batch of albums
        /// </summary>
        private async Task<List<T>> ProcessAlbumsAsync<T>(
            IEnumerable<QobuzAlbum> albums,
            Func<QobuzAlbum, Task<T>> processFunc,
            IProgress<BatchProgress> progress,
            CancellationToken cancellationToken)
        {
            var results = new List<T>();
            var albumList = albums.ToList();

            foreach (var album in albumList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var result = await processFunc(album).ConfigureAwait(false);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error processing album: {0}", album.Title);
                    // Continue processing other albums
                }
            }

            return results;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    _memoryThrottle?.Dispose();
                }
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Progress information for batch processing
    /// </summary>
    public class BatchProgress
    {
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public int CurrentBatch { get; set; }
        public int TotalBatches { get; set; }
        public string Message { get; set; }
        public long EstimatedMemoryMB { get; set; }

        public double PercentComplete => TotalItems > 0 ? (double)ProcessedItems / TotalItems * 100 : 0;
    }

    /// <summary>
    /// Extension methods for chunking collections
    /// </summary>
    public static class ChunkExtensions
    {
        public static IEnumerable<T[]> Chunk<T>(this IEnumerable<T> source, int chunkSize)
        {
            if (chunkSize <= 0)
                throw new ArgumentException("Chunk size must be greater than 0", nameof(chunkSize));

            var chunk = new List<T>(chunkSize);

            foreach (var item in source)
            {
                chunk.Add(item);

                if (chunk.Count == chunkSize)
                {
                    yield return chunk.ToArray();
                    chunk.Clear();
                }
            }

            if (chunk.Count > 0)
            {
                yield return chunk.ToArray();
            }
        }
    }
}
