using System;
using System.Threading;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// A semaphore wrapper that supports dynamic concurrency adjustment without
    /// disposing the semaphore while callers still hold references. Callers
    /// <see cref="Acquire"/> a reference before WaitAsync and <see cref="Return"/>
    /// it in finally. <see cref="Swap"/> replaces the active semaphore; the old
    /// one is disposed only when the last outstanding caller returns it.
    /// </summary>
    internal sealed class SwappableSemaphore : IDisposable
    {
        private volatile SemaphoreSlim _current;
        private int _outstandingRefs;
        private int _disposed;

        public SwappableSemaphore(int initialCount)
        {
            if (initialCount <= 0) throw new ArgumentOutOfRangeException(nameof(initialCount));
            _current = new SemaphoreSlim(initialCount, initialCount);
        }

        public int CurrentCount => _current.CurrentCount;

        /// <summary>
        /// Acquires a reference to the current semaphore. The caller MUST call
        /// <see cref="Return"/> with the same reference in a finally block.
        /// </summary>
        public SemaphoreSlim Acquire()
        {
            Interlocked.Increment(ref _outstandingRefs);
            return _current;
        }

        /// <summary>
        /// Returns a previously acquired semaphore reference. If the semaphore was
        /// swapped out and this is the last outstanding reference, disposes it.
        /// </summary>
        public void Return(SemaphoreSlim semaphore)
        {
            var remaining = Interlocked.Decrement(ref _outstandingRefs);
            if (semaphore != _current && remaining <= 0)
            {
                try { semaphore.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// Atomically replaces the active semaphore with one of a different count.
        /// Outstanding callers continue using the old semaphore safely; it is
        /// disposed when the last caller returns it.
        /// </summary>
        public void Swap(int newCount)
        {
            if (newCount <= 0) throw new ArgumentOutOfRangeException(nameof(newCount));
            var old = _current;
            _current = new SemaphoreSlim(newCount, newCount);
            if (Interlocked.CompareExchange(ref _outstandingRefs, 0, 0) == 0)
            {
                try { old.Dispose(); } catch { }
            }
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
            try { _current.Dispose(); } catch { }
        }
    }
}
