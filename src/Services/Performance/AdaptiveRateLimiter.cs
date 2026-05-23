using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Performance;

namespace Lidarr.Plugin.Qobuzarr.Services.Performance
{
    // Adapter that lives in the plugin assembly so Lidarr's auto-registration
    // discovers and injects an IUniversalAdaptiveRateLimiter implementation.
    public class AdaptiveRateLimiter : IUniversalAdaptiveRateLimiter
    {
        private readonly UniversalAdaptiveRateLimiter _inner = new UniversalAdaptiveRateLimiter();
        private bool _disposed;

        public Task<bool> WaitIfNeededAsync(string service, string endpoint, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _inner.WaitIfNeededAsync(service, endpoint, cancellationToken);
        }

        public void RecordResponse(string service, string endpoint, HttpResponseMessage response)
        {
            if (_disposed) return;
            _inner.RecordResponse(service, endpoint, response);
        }

        public void RecordAuthFailure(string service, string endpoint)
        {
            if (_disposed) return;
            _inner.RecordAuthFailure(service, endpoint);
        }

        public int GetCurrentLimit(string service, string endpoint)
        {
            ThrowIfDisposed();
            return _inner.GetCurrentLimit(service, endpoint);
        }

        public ServiceRateLimitStats GetServiceStats(string service)
        {
            ThrowIfDisposed();
            return _inner.GetServiceStats(service);
        }

        public GlobalRateLimitStats GetGlobalStats()
        {
            ThrowIfDisposed();
            return _inner.GetGlobalStats();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _inner.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AdaptiveRateLimiter));
        }
    }
}

