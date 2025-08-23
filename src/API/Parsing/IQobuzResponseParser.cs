using System;
using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.API.Parsing
{
    /// <summary>
    /// Handles parsing and validation of Qobuz API responses.
    /// This interface is responsible for deserializing JSON responses and handling parsing errors.
    /// </summary>
    public interface IQobuzResponseParser
    {
        /// <summary>
        /// Parses a JSON response from the Qobuz API.
        /// </summary>
        /// <typeparam name="T">The expected response type.</typeparam>
        /// <param name="content">The JSON content to parse.</param>
        /// <returns>The deserialized object of type T.</returns>
        /// <exception cref="QobuzResponseParsingException">Thrown when parsing fails.</exception>
        T ParseResponse<T>(string content) where T : class;

        /// <summary>
        /// Attempts to parse an error response from the Qobuz API.
        /// </summary>
        /// <param name="content">The JSON content to parse.</param>
        /// <returns>The parsed error response, or null if parsing fails.</returns>
        QobuzErrorResponse? TryParseErrorResponse(string content);

        /// <summary>
        /// Validates that a response contains expected data.
        /// </summary>
        /// <typeparam name="T">The type of the response object.</typeparam>
        /// <param name="response">The response object to validate.</param>
        /// <returns>True if the response is valid; false otherwise.</returns>
        bool ValidateResponse<T>(T response) where T : class;

        /// <summary>
        /// Gets custom JSON serializer settings for Qobuz API responses.
        /// </summary>
        /// <returns>The configured JsonSerializerSettings.</returns>
        JsonSerializerSettings GetSerializerSettings();
    }

    /// <summary>
    /// Represents an error response from the Qobuz API.
    /// </summary>
    public class QobuzErrorResponse
    {
        /// <summary>
        /// Gets or sets the error message.
        /// </summary>
        [JsonProperty("message")]
        public string? Message { get; set; }

        /// <summary>
        /// Gets or sets the error code.
        /// </summary>
        [JsonProperty("code")]
        public int? Code { get; set; }

        /// <summary>
        /// Gets or sets the error status.
        /// </summary>
        [JsonProperty("status")]
        public string? Status { get; set; }
    }

    /// <summary>
    /// Exception thrown when response parsing fails.
    /// </summary>
    public class QobuzResponseParsingException : Exception
    {
        /// <summary>
        /// Gets the raw content that failed to parse.
        /// </summary>
        public string RawContent { get; }

        /// <summary>
        /// Gets the target type that parsing was attempted for.
        /// </summary>
        public Type TargetType { get; }

        /// <summary>
        /// Initializes a new instance of the QobuzResponseParsingException class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="rawContent">The raw content that failed to parse.</param>
        /// <param name="targetType">The target type for parsing.</param>
        /// <param name="innerException">The inner exception.</param>
        public QobuzResponseParsingException(string message, string rawContent, Type targetType, Exception? innerException = null)
            : base(message, innerException)
        {
            RawContent = rawContent;
            TargetType = targetType;
        }
    }
}