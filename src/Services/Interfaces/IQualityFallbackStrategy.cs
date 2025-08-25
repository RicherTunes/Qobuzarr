using System.Collections.Generic;

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
    }
}