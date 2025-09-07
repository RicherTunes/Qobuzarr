using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Lidarr.Plugin.Qobuzarr.Observability
{
    // Common logging keys for structured correlation
    public static class LogScopeKeys
    {
        public const string CorrelationId = "correlationId";   // Top-level correlation across components
        public const string OperationId = "operationId";       // Logical operation (e.g., "Search", "Download")
        public const string RequestId = "requestId";           // External HTTP request Id
        public const string SessionId = "sessionId";           // Qobuz auth/session Id
        public const string AlbumId = "albumId";
        public const string TrackId = "trackId";
        public const string ArtistId = "artistId";
        public const string JobId = "jobId";                   // Queue or orchestrator job Id
        public const string RetryAttempt = "retryAttempt";     // For transient failures & policies
        public const string RateLimitBucket = "rateLimitBucket";// Which limiter/partition handled the call
    }

    public static class LoggingScopeExtensions
    {
        public static IDisposable BeginOperationScope(this ILogger logger,
            string operationName,
            string? correlationId = null,
            string? requestId = null,
            string? sessionId = null,
            string? albumId = null,
            string? trackId = null,
            string? artistId = null,
            string? jobId = null,
            int? retryAttempt = null,
            string? rateLimitBucket = null)
        {
            var scope = new Dictionary<string, object?>
            {
                [LogScopeKeys.OperationId] = operationName,
            };

            // Populate if provided
            if (!string.IsNullOrWhiteSpace(correlationId)) scope[LogScopeKeys.CorrelationId] = correlationId;
            if (!string.IsNullOrWhiteSpace(requestId))     scope[LogScopeKeys.RequestId]     = requestId;
            if (!string.IsNullOrWhiteSpace(sessionId))     scope[LogScopeKeys.SessionId]     = sessionId;
            if (!string.IsNullOrWhiteSpace(albumId))       scope[LogScopeKeys.AlbumId]       = albumId;
            if (!string.IsNullOrWhiteSpace(trackId))       scope[LogScopeKeys.TrackId]       = trackId;
            if (!string.IsNullOrWhiteSpace(artistId))      scope[LogScopeKeys.ArtistId]      = artistId;
            if (!string.IsNullOrWhiteSpace(jobId))         scope[LogScopeKeys.JobId]         = jobId;
            if (retryAttempt.HasValue)                      scope[LogScopeKeys.RetryAttempt]  = retryAttempt.Value;
            if (!string.IsNullOrWhiteSpace(rateLimitBucket)) scope[LogScopeKeys.RateLimitBucket] = rateLimitBucket;

            // Ensure a top-level correlation id exists
            if (!scope.ContainsKey(LogScopeKeys.CorrelationId))
            {
                scope[LogScopeKeys.CorrelationId] = Guid.NewGuid().ToString("N");
            }

            return logger.BeginScope(scope);
        }
    }
}

