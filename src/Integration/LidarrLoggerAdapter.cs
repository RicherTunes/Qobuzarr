using System;
using NLog;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Integration
{
    /// <summary>
    /// Adapts NLog Logger to our simple interface
    /// </summary>
    public class LidarrLoggerAdapter : IQobuzLogger
    {
        private readonly Logger _logger;

        public LidarrLoggerAdapter(Logger logger)
        {
            _logger = logger;
        }

        public void Debug(string message, params object[] args)
        {
            _logger.Debug(message, args);
        }

        public void Info(string message, params object[] args)
        {
            _logger.Info(message, args);
        }

        public void Warn(string message, params object[] args)
        {
            _logger.Warn(message, args);
        }

        public void Warn(Exception ex, string message, params object[] args)
        {
            _logger.Warn(ex, message, args);
        }

        public void Error(string message, params object[] args)
        {
            _logger.Error(message, args);
        }

        public void Error(Exception ex, string message, params object[] args)
        {
            _logger.Error(ex, message, args);
        }
    }
}