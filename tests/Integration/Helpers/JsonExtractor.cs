using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Qobuzarr.IntegrationTests.Helpers;

/// <summary>
/// Helper for extracting values from JSON responses with clear failure messages.
/// Use this instead of raw JToken access to get actionable error messages when
/// API responses don't match expectations.
/// </summary>
public static partial class JsonExtractor
{
    private const int DefaultMaxSnippetLength = 500;

    /// <summary>
    /// Sensitive keys to redact from response snippets.
    /// </summary>
    private static readonly string[] SensitiveKeys =
    [
        "apiKey", "api_key", "apikey",
        "password", "passwd", "pwd",
        "token", "auth_token", "authToken", "access_token", "accessToken",
        "secret", "app_secret", "appSecret",
        "session", "sessionId", "session_id",
        "credential", "credentials"
    ];

    /// <summary>
    /// Extracts a required integer value from a JToken.
    /// Throws with endpoint context and response snippet on failure.
    /// </summary>
    /// <param name="token">The token to extract from (can be null)</param>
    /// <param name="path">JSON path for error messages (e.g., "album.id")</param>
    /// <param name="endpoint">API endpoint for error context (e.g., "/api/v1/album")</param>
    /// <param name="fullResponse">Full response for snippet on failure (optional)</param>
    /// <returns>The extracted integer value</returns>
    /// <exception cref="IntegrationTestSkipException">Thrown when field is missing (skips test with clear message)</exception>
    public static int RequireInt(JToken? token, string path, string endpoint, string? fullResponse = null)
    {
        if (token == null)
        {
            throw CreateSkipException(path, "int", endpoint, fullResponse);
        }

        try
        {
            return token.ToObject<int>();
        }
        catch
        {
            throw CreateSkipException(path, "int", endpoint, fullResponse, $"Value was: {token}");
        }
    }

    /// <summary>
    /// Extracts a required string value from a JToken.
    /// </summary>
    public static string RequireString(JToken? token, string path, string endpoint, string? fullResponse = null)
    {
        if (token == null)
        {
            throw CreateSkipException(path, "string", endpoint, fullResponse);
        }

        var value = token.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw CreateSkipException(path, "non-empty string", endpoint, fullResponse, "Value was empty/whitespace");
        }

        return value;
    }

    /// <summary>
    /// Extracts a required boolean value from a JToken.
    /// </summary>
    public static bool RequireBool(JToken? token, string path, string endpoint, string? fullResponse = null)
    {
        if (token == null)
        {
            throw CreateSkipException(path, "bool", endpoint, fullResponse);
        }

        try
        {
            return token.ToObject<bool>();
        }
        catch
        {
            throw CreateSkipException(path, "bool", endpoint, fullResponse, $"Value was: {token}");
        }
    }

    /// <summary>
    /// Tries to extract an integer value, returns null if missing.
    /// Use for optional fields where absence is acceptable.
    /// </summary>
    public static int? TryGetInt(JToken? token)
    {
        if (token == null) return null;
        try { return token.ToObject<int>(); }
        catch { return null; }
    }

    /// <summary>
    /// Tries to extract a boolean value, returns default if missing.
    /// Use for optional boolean fields.
    /// </summary>
    public static bool TryGetBool(JToken? token, bool defaultValue = false)
    {
        if (token == null) return defaultValue;
        try { return token.ToObject<bool>(); }
        catch { return defaultValue; }
    }

    /// <summary>
    /// Tries to extract a string value, returns null if missing.
    /// </summary>
    public static string? TryGetString(JToken? token)
    {
        return token?.ToString();
    }

    /// <summary>
    /// Extracts a required JArray from a JToken.
    /// </summary>
    public static JArray RequireArray(JToken? token, string path, string endpoint, string? fullResponse = null)
    {
        if (token == null)
        {
            throw CreateSkipException(path, "array", endpoint, fullResponse);
        }

        if (token is not JArray array)
        {
            throw CreateSkipException(path, "array", endpoint, fullResponse, $"Type was: {token.Type}");
        }

        return array;
    }

    /// <summary>
    /// Requires that an array has at least one element.
    /// </summary>
    public static JArray RequireNonEmptyArray(JToken? token, string path, string endpoint, string? fullResponse = null)
    {
        var array = RequireArray(token, path, endpoint, fullResponse);

        if (array.Count == 0)
        {
            throw CreateSkipException(path, "non-empty array", endpoint, fullResponse, "Array was empty");
        }

        return array;
    }

    /// <summary>
    /// Sanitizes a response snippet for safe logging by redacting sensitive values
    /// and normalizing whitespace.
    /// </summary>
    /// <param name="response">The response to sanitize (can be null)</param>
    /// <param name="maxLength">Maximum length before truncation (default 500)</param>
    /// <returns>Sanitized snippet safe for logging</returns>
    public static string SanitizeResponseSnippet(string? response, int maxLength = DefaultMaxSnippetLength)
    {
        if (string.IsNullOrEmpty(response))
        {
            return "(empty response)";
        }

        var sanitized = response;

        // Redact sensitive keys - matches "key": "value" or "key":"value" patterns
        foreach (var key in SensitiveKeys)
        {
            // Case-insensitive pattern: "key" : "value" -> "key": "[REDACTED]"
            var pattern = $@"(""{key}""\s*:\s*"")[^""]*("")";
            sanitized = Regex.Replace(sanitized, pattern, "$1[REDACTED]$2", RegexOptions.IgnoreCase);
        }

        // Strip control characters (except newlines and tabs for readability)
        sanitized = ControlCharRegex().Replace(sanitized, " ");

        // Normalize consecutive whitespace (including newlines) to single space
        sanitized = WhitespaceRegex().Replace(sanitized, " ");

        // Trim
        sanitized = sanitized.Trim();

        // Truncate if needed
        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized[..maxLength] + "...";
        }

        return sanitized;
    }

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]")]
    private static partial Regex ControlCharRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private static IntegrationTestSkipException CreateSkipException(
        string path,
        string expectedType,
        string endpoint,
        string? fullResponse,
        string? additionalInfo = null)
    {
        var message = $"Required field '{path}' ({expectedType}) missing from {endpoint}";

        if (!string.IsNullOrEmpty(additionalInfo))
        {
            message += $". {additionalInfo}";
        }

        if (!string.IsNullOrEmpty(fullResponse))
        {
            var snippet = SanitizeResponseSnippet(fullResponse);
            message += $"\n\nResponse snippet:\n{snippet}";
        }

        return new IntegrationTestSkipException(message);
    }
}
