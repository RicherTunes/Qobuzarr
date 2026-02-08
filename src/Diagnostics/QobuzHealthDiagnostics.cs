// <copyright file="QobuzHealthDiagnostics.cs" company="RicherTunes">
// Copyright (c) RicherTunes. All rights reserved.
// </copyright>

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Abstractions.Diagnostics;

namespace Lidarr.Plugin.Qobuzarr.Diagnostics;

/// <summary>
/// Produces structured <see cref="DiagnosticHealthResult"/> for Qobuz provider health checks.
/// </summary>
internal static class QobuzHealthDiagnostics
{
    private const string ProviderName = "qobuz";
    private const string AuthMethodName = "app-secret";

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
        var sw = Stopwatch.StartNew();
        try
        {
            var (success, error) = await testAuth().ConfigureAwait(false);
            sw.Stop();

            return success
                ? DiagnosticHealthResult.Healthy(
                    responseTime: sw.Elapsed,
                    provider: ProviderName,
                    authMethod: AuthMethodName,
                    diagnosticType: "auth_validate",
                    capability: "lossless_download")
                : DiagnosticHealthResult.Unhealthy(
                    error ?? "Authentication failed",
                    responseTime: sw.Elapsed,
                    provider: ProviderName,
                    authMethod: AuthMethodName,
                    diagnosticType: "auth_validate",
                    capability: "lossless_download",
                    errorCode: "AUTH_FAILED");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return DiagnosticHealthResult.Unhealthy(
                ex.Message,
                responseTime: sw.Elapsed,
                provider: ProviderName,
                authMethod: AuthMethodName,
                diagnosticType: "auth_validate",
                errorCode: "CONNECTION_FAILED");
        }
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
                diagnosticType: "connectivity",
                capability: "search")
            : DiagnosticHealthResult.Unhealthy(
                "No search requests generated",
                responseTime: elapsed,
                provider: ProviderName,
                diagnosticType: "connectivity",
                capability: "search",
                errorCode: "CONNECTION_FAILED");
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
                diagnosticType: "connectivity",
                capability: "lossless_download")
            : DiagnosticHealthResult.Unhealthy(
                errorMessage ?? "Download path not accessible",
                provider: ProviderName,
                diagnosticType: "connectivity",
                capability: "lossless_download",
                errorCode: "CONNECTION_FAILED");
    }
}
