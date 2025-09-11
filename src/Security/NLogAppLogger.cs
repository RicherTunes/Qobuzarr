using System;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    // Adapter around NLog to satisfy IAppLogger while keeping NLog as the backend
    public sealed class NLogAppLogger : IAppLogger
    {
        private readonly Logger _logger;
        public NLogAppLogger(Logger logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public void Info(string message, params object[] args) => _logger.Info(message, args);
        public void Debug(string message, params object[] args) => _logger.Debug(message, args);
        public void Debug(Exception ex, string message, params object[] args) => _logger.Debug(ex, message, args);
        public void Warn(string message, params object[] args) => _logger.Warn(message, args);
        public void Warn(Exception ex, string message, params object[] args) => _logger.Warn(ex, message, args);
        public void Error(Exception ex, string message, params object[] args) => _logger.Error(ex, message, args);
    }
}

