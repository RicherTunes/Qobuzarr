using System;
using Lidarr.Plugin.Qobuzarr.API;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Localization;

namespace Lidarr.Plugin.Qobuzarr.Integration;

/// <summary>
/// Surfaces a latched Qobuz <see cref="Lidarr.Plugin.Common.Services.Bridge.AuthFailureGate"/>
/// (session expired / credentials rejected) in Lidarr's native Health banner
/// (Dashboard / System &gt; Status), via the host's <see cref="IProvideHealthCheck"/> contract.
///
/// <para>
/// <b>Why this exists:</b> when auth is latched bad, <c>QobuzIndexer</c>'s pre-flight short
/// circuit (see <c>QobuzIndexer.IsAuthShortCircuited</c> / the ecosystem-shared
/// <c>AuthGatedSearchHelper</c> pattern in Common) makes every subsequent search return an
/// EMPTY result without throwing. That is the correct behavior for the search itself (it stops
/// hammering a known-bad credential — the qobuzarr IP-ban amplification fix — see
/// <c>AuthFailureGate</c>'s doc comment) but it means Lidarr's own
/// <c>NzbDrone.Core.HealthCheck.Checks.IndexerStatusCheck</c> never fires: that check only
/// reacts to indexer PROVIDER-STATUS backoff (repeated thrown exceptions recorded by
/// <c>IIndexerStatusService</c>), and a gated search that returns an empty list instead of
/// throwing never trips it. The user just sees "no results" with no visible explanation.
/// This class closes that gap by reading the gate directly and reporting it through a
/// SEPARATE, purpose-built health check instead.
/// </para>
///
/// <para>
/// <b>Architecture note:</b> this class is intentionally NOT in
/// <c>Lidarr.Plugin.Common</c> — Common's shipped library references no host (Lidarr)
/// assembly at all, so a type deriving from the host's <see cref="HealthCheckBase"/> cannot
/// live there. The signals it reads (<see cref="Lidarr.Plugin.Common.Services.Bridge.AuthFailureGate"/>,
/// <see cref="Lidarr.Plugin.Common.Resilience.BackendHealthCache"/>) are shared in Common;
/// this thin (~50 LOC) per-plugin adapter is authored here, mirroring how
/// <see cref="QobuzarrInstalledPlugin"/> extends the host's <c>NzbDrone.Core.Plugins.Plugin</c>
/// directly in qobuz rather than in Common.
/// </para>
///
/// <para>
/// <b>Discovery — CONFIRMED live 2026-07-03:</b> Lidarr's DryIoC composition root DOES
/// auto-discover <see cref="IProvideHealthCheck"/> implementations from a plugin
/// <c>AssemblyLoadContext</c>, the same way it discovers <c>NzbDrone.Core.Plugins.Plugin</c>
/// subclasses (see <see cref="QobuzarrInstalledPlugin"/>'s doc comment) — via
/// <c>NzbDrone.Common.Composition.Extensions.AutoAddServices</c>' <c>RegisterMany</c> assembly
/// scan. Verified against a real <c>ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913</c> container
/// with this plugin's merged DLL mounted: this class was constructed via DI (both
/// <see cref="IQobuzApiClient"/> and <see cref="ILocalizationService"/> resolved with no error),
/// and Lidarr's own <c>HealthCheckService</c> invoked <c>Check()</c> on it — evidenced by
/// Trace-level log lines <c>"Check health -&gt; QobuzAuthHealthCheck"</c> /
/// <c>"Check health &lt;- QobuzAuthHealthCheck"</c> — both on the automatic startup health-check
/// pass AND on a manually-triggered <c>CheckHealthCommand</c>. See qobuzarr/CLAUDE.md for the
/// exact reproduction steps.
/// </para>
///
/// <para>
/// <b>Native warning path:</b> the live host-native path
/// <c>IQobuzApiClient</c> → <c>AdaptiveQobuzApiClient</c> → <c>QobuzApiClient</c>
/// now exposes a plugin-local <c>AuthFailureGate</c>. <c>QobuzApiClient</c> records
/// HTTP 401 responses plus auth-endpoint HTTP 403 responses into that gate and clears it on real origin success,
/// so this health check can report a Warning for the currently-live indexer wiring.
/// This is covered by <c>QobuzApiClientCovTests</c>; the remaining end-to-end proof is
/// a live Lidarr run with deliberately failing Qobuz credentials, then re-auth.
/// </para>
/// </summary>
public sealed class QobuzAuthHealthCheck : HealthCheckBase
{
    private readonly IQobuzApiClient _apiClient;

    public QobuzAuthHealthCheck(IQobuzApiClient apiClient, ILocalizationService localizationService)
        : base(localizationService)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    /// <inheritdoc />
    public override HealthCheck Check()
    {
        // A null gate means no AuthFailureGate is wired on this IQobuzApiClient
        // implementation (normally only a test fake or unsupported adapter). Treat that the
        // same way every other gate consumer in this codebase does
        // (QobuzIndexer.IsAuthShortCircuited / QobuzDownloadClient.IsAuthShortCircuited):
        // "a null gate is always considered healthy" — never throw, never warn on absence.
        var gate = _apiClient.Gate;
        if (gate is null || gate.IsHealthy)
        {
            return new HealthCheck(GetType());
        }

        // Deliberately a fixed, generic, actionable message — never interpolate the
        // upstream AuthFailure's raw Message/ErrorCode here. That upstream text can
        // originate from an HTTP response body and is not vetted for safety to render
        // verbatim in Lidarr's Health banner (a host-wide UI surface, not a per-plugin log).
        return new HealthCheck(
            GetType(),
            HealthCheckResult.Warning,
            "Qobuz authentication is failing — your session may have expired or your " +
            "credentials may have been rejected. Go to Settings > Indexers, open your Qobuz " +
            "indexer, and click Test to re-authenticate (re-enter your Qobuz email/password " +
            "if prompted).",
            "#qobuz-authentication-is-failing");
    }
}
