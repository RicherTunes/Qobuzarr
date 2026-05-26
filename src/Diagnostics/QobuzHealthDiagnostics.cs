// <copyright file="QobuzHealthDiagnostics.cs" company="RicherTunes">
// Copyright (c) RicherTunes. All rights reserved.
// </copyright>

using System;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Abstractions.Diagnostics;
using Lidarr.Plugin.Common.Diagnostics;
using Codes = Lidarr.Plugin.Common.Abstractions.Diagnostics.DiagnosticErrorCodes;

namespace Lidarr.Plugin.Qobuzarr.Diagnostics;

/// <summary>
/// Produces structured <see cref="DiagnosticHealthResult"/> for Qobuz provider health checks.
/// </summary>
internal static class QobuzHealthDiagnostics
{
    private const string ProviderName = "qobuz";
    private const string AuthMethodName = "app-secret";

    /// <summary>
    /// Delegates to <see cref="Codes"/> for ecosystem-wide parity.
    /// Local alias kept to minimize downstream churn.
    /// </summary>
    public static class ErrorCodes
    {
        public const string AuthFailed = Codes.AuthFailed;
        public const string ConnectionFailed = Codes.ConnectionFailed;
    }

    /// <summary>
    /// Well-known diagnostic types emitted by Qobuz diagnostics.
    /// </summary>
    public static class DiagnosticTypes
    {
        public const string AuthValidate = "auth_validate";
        public const string Connectivity = "connectivity";
    }

    /// <summary>
    /// Well-known capabilities reported by Qobuz diagnostics.
    /// </summary>
    public static class Capabilities
    {
        public const string LosslessDownload = "lossless_download";
        public const string Search = "search";
    }

    /// <summary>
    /// Performs an authentication health check against the Qobuz API.
    /// </summary>
    /// <param name="testAuth">A delegate that tests authentication and returns success/error tuple.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A <see cref="DiagnosticHealthResult"/> indicating auth health.</returns>
    public static async Task<DiagnosticHealthResult> CheckAuthAsync(
        Func<Task<(bool Success, string? Error)>> testAuth,
        CancellationToken cancellationToken = default)
    {
        // Qobuz's probe returns a (Success, Error) tuple rather than a bare bool.
        // We adapt it: the error string from the API is captured in a mutable slot
        // so it can be read back after the probe resolves and used as the
        // unhealthy message override — the tuple error is provider-supplied whereas
        // HealthCheckHelper's unhealthyMessage must be known before the probe runs.
        string? apiError = null;
        bool? probeSuccess = null;
        var result = await HealthCheckHelper.CheckAuthAsync(
            probe: async _ =>
            {
                var (success, error) = await testAuth().ConfigureAwait(false);
                apiError = error;
                probeSuccess = success;
                return success;
            },
            provider: ProviderName,
            authMethod: AuthMethodName,
            diagnosticType: DiagnosticTypes.AuthValidate,
            capability: Capabilities.LosslessDownload,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // If probe returned false and the API supplied a specific error message,
        // replace the generic "Authentication failed" status with it.
        if (probeSuccess == false && apiError is not null)
        {
            result = DiagnosticHealthResult.Unhealthy(
                apiError,
                responseTime: result.ResponseTime,
                provider: result.Provider,
                authMethod: result.AuthMethod,
                diagnosticType: result.DiagnosticType,
                capability: result.Capability,
                errorCode: result.ErrorCode);
        }

        return result;
    }

    /// <summary>
    /// Performs a connectivity check (search pipeline validation).
    /// </summary>
    /// <param name="hasRequests">Whether the search pipeline generated requests.</param>
    /// <param name="elapsed">Optional elapsed time for the connectivity check.</param>
    /// <returns>A <see cref="DiagnosticHealthResult"/> indicating connectivity health.</returns>
    public static DiagnosticHealthResult CheckConnectivity(
        bool hasRequests,
        TimeSpan? elapsed = null)
    {
        return hasRequests
            ? DiagnosticHealthResult.Healthy(
                responseTime: elapsed,
                provider: ProviderName,
                authMethod: AuthMethodName,
                diagnosticType: DiagnosticTypes.Connectivity,
                capability: Capabilities.Search)
            : DiagnosticHealthResult.Unhealthy(
                "No search requests generated",
                responseTime: elapsed,
                provider: ProviderName,
                diagnosticType: DiagnosticTypes.Connectivity,
                capability: Capabilities.Search,
                errorCode: ErrorCodes.ConnectionFailed);
    }

    /// <summary>
    /// Performs a download path accessibility check.
    /// </summary>
    /// <param name="pathValid">Whether the download path is valid and accessible.</param>
    /// <param name="errorMessage">Optional error message when path is invalid.</param>
    /// <returns>A <see cref="DiagnosticHealthResult"/> indicating download path health.</returns>
    public static DiagnosticHealthResult CheckDownloadPath(
        bool pathValid,
        string? errorMessage = null)
    {
        return pathValid
            ? DiagnosticHealthResult.Healthy(
                provider: ProviderName,
                diagnosticType: DiagnosticTypes.Connectivity,
                capability: Capabilities.LosslessDownload)
            : DiagnosticHealthResult.Unhealthy(
                errorMessage ?? "Download path not accessible",
                provider: ProviderName,
                diagnosticType: DiagnosticTypes.Connectivity,
                capability: Capabilities.LosslessDownload,
                errorCode: ErrorCodes.ConnectionFailed);
    }
}
