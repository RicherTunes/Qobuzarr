using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Interface for managing quality fallback strategies
    /// </summary>
    public interface IQualityFallbackProvider
    {
        /// <summary>
        /// Gets the quality fallback chain for a preferred quality
        /// </summary>
        /// <param name="preferredQuality">The preferred quality format ID</param>
        /// <returns>List of quality IDs to try in order</returns>
        List<int> GetFallbackQualities(int preferredQuality);

        /// <summary>
        /// Determines why a track is unavailable from restriction messages
        /// </summary>
        /// <param name="restrictionMessage">API restriction message</param>
        /// <returns>Reason for unavailability</returns>
        TrackUnavailableReason DetermineUnavailableReason(string restrictionMessage);

        /// <summary>
        /// Determines if an exception is retryable for quality fallback
        /// </summary>
        /// <param name="ex">Exception that occurred</param>
        /// <returns>True if should retry with lower quality</returns>
        bool IsRetryableException(System.Exception ex);
    }
}