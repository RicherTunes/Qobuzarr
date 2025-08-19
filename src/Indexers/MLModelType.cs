namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Types of ML models available for query optimization
    /// </summary>
    public enum MLModelType
    {
        /// <summary>
        /// Pre-trained baseline model that ships with the plugin
        /// Trained on 500,000+ diverse albums for broad compatibility
        /// Works well for most users out-of-the-box
        /// </summary>
        Baseline = 0,
        
        /// <summary>
        /// Personal model trained on user's specific library
        /// Requires training scripts and user's music data
        /// Provides maximum personalization for large, diverse libraries
        /// </summary>
        Personal = 1,
        
        /// <summary>
        /// Hybrid model combining baseline + personal models
        /// Uses confidence-based routing to get best of both worlds
        /// Recommended for users who have trained personal models
        /// </summary>
        Hybrid = 2
    }
}