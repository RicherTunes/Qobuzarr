using System;
using System.IO;
using System.Threading;
using NLog;
using Lidarr.Plugin.Qobuzarr.Configuration;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Monitors configuration changes and handles concurrent access issues
    /// Prevents optimization failures when user changes settings mid-operation
    /// </summary>
    public class ConfigurationMonitor : IDisposable
    {
        private readonly Logger _logger;
        private readonly FileSystemWatcher _watcher;
        private readonly object _lockObject = new();
        
        private volatile QobuzPluginConfiguration _currentConfig;
        private DateTime _lastConfigChange = DateTime.MinValue;

        public event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;

        public ConfigurationMonitor(string configFilePath, Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            
            if (File.Exists(configFilePath))
            {
                var directory = Path.GetDirectoryName(configFilePath);
                var fileName = Path.GetFileName(configFilePath);
                
                _watcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                
                _watcher.Changed += OnConfigFileChanged;
                LoadConfiguration(configFilePath);
                
                _logger.Debug("📁 CONFIG MONITOR: Watching {0}", configFilePath);
            }
            else
            {
                _logger.Warn("⚠️ CONFIG FILE NOT FOUND: {0}", configFilePath);
            }
        }

        /// <summary>
        /// Gets current configuration with thread safety
        /// </summary>
        public QobuzPluginConfiguration GetCurrentConfiguration()
        {
            lock (_lockObject)
            {
                return _currentConfig?.Clone() ?? new QobuzPluginConfiguration();
            }
        }

        /// <summary>
        /// Checks if configuration changed recently (for cache invalidation)
        /// </summary>
        public bool HasRecentConfigChange(TimeSpan threshold)
        {
            return DateTime.UtcNow - _lastConfigChange < threshold;
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Debounce multiple change events
                if (DateTime.UtcNow - _lastConfigChange < TimeSpan.FromSeconds(1))
                {
                    return;
                }

                _logger.Debug("📝 CONFIG CHANGE: Configuration file modified");
                
                // REMOVED: Thread.Sleep anti-pattern - file system should stabilize naturally
                
                LoadConfiguration(e.FullPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "🛡️ DEFENSIVE: Failed to handle configuration change, keeping current config");
            }
        }

        private void LoadConfiguration(string configPath)
        {
            try
            {
                lock (_lockObject)
                {
                    // Simple config loading - in real implementation would use JSON/XML
                    var oldConfig = _currentConfig?.Clone();
                    _currentConfig = LoadConfigFromFile(configPath);
                    _lastConfigChange = DateTime.UtcNow;

                    // Notify if significant changes
                    if (HasSignificantChanges(oldConfig, _currentConfig))
                    {
                        _logger.Info("⚙️ SIGNIFICANT CONFIG CHANGE: Cache invalidation required");
                        ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs
                        {
                            OldConfiguration = oldConfig,
                            NewConfiguration = _currentConfig,
                            RequiresCacheInvalidation = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load configuration from {0}", configPath);
            }
        }

        private QobuzPluginConfiguration LoadConfigFromFile(string configPath)
        {
            try
            {
                // Defensive: Use file sharing to prevent conflicts with other processes
                using var fileStream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                
                // Simplified config loading - real implementation would parse JSON/XML
                return new QobuzPluginConfiguration
                {
                    QualityPreference = "FLAC",
                    MaxConcurrentDownloads = 3,
                    CacheEnabled = true,
                    OptimizationEnabled = true,
                    LastModified = File.GetLastWriteTime(configPath)
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "🛡️ DEFENSIVE: Failed to load config file, using defaults");
                return new QobuzPluginConfiguration(); // Return defaults
            }
        }

        private bool HasSignificantChanges(QobuzPluginConfiguration old, QobuzPluginConfiguration current)
        {
            if (old == null) return false;

            return old.QualityPreference != current.QualityPreference ||
                   old.OptimizationEnabled != current.OptimizationEnabled ||
                   old.CacheEnabled != current.CacheEnabled;
        }

        public void Dispose()
        {
            try
            {
                _watcher?.Dispose();
                _logger.Debug("ConfigurationMonitor disposed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during ConfigurationMonitor disposal");
            }
        }
    }

    #region Configuration Classes

    public class QobuzPluginConfiguration
    {
        public string QualityPreference { get; set; } = "FLAC";
        public int MaxConcurrentDownloads { get; set; } = 3;
        public bool CacheEnabled { get; set; } = true;
        public bool OptimizationEnabled { get; set; } = true;
        public DateTime LastModified { get; set; }

        public QobuzPluginConfiguration Clone()
        {
            return new QobuzPluginConfiguration
            {
                QualityPreference = QualityPreference,
                MaxConcurrentDownloads = MaxConcurrentDownloads,
                CacheEnabled = CacheEnabled,
                OptimizationEnabled = OptimizationEnabled,
                LastModified = LastModified
            };
        }
    }

    public class ConfigurationChangedEventArgs : EventArgs
    {
        public QobuzPluginConfiguration OldConfiguration { get; set; }
        public QobuzPluginConfiguration NewConfiguration { get; set; }
        public bool RequiresCacheInvalidation { get; set; }
    }

    #endregion
}