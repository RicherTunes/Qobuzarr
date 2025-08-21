using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    public class PerformanceMonitoringMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITelemetryService _telemetryService;
        private readonly ILogger<PerformanceMonitoringMiddleware> _logger;
        
        public PerformanceMonitoringMiddleware(
            RequestDelegate next,
            ITelemetryService telemetryService,
            ILogger<PerformanceMonitoringMiddleware> logger)
        {
            _next = next;
            _telemetryService = telemetryService;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestId = Guid.NewGuid().ToString("N");
            
            // Add request ID to response headers for tracing
            context.Response.Headers.Add("X-Request-Id", requestId);
            
            // Capture request details
            var tags = new Dictionary<string, string>
            {
                ["method"] = context.Request.Method,
                ["path"] = context.Request.Path.Value,
                ["request_id"] = requestId
            };
            
            try
            {
                // Track request start
                _telemetryService.RecordEvent("request_started", new Dictionary<string, string>
                {
                    ["request_id"] = requestId,
                    ["method"] = context.Request.Method,
                    ["path"] = context.Request.Path.Value,
                    ["query"] = context.Request.QueryString.Value,
                    ["user_agent"] = context.Request.Headers["User-Agent"].ToString()
                });
                
                // Execute the request
                await _next(context);
                
                // Record success metrics
                tags["status_code"] = context.Response.StatusCode.ToString();
                tags["status_category"] = GetStatusCategory(context.Response.StatusCode);
                
                _telemetryService.RecordMetric("requests.total", 1, tags);
                _telemetryService.RecordDuration("request", stopwatch.Elapsed, tags);
                
                // Log slow requests
                if (stopwatch.ElapsedMilliseconds > 1000)
                {
                    _logger.LogWarning("Slow request detected: {Method} {Path} took {ElapsedMs}ms",
                        context.Request.Method,
                        context.Request.Path,
                        stopwatch.ElapsedMilliseconds);
                    
                    _telemetryService.RecordEvent("slow_request", new Dictionary<string, string>
                    {
                        ["request_id"] = requestId,
                        ["duration_ms"] = stopwatch.ElapsedMilliseconds.ToString(),
                        ["method"] = context.Request.Method,
                        ["path"] = context.Request.Path.Value
                    });
                }
            }
            catch (Exception ex)
            {
                // Record error metrics
                tags["status_code"] = "500";
                tags["status_category"] = "5xx";
                tags["exception_type"] = ex.GetType().Name;
                
                _telemetryService.RecordMetric("requests.failed", 1, tags);
                _telemetryService.RecordDuration("request", stopwatch.Elapsed, tags);
                _telemetryService.RecordException(ex, new Dictionary<string, string>
                {
                    ["request_id"] = requestId,
                    ["method"] = context.Request.Method,
                    ["path"] = context.Request.Path.Value
                });
                
                _logger.LogError(ex, "Request {RequestId} failed after {ElapsedMs}ms",
                    requestId, stopwatch.ElapsedMilliseconds);
                
                throw;
            }
            finally
            {
                // Always record request completion
                _telemetryService.RecordEvent("request_completed", new Dictionary<string, string>
                {
                    ["request_id"] = requestId,
                    ["duration_ms"] = stopwatch.ElapsedMilliseconds.ToString(),
                    ["status_code"] = context.Response.StatusCode.ToString()
                });
                
                // Add performance headers
                context.Response.Headers.Add("X-Response-Time", $"{stopwatch.ElapsedMilliseconds}ms");
                
                _logger.LogDebug("Request {RequestId} completed in {ElapsedMs}ms with status {StatusCode}",
                    requestId, stopwatch.ElapsedMilliseconds, context.Response.StatusCode);
            }
        }

        private string GetStatusCategory(int statusCode)
        {
            return statusCode switch
            {
                >= 200 and < 300 => "2xx",
                >= 300 and < 400 => "3xx",
                >= 400 and < 500 => "4xx",
                >= 500 => "5xx",
                _ => "unknown"
            };
        }
    }

    public static class PerformanceMonitoringMiddlewareExtensions
    {
        public static IApplicationBuilder UsePerformanceMonitoring(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PerformanceMonitoringMiddleware>();
        }
    }
}