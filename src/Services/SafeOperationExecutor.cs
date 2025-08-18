using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Configuration;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Simple utility class for executing operations safely with common defensive patterns
    /// Prevents cascading failures and provides consistent error handling
    /// </summary>
    public static class SafeOperationExecutor
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Executes file operation with proper file locking and retry logic
        /// </summary>
        public static T ExecuteFileOperation<T>(
            string filePath, 
            Func<string, T> operation, 
            T fallbackValue = default,
            int maxRetries = 3)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                Logger.Warn("🛡️ DEFENSIVE: File path is null/empty, using fallback");
                return fallbackValue;
            }

            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Defensive: Ensure file exists before operation
                    if (!File.Exists(filePath))
                    {
                        Logger.Debug("🛡️ DEFENSIVE: File not found '{0}', using fallback", filePath);
                        return fallbackValue;
                    }

                    return operation(filePath);
                }
                catch (IOException ex) when (attempt < maxRetries)
                {
                    lastException = ex;
                    Logger.Debug("🔄 FILE RETRY: Attempt {0} failed for '{1}': {2}", attempt, filePath, ex.Message);
                    
                    // Wait briefly before retry (file might be locked)
                    Thread.Sleep(QobuzConstants.Timing.FileOperations.RetryBaseDelayMs * attempt);
                }
                catch (UnauthorizedAccessException ex)
                {
                    Logger.Warn(ex, "🛡️ DEFENSIVE: Access denied for '{0}', using fallback", filePath);
                    return fallbackValue;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "🛡️ DEFENSIVE: File operation failed for '{0}', using fallback", filePath);
                    return fallbackValue;
                }
            }

            Logger.Error(lastException, "🛡️ DEFENSIVE: File operation exhausted retries for '{0}', using fallback", filePath);
            return fallbackValue;
        }

        /// <summary>
        /// Executes async operation with timeout and defensive error handling
        /// </summary>
        public static async Task<T> ExecuteWithTimeoutAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            TimeSpan timeout,
            T fallbackValue = default,
            string operationName = "Operation")
        {
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                return await operation(cts.Token);
            }
            catch (OperationCanceledException) when (timeout != Timeout.InfiniteTimeSpan)
            {
                Logger.Warn("⏰ DEFENSIVE: {0} timed out after {1:F1}s, using fallback", operationName, timeout.TotalSeconds);
                return fallbackValue;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "🛡️ DEFENSIVE: {0} failed, using fallback", operationName);
                return fallbackValue;
            }
        }

        /// <summary>
        /// Executes operation with null safety and validation
        /// </summary>
        public static T ExecuteWithNullSafety<TInput, T>(
            TInput input,
            Func<TInput, T> operation,
            T fallbackValue = default,
            string operationName = "Operation")
        {
            if (input == null)
            {
                Logger.Debug("🛡️ DEFENSIVE: {0} input is null, using fallback", operationName);
                return fallbackValue;
            }

            try
            {
                return operation(input);
            }
            catch (ArgumentNullException ex)
            {
                Logger.Warn(ex, "🛡️ DEFENSIVE: {0} null argument, using fallback", operationName);
                return fallbackValue;
            }
            catch (NullReferenceException ex)
            {
                Logger.Warn(ex, "🛡️ DEFENSIVE: {0} null reference, using fallback", operationName);
                return fallbackValue;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "🛡️ DEFENSIVE: {0} failed, using fallback", operationName);
                return fallbackValue;
            }
        }

        /// <summary>
        /// Validates string input and provides safe fallback
        /// </summary>
        public static string ValidateString(string input, string fallback = "Unknown", bool allowEmpty = false)
        {
            if (input == null)
                return fallback;

            if (!allowEmpty && string.IsNullOrWhiteSpace(input))
                return fallback;

            // Defensive: Limit string length to prevent memory issues
            if (input.Length > 10000)
            {
                Logger.Warn("🛡️ DEFENSIVE: String too long ({0} chars), truncating", input.Length);
                return input.Substring(0, 1000) + "...";
            }

            return input;
        }

        /// <summary>
        /// Safe directory creation with proper error handling
        /// </summary>
        public static bool EnsureDirectoryExists(string directoryPath, string operationContext = "Operation")
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                Logger.Warn("🛡️ DEFENSIVE: {0} directory path is null/empty", operationContext);
                return false;
            }

            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                    Logger.Debug("📁 DIRECTORY CREATED: {0}", directoryPath);
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "🛡️ DEFENSIVE: {0} failed to create directory '{1}'", operationContext, directoryPath);
                return false;
            }
        }

        /// <summary>
        /// Safe disposal with error handling
        /// </summary>
        public static void SafeDispose(IDisposable disposable, string resourceName = "Resource")
        {
            try
            {
                disposable?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "🛡️ DEFENSIVE: Failed to dispose {0}", resourceName);
            }
        }

        /// <summary>
        /// Validates numeric values with range checking
        /// </summary>
        public static T ValidateNumeric<T>(T value, T min, T max, T fallback, string parameterName = "Value") 
            where T : IComparable<T>
        {
            try
            {
                if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
                {
                    Logger.Warn("🛡️ DEFENSIVE: {0} out of range [{1}-{2}]: {3}, using fallback", 
                               parameterName, min, max, value);
                    return fallback;
                }
                return value;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "🛡️ DEFENSIVE: Failed to validate {0}, using fallback", parameterName);
                return fallback;
            }
        }
    }
}