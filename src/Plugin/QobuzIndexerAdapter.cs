using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Abstractions.Models;
using Lidarr.Plugin.Common.Services.Performance;

namespace Lidarr.Plugin.Qobuzarr.Plugin
{
    internal sealed class QobuzIndexerAdapter : IIndexer
    {
        private readonly QobuzSettings _settings;
        private HttpClient? _httpClient;
        private QobuzApiShim? _shim;
        private readonly IUniversalAdaptiveRateLimiter _limiter = new UniversalAdaptiveRateLimiter();

        public QobuzIndexerAdapter(QobuzSettings settings) => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        public async ValueTask<PluginValidationResult> InitializeAsync(CancellationToken cancellationToken = default)
        {
            var userId = _settings.UserId;
            var token = _settings.AuthToken;
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            {
                var errors = new List<string>();
                if (string.IsNullOrWhiteSpace(userId)) errors.Add("userId is required");
                if (string.IsNullOrWhiteSpace(token)) errors.Add("authToken is required");
                return PluginValidationResult.Failure(errors);
            }

            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 8,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
            _httpClient = new HttpClient(handler, disposeHandler: true);
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Qobuzarr/1.0 (+https://github.com/richertunes/qobuzarr)");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            _shim = new QobuzApiShim(_httpClient, _settings.AppId, userId, token, _settings.CountryCode, _settings.Locale);

            // Live probe: validate credentials quickly (host "Test" button typically calls Initialize)
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(3));
                _ = await _shim.SearchAlbumsAsync("a", 1).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return PluginValidationResult.Failure(new []{"Credential probe timed out (network?)"});
            }
            catch (HttpRequestException ex)
            {
                return PluginValidationResult.Failure(new []{$"Credential probe failed: {ex.Message}"});
            }
            catch (Exception ex)
            {
                return PluginValidationResult.Failure(new []{$"Credential probe error: {ex.Message}"});
            }

            return PluginValidationResult.Success();
        }

        public async ValueTask<IReadOnlyList<StreamingAlbum>> SearchAlbumsAsync(string query, CancellationToken cancellationToken = default)
        {
            if (_shim is null) throw new InvalidOperationException("Indexer not initialized");
            await _limiter.WaitIfNeededAsync("Qobuz", "album/search", cancellationToken);
            return await _shim.SearchAlbumsAsync(query);
        }

        public async ValueTask<IReadOnlyList<StreamingTrack>> SearchTracksAsync(string query, CancellationToken cancellationToken = default)
        {
            if (_shim is null) throw new InvalidOperationException("Indexer not initialized");
            await _limiter.WaitIfNeededAsync("Qobuz", "track/search", cancellationToken);
            return await _shim.SearchTracksAsync(query);
        }

        public async ValueTask<StreamingAlbum?> GetAlbumAsync(string albumId, CancellationToken cancellationToken = default)
        {
            if (_shim is null) throw new InvalidOperationException("Indexer not initialized");
            await _limiter.WaitIfNeededAsync("Qobuz", "album/get", cancellationToken);
            return await _shim.GetAlbumAsync(albumId);
        }

        public async IAsyncEnumerable<StreamingAlbum> SearchAlbumsStreamAsync(string query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var results = await SearchAlbumsAsync(query, cancellationToken);
            foreach (var a in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return a;
            }
        }

        public async IAsyncEnumerable<StreamingTrack> SearchTracksStreamAsync(string query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var results = await SearchTracksAsync(query, cancellationToken);
            foreach (var t in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return t;
            }
        }

        public ValueTask DisposeAsync()
        {
            _httpClient?.Dispose();
            _httpClient = null;
            _shim = null;
            return ValueTask.CompletedTask;
        }
    }
}
