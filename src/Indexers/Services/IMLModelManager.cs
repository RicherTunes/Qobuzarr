using System;
using NLog;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Services
{
    /// <summary>
    /// Manages ML model lifecycle including loading, validation, and selection
    /// </summary>
    public interface IMLModelManager
    {
        /// <summary>
        /// Gets the appropriate ML optimizer based on configuration
        /// </summary>
        IPatternLearningEngine GetOptimizer(MLModelType modelType);
        
        /// <summary>
        /// Loads a personal ML model with security validation
        /// </summary>
        IPatternLearningEngine TryLoadPersonalModel();
        
        /// <summary>
        /// Validates model behavior for safety
        /// </summary>
        bool ValidateModelBehavior(IPatternLearningEngine model);
        
        /// <summary>
        /// Gets performance statistics for the current model
        /// </summary>
        object GetPerformanceStatistics();
        
        /// <summary>
        /// Gets health status for the current model
        /// </summary>
        object GetHealthStatus();
        
        /// <summary>
        /// Generates a performance report
        /// </summary>
        string GetPerformanceReport();
    }
}