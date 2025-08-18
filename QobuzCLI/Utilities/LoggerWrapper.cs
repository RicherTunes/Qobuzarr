using Microsoft.Extensions.Logging;
using System;

namespace QobuzCLI.Utilities
{
    /// <summary>
    /// Wrapper to adapt Microsoft.Extensions.Logging.ILogger to different logger types
    /// </summary>
    public class LoggerWrapper<T> : ILogger<T>
    {
        private readonly ILogger _innerLogger;

        public LoggerWrapper(ILogger innerLogger)
        {
            _innerLogger = innerLogger;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return _innerLogger.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _innerLogger.IsEnabled(logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _innerLogger.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}