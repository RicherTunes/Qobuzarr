using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Common.Services.Bridge;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Integration.Bridge;

/// <summary>
/// Lightweight <see cref="IQobuzApiClient"/> implementation for the bridge context.
/// Uses <see cref="System.Net.Http.HttpClient"/> directly (no Lidarr host dependencies).
///
/// Limitations compared to the full <see cref="QobuzApiClient"/>:
/// - No request signing (not needed for search/browse endpoints)
/// - No response caching (bridge context is short-lived)
/// - No rate limiting (relies on caller to throttle)
/// - Streaming endpoints throw <see cref="NotSupportedException"/> (download client deferred)
/// - App ID/secret must be provided via settings or environment variables;
///   dynamic extraction from the Qobuz web player bundle.js is NOT supported.
/// </summary>
public sealed class BridgeQobuzApiClient : IQobuzApiClient, IDisposable
{
    private const string BaseUrl = "https://www.qobuz.com/api.json/0.2";

    private readonly HttpClient _httpClient;
    private readonly ILogger<BridgeQobuzApiClient> _logger;
    private readonly AuthFailureGate _authFailureGate;
    private readonly bool _ownsHttpClient;
    private QobuzSession? _session;

    /// <summary>
    /// Creates a new bridge API client with the given HTTP client.
    /// </summary>
    /// <param name="httpClient">HTTP client to use for requests. Caller retains ownership.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="authFailureGate">Gate that prevents hammering Qobuz when credentials are known bad.</param>
    public BridgeQobuzApiClient(HttpClient httpClient, ILogger<BridgeQobuzApiClient> logger, AuthFailureGate authFailureGate)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _authFailureGate = authFailureGate ?? throw new ArgumentNullException(nameof(authFailureGate));
        _ownsHttpClient = false;
    }

    /// <summary>
    /// Creates a new bridge API client with an internally managed HTTP client.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="authFailureGate">Gate that prevents hammering Qobuz when credentials are known bad.</param>
    public BridgeQobuzApiClient(ILogger<BridgeQobuzApiClient> logger, AuthFailureGate authFailureGate)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _authFailureGate = authFailureGate ?? throw new ArgumentNullException(nameof(authFailureGate));
        _httpClient = Lidarr.Plugin.Qobuzarr.Services.Http.SharedSystemHttpClient.Instance;
        _ownsHttpClient = false;
    }

    /// <inheritdoc />
    public AuthFailureGate? Gate => _authFailureGate;

    /// <inheritdoc />
    public bool HasValidSession()
    {
        return _session is not null && _session.IsValid();
    }

    /// <inheritdoc />
    public void SetSession(QobuzSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _logger.LogDebug("Bridge API client session set for user {UserId}", session.UserId);
    }

    /// <inheritdoc />
    public void ClearSession()
    {
        _session = null;
        _logger.LogDebug("Bridge API client session cleared");
    }

    /// <inheritdoc />
    public async Task<T> GetAsync<T>(string endpoint, Dictionary<string, string>? parameters = null) where T : class
    {
        // Fail fast if auth is known bad — prevents IP-ban cascade when credentials are revoked.
        _authFailureGate.EnsureCanProceed();

        var url = BuildUrl(endpoint, parameters);

        _logger.LogDebug("Bridge GET {Endpoint}", endpoint);

        using var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
        return await DeserializeResponseAsync<T>(response, endpoint).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<T> PostAsync<T>(string endpoint, object? data = null) where T : class
    {
        // Fail fast if auth is known bad — prevents IP-ban cascade when credentials are revoked.
        _authFailureGate.EnsureCanProceed();

        var url = BuildUrl(endpoint, parameters: null);

        _logger.LogDebug("Bridge POST {Endpoint}", endpoint);

        HttpContent? content = null;
        if (data is not null)
        {
            var json = JsonConvert.SerializeObject(data);
            content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);
        return await DeserializeResponseAsync<T>(response, endpoint).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<string> GetStreamingUrlAsync(string trackId, int formatId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Streaming URL retrieval is not supported in the bridge context. " +
            "This requires request signing with the app secret, which is deferred to the full host integration.");
    }

    /// <inheritdoc />
    public Task<QobuzStreamResponse> GetStreamingInfoAsync(string trackId, int formatId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Streaming info retrieval is not supported in the bridge context. " +
            "This requires request signing with the app secret, which is deferred to the full host integration.");
    }

    /// <summary>
    /// Disposes the internally managed HTTP client if this instance owns it.
    /// </summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    // ---- Private helpers ----

    private string BuildUrl(string endpoint, Dictionary<string, string>? parameters)
    {
        // Normalise endpoint: ensure leading slash
        var ep = string.IsNullOrWhiteSpace(endpoint)
            ? string.Empty
            : (endpoint.StartsWith("/") ? endpoint : "/" + endpoint);

        var queryParams = new Dictionary<string, string>(parameters ?? new Dictionary<string, string>());

        // Inject auth parameters when a session is active
        if (_session is not null)
        {
            if (!string.IsNullOrWhiteSpace(_session.AppId))
            {
                queryParams["app_id"] = _session.AppId;
            }

            if (!string.IsNullOrWhiteSpace(_session.AuthToken))
            {
                queryParams["user_auth_token"] = _session.AuthToken;
            }
        }

        if (queryParams.Count == 0)
        {
            return $"{BaseUrl}{ep}";
        }

        var qs = string.Join("&", queryParams.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return $"{BaseUrl}{ep}?{qs}";
    }

    private async Task<T> DeserializeResponseAsync<T>(HttpResponseMessage response, string endpoint) where T : class
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            _logger.LogError("Bridge API error: HTTP {StatusCode} for {Endpoint}: {Body}",
                statusCode, endpoint, body.Length > 500 ? body[..500] + "..." : body);

            // Signal the auth gate on 401/403 to prevent IP-ban cascades when credentials are bad.
            if (statusCode is 401 or 403)
            {
                await _authFailureGate.HandleFailureAsync(new AuthFailure
                {
                    ErrorCode = statusCode.ToString(),
                    Message = $"Qobuz returned HTTP {statusCode} for {endpoint}. Check your credentials.",
                    CanReauthenticate = true
                }).ConfigureAwait(false);
            }

            throw new Exceptions.QobuzApiException(
                $"HTTP {statusCode}: {(body.Length > 200 ? body[..200] : body)}",
                endpoint,
                response.StatusCode);
        }

        // Success path: reset the gate so a prior transient failure doesn't block indefinitely.
        await _authFailureGate.HandleSuccessAsync().ConfigureAwait(false);

        var result = JsonConvert.DeserializeObject<T>(body);
        if (result is null)
        {
            throw new Exceptions.QobuzApiException(
                $"Failed to deserialize response from {endpoint} to {typeof(T).Name}",
                endpoint);
        }

        return result;
    }
}
