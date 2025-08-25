using System;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    /// <summary>
    /// Interface for secure loading of external ML model assemblies.
    /// This interface enables dependency injection and singleton pattern for the SecureMLModelLoader.
    /// </summary>
    public interface ISecureMLModelLoader : IDisposable
    {
        /// <summary>
        /// Securely loads an ML model from an external assembly with comprehensive validation.
        /// </summary>
        /// <param name="modelPath">Path to the model assembly</param>
        /// <param name="requireSignature">Whether to require digital signature verification</param>
        /// <returns>Loaded pattern learning engine or null if validation fails</returns>
        IPatternLearningEngine LoadSecureModel(string modelPath, bool requireSignature = true);

        /// <summary>
        /// Attempts to load a secure model from multiple possible paths.
        /// </summary>
        /// <param name="paths">Array of possible paths to check</param>
        /// <param name="requireSignature">Whether to require digital signature verification</param>
        /// <returns>First successfully loaded model or null if all fail</returns>
        IPatternLearningEngine TryLoadFromPaths(string[] paths, bool requireSignature = false);

        /// <summary>
        /// Gets security statistics for monitoring and reporting.
        /// </summary>
        /// <returns>Security statistics including load attempts and validation results</returns>
        ModelLoadSecurityStats GetSecurityStats();
    }
}