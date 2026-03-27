using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Integration.Bridge;

/// <summary>
/// Lightweight authentication helper for the bridge context.
/// POSTs to the Qobuz <c>/user/login</c> endpoint using <see cref="System.Net.Http.HttpClient"/>.
///
/// Limitations:
/// - App ID must be provided explicitly (via settings or <c>QOBUZ_APP_ID</c> environment variable).
///   Dynamic extraction from the Qobuz web player bundle.js is NOT supported in the bridge.
/// - App secret is similarly required from settings or <c>QOBUZ_APP_SECRET</c> environment variable.
///   If not provided, the session is created without an app secret and streaming URL endpoints
///   (which require request signing) will not work. Search/browse endpoints work without it.
/// </summary>
public static class BridgeQobuzAuthHelper
{
    private const string LoginEndpoint = "/user/login";
    private const string BaseUrl = "https://www.qobuz.com/api.json/0.2";

    /// <summary>
    /// Authenticates with the Qobuz API using email and plaintext password.
    /// The password is MD5-hashed before being sent (as required by the Qobuz API).
    /// </summary>
    /// <param name="httpClient">HTTP client to use for the login request.</param>
    /// <param name="email">Qobuz account email.</param>
    /// <param name="password">Qobuz account password (plaintext -- will be MD5-hashed).</param>
    /// <param name="appId">
    /// Qobuz app ID. Falls back to <c>QOBUZ_APP_ID</c> environment variable,
    /// then to <see cref="QobuzConstants.Api.DefaultAppId"/>.
    /// </param>
    /// <param name="appSecret">
    /// Qobuz app secret. Falls back to <c>QOBUZ_APP_SECRET</c> environment variable.
    /// Required for streaming URL endpoints but not for search/browse.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An authenticated <see cref="QobuzSession"/> with 24-hour TTL.</returns>
    /// <exception cref="InvalidOperationException">When authentication fails.</exception>
    public static async Task<QobuzSession> AuthenticateAsync(
        HttpClient httpClient,
        string email,
        string password,
        string? appId = null,
        string? appSecret = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required for Qobuz authentication.", nameof(email));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required for Qobuz authentication.", nameof(password));

        // Resolve app ID: parameter -> env var -> constant default
        var effectiveAppId = ResolveValue(appId,
            QobuzConstants.Authentication.AppIdEnvironmentVariable,
            QobuzConstants.Api.DefaultAppId);

        // Resolve app secret: parameter -> env var -> empty (search-only mode)
        var effectiveAppSecret = ResolveValue(appSecret,
            QobuzConstants.Authentication.AppSecretEnvironmentVariable,
            string.Empty);

        if (string.IsNullOrWhiteSpace(effectiveAppId))
        {
            throw new InvalidOperationException(
                "Qobuz App ID is required for authentication. " +
                "Provide it via settings (AppId), the QOBUZ_APP_ID environment variable, " +
                "or ensure QobuzConstants.Api.DefaultAppId is set.");
        }

        // MD5-hash the password (Qobuz API requirement)
        var md5Password = HashPasswordMD5(password);

        // Build login URL with query parameters (Qobuz login uses GET-style query params)
        var loginUrl = $"{BaseUrl}{LoginEndpoint}" +
            $"?app_id={Uri.EscapeDataString(effectiveAppId)}" +
            $"&email={Uri.EscapeDataString(email)}" +
            $"&password={Uri.EscapeDataString(md5Password)}";

        logger?.LogDebug("Bridge auth: attempting login for {Email}", email);

        using var request = new HttpRequestMessage(HttpMethod.Get, loginUrl);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            logger?.LogError("Bridge auth failed: HTTP {StatusCode}: {Body}",
                (int)response.StatusCode, body.Length > 300 ? body[..300] : body);
            throw new InvalidOperationException(
                $"Qobuz authentication failed with HTTP {(int)response.StatusCode}. " +
                "Check email, password, and app ID.");
        }

        var loginResponse = JsonConvert.DeserializeObject<QobuzLoginResponse>(body);
        if (loginResponse is null || !loginResponse.IsSuccess)
        {
            var message = loginResponse?.Message ?? "Unknown error";
            logger?.LogError("Bridge auth failed: {Message}", message);
            throw new InvalidOperationException($"Qobuz authentication failed: {message}");
        }

        if (string.IsNullOrWhiteSpace(loginResponse.UserAuthToken))
        {
            throw new InvalidOperationException("Qobuz authentication succeeded but no auth token was returned.");
        }

        var userId = loginResponse.User?.Id ?? "unknown";
        var subscription = loginResponse.User?.Subscription?.ToSubscription();

        logger?.LogInformation("Bridge auth: authenticated user {UserId}, subscription: {Tier}",
            userId, subscription?.GetTierDescription() ?? "unknown");

        // CreateSession requires a non-empty appSecret. When the secret is unavailable
        // (common in bridge context), use a placeholder. Streaming URL endpoints that
        // depend on request signing will still throw NotSupportedException.
        var sessionSecret = string.IsNullOrWhiteSpace(effectiveAppSecret)
            ? "bridge-no-secret"
            : effectiveAppSecret;

        return QobuzSession.CreateSession(
            userId,
            loginResponse.UserAuthToken,
            effectiveAppId,
            sessionSecret,
            subscription);
    }

    /// <summary>
    /// Hashes a plaintext password using MD5 (Qobuz API requirement).
    /// </summary>
    internal static string HashPasswordMD5(string password)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ResolveValue(string? explicitValue, string envVarName, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(explicitValue))
            return explicitValue;

        var envValue = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrWhiteSpace(envValue))
            return envValue;

        return fallback;
    }
}
