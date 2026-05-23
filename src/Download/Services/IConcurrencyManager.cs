using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Service for managing download concurrency using semaphores.
    /// Provides thread-safe concurrency control with dynamic limit adjustment.
    /// </summary>
    public interface IConcurrencyManager : IDisposable
    {
        /// <summary>
        /// Acquires a concurrency slot, blocking until one becomes available.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Disposable that releases the slot when disposed</returns>
        Task<IDisposable> AcquireSlotAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the maximum number of concurrent operations allowed.
        /// </summary>
        /// <param name="newLimit">New concurrency limit (must be positive)</param>
        void UpdateConcurrencyLimit(int newLimit);

        /// <summary>
        /// Gets the current concurrency limit.
        /// </summary>
        int CurrentLimit { get; }

        /// <summary>
        /// Gets the number of currently active concurrent operations.
        /// </summary>
        int ActiveCount { get; }

        /// <summary>
        /// Gets the number of operations waiting for a slot.
        /// </summary>
        int WaitingCount { get; }

        /// <summary>
        /// Gets concurrency manager statistics.
        /// </summary>
        /// <returns>Current concurrency statistics</returns>
        ConcurrencyStatistics GetStatistics();
    }

    /// <summary>
    /// Statistics about concurrency manager state.
    /// </summary>
    public class ConcurrencyStatistics
    {
        public int MaxConcurrency { get; set; }
        public int ActiveOperations { get; set; }
        public int QueuedOperations { get; set; }
        public int TotalSlotsUsed { get; set; }
        public TimeSpan AverageWaitTime { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Disposable wrapper for releasing concurrency slots.
    /// </summary>
    internal class ConcurrencySlot : IDisposable
    {
        private readonly Action _release;
        private volatile bool _disposed;

        public ConcurrencySlot(Action release)
        {
            _release = release ?? throw new ArgumentNullException(nameof(release));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                _release.Invoke();
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}
