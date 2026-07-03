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
/// <b>STILL UNPROVEN / KNOWN GAP — the Warning path is practically inert in production
/// today.</b> The probe above only exercises the Ok branch: the container had no Qobuz
/// credentials configured, and the ONE indexer type Lidarr's live schema actually resolves is
/// the classic host-native <c>QobuzIndexer</c>, which is wired to <c>IQobuzApiClient</c> →
/// <c>AdaptiveQobuzApiClient</c> → <c>QobuzApiClient</c> — and <c>QobuzApiClient.Gate</c> is
/// HARDCODED to always return <c>null</c> (see its doc comment: "the gate lives on
/// <c>BridgeQobuzApiClient</c>", which is a separate, not-currently-live code path — its
/// <c>CreateDownloadClientAsync</c> returns <c>null</c> and only its indexer adapter is even
/// registered). Consequence: on the currently-live wiring, <see cref="Check"/> will return
/// <c>Ok</c> UNCONDITIONALLY, no matter how badly auth is failing — this health check cannot
/// yet produce a Warning in production. Closing this gap (wiring a populated
/// <c>AuthFailureGate</c> onto the native <c>QobuzApiClient</c>/<c>QobuzIndexer</c> path, or an
/// equivalent always-live auth signal) is a REQUIRED follow-up before this pilot delivers its
/// intended user-facing value; it was deliberately left out of this PILOT's scope (a
/// behavior-changing edit to the live auth/HTTP pipeline, not a ~50 LOC adapter). See
/// qobuzarr/CLAUDE.md for the full analysis.
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
        // implementation (e.g. the Lidarr-native QobuzApiClient path, which currently
        // always returns null — see IQobuzApiClient.Gate's doc comment). Treat that the
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
