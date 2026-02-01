using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for validating Qobuz streaming URLs.
    /// </summary>
    /// <remarks>
    /// This interface provides validation capabilities for streaming URLs
    /// to ensure they are valid, accessible, and have expected properties.
    /// 
    /// Key Features:
    /// - URL format validation
    /// - Accessibility testing
    /// - Content type validation
    /// - Expiration checking
    /// - Security validation
    /// 
    /// URL validation helps prevent download failures and ensures
    /// streaming URLs meet quality and security requirements.
    /// </remarks>
    public interface IStreamUrlValidator
    {
        /// <summary>
        /// Validates a streaming URL for format and accessibility.
        /// </summary>
        /// <param name="url">The streaming URL to validate</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Validation result with details</returns>
        Task<StreamUrlValidationResult> ValidateUrlAsync(string url, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a URL format is valid for Qobuz streaming.
        /// </summary>
        /// <param name="url">The URL to check</param>
        /// <returns>True if the URL format is valid</returns>
        bool IsValidUrlFormat(string url);

        /// <summary>
        /// Tests if a streaming URL is accessible.
        /// </summary>
        /// <param name="url">The URL to test</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>True if the URL is accessible</returns>
        Task<bool> IsUrlAccessibleAsync(string url, CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates the content type of a streaming URL.
        /// </summary>
        /// <param name="url">The URL to validate</param>
        /// <param name="expectedFormat">The expected audio format (e.g., "flac", "mp3")</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>True if the content type matches expectations</returns>
        Task<bool> ValidateContentTypeAsync(string url, string expectedFormat, CancellationToken cancellationToken = default);

        /// <summary>
        /// Estimates URL expiration time if possible.
        /// </summary>
        /// <param name="url">The URL to analyze</param>
        /// <returns>Estimated expiration time or null if unknown</returns>
        System.DateTime? EstimateUrlExpiration(string url);
    }

    /// <summary>
    /// Result of streaming URL validation.
    /// </summary>
    public class StreamUrlValidationResult
    {
        public bool IsValid { get; set; }
        public bool IsAccessible { get; set; }
        public bool IsSecure { get; set; }
        public string ContentType { get; set; }
        public long? ContentLength { get; set; }
        public System.DateTime? ExpiresAt { get; set; }
        public string Error { get; set; }
        public System.TimeSpan ValidationDuration { get; set; }
    }
}
