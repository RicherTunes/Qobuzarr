using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Utilities;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Centralizes quality fallback logic to eliminate code duplication across download services.
    /// Provides a consistent strategy for attempting downloads at different quality levels.
    /// </summary>
    public class QualityFallbackService : IQualityFallbackService
    {
        private readonly ILogger _logger;
        private readonly List<QobuzAudioQuality> _defaultQualityChain;

        /// <summary>
        /// Initializes a new instance of the <see cref="QualityFallbackService"/> class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        public QualityFallbackService(ILogger logger)
        {
            _logger = Guard.NotNull(logger, nameof(logger));
            
            // Default quality chain from highest to lowest
            _defaultQualityChain = new List<QobuzAudioQuality>
            {
                QobuzAudioQuality.FLACHiRes24Bit192Khz,
                QobuzAudioQuality.FLACHiRes24Bit96kHz,
                QobuzAudioQuality.FLACLossless,
                QobuzAudioQuality.MP3320
            };
        }

        /// <summary>
        /// Attempts to execute an operation with quality fallback.
        /// </summary>
        /// <typeparam name="T">The type of result expected from the operation.</typeparam>
        /// <param name="operation">The operation to execute with a specific quality.</param>
        /// <param name="qualityChain">Optional custom quality chain. Uses default if not provided.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>The result of the successful operation.</returns>
        /// <exception cref="QualityFallbackException">Thrown when all quality levels fail.</exception>
        public async Task<T> ExecuteWithFallbackAsync<T>(
            Func<QobuzAudioQuality, CancellationToken, Task<T>> operation,
            IEnumerable<QobuzAudioQuality> qualityChain = null,
            CancellationToken cancellationToken = default)
        {
            Guard.NotNull(operation, nameof(operation));
            
            var qualities = (qualityChain ?? _defaultQualityChain).ToList();
            if (!qualities.Any())
            {
                qualities = _defaultQualityChain;
            }

            var exceptions = new List<Exception>();
            
            foreach (var quality in qualities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    _logger.Debug($"Attempting operation with quality: {quality}");
                    var result = await operation(quality, cancellationToken).ConfigureAwait(false);
                    
                    _logger.Info($"Operation succeeded with quality: {quality}");
                    return result;
                }
                catch (OperationCanceledException)
                {
                    _logger.Debug("Operation cancelled by user");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Operation failed with quality {quality}: {ex.Message}");
                    exceptions.Add(new QualityAttemptException(quality, ex));
                    
                    // Continue to next quality level
                    if (quality != qualities.Last())
                    {
                        _logger.Debug($"Falling back to next quality level...");
                    }
                }
            }

            // All qualities failed
            throw new QualityFallbackException(
                "All quality levels failed. See inner exceptions for details.",
                exceptions);
        }

        /// <summary>
        /// Attempts to execute an operation with quality fallback (synchronous version).
        /// </summary>
        /// <typeparam name="T">The type of result expected from the operation.</typeparam>
        /// <param name="operation">The operation to execute with a specific quality.</param>
        /// <param name="qualityChain">Optional custom quality chain. Uses default if not provided.</param>
        /// <returns>The result of the successful operation.</returns>
        /// <exception cref="QualityFallbackException">Thrown when all quality levels fail.</exception>
        public T ExecuteWithFallback<T>(
            Func<QobuzAudioQuality, T> operation,
            IEnumerable<QobuzAudioQuality> qualityChain = null)
        {
            Guard.NotNull(operation, nameof(operation));
            
            var qualities = (qualityChain ?? _defaultQualityChain).ToList();
            if (!qualities.Any())
            {
                qualities = _defaultQualityChain;
            }

            var exceptions = new List<Exception>();
            
            foreach (var quality in qualities)
            {
                try
                {
                    _logger.Debug($"Attempting operation with quality: {quality}");
                    var result = operation(quality);
                    
                    _logger.Info($"Operation succeeded with quality: {quality}");
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Operation failed with quality {quality}: {ex.Message}");
                    exceptions.Add(new QualityAttemptException(quality, ex));
                    
                    // Continue to next quality level
                    if (quality != qualities.Last())
                    {
                        _logger.Debug($"Falling back to next quality level...");
                    }
                }
            }

            // All qualities failed
            throw new QualityFallbackException(
                "All quality levels failed. See inner exceptions for details.",
                exceptions);
        }

        /// <summary>
        /// Gets the default quality chain used for fallback.
        /// </summary>
        /// <returns>The default quality chain.</returns>
        public IReadOnlyList<QobuzAudioQuality> GetDefaultQualityChain()
        {
            return _defaultQualityChain.AsReadOnly();
        }

        /// <summary>
        /// Creates a custom quality chain based on maximum allowed quality.
        /// </summary>
        /// <param name="maxQuality">The maximum quality to include in the chain.</param>
        /// <returns>A quality chain up to and including the specified maximum quality.</returns>
        public IEnumerable<QobuzAudioQuality> CreateQualityChain(QobuzAudioQuality maxQuality)
        {
            return _defaultQualityChain
                .Where(q => (int)q <= (int)maxQuality)
                .OrderByDescending(q => (int)q);
        }

        /// <summary>
        /// Determines if a quality fallback should be attempted based on the exception type.
        /// </summary>
        /// <param name="exception">The exception to evaluate.</param>
        /// <returns>True if fallback should be attempted; otherwise, false.</returns>
        public bool ShouldAttemptFallback(Exception exception)
        {
            Guard.NotNull(exception, nameof(exception));
            
            // Don't retry on cancellation
            if (exception is OperationCanceledException)
                return false;
            
            // Don't retry on authentication failures
            if (exception.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
                return false;
            
            // Don't retry on argument exceptions (programming errors)
            if (exception is ArgumentException)
                return false;
            
            // Retry on most other exceptions (network issues, quality unavailable, etc.)
            return true;
        }
    }

    /// <summary>
    /// Interface for quality fallback service.
    /// </summary>
    public interface IQualityFallbackService
    {
        /// <summary>
        /// Attempts to execute an operation with quality fallback.
        /// </summary>
        Task<T> ExecuteWithFallbackAsync<T>(
            Func<QobuzAudioQuality, CancellationToken, Task<T>> operation,
            IEnumerable<QobuzAudioQuality> qualityChain = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempts to execute an operation with quality fallback (synchronous version).
        /// </summary>
        T ExecuteWithFallback<T>(
            Func<QobuzAudioQuality, T> operation,
            IEnumerable<QobuzAudioQuality> qualityChain = null);

        /// <summary>
        /// Gets the default quality chain used for fallback.
        /// </summary>
        IReadOnlyList<QobuzAudioQuality> GetDefaultQualityChain();

        /// <summary>
        /// Creates a custom quality chain based on maximum allowed quality.
        /// </summary>
        IEnumerable<QobuzAudioQuality> CreateQualityChain(QobuzAudioQuality maxQuality);

        /// <summary>
        /// Determines if a quality fallback should be attempted based on the exception type.
        /// </summary>
        bool ShouldAttemptFallback(Exception exception);
    }

    /// <summary>
    /// Exception thrown when all quality levels fail during fallback.
    /// </summary>
    public class QualityFallbackException : AggregateException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QualityFallbackException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerExceptions">The collection of exceptions from each quality attempt.</param>
        public QualityFallbackException(string message, IEnumerable<Exception> innerExceptions)
            : base(message, innerExceptions)
        {
        }

        /// <summary>
        /// Gets the quality attempts that failed.
        /// </summary>
        public IEnumerable<QualityAttemptException> QualityAttempts =>
            InnerExceptions.OfType<QualityAttemptException>();
    }

    /// <summary>
    /// Exception representing a failed quality attempt.
    /// </summary>
    public class QualityAttemptException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QualityAttemptException"/> class.
        /// </summary>
        /// <param name="quality">The quality level that failed.</param>
        /// <param name="innerException">The original exception.</param>
        public QualityAttemptException(QobuzAudioQuality quality, Exception innerException)
            : base($"Failed at quality {quality}", innerException)
        {
            Quality = quality;
        }

        /// <summary>
        /// Gets the quality level that failed.
        /// </summary>
        public QobuzAudioQuality Quality { get; }
    }
}