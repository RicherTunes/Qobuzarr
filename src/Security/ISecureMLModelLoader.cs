using System;
using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    /// <summary>
    /// Interface for secure loading of external ML model assemblies with comprehensive security validation.
    /// This service is registered as a singleton in Lidarr's DI container to avoid multiple instances.
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
        /// Attempts to load a model from multiple paths, returning the first valid one.
        /// </summary>
        /// <param name="possiblePaths">Collection of paths to try</param>
        /// <param name="requireSignature">Whether to require digital signature verification</param>
        /// <returns>Loaded pattern learning engine or null if all paths fail</returns>
        IPatternLearningEngine TryLoadFromPaths(IEnumerable<string> possiblePaths, bool requireSignature = true);

        /// <summary>
        /// Checks if a model has been loaded from a specific path previously.
        /// </summary>
        /// <param name="modelPath">Path to check</param>
        /// <returns>True if model was loaded from this path</returns>
        bool IsModelLoaded(string modelPath);

        /// <summary>
        /// Gets security statistics about model loading attempts.
        /// </summary>
        /// <returns>Security statistics including successful loads and failures</returns>
        SecurityStats GetSecurityStats();

        /// <summary>
        /// Validates that a loaded model engine behaves correctly.
        /// </summary>
        /// <param name="engine">The engine to validate</param>
        /// <returns>True if the engine passes validation tests</returns>
        bool ValidateModelBehavior(IPatternLearningEngine engine);

        /// <summary>
        /// Validates that a type name meets security requirements.
        /// </summary>
        /// <param name="typeName">The type name to validate</param>
        /// <returns>True if the type name is secure</returns>
        bool IsTypeNameSecure(string typeName);
    }

    /// <summary>
    /// Security statistics for model loading operations.
    /// </summary>
    public class SecurityStats
    {
        public int TotalLoadAttempts { get; set; }
        public int SuccessfulLoads { get; set; }
        public int FailedValidations { get; set; }
        public DateTime LastLoadAttempt { get; set; }
        public List<string> LoadedModels { get; set; } = new List<string>();
    }
}