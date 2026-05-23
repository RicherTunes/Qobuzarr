using System;
using System.Net;

namespace Lidarr.Plugin.Qobuzarr.Exceptions
{
    /// <summary>
    /// Exception thrown when Qobuz API requests fail.
    /// </summary>
    public class QobuzApiException : Exception
    {
        /// <summary>
        /// The HTTP status code if applicable.
        /// </summary>
        public HttpStatusCode? StatusCode { get; }

        /// <summary>
        /// The API endpoint that failed.
        /// </summary>
        public string Endpoint { get; }

        /// <summary>
        /// The error code returned by Qobuz API.
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        /// Whether this error is likely temporary and worth retrying.
        /// </summary>
        public bool IsRetryable { get; }

        /// <summary>
        /// Alias for <see cref="ErrorCode"/>.
        /// </summary>
        public string ErrorType => ErrorCode ?? "Unknown";

        public QobuzApiException(string message, string endpoint, HttpStatusCode? statusCode = null, string errorCode = null, bool isRetryable = false)
            : base(message)
        {
            Endpoint = endpoint;
            StatusCode = statusCode;
            ErrorCode = errorCode;
            IsRetryable = isRetryable;
        }

        /// <summary>
        /// Compact constructor for callers where the endpoint isn't readily
        /// available at the throw site (e.g. centralized HTTP error handlers).
        /// </summary>
        public QobuzApiException(string message, int statusCode, string errorType)
            : this(message, endpoint: string.Empty, statusCode: (HttpStatusCode)statusCode, errorCode: errorType)
        {
        }

        public QobuzApiException(string message, string endpoint, Exception innerException, HttpStatusCode? statusCode = null, string errorCode = null, bool isRetryable = false)
            : base(message, innerException)
        {
            Endpoint = endpoint;
            StatusCode = statusCode;
            ErrorCode = errorCode;
            IsRetryable = isRetryable;
        }

        /// <summary>
        /// Creates an exception for rate limiting.
        /// </summary>
        public static QobuzApiException RateLimitExceeded(string endpoint, TimeSpan? retryAfter = null)
        {
            var message = $"Rate limit exceeded for endpoint: {endpoint}";
            if (retryAfter.HasValue)
                message += $". Retry after {retryAfter.Value.TotalSeconds} seconds.";

            return new QobuzApiException(message, endpoint, HttpStatusCode.TooManyRequests, "rate_limit_exceeded", isRetryable: true);
        }

        /// <summary>
        /// Creates an exception for invalid request signature.
        /// </summary>
        public static QobuzApiException InvalidRequestSignature(string endpoint)
        {
            return new QobuzApiException(
                "Invalid request signature. This usually indicates mismatched App ID and App Secret.",
                endpoint,
                HttpStatusCode.Unauthorized,
                "invalid_request_sig");
        }

        /// <summary>
        /// Creates an exception for network timeout.
        /// </summary>
        public static QobuzApiException Timeout(string endpoint, Exception innerException)
        {
            return new QobuzApiException(
                $"Request to {endpoint} timed out",
                endpoint,
                innerException,
                statusCode: HttpStatusCode.RequestTimeout,
                isRetryable: true);
        }

        /// <summary>
        /// Creates an exception for server errors.
        /// </summary>
        public static QobuzApiException ServerError(string endpoint, HttpStatusCode statusCode)
        {
            return new QobuzApiException(
                $"Qobuz server error at {endpoint}: {statusCode}",
                endpoint,
                statusCode,
                errorCode: "server_error",
                isRetryable: statusCode >= HttpStatusCode.InternalServerError);
        }
    }
}
