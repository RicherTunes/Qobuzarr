using System;
using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for managing quality fallback strategies.
    /// </summary>
    /// <remarks>
    /// This interface provides quality fallback logic when the preferred quality
    /// is not available for a track.
    /// 
    /// Key Features:
    /// - Fallback chain generation from preferred quality
    /// - Intelligent fallback based on format compatibility
    /// - Customizable fallback strategies
    /// - Quality hierarchy awareness
    /// - Bitrate-based fallback logic
    /// 
    /// Fallback strategies ensure users get the best available quality
    /// even when their preferred choice isn't available.
    /// </remarks>
    public interface IQualityFallbackStrategy
    {
        /// <summary>
        /// Gets the fallback chain for a preferred quality.
        /// </summary>
        /// <param name="preferredQuality">The preferred quality ID</param>
        /// <returns>List of quality IDs in fallback order (including preferred)</returns>
        List<int> GetFallbackChain(int preferredQuality);

        /// <summary>
        /// Gets the next quality in the fallback chain.
        /// </summary>
        /// <param name="currentQuality">The current quality ID</param>
        /// <returns>The next quality ID or null if no fallback available</returns>
        int? GetNextFallback(int currentQuality);

        /// <summary>
        /// Checks if a quality is a suitable fallback for the preferred quality.
        /// </summary>
        /// <param name="preferredQuality">The preferred quality ID</param>
        /// <param name="availableQuality">The available quality ID</param>
        /// <returns>True if the available quality is a suitable fallback</returns>
        bool IsSuitableFallback(int preferredQuality, int availableQuality);

        /// <summary>
        /// Selects the best quality from available options based on preference.
        /// </summary>
        /// <param name="preferredQuality">The preferred quality ID</param>
        /// <param name="availableQualities">List of available quality IDs</param>
        /// <returns>The best available quality ID or null if none suitable</returns>
        int? SelectBestAvailableQuality(int preferredQuality, IReadOnlyList<int> availableQualities);

        /// <summary>
        /// Gets the quality fallback strategy name/type.
        /// </summary>
        /// <returns>The strategy name</returns>
        string GetStrategyName();

        /// <summary>
        /// Creates a fallback chain for the given quality format.
        /// </summary>
        /// <param name="preferred">The preferred quality format</param>
        /// <returns>Fallback chain in order of preference</returns>
        IReadOnlyList<QualityFormat> CreateFallbackChain(QualityFormat preferred);

        /// <summary>
        /// Creates a fallback chain considering subscription tier constraints.
        /// </summary>
        /// <param name="preferred">The preferred quality format</param>
        /// <param name="subscriptionTier">User's subscription tier</param>
        /// <returns>Fallback chain with subscription constraints applied</returns>
        IReadOnlyList<QualityFormat> CreateFallbackChain(QualityFormat preferred, QobuzSubscriptionTier subscriptionTier);

        /// <summary>
        /// Creates a fallback chain from a Lidarr quality profile.
        /// </summary>
        /// <param name="profile">The Lidarr quality profile</param>
        /// <returns>Fallback chain based on profile settings</returns>
        IReadOnlyList<QualityFormat> CreateFallbackChainFromProfile(LidarrQualityProfile profile);

        /// <summary>
        /// Determines if fallback should be attempted for a given exception.
        /// </summary>
        /// <param name="exception">The exception that occurred</param>
        /// <param name="attemptedQuality">The quality that was attempted</param>
        /// <returns>True if fallback should be attempted</returns>
        bool ShouldAttemptFallback(Exception exception, QualityFormat attemptedQuality);

        /// <summary>
        /// Gets the next fallback quality in the chain.
        /// </summary>
        /// <param name="current">Current quality</param>
        /// <param name="chain">Fallback chain</param>
        /// <returns>Next quality or null if no fallback available</returns>
        QualityFormat GetNextFallbackQuality(QualityFormat current, IReadOnlyList<QualityFormat> chain);
    }

    /// <summary>
    /// Qobuz subscription tiers for quality constraints.
    /// </summary>
    public enum QobuzSubscriptionTier
    {
        Free = 0,
        Sublime = 1,
        StudioPremier = 2,
        StudioSublime = 3
    }
}