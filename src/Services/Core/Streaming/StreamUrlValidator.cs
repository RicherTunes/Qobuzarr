using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;
using IStreamUrlValidatorInterface = Lidarr.Plugin.Qobuzarr.Services.Interfaces.IStreamUrlValidator;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;

namespace Lidarr.Plugin.Qobuzarr.Services.Core.Streaming
{
    /// <summary>
    /// Implementation of stream URL validator with comprehensive validation rules.
    /// Implements the centralized IStreamUrlValidator interface.
    /// </summary>
    public class StreamUrlValidator : IStreamUrlValidatorInterface
    {
        private readonly IQobuzLogger _logger;

        // Preview/sample detection patterns
        private static readonly string[] PreviewPatterns = 
        {
            "_preview", "preview_", "/preview/", "preview=true",
            "_sample", "sample_", "/sample/", "sample=true", 
            "_demo", "demo_", "_30sec", "_30s", "duration=30",
            "_clip", "clip_", "_short", "preview.", "sample.", "demo."
        };

        // Subscription restriction indicators
        private static readonly string[] SubscriptionPatterns =
        {
            "subscription", "premium", "plan", "credentials", 
            "purchase", "restricted", "forbidden"
        };

        // Geographic restriction indicators  
        private static readonly string[] GeographicPatterns =
        {
            "region", "country", "geographic", "location", "territory"
        };

        public StreamUrlValidator(IQobuzLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Implement centralized interface methods
        public async Task<StreamUrlValidationResult> ValidateUrlAsync(string url, CancellationToken cancellationToken = default)
        {
            var result = new StreamUrlValidationResult
            {
                IsValid = false,
                IsAccessible = false,
                IsSecure = false
            };

            var startTime = DateTime.UtcNow;

            try
            {
                result.IsValid = IsValidUrlFormat(url);
                result.IsSecure = url?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true;
                result.IsAccessible = await IsUrlAccessibleAsync(url, cancellationToken);
                result.ExpiresAt = EstimateUrlExpiration(url);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            result.ValidationDuration = DateTime.UtcNow - startTime;
            return result;
        }

        public bool IsValidUrlFormat(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return uri.Scheme == "http" || uri.Scheme == "https";
        }

        public async Task<bool> IsUrlAccessibleAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
                var response = await client.SendAsync(request, cancellationToken);
                
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ValidateContentTypeAsync(string url, string expectedFormat, CancellationToken cancellationToken = default)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
                var response = await client.SendAsync(request, cancellationToken);
                
                var contentType = response.Content.Headers.ContentType?.MediaType?.ToLower();
                
                return expectedFormat.ToLower() switch
                {
                    "flac" => contentType == "audio/flac" || contentType == "audio/x-flac",
                    "mp3" => contentType == "audio/mpeg" || contentType == "audio/mp3",
                    _ => true // Allow unknown formats
                };
            }
            catch
            {
                return false;
            }
        }

        public DateTime? EstimateUrlExpiration(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            // Try to extract expiration from URL parameters
            var uri = Uri.TryCreate(url, UriKind.Absolute, out var parsedUri) ? parsedUri : null;
            if (uri == null) return null;

            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            
            // Common parameter names for expiration
            var expiryParams = new[] { "expires", "expire", "exp", "expiry" };
            
            foreach (var param in expiryParams)
            {
                var value = query[param];
                if (string.IsNullOrEmpty(value)) continue;
                
                // Try Unix timestamp
                if (long.TryParse(value, out var timestamp))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
                }
            }

            // Default assumption: URLs expire in 6 hours
            return DateTime.UtcNow.AddHours(6);
        }

        public StreamValidationResult ValidateStreamUrl(string url)
        {
            var result = new StreamValidationResult
            {
                Url = url,
                ValidatedAt = DateTime.UtcNow
            };

            // Basic URL validation
            if (string.IsNullOrWhiteSpace(url))
            {
                result.IsValid = false;
                result.Issue = StreamValidationIssue.EmptyUrl;
                result.Message = "Stream URL is null or empty";
                return result;
            }

            // Check for preview/sample indicators
            if (IsPreviewOrSampleUrl(url))
            {
                result.IsValid = false;
                result.Issue = StreamValidationIssue.PreviewOnly;
                result.Message = "URL appears to be preview/sample, not full track";
                _logger.Debug("Stream URL validation failed: preview/sample detected in {0}", url);
                return result;
            }

            // Check URL format validity
            if (!IsValidUrlFormatLegacy(url))
            {
                result.IsValid = false;
                result.Issue = StreamValidationIssue.InvalidFormat;
                result.Message = "URL format appears invalid";
                _logger.Debug("Stream URL validation failed: invalid format {0}", url);
                return result;
            }

            // URL passes validation
            result.IsValid = true;
            result.Message = "Stream URL validation successful";
            _logger.Debug("Stream URL validation passed for {0}", url);
            
            return result;
        }

