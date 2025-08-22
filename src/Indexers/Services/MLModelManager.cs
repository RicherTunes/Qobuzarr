using System;
using System.Linq;
using NLog;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Security;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Services
{
    /// <summary>
    /// Manages ML model lifecycle including loading, validation, and selection
    /// </summary>
    public class MLModelManager : IMLModelManager, IDisposable
    {
        private readonly Logger _logger;
        private readonly SecureMLModelLoader _secureModelLoader;
        private IPatternLearningEngine _currentEngine;
        private bool _disposed;

        public MLModelManager(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _secureModelLoader = new SecureMLModelLoader(logger);
        }

        public IPatternLearningEngine GetOptimizer(MLModelType modelType)
        {
            if (_currentEngine != null)
                return _currentEngine;

            _logger.Info($"Initializing ML optimizer: {modelType}");

            try
            {
                switch (modelType)
                {
                    case MLModelType.Baseline:
                        _logger.Info("Using baseline ML model (trained on 500K+ albums)");
                        _currentEngine = new CompiledMLQueryOptimizer(_logger);
                        break;

                    case MLModelType.Personal:
                        _logger.Info("Attempting to load personal ML model");
                        var personalModel = TryLoadPersonalModel();
                        if (personalModel != null)
                        {
                            _logger.Info("Personal ML model loaded successfully");
                            _currentEngine = personalModel;
                        }
                        else
                        {
                            _logger.Warn("Personal ML model not found, falling back to baseline");
                            _currentEngine = new CompiledMLQueryOptimizer(_logger);
                        }
                        break;

                    case MLModelType.Hybrid:
                        _logger.Info("Initializing hybrid ML model (baseline + personal)");
                        var baselineModel = new CompiledMLQueryOptimizer(_logger);
                        var personalModelForHybrid = TryLoadPersonalModel();
                        
                        if (personalModelForHybrid != null)
                        {
                            _logger.Info("Hybrid ML model initialized with both baseline and personal models");
                            _currentEngine = new HybridMLQueryOptimizer(_logger, baselineModel, personalModelForHybrid);
                        }
                        else
                        {
                            _logger.Warn("Personal model not available for hybrid mode, using baseline only");
                            _currentEngine = baselineModel;
                        }
                        break;

                    default:
                        _logger.Warn($"Unknown ML model type: {modelType}, using baseline");
                        _currentEngine = new CompiledMLQueryOptimizer(_logger);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize ML optimizer, falling back to baseline");
                _currentEngine = new CompiledMLQueryOptimizer(_logger);
            }

            return _currentEngine;
        }

        public IPatternLearningEngine TryLoadPersonalModel()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var possiblePaths = new[]
                {
                    System.IO.Path.Combine(baseDir, "PersonalizedMLQueryOptimizer.dll"),
                    System.IO.Path.Combine(baseDir, "PersonalMLQueryOptimizer.dll"),
                    System.IO.Path.Combine(baseDir, "plugins", "Qobuzarr", "PersonalizedMLQueryOptimizer.dll"),
                    System.IO.Path.Combine(baseDir, "ML", "PersonalizedMLQueryOptimizer.dll"),
                    System.IO.Path.Combine(baseDir, "plugins", "Qobuzarr", "ML", "PersonalMLQueryOptimizer.dll")
                };

                _logger.Info("Attempting to load personal ML model with security validation");
                
                var externalModel = _secureModelLoader.TryLoadFromPaths(possiblePaths, requireSignature: false);
                if (externalModel != null)
                {
                    _logger.Info("Successfully loaded and validated external personal ML model");
                    
                    var securityStats = _secureModelLoader.GetSecurityStats();
                    _logger.Debug("Model loader security stats - Total attempts: {0}, Successful: {1}, Failed: {2}", 
                        securityStats.TotalLoadAttempts, 
                        securityStats.SuccessfulLoads, 
                        securityStats.FailedValidations);
                    
                    return externalModel;
                }

                _logger.Debug("No external model found or validation failed, checking for embedded models");
                
                var currentAssembly = System.Reflection.Assembly.GetExecutingAssembly();
                var personalTypes = currentAssembly.GetTypes()
                    .Where(t => typeof(IPatternLearningEngine).IsAssignableFrom(t) && 
                              !t.IsInterface && !t.IsAbstract &&
                              t.Name.Contains("Personal") && 
                              !t.Name.Contains("Compiled"))
                    .ToList();

                if (personalTypes.Any())
                {
                    var personalType = personalTypes.First();
                    
                    if (!IsTypeNameSecure(personalType.Name))
                    {
                        _logger.Warn("Embedded personal model type name failed security validation: {0}", personalType.Name);
                        return null;
                    }
                    
                    try
                    {
                        var personalOptimizer = Activator.CreateInstance(personalType, _logger) as IPatternLearningEngine;
                        
                        if (personalOptimizer != null && ValidateModelBehavior(personalOptimizer))
                        {
                            _logger.Info("Loaded and validated embedded personal model: {0}", personalType.Name);
                            return personalOptimizer;
                        }
                        else
                        {
                            _logger.Warn("Embedded personal model failed behavior validation");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to instantiate embedded personal model: {0}", personalType.Name);
                    }
                }

                _logger.Debug("No valid personal ML model found after security validation");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load personal ML model securely");
                return null;
            }
        }

        public bool ValidateModelBehavior(IPatternLearningEngine model)
        {
            try
            {
                var testComplexity = model.PredictComplexity("Test Artist", "Test Album");
                var testConfidence = model.GetConfidenceScore("Test Artist", "Test Album", testComplexity);
                var testStats = model.GetStatistics();

                if (testConfidence < 0 || testConfidence > 1)
                {
                    _logger.Warn("Model returned invalid confidence score: {0}", testConfidence);
                    return false;
                }

                if (testStats == null)
                {
                    _logger.Warn("Model returned null statistics");
                    return false;
                }

                // Test edge cases
                model.PredictComplexity("", "");
                model.PredictComplexity(null, null);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Model behavior validation failed");
                return false;
            }
        }

        public object GetPerformanceStatistics()
        {
            if (_currentEngine == null)
                return new { success = false, message = "ML engine not initialized" };

            try
            {
                var stats = _currentEngine.GetStatistics();
                
                return new
                {
                    success = true,
                    modelType = stats.HybridStatistics?.ContainsKey("ModelType") == true ? 
                               stats.HybridStatistics["ModelType"] : "Unknown",
                    accuracy = stats.Accuracy,
                    totalPredictions = stats.TotalPredictions,
                    correctPredictions = stats.CorrectPredictions,
                    isUsingMLEngine = stats.IsUsingMLEngine,
                    lastModelUpdate = stats.LastModelUpdate,
                    cacheHitRatio = stats.HybridStatistics?.ContainsKey("CacheHitRatio") == true ? 
                                   stats.HybridStatistics["CacheHitRatio"] : 0.0,
                    apiCallReduction = stats.HybridStatistics?.ContainsKey("ApiCallReduction") == true ? 
                                      stats.HybridStatistics["ApiCallReduction"] : 0.0,
                    patternDistribution = stats.PatternDistribution
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error retrieving ML performance data");
                return new { success = false, message = ex.Message };
            }
        }

        public object GetHealthStatus()
        {
            if (_currentEngine == null)
                return new { success = false, message = "ML engine not initialized" };

            try
            {
                PerformanceHealth healthStatus = null;
                
                if (_currentEngine is CompiledMLQueryOptimizer compiledOptimizer)
                {
                    healthStatus = compiledOptimizer.GetPerformanceHealth();
                }
                else if (_currentEngine is HybridMLQueryOptimizer hybridOptimizer)
                {
                    healthStatus = hybridOptimizer.GetPerformanceHealth();
                }
                
                if (healthStatus == null)
                {
                    return new { success = false, message = "Health status not available for this ML engine type" };
                }
                
                return new
                {
                    success = true,
                    health = new
                    {
                        status = healthStatus.Status,
                        score = healthStatus.Score,
                        isHealthy = healthStatus.IsHealthy,
                        hasWarnings = healthStatus.HasWarnings,
                        isCritical = healthStatus.IsCritical,
                        issues = healthStatus.Issues,
                        issueCount = healthStatus.Issues.Count
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error retrieving ML health status");
                return new { success = false, message = ex.Message };
            }
        }

        public string GetPerformanceReport()
        {
            try
            {
                if (_currentEngine != null)
                {
                    if (_currentEngine is CompiledMLQueryOptimizer compiledOptimizer)
                    {
                        return compiledOptimizer.GetPerformanceReport();
                    }
                    else if (_currentEngine is HybridMLQueryOptimizer hybridOptimizer)
                    {
                        return hybridOptimizer.GetPerformanceReport();
                    }
                }
                
                return "ML performance monitoring not available - pattern learning engine not initialized";
            }
            catch (Exception ex)
            {
                return $"Error generating ML performance report: {ex.Message}";
            }
        }

        private bool IsTypeNameSecure(string typeName)
        {
            var suspiciousPatterns = new[] { "..", "\\", "/", "<", ">", "|", ":", "*", "?", "\"", "\0" };
            return !suspiciousPatterns.Any(pattern => typeName.Contains(pattern));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        if (_currentEngine is IDisposable disposableEngine)
                        {
                            disposableEngine.Dispose();
                            _logger.Debug("ML pattern learning engine disposed");
                        }
                        
                        _secureModelLoader?.Dispose();
                        _logger.Debug("ML model manager disposed");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error disposing ML model manager resources");
                    }
                }
                
                _disposed = true;
            }
        }
    }
}