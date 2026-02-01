using System;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Core
{
    /// <summary>
    /// Manages rate limiting for the Qobuz indexer to respect API limits.
    /// Extracted from QobuzIndexer to follow Single Responsibility Principle.
    /// </summary>
    public interface IIndexerRateLimitManager
    {
        /// <summary>
        /// Applies rate limiting before making API requests.
        /// </summary>
        Task ApplyRateLimitAsync();

        /// <summary>
        /// Records that a request was made for rate limiting calculations.
        /// </summary>
        void RecordRequest();

        /// <summary>
        /// Gets the time until the next request can be made.
        /// </summary>
        TimeSpan GetTimeUntilNextRequest();

        /// <summary>
        /// Checks if a request can be made immediately.
        /// </summary>
        bool CanMakeRequest();
    }
}