        public StreamValidationResult ValidateStreamResponse(QobuzStreamResponse response)
        {
            var result = new StreamValidationResult
            {
                ValidatedAt = DateTime.UtcNow
            };

            if (response == null)
            {
                result.IsValid = false;
                result.Issue = StreamValidationIssue.NoResponse;
                result.Message = "No response from Qobuz API";
                return result;
            }

            result.Url = response.Url;

            // Check if response indicates sample/preview
            if (response.Sample == true)
            {
                result.IsValid = false;
                result.Issue = StreamValidationIssue.PreviewOnly;
                result.Message = "Response indicates sample/preview track only";
                _logger.Debug("Stream response validation failed: sample flag set");
                return result;
            }

            // Check for API error status
            if (!response.IsSuccess)
            {
                result.IsValid = false;
                result.Issue = IdentifyApiErrorIssue(response);
                result.Message = GetDetailedErrorMessage(response);
                _logger.Debug("Stream response validation failed: API error - {0}", result.Message);
                return result;
            }

            // Validate the URL within the response
            var urlValidation = ValidateStreamUrl(response.Url);
            if (!urlValidation.IsValid)
            {
                return urlValidation; // Forward URL validation result
            }

            // Response passes validation
            result.IsValid = true;
            result.Message = "Stream response validation successful";
            
            return result;
        }

        public bool IsPreviewOrSampleUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return true;

            var urlLower = url.ToLower();
            return PreviewPatterns.Any(pattern => urlLower.Contains(pattern));
        }

        public StreamValidationIssue IdentifyValidationIssue(string url, QobuzStreamResponse response = null)
        {
            // Check response-specific issues first
            if (response != null)
            {
                if (response.Sample == true)
                    return StreamValidationIssue.PreviewOnly;

                if (!response.IsSuccess)
                    return IdentifyApiErrorIssue(response);
            }

            // Check URL-specific issues
            if (string.IsNullOrWhiteSpace(url))
                return StreamValidationIssue.EmptyUrl;

            if (IsPreviewOrSampleUrl(url))
                return StreamValidationIssue.PreviewOnly;

            if (!IsValidUrlFormat(url))
                return StreamValidationIssue.InvalidFormat;

            return StreamValidationIssue.Unknown;
        }

        private bool IsValidUrlFormatLegacy(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            // Basic URL format validation
            try
            {
                var uri = new Uri(url);
                return uri.Scheme == "http" || uri.Scheme == "https";
            }
            catch
            {
                return false;
            }
        }

        private StreamValidationIssue IdentifyApiErrorIssue(QobuzStreamResponse response)
        {
            var message = response.Message?.ToLowerInvariant() ?? "";

            // Check for subscription issues
            if (SubscriptionPatterns.Any(pattern => message.Contains(pattern)))
                return StreamValidationIssue.SubscriptionRestriction;

            // Check for geographic restrictions
            if (GeographicPatterns.Any(pattern => message.Contains(pattern)))
                return StreamValidationIssue.RegionalRestriction;

            // Check for format/quality issues
            if (message.Contains("format") || message.Contains("quality"))
                return StreamValidationIssue.QualityUnavailable;

            // Check HTTP status codes
            if (response.Code == 403)
                return StreamValidationIssue.SubscriptionRestriction;

            if (response.Code == 404)
                return StreamValidationIssue.TrackNotFound;

            if (response.Code >= 500)
                return StreamValidationIssue.ServerError;

            return StreamValidationIssue.ApiError;
        }

        private string GetDetailedErrorMessage(QobuzStreamResponse response)
        {
            var parts = new List<string>();

            if (response.Code.HasValue && response.Code != 200)
            {
                parts.Add($"HTTP {response.Code}");
            }

            if (!string.IsNullOrWhiteSpace(response.Message))
            {
                parts.Add(response.Message);
            }

            var restrictionMessage = response.GetRestrictionMessage();
            if (!string.IsNullOrWhiteSpace(restrictionMessage))
            {
                parts.Add(restrictionMessage);
            }

            return parts.Any() ? string.Join(" - ", parts) : "API request failed";
        }
    }

    /// <summary>
    /// Result of stream URL validation.
    /// </summary>
    public class StreamValidationResult
    {
        public string Url { get; set; }
        public bool IsValid { get; set; }
        public StreamValidationIssue Issue { get; set; }
        public string Message { get; set; }
        public DateTime ValidatedAt { get; set; }
    }

    /// <summary>
    /// Types of stream validation issues.
    /// </summary>
    public enum StreamValidationIssue
    {
        None = 0,
        EmptyUrl,
        InvalidFormat,
        PreviewOnly,
        SubscriptionRestriction,
        RegionalRestriction,
        QualityUnavailable,
        TrackNotFound,
        ServerError,
        ApiError,
        NoResponse,
        Unknown
    }
}