// <copyright file="TestConnectionResult.cs" company="Lidarr.Plugin.Qobuzarr">
// Copyright (c) Lidarr.Plugin.Qobuzarr. All rights reserved.
// </copyright>

using System.Text.Json;
using System.Text.Json.Serialization;
using Lidarr.Plugin.Common.Abstractions.Llm;

namespace Lidarr.Plugin.Qobuzarr.Indexers;

/// <summary>
/// Adapter class for Test Connection results that provides standardized JSON structure.
/// Implements DIAG-01 (standardized JSON structure) and DIAG-02 (extended fields).
/// </summary>
public sealed class TestConnectionResult
{
    /// <summary>
    /// Provider identifier (e.g., "qobuz")
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Authentication method used (e.g., "oauth")
    /// </summary>
    [JsonPropertyName("authMethod")]
    public string AuthMethod { get; set; } = string.Empty;

    /// <summary>
    /// Streaming service model identifier
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// Response time in milliseconds
    /// </summary>
    [JsonPropertyName("latencyMs")]
    public long LatencyMs { get; set; }

    /// <summary>
    /// Whether the connection is healthy
    /// </summary>
    [JsonPropertyName("isHealthy")]
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Status message describing the health state
    /// </summary>
    [JsonPropertyName("statusMessage")]
    public string? StatusMessage { get; set; }

    /// <summary>
    /// Error code if not healthy (DIAG-02)
    /// </summary>
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Full error details (for serialization)
    /// </summary>
    [JsonPropertyName("errorDetails")]
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// Creates a successful test connection result
    /// </summary>
    public static TestConnectionResult Success(string provider, string authMethod, string model, long latencyMs)
    {
        return new TestConnectionResult
        {
            Provider = provider,
            AuthMethod = authMethod,
            Model = model,
            LatencyMs = latencyMs,
            IsHealthy = true,
            StatusMessage = "Connection successful",
            ErrorCode = null
        };
    }

    /// <summary>
    /// Creates a failed test connection result
    /// </summary>
    public static TestConnectionResult Failure(string provider, string authMethod, string errorCode, string message, long latencyMs, string? details = null)
    {
        return new TestConnectionResult
        {
            Provider = provider,
            AuthMethod = authMethod,
            Model = null,
            LatencyMs = latencyMs,
            IsHealthy = false,
            StatusMessage = message,
            ErrorCode = errorCode,
            ErrorDetails = details
        };
    }

    /// <summary>
    /// Converts ProviderHealthResult to TestConnectionResult for JSON serialization
    /// </summary>
    public static TestConnectionResult FromProviderHealthResult(ProviderHealthResult result)
    {
        return new TestConnectionResult
        {
            Provider = result.Provider ?? "qobuz",
            AuthMethod = result.AuthMethod ?? "oauth",
            Model = result.Model ?? "quality_detect",
            LatencyMs = (long)(result.ResponseTime?.TotalMilliseconds ?? 0),
            IsHealthy = result.IsHealthy,
            StatusMessage = result.StatusMessage,
            ErrorCode = result.ErrorCode
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Returns JSON string representation for DIAG-01 standardized structure
    /// </summary>
    public string ToJson()
    {
        // Ensure model has default value if null (DIAG-02 requirement)
        var resultToSerialize = new TestConnectionResult
        {
            Provider = Provider,
            AuthMethod = AuthMethod,
            Model = Model ?? "quality_detect",
            LatencyMs = LatencyMs,
            IsHealthy = IsHealthy,
            StatusMessage = StatusMessage,
            ErrorCode = ErrorCode,
            ErrorDetails = ErrorDetails
        };

        return JsonSerializer.Serialize(resultToSerialize, JsonOptions);
    }
}
