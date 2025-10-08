using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    public sealed class AlternateReleaseResolver : IAlternateReleaseResolver
    {
        private readonly IQobuzApiClient _apiClient;
        private readonly IStreamUrlProvider _streamUrlProvider;
        private readonly IQobuzLogger _logger;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (Lidarr.Plugin.Qobuzarr.Download.TrackUnavailableReason reason, System.DateTime expires)> _neg = new();
        private const int DefaultMaxCandidates = 6;
        private static readonly System.TimeSpan DefaultNegTtl = System.TimeSpan.FromHours(36);

        public AlternateReleaseResolver(IQobuzApiClient apiClient, IStreamUrlProvider streamUrlProvider, IQobuzLogger logger)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _streamUrlProvider = streamUrlProvider ?? throw new ArgumentNullException(nameof(streamUrlProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string?> ResolvePlayableTrackIdAsync(QobuzTrack original, CancellationToken cancellationToken)
            => await ResolvePlayableTrackIdAsync(original, Lidarr.Plugin.Qobuzarr.Download.TrackUnavailableReason.Unknown, DefaultMaxCandidates, DefaultNegTtl, cancellationToken);

        public async Task<string?> ResolvePlayableTrackIdAsync(
            QobuzTrack original,
            Lidarr.Plugin.Qobuzarr.Download.TrackUnavailableReason originalReason,
            int maxCandidates,
            System.TimeSpan negativeCacheTtl,
            CancellationToken cancellationToken)
        {
            if (original == null) return null;

            var candidates = new List<QobuzTrack>();

            // Negative cache keys (prefer ISRC if present)
            var negKey = BuildNegativeKey(original);
            if (TryGetNeg(negKey, out var cachedReason) &&
                (cachedReason == Lidarr.Plugin.Qobuzarr.Download.TrackUnavailableReason.PreviewOnly ||
                 cachedReason == Lidarr.Plugin.Qobuzarr.Download.TrackUnavailableReason.RegionalRestriction))
            {
                return null;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(original.ISRC))
                {
                    var byIsrc = await SearchTracksAsync(original.ISRC.Trim(), 10, cancellationToken).ConfigureAwait(false);
                    candidates.AddRange(byIsrc);
                }

                if (candidates.Count == 0)
                {
                    var title = original.Title ?? string.Empty;
                    var artist = original.Album?.Artist?.Name ?? original.Performer?.Name ?? string.Empty;
                    var query = $"{artist} {title}".Trim();
                    var byText = await SearchTracksAsync(query, 25, cancellationToken).ConfigureAwait(false);
                    candidates.AddRange(byText);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Alternate search failed for track {0}", original?.Id ?? "<null>");
            }

            if (candidates.Count == 0)
            {
                SetNeg(negKey, originalReason, negativeCacheTtl);
                return null;
            }

            foreach (var c in RankCandidates(original, candidates).Take(Math.Max(1, maxCandidates)))
            {
                // Try qualities typical for user expectations: 7, 6, 5
                var chain = new[] { 7, 6, 5 };
                var probe = await _streamUrlProvider.TryGetStreamUrlAsync(c.Id, chain, cancellationToken).ConfigureAwait(false);
                if (probe.Success && !string.IsNullOrWhiteSpace(probe.Url))
                {
                    _logger.Info("Resolved alternate for {0} -> {1} ({2})", original.Id, c.Id, c.GetFullTitle());
                    return c.Id;
                }
            }

            SetNeg(negKey, originalReason, negativeCacheTtl);
            return null;
        }

        private static IEnumerable<QobuzTrack> RankCandidates(QobuzTrack original, IEnumerable<QobuzTrack> pool)
        {
            string Norm(string s) => Regex.Replace(s ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();
            var title = Norm(original.Title);
            var artist = Norm(original.Album?.Artist?.Name ?? original.Performer?.Name ?? string.Empty);
            var dur = original.DurationSeconds;

            return pool
                .Select(p => new
                {
                    Item = p,
                    Score = (Norm(p.Title) == title ? 30 : 0)
                          + (Norm(p.Album?.Artist?.Name ?? p.Performer?.Name ?? string.Empty) == artist ? 30 : 0)
                          + (Math.Abs(p.DurationSeconds - dur) <= 2 ? 20 : 0)
                          + (p.Album?.Label?.Name == original.Album?.Label?.Name ? 8 : 0)
                          + (p.Album?.ReleaseDate > original.Album?.ReleaseDate ? 2 : 0)
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Item.Album?.ReleaseDate)
                .Select(x => x.Item);
        }

        private async Task<List<QobuzTrack>> SearchTracksAsync(string query, int limit, CancellationToken ct)
        {
            var parameters = new Dictionary<string, string> { ["query"] = query, ["limit"] = limit.ToString() };
            try
            {
                var resp = await _apiClient.GetAsync<QobuzSearchResponse>("/track/search", parameters).ConfigureAwait(false);
                return resp?.Tracks?.Items ?? new List<QobuzTrack>();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "track/search failed for query: {0}", query);
                return new List<QobuzTrack>();
            }
        }

        private static string BuildNegativeKey(QobuzTrack t)
        {
            if (!string.IsNullOrWhiteSpace(t.ISRC)) return $"isrc:{t.ISRC.Trim().ToUpperInvariant()}";
            string Norm(string s) => Regex.Replace(s ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();
            var title = Norm(t.Title);
            var artist = Norm(t.Album?.Artist?.Name ?? t.Performer?.Name ?? string.Empty);
            return $"txt:{artist}:{title}";
        }

        private bool TryGetNeg(string key, out Lidarr.Plugin.Qobuzarr.Download.TrackUnavailableReason reason)
        {
            reason = Lidarr.Plugin.Qobuzarr.Download.TrackUnavailableReason.Unknown;
            if (_neg.TryGetValue(key, out var tuple))
            {
                if (System.DateTime.UtcNow < tuple.expires)
                {
                    reason = tuple.reason;
                    return true;
                }
                _ = _neg.TryRemove(key, out _);
            }
            return false;
        }

        private void SetNeg(string key, Lidarr.Plugin.Qobuzarr.Download.TrackUnavailableReason reason, System.TimeSpan ttl)
        {
            var until = System.DateTime.UtcNow.Add(ttl <= System.TimeSpan.Zero ? DefaultNegTtl : ttl);
            _neg[key] = (reason, until);
        }
    }
}
