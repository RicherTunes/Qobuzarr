using System;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Abstractions
{
    /// <summary>
    /// Adapter to convert NLog Logger to IQobuzLogger interface
    /// </summary>
    public class NLogToQobuzLoggerAdapter : IQobuzLogger
    {
        private readonly Logger _logger;

        public NLogToQobuzLoggerAdapter(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // IQobuzLogger implementation methods
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

        public void Warn(Exception exception, string message, params object[] args)
        {
            _logger.Warn(exception, message, args);
        }

        public void Error(string message, params object[] args)
        {
            _logger.Error(message, args);
        }

        public void Error(Exception exception, string message, params object[] args)
        {
            _logger.Error(exception, message, args);
        }

        // Additional convenience methods
        public void LogDebug(string message, params object[] args)
        {
            _logger.Debug(message, args);
        }

        public void LogInformation(string message, params object[] args)
        {
            _logger.Info(message, args);
        }

        public void LogWarning(string message, params object[] args)
        {
            _logger.Warn(message, args);
        }

        public void LogError(string message, params object[] args)
        {
            _logger.Error(message, args);
        }

        public void LogError(Exception exception, string message, params object[] args)
        {
            _logger.Error(exception, message, args);
        }

        public void LogCritical(string message, params object[] args)
        {
            _logger.Fatal(message, args);
        }

        public void LogCritical(Exception exception, string message, params object[] args)
        {
            _logger.Fatal(exception, message, args);
        }

        public void LogTrace(string message, params object[] args)
        {
            _logger.Trace(message, args);
        }

        public void LogSecurityEvent(string eventType, string message, params object[] args)
        {
            _logger.Info($"[SECURITY:{eventType}] {message}", args);
        }

        public void LogPerformance(string operation, long elapsedMilliseconds, string details = null)
        {
            _logger.Debug($"[PERFORMANCE] {operation} took {elapsedMilliseconds}ms. {details}");
        }

        // Implicit conversion operator for convenience
        public static implicit operator NLogToQobuzLoggerAdapter(Logger logger)
        {
            return new NLogToQobuzLoggerAdapter(logger);
        }

        // Implicit conversion back to Logger
        public static implicit operator Logger(NLogToQobuzLoggerAdapter adapter)
        {
            return adapter._logger;
        }
    }
}