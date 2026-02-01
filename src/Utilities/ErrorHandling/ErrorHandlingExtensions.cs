using System;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Utilities.ErrorHandling
{
    /// <summary>
    /// Standardized error handling extensions that provide consistent error handling patterns
    /// across the entire codebase. These methods ensure proper logging, error context, and
    /// appropriate recovery strategies.
    /// </summary>
    public static class ErrorHandlingExtensions
    {
        /// <summary>
        /// Executes an operation with standardized error logging and rethrowing.
        /// Use this for critical operations that should fail-fast.
        /// </summary>
        /// <typeparam name="T">Return type of the operation</typeparam>
        /// <param name="operation">The operation to execute</param>
        /// <param name="logger">Logger for recording errors</param>
        /// <param name="operationContext">Human-readable description of what was being attempted</param>
        /// <param name="additionalContext">Additional context information</param>
        /// <returns>Result of the operation</returns>
        /// <exception cref="Exception">Rethrows the original exception after logging</exception>
        public static T ExecuteWithStandardErrorHandling<T>(
            Func<T> operation,
            IQobuzLogger logger,
            string operationContext,
            object additionalContext = null)
        {
            try
            {
                return operation();
            }
            catch (Exception ex)
            {
                LogStandardError(logger, ex, operationContext, additionalContext);
                throw;
            }
        }

        /// <summary>
        /// Executes an async operation with standardized error logging and rethrowing.
        /// Use this for critical async operations that should fail-fast.
        /// </summary>
        public static async Task<T> ExecuteWithStandardErrorHandlingAsync<T>(
            Func<Task<T>> operation,
            IQobuzLogger logger,
            string operationContext,
            object additionalContext = null)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogStandardError(logger, ex, operationContext, additionalContext);
                throw;
            }
        }

        /// <summary>
        /// Executes an operation with graceful error handling.
        /// Use this for non-critical operations that should continue on failure.
        /// </summary>
        /// <typeparam name="T">Return type of the operation</typeparam>
        /// <param name="operation">The operation to execute</param>
        /// <param name="logger">Logger for recording errors</param>
        /// <param name="operationContext">Human-readable description of what was being attempted</param>
        /// <param name="fallbackValue">Value to return if operation fails</param>
        /// <param name="logLevel">Severity level for logging (default: Warn)</param>
        /// <param name="additionalContext">Additional context information</param>
        /// <returns>Result of operation or fallback value</returns>
        public static T ExecuteWithGracefulErrorHandling<T>(
            Func<T> operation,
            IQobuzLogger logger,
            string operationContext,
            T fallbackValue = default(T),
            LogLevel logLevel = LogLevel.Warn,
            object additionalContext = null)
        {
            try
            {
                return operation();
            }
            catch (Exception ex)
            {
                LogGracefulError(logger, ex, operationContext, logLevel, additionalContext);
                return fallbackValue;
            }
        }

        /// <summary>
        /// Executes an async operation with graceful error handling.
        /// Use this for non-critical async operations that should continue on failure.
        /// </summary>
        public static async Task<T> ExecuteWithGracefulErrorHandlingAsync<T>(
            Func<Task<T>> operation,
            IQobuzLogger logger,
            string operationContext,
            T fallbackValue = default(T),
            LogLevel logLevel = LogLevel.Warn,
            object additionalContext = null)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogGracefulError(logger, ex, operationContext, logLevel, additionalContext);
                return fallbackValue;
            }
        }

        /// <summary>
        /// Executes an operation and swallows specific expected exception types.
        /// Use this carefully and only for truly expected exceptions.
        /// </summary>
        /// <typeparam name="TException">Type of exception to swallow</typeparam>
        /// <param name="operation">The operation to execute</param>
        /// <param name="logger">Logger for recording swallowed exceptions</param>
        /// <param name="operationContext">Human-readable description of what was being attempted</param>
        /// <param name="logSwallowedExceptions">Whether to log swallowed exceptions (default: true at Debug level)</param>
        public static void ExecuteWithSwallowedException<TException>(
            Action operation,
            IQobuzLogger logger,
            string operationContext,
            bool logSwallowedExceptions = true)
            where TException : Exception
        {
            try
            {
                operation();
            }
            catch (TException ex) when (logSwallowedExceptions)
            {
                logger.Debug("Swallowed expected exception during {0}: {1}", operationContext, ex.Message);
            }
            catch (TException) when (!logSwallowedExceptions)
            {
                // Silently swallow - use sparingly
            }
        }

        private static void LogStandardError(IQobuzLogger logger, Exception ex, string operationContext, object additionalContext)
        {
            var contextMessage = FormatContextMessage(operationContext, additionalContext);
            logger.Error(ex, "❌ CRITICAL ERROR: {0} - {1}", contextMessage, ex.Message);

            // Log additional exception details for debugging
            if (ex.InnerException != null)
            {
                logger.Debug("Inner exception: {0}", ex.InnerException.Message);
            }
        }

        private static void LogGracefulError(IQobuzLogger logger, Exception ex, string operationContext, LogLevel logLevel, object additionalContext)
        {
            var contextMessage = FormatContextMessage(operationContext, additionalContext);

            switch (logLevel)
            {
                case LogLevel.Error:
                    logger.Error(ex, "🚫 GRACEFUL ERROR: {0} - {1}", contextMessage, ex.Message);
                    break;
                case LogLevel.Warn:
                    logger.Warn(ex, "⚠️ GRACEFUL WARNING: {0} - {1}", contextMessage, ex.Message);
                    break;
                case LogLevel.Info:
                    logger.Info("ℹ️ GRACEFUL INFO: {0} - {1}", contextMessage, ex.Message);
                    break;
                case LogLevel.Debug:
                    logger.Debug("🐛 GRACEFUL DEBUG: {0} - {1}", contextMessage, ex.Message);
                    break;
            }
        }

        private static string FormatContextMessage(string operationContext, object additionalContext)
        {
            if (additionalContext == null)
                return operationContext;

            return $"{operationContext} (Context: {additionalContext})";
        }
    }

    /// <summary>
    /// Log level enumeration for graceful error handling
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }
}
