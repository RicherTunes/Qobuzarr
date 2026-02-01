using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Core
{
    /// <summary>
    /// Handles rate limiting for the Qobuz indexer to respect API limits.
    /// Extracted from QobuzIndexer god class to improve maintainability.
    /// </summary>
    public class IndexerRateLimitManager : IIndexerRateLimitManager
    {
        private readonly Logger _logger;
        private readonly object _rateLimitLock = new object();
        private DateTime _lastRequestTime = DateTime.MinValue;

        // Rate limiting configuration
        private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(100); // 10 requests/second max
        private static readonly TimeSpan BurstDelay = TimeSpan.FromSeconds(1); // Delay after burst detected

        public IndexerRateLimitManager(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ApplyRateLimitAsync()
        {
            TimeSpan delayNeeded;

            lock (_rateLimitLock)
            {
                var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
                delayNeeded = MinRequestInterval - timeSinceLastRequest;

                if (delayNeeded > TimeSpan.Zero)
                {
                    _logger.Debug("⏳ Rate limiting: Waiting {0}ms before next request", delayNeeded.TotalMilliseconds);
                }
            }

            if (delayNeeded > TimeSpan.Zero)
            {
                await Task.Delay(delayNeeded).ConfigureAwait(false);
            }
        }

        public void RecordRequest()
        {
            lock (_rateLimitLock)
            {
                _lastRequestTime = DateTime.UtcNow;
            }
        }

        public TimeSpan GetTimeUntilNextRequest()
        {
            lock (_rateLimitLock)
            {
                var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
                var delay = MinRequestInterval - timeSinceLastRequest;
                return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
            }
        }

        public bool CanMakeRequest()
        {
            return GetTimeUntilNextRequest() == TimeSpan.Zero;
        }
    }
}
