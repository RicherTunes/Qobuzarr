# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Qobuzarr is a high-performance Lidarr plugin for Qobuz streaming service with ML-powered optimization. Built on TrevTV's foundation, it provides both indexing and download capabilities for lossless audio content.

## Runtime & Docker Image Requirements (CRITICAL)

**Target framework**: `net8.0` -- this plugin MUST target .NET 8.

**Lidarr Docker image**: Use ONLY a `.NET 8` plugins-branch image for CI and local testing. The correct tag format is `pr-plugins-3.x.y.z` (net8). Example:
```
LIDARR_DOCKER_VERSION=pr-plugins-3.1.2.4913
```
- Image: `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913`

**NEVER use `pr-plugins-2.x` tags** (e.g., `pr-plugins-2.14.2.4786`, `pr-plugins-2.13.3.4692`) -- those are .NET 6 images. Loading a .NET 8 plugin into a .NET 6 host causes `System.Runtime` assembly load failures and Lidarr crash-loops (`Could not load file or assembly 'System.Runtime, Version=8.0.0.0'`).

When bumping the Docker image tag, search the entire repo for the old tag string and update all hits (workflows, scripts, docs).

## Plugin Registration (CRITICAL -- controls Lidarr System->Plugins UI visibility)

Lidarr has **two** distinct `IPlugin` interfaces, and conflating them silently breaks the System->Plugins UI:

| Interface | From | Used by |
|---|---|---|
| `NzbDrone.Core.Plugins.IPlugin` | `Lidarr.Core.dll` (host) | `/api/v1/system/plugins` -- UI listing, update checks, uninstall |
| `Lidarr.Plugin.Abstractions.IPlugin` | Common (internalized via ILRepack) | TestKit `PluginSandbox` -- never read by the live host |

`QobuzarrStreamingPlugin : StreamingPlugin<TModule,TSettings>` satisfies Common's contract. `QobuzIndexer`/`QobuzDownloadClient` are discovered through their Lidarr base classes. Neither satisfies the host's `IPlugin`, so without an additional class the plugin loads fully and works but doesn't appear in System->Plugins (and can't be auto-updated/uninstalled through the UI).

`src/Integration/QobuzarrInstalledPlugin.cs` extends the host's `NzbDrone.Core.Plugins.Plugin` to close the gap:

```csharp
public sealed class QobuzarrInstalledPlugin : NzbDrone.Core.Plugins.Plugin
{
    public override string Name => "Qobuzarr";
    public override string Owner => "RicherTunes";
    public override string GithubUrl => "https://github.com/RicherTunes/Qobuzarr";
}
```

DryIoc's `RegisterMany` (in `NzbDrone.Common.Composition.Extensions.AutoAddServices`) auto-discovers this class from the loaded plugin assembly. `InstalledVersion` is derived from `AssemblyInformationalVersionAttribute` via the base class -- do **not** hardcode it.

The class uses the fully-qualified base type `NzbDrone.Core.Plugins.Plugin` (no `using NzbDrone.Core.Plugins;`) because Qobuzarr's namespace `Lidarr.Plugin.Qobuzarr.Integration` ambiguously imports `Lidarr.Plugin.*` namespaces, making the unqualified `Plugin` lookup resolve to a namespace instead of the type.

## Release Asset Naming (CRITICAL -- controls Lidarr UI install)

**Every release asset filename MUST contain the literal substring `net8.0.zip`.**

Lidarr's plugin install (UI "Install" on a GitHub URL) is implemented in `src/NzbDrone.Core/Plugins/PluginService.cs` on the `plugins` branch. The asset filter is:

```csharp
release.Assets.Any(a => a.Name.Contains($"{Framework}.zip", StringComparison.OrdinalIgnoreCase))
// where Framework = $"net{_platformInfo.Version.Major}.0"  ->  "net8.0"
```

If no asset matches, `GetRemotePlugin` returns `null` and `InstallPluginService.Execute` silently no-ops -- **the UI spinner spins forever with no error**. This is the failure mode users see as "Install button does nothing."

Other constraints the install enforces:

- `draft: false`
- `target_commitish` in `{main, master}` (case-insensitive)
- Tag parses as a version (`v1.2.3`, `1.2.3`, or `1.2.3-prerelease`)
- Optional `Minimum Lidarr Version: X.Y.Z.W` in release body must be <= host version

Our release zip MUST be named with the `net8.0.zip` suffix (e.g., `Lidarr.Plugin.Qobuzarr-v<VERSION>.net8.0.zip`). Do not rename without keeping the `net8.0.zip` suffix.

**Verify a release is installable:**

```bash
gh api repos/RicherTunes/qobuzarr/releases --jq '.[0] | {tag_name, draft, target_commitish, assets: [.assets[].name]}'
```

At least one asset name must contain `net8.0.zip`.

**ALWAYS**:
- Use constants from `QobuzarrConstants.cs` rather than hardcoding.
- Expose to the user what brings value in `QobuzSettings.cs`; otherwise, it should be in `QobuzarrConstants.cs`.
- Be aware that this project shares a common library with http://github.com/RicherTunes/Lidarr.Plugin.Common so always think of ways to ensure generic code can be shared with this library so other projects may benefits. Think architecturally when doing so.

## Plugin DLL Naming Contract (CRITICAL)

**The main plugin DLL filename MUST match the glob `Lidarr.Plugin.*.dll`.** Lidarr's PluginLoader (`NzbDrone.Common/Extensions/PathExtensions.cs:334`) scans `/config/plugins/{owner}/{name}/` with `Directory.GetFiles(folder, "Lidarr.Plugin.*.dll")` — any other filename is silently ignored. No error, no warning, no log line; the plugin just never appears in `/api/v1/system/plugins`.

For Qobuzarr this is satisfied by `<AssemblyName>Lidarr.Plugin.Qobuzarr</AssemblyName>` in `Qobuzarr.csproj`. Don't drop that line "to clean up" — it's load-bearing.

## Download-client item id contract (CRITICAL)

`GetItems()` MUST report `DownloadClientItem`s whose `DownloadClientInfo.Id` equals **this client's `Definition.Id`** (and `.Name` its `Definition.Name`). Lidarr's `DownloadMonitoringService` → `CompletedDownloadService` → `ProvideImportItemService` → `DownloadClientProvider.Get(DownloadClientInfo.Id)` resolves the owning client by that id; a wrong/zero id makes its `.Single(...)` throw `Sequence contains no matching element`, so **every completed download wedges** at `Couldn't process tracked download` and never reaches import.

Pass `Definition.Id` (not `0`) to `QobuzDownloadItem.ToDownloadClientItem(...)` at all three `GetItems()` call sites (Tidalarr achieves the same via `DownloadClientItemClientInfo.FromDownloadClient(this)`). Pinned by `GetItems_ReturnsItemsCarryingRegisteredClientId_NotZero`.

**Regression history (DO NOT REPEAT):** shipped with `ToDownloadClientItem(0, Name)` — found 2026-05-31 by driving real downloads on the live instance (every qobuz download stuck). Unit tests covered `ToDownloadClientItem` in isolation (it honors whatever id it's given) but never asserted the `GetItems()` call site, and the Docker E2E never ran a real grab→download→import flow. Lesson: test the `GetItems()`→`DownloadClientInfo.Id` contract and a real import flow, not just the converter in isolation.

## Album-completion contract — incomplete ⇒ Failed (CRITICAL)

An album download is successful **only when every track lands on disk** (`successfulTracks == totalTracks`). An incomplete album MUST be reported to Lidarr as `DownloadItemStatus.Failed`, **never** `Completed`. Reason: Lidarr's `NoMissingOrUnmatchedTracksSpecification` permanently rejects an incomplete release with `[Permanent] "Has missing tracks"` — so a partial album reported `Completed` imports **zero** tracks, never retries, and never falls back; the good files are silently wasted. Reporting `Failed` instead triggers Lidarr's Failed-Download-Handling: blocklist the release + re-search across all indexers (fall back to another source/edition).

The gate lives in `DownloadPolicy.IsAlbumDownloadSuccessful` (`successfulTracks < totalTracks ⇒ false`; the `MinimumSuccessRate`/`TreatPreviewAsFailure` knobs can only ever gate a *complete* album, never rescue an incomplete one). The `else` branch in `DownloadAlbumTracksAsync` calls `downloadItem.SetFailed(...)` then throws `AlbumDownloadException`; `SetFailed` sets the item status, the `Tracker` retains failed items (30-min retention) and `GetItems()` reports them, so the `Failed` status reaches Lidarr end-to-end. Parity with Tidalarr (`failedTracks > 0 ⇒ Failed`). Canonical follow-up: a shared `AlbumCompletionPolicy` in Common + an `EcosystemParityTestBase` guard so every plugin's download client provably reports `Failed` for an incomplete album.

**Regression history (DO NOT REPEAT):** `IsAlbumDownloadSuccessful` returned `true` for a partial album (`29/30 = 96.7% ≥ 80% MinimumSuccessRate`). Found 2026-05-31 on the live instance: *Aphex Twin – Drukqs* (qobuz edition 30 tracks, track 21 unavailable) downloaded 29 FLACs, was reported `Completed`, and Lidarr rejected the import with "Has missing tracks" → `importFailed`, 0 imported, no fallback. (The same album also exposes a release-edition mismatch — Lidarr's matched release was 34 tracks vs qobuz's 30 — which makes "fail + fall back" doubly correct: qobuz can't satisfy that release at all.) The prior `IsAlbumDownloadSuccessful_ShouldReturnCorrectResult` cases *asserted the bug as correct* (80% ⇒ `true`); no test encoded the integration truth that Lidarr permanently rejects an incomplete album. Pinned now by `DownloadPolicyTests` (`...PartialAlbum_29Of30_ReportsFailure`, `...IncompleteAlbumIsNeverSuccessful_RegardlessOfThreshold`).

**Terminal release suppression (2026-07-01) — permanently-restricted tracks never blocklist:** the contract above (incomplete ⇒ Failed, never Completed) is UNCHANGED and still enforced for every case, including a permanently-restricted track — `TrackDownloadService.DownloadAlbumAsync` still always throws `AlbumDownloadException` on any deficit. What's new is a Common-backed suppression mechanism that stops the re-grab loop WITHOUT touching the completion decision, because Lidarr's blocklist cannot be relied on here: a track that Qobuz permanently refuses to serve for rights reasons (`TrackRestrictedByPurchaseCredentials`, `FormatRestrictedBySubscription`/subscription-tier) is a property of the exact track id, present identically in *every* quality-tier release Qobuz can offer for that catalog entry (`QobuzDownloadClient.Download`/`PerformDownloadAsync` always resolves streams via the download client's own `settings.PreferredQuality`, ignoring which quality-tier `ReleaseInfo.Guid` Lidarr actually grabbed — see `QobuzParser.ConvertAlbumToReleases`, which emits one `ReleaseInfo` per quality tier for the same album id). Live-confirmed 2026-06-29ish: 3 albums, 55+ re-grab cycles over 3 hours, ~145GB wasted EACH, and **no blocklist entry was ever created** despite 55+ reported failures — Lidarr's blocklist mechanism provably does not fire for this failure mode on the live instance (`blocklist_total=0` after 55+ failures), so a fix that depends on it (report `Completed` instead, or trust blocklist-driven fallback) does not actually terminate the loop.

Fix: Common owns the durable primitive: `TerminalReleaseSuppressionStore` (`ext/Lidarr.Plugin.Common/src/HostBridge/TerminalReleaseSuppressionStore.cs`) is a small, bounded, TTL'd, disk-persisted (`JsonFileStore<string, TerminalReleaseSuppressionRecord>`) map of `releaseId -> suppression record`. Qobuz keeps only a thin policy adapter (`src/Services/RestrictedReleaseSuppressionStore.cs`) that maps qobuz album ids onto the Common store and refuses to suppress non-terminal reasons. `QobuzDownloadClient.PerformDownloadAsync` catches `AlbumDownloadException` (before the generic `catch (Exception ex)`) and, when **any** deficit track's classifier-recorded reason satisfies `TrackUnavailableReason.IsPermanentlyUnavailable()` (`Restricted` / `SubscriptionRestriction` ONLY — deliberately NOT `RegionalRestriction`/geo, `PreviewOnly`, `NoQualityAvailable`, `NotStreamable`, `ApiError`, or `Unknown`, any of which may be transient, soft, or symptomatic of a recoverable edition mismatch), records the album id in the store. `QobuzParser.ConvertAlbumToReleases` checks the store (optional constructor dependency, defaults to a no-op so every pre-existing `new QobuzParser(settings, logger)` call site keeps compiling; `QobuzIndexer` wires the real `RestrictedReleaseSuppressionStore.Shared` instance) and skips emitting **any** `ReleaseInfo` for a suppressed album id, so the next search returns nothing for it — Lidarr has nothing to grab, and the loop stops without ever depending on blocklist. The download-client-facing report to Lidarr (`Failed`, with the same message as before) is completely unchanged; suppression is a pure search-side side effect.

Suppression keys on **album id only** (not the quality-tier GUID) — deliberate, given the `settings.PreferredQuality`-ignores-grabbed-release fact above: keying by GUID would only suppress one of the ~4 quality tiers per failure, so Lidarr could still cycle through the others (bounded to ~4 grabs instead of infinite, but not immediate). Keying by album id stops the loop after exactly one failed grab. TTL (30 days) and a 500-entry oldest-write cap bound the store and give a self-healing path if a track's restriction is later lifted server-side — see the store's own doc comments for the staleness caveat (the in-memory suppression snapshot is only refreshed on a new suppression write or a periodic in-process check, not continuously against the TTL).

**User recovery — interactive search bypasses suppression:** `QobuzParser.ConvertAlbumToReleases` only withholds a suppressed album when the current search is NOT interactive (`_currentSearchCriteria?.InteractiveSearch != true`, set via `SetSearchContext` at `QobuzIndexer.cs:169`). An interactive (user-initiated) search — the explicit "I want this now", e.g. after a subscription upgrade — offers the album immediately without waiting out the 30-day TTL; if it still can't be satisfied the next download re-suppresses it after one bounded cycle. AUTOMATIC/RSS searches (the actual re-grab-loop driver) keep respecting suppression, so this override cannot reopen the loop. Deliberately does NOT auto-clear the store (a sync-over-async smell in the parser, and unnecessary — a successful interactive grab imports the album and a failing one re-suppresses). Pinned by `QobuzParserSuppressionTests` (`...InteractiveSearch_IsOffered_UserOverride`, `...AutomaticSearch_StillWithheld`).

The classification seam feeding this: `QobuzApiClient.GetStreamingInfoAsync`'s restriction branch used to throw a raw, unclassified `InvalidOperationException` (e.g. literally `"Content restricted (TrackRestrictedByPurchaseCredentials)"`) that reached `TrackDownloadService.ResolveStreamAsync`'s generic catch as an opaque hard failure — indistinguishable from a network blip or a genuine edition mismatch, and never fed into `AlbumDownloadException.TrackResults[].Reason`. It now throws a classified `TrackUnavailableException` via `QobuzApiClient.ClassifyRestrictionReason` (keyed off `QobuzStreamRestriction.Code`, `ReasonCode`, and tightly-known reason text such as `TrackRestrictedByPurchaseCredentials`; the API's own `"TEMP"` reason code is explicitly mapped away from permanent, since it means transient). `ResolveStreamAsync` now records the reason for **every** classified `TrackUnavailableException` (previously only `PreviewOnly`/`NoQualityAvailable` were recorded), so `AlbumDownloadException.TrackResults` — and therefore the suppression check — has an accurate reason for every deficit track.

Pinned by Common `TerminalReleaseSuppressionStoreTests` (persistence/bounds/TTL/normalization), qobuz `RestrictedReleaseSuppressionStoreTests` (qobuz permanent-only adapter policy), `QobuzApiClientCovTests` (`GetStreamingInfoAsync_With*Restriction*`), `QobuzDownloadClientTests` (terminal `AlbumDownloadException` writes suppression while still reporting Failed), `TrackDownloadServiceOrchestratorTests.ResolveStreamAsync_RestrictedTrackUnavailableException_IsRecordedWithReason`, and `QobuzParserSuppressionTests` (the suppressed album id is absent from `ParseResponse`'s emitted releases; a non-suppressed album is unaffected).

**Edge case — an album that is BOTH restricted AND edition-mismatched:** suppression wins for future searches (no releases are offered for that album id at all, regardless of edition), because the restricted track can never be satisfied by *any* Qobuz edition of that catalog entry — falling back to a different Qobuz edition is exactly as hopeless as retrying the same one. This is a deliberate, narrower scope than a full per-release-GUID or per-edition suppression; documented as a known simplification.

## Reason-grouped failure messages (2026-07-03) — WHY tracks failed, not just how many

`AlbumDownloadException.GetIssuesSummary()` (groups `TrackResults` by classified `TrackUnavailableReason`) existed since the terminal-release-suppression work above but had zero callers — the `Message` Lidarr's queue showed for a failed download was always a generic count/summary, never the classified reasons. `ErrorMessageFormatter.FormatGroupedFailureReasons(AlbumDownloadException)` (`src/Utilities/ErrorMessageFormatter.cs`) is the first (and currently only) caller: it turns `GetIssuesSummary()` into a comma-separated, count-first, human-readable string ordered largest-group-first, e.g. `"2 restricted (subscription tier), 1 region-locked"` instead of a bare `"1 failed"`. Each `TrackUnavailableReason` maps to a short queue-friendly label (`GroupedFailureLabels`) distinct from the longer, emoji'd sentences `GetDetailedReason`/`FormatTrackError` already produce for single-track detail views. Returns `null` — not a fabricated reason — when no deficit track carries a classified `Reason` at all, so the caller can fall back to the exception's own generic message; when classified and unclassified deficits are mixed, an `"N unspecified"` bucket is appended so the total group count never silently drops tracks.

Wired into exactly one call site: `QobuzDownloadClient.PerformDownloadAsync`'s `catch (AlbumDownloadException ex)` block, replacing only the string passed to `downloadItem.SetFailed(...)`. **This is deliberately message-formatting only** — `TryRecordTerminalReleaseSuppressionAsync(downloadItem, ex)` (the terminal-suppression decision above) and the `Failed` status set by `SetFailed` are unchanged; only the human-readable text differs. Do not fold reason-formatting logic into the suppression/completion decision path — they are intentionally separate concerns reading the same `AlbumDownloadException.TrackResults`.

**Fixed alongside (pre-existing, unrelated to the completion contract):** `QobuzDownloadItem.GetStatusMessage()` already prefixes any `Failed`-status message with `"Download failed: "` before it reaches `DownloadClientItem.Message` (via `ToDownloadClientItem`). Both `PerformDownloadAsync` catch blocks (`AlbumDownloadException` and the generic `Exception` fallback) were *also* prepending that prefix themselves before calling `SetFailed`, so every failed-download message Lidarr's queue displayed actually read `"Download failed: Download failed: ..."`. Fixed by no longer prefixing at either call site (`SetFailed(groupedReasons ?? ex.Message)` / `SetFailed(ex.Message)`); `GetStatusMessage()` remains the single owner of that prefix. Pinned by an exact-match assertion (`item.Message.Should().Be("Download failed: 1 restricted (subscription tier)")`) in `QobuzDownloadClientTests`.

Pinned by `ErrorMessageFormatterCovTests` (`FormatGroupedFailureReasons_*`: single reason, multiple reasons ordered by count, no-classified-reason fallback to `null`, mixed classified/unclassified appending an unspecified bucket, all 8 `TrackUnavailableReason` label mappings, null-exception guard) and `QobuzDownloadClientTests` (`Download_WithPermanentTrackRestriction_ReportsGroupedReasonInMessage_NotBareCount`, `Download_WithMultiplePermanentRestrictionReasons_ReportsGroupedReasonsInMessage`, `Download_WithUnclassifiedDeficit_FallsBackToGenericMessage_NoException`) driving the enrichment end-to-end through `GetItems()` → `DownloadClientItem.Message`.

## Health-check pilot — `QobuzAuthHealthCheck` surfaces latched auth in Lidarr's Health banner (2026-07-03)

**Problem this closes:** when the auth gate is latched bad (session expired / credentials rejected), `QobuzIndexer`'s pre-flight short circuit (`IsAuthShortCircuited`, mirroring Common's `AuthGatedSearchHelper` pattern) makes every subsequent search return an EMPTY result without throwing. That's correct for the search itself (the qobuzarr IP-ban amplification fix — don't hammer a known-bad credential) but it means Lidarr's own `IndexerStatusCheck` never fires (it only reacts to thrown-exception provider backoff, not a gated empty result) — the user just sees "no results" with zero visible explanation.

**Fix:** `src/Integration/QobuzAuthHealthCheck.cs` — a `NzbDrone.Core.HealthCheck.HealthCheckBase` subclass (`IProvideHealthCheck` contract) that reads `IQobuzApiClient.Gate` (`AuthFailureGate?`) directly: `Warning` with a fixed, actionable, non-sensitive message when `gate is not null && !gate.IsHealthy`; `Ok` otherwise (including when the gate is absent/null — same "null gate is always healthy" convention as `QobuzIndexer.IsAuthShortCircuited`). TDD (`tests/Qobuzarr.Tests/Integration/QobuzAuthHealthCheckTests.cs`, 9 tests, RED→GREEN verified by temporarily inverting the Ok/Warning branch and confirming 4 tests failed with the expected mismatch before reverting): Warning+message-contains-actionable-guidance, Warning never leaks the raw upstream `AuthFailure.Message`/token, `Source` is the concrete type (required for `HealthCheckService`'s dedup-by-source), Ok on healthy/Unknown/absent gate, `CheckOnStartup`/`CheckOnSchedule` both true (latching can develop mid-uptime, not just at boot), null-apiClient ctor guard.

**Architecture:** intentionally NOT in Common — Common's shipped library references no host (Lidarr) assembly at all, so a `HealthCheckBase` subclass can't compile there. The signals (`AuthFailureGate`, `BackendHealthCache`) are shared in Common; this ~50 LOC adapter is per-plugin, mirroring how `QobuzarrInstalledPlugin` extends the host's `NzbDrone.Core.Plugins.Plugin` directly rather than via Common.

**Discovery — CONFIRMED live, not just unit-tested.** Whether Lidarr's DryIoC composition root auto-discovers an arbitrary `IProvideHealthCheck` from a *plugin* `AssemblyLoadContext` (as opposed to the handful of contracts the plugin loader is known to special-case) was an open, previously-unproven question. Verified 2026-07-03 against a real `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913` container with this plugin's ILRepack-merged DLL mounted at `/config/plugins/RicherTunes/Qobuzarr` (no config beyond the mount — `usePlugins=true` is Lidarr's default startup path, always attempted first): `/api/v1/system/plugins` listed `Qobuzarr`, `/api/v1/indexer/schema` listed `QobuzIndexer`, and — after setting `logLevel=trace` via `PUT /api/v1/config/host` and POSTing `{"name":"CheckHealth"}` to `/api/v1/command` — `/config/logs/lidarr.trace.txt` contained `Check health -> QobuzAuthHealthCheck` / `Check health <- QobuzAuthHealthCheck` on BOTH the automatic startup pass and the manual trigger, with no construction/DI errors (both `IQobuzApiClient` and `ILocalizationService` resolved cleanly). This proves discovery + DI construction + invocation all work from a plugin ALC.

Reproduction: `pwsh scripts/verify-local.ps1 -SkipExtract -SkipTests` (produces `bin/Lidarr.Plugin.Qobuzarr.dll`), mount `bin/` at `/config/plugins/RicherTunes/Qobuzarr` in a fresh `pr-plugins-3.1.2.4913` container (on Windows: use PowerShell for `docker run`/`docker exec`, NOT git-bash — MSYS path-mangles `-v` bind-mount arguments and silently produces a broken/empty mount with no error), hit `/initialize.json` for the API key, `PUT /api/v1/config/host` with `logLevel: "trace"`, `POST /api/v1/command {"name":"CheckHealth"}`, then `grep QobuzAuthHealthCheck /config/logs/lidarr.trace.txt` inside the container.

**Native-path gate wiring follow-up (2026-07-03):** the live host-native path (`IQobuzApiClient` → `AdaptiveQobuzApiClient` → `QobuzApiClient`) now exposes a plugin-local `AuthFailureGate` instead of returning `null`. `QobuzApiClient` records precise auth failures (HTTP 401, plus HTTP 403 only on authentication endpoints such as `/user/login`) into that gate and clears it only on real origin success (`CacheHitKind.Miss` / `NotModifiedFold` for cached GETs, any successful uncached POST) or a successful explicit indexer Test. Generic album/track 403s are deliberately not latched because Qobuz uses them for subscription/resource denials. It deliberately does **not** call `EnsureCanProceed()` inside the API client: `QobuzIndexer` / `QobuzDownloadClient` own the background-loop probe-slot short-circuit policy, and adding a second fail-fast inside the HTTP client would consume the recovery probe then block the actual probe request. TDD: `QobuzApiClientCovTests.NativeClient_Gate_IsPresent`, `ExecuteRequestAsync_With401Response_LatchesNativeGate_AndHealthCheckWarns`, `ExecuteRequestAsync_WithForbiddenResourceResponse_DoesNotLatchNativeGate`, `ExecuteRequestAsync_WithSensitiveErrorBody_RedactsBodyBeforeLogging`, `ExecuteRequestAsync_WithSuccessfulOriginResponse_ClearsNativeGate`, and `QobuzIndexerBespokeLoopTests.Test_WhenGateLatchedAndProbeExhausted_AttemptsAuthAndClearsGateOnSuccess` were run RED against the merged pilot/current patch and GREEN after the native wiring/hardening.

**Live validation the lead still needs to run** (the part no container probe without real credentials can prove): configure a real Qobuz indexer with credentials that will fail (or force-expire a valid session), confirm a `Warning` entry sourced from `QobuzAuthHealthCheck` appears via `GET /api/v1/health` and on the Dashboard/System > Status banner, then re-authenticate and confirm it clears.

## Submodule pin coordination (ext-common-sha.txt)

`ext/Lidarr.Plugin.Common` is a git submodule pinned to a specific Common SHA. Two things must always agree on that SHA:

1. **The submodule gitlink** — what `git ls-tree HEAD ext/Lidarr.Plugin.Common` reports (updated by `git add ext/Lidarr.Plugin.Common` after checking out a new Common commit).
2. **`ext-common-sha.txt`** — a plaintext sentinel (40 hex chars + LF) at the repo root. The Gitea CI job (`.gitea/workflows/ci.yml`) validates that the gitlink and this file agree; a mismatch fails the build.

**Why the sentinel exists**: the gitlink is invisible in a plain `git diff` (it shows only `-Subproject commit <sha>`), so the sentinel makes the pinned version greppable, reviewable in PRs, and assertable in tests (`VersionContractTests` cross-checks it against `plugin.json`'s `commonVersion`). Seeing `ext-common-sha.txt` dirtied in `git status` after a submodule bump is expected — commit it together with the gitlink.

**To bump the pin**: `pwsh ext/Lidarr.Plugin.Common/scripts/repin-common-submodule.sh --sha-from-submodule --stage` (or the `.ps1` variant) reads the submodule HEAD, rewrites `ext-common-sha.txt`, and stages both so they can't drift. Re-pin **manually** when Common's main advances — there is no scheduled auto-bump workflow on the Gitea-primary copy (GitHub Actions is out of credits).

## Common helpers in use

- `PluginConfigRoots.Resolve("Qobuzarr")` — `src/Authentication/SessionManager.cs:263`
- `FileTokenStore<QobuzSession>` + `StreamingTokenManager<QobuzSession, QobuzCredentials>` — `src/Authentication/SessionManager.cs:86-90`. Common's canonical token-store stack with at-rest encryption (DPAPI on Windows, Keychain on macOS, Secret Service / DataProtection fallback on Linux). Session envelope persisted to `PluginConfigRoots.Resolve("Qobuzarr")/session.json`. The audit-mismatch axis "Qobuz uses custom JSON I/O for sessions" was a stale finding — the wave-8B `SecureSessionManager` rip-out already migrated to Common; this CLAUDE entry pins the evidence.
- `BackendHealthCache` — `src/API/Http/QobuzHttpClient.cs:31` (fail-fast gate in `ExecuteAsync`), `src/API/Http/QobuzHttpClient.cs:104`
- `AuthFailureGate` — native path: `src/API/QobuzApiClient.cs` (`NativeGate` + precise 401/auth-endpoint-403/success recording); bridge path: `src/Integration/QobuzarrStreamingPlugin.cs:36` (singleton registration), `src/Integration/Bridge/BridgeQobuzApiClient.cs:35`
- `HttpExceptionClassifier` — `src/API/AdaptiveQobuzApiClient.cs:39`, `src/Indexers/QobuzIndexer.cs:331` (Test() catch), `src/Download/Clients/QobuzDownloadClient.cs:800` (Test() catch), and `src/Download/Clients/QobuzDownloadClient.cs:1034` (download failure classification). Wave-31 adoption: replaces generic "Test failed (ExceptionType)" with categorized actionable hints — Auth failures route to the "Authentication" field.
- `DownloadPathValidator` — `src/Download/Clients/QobuzDownloadClient.cs:760` (Test() pre-check). Wave-31 adoption: syntactic path validation (traversal, relative, invalid chars) before filesystem probe.
- `PluginLogContext` — `src/Indexers/QobuzIndexer.cs:180` (Search scope), `src/Indexers/QobuzIndexer.cs:291` (Test scope)
- `WarnOnce` — `src/Indexers/QobuzIndexer.cs:58` (wire-warn gate)
- `Scrub` — `src/API/Http/QobuzHttpClient.cs:289` (`Scrub.Url`), `src/API/Signing/QobuzRequestSigner.cs:64` (`Scrub.Secret`)
- `PrefixedReleaseGuidParser` — `src/Download/Clients/QobuzDownloadClient.cs:1173`, `src/Download/Services/AlbumIdExtractor.cs:55` (`ExtractAlbumIdFromGuid`; the new `qobuz:album:{id}` GUID grammar is also documented in a comment at `src/Indexers/QobuzParser.cs:256`)
- `BoundedConcurrentDictionary<TKey, TValue>` — available (Common v1.15.0+ exposes `ContainsKey`, `Values`, indexer setter, and `IEnumerable<KeyValuePair>` alongside the original v1.10.0 TryAdd/TryGetValue/AddOrUpdate/GetOrAdd surface). No qobuz call sites yet — `QobuzHttpClient._hostGates` (`src/API/Http/QobuzHttpClient.cs:40`) is domain-bounded by host count (1-2 hosts in practice) so adoption isn't required; revisit if user-controlled keys grow unboundedly.

See `ext/Lidarr.Plugin.Common/CHANGELOG.md` for the full catalog and [`docs/ECOSYSTEM_PARITY_MATRIX.md`](ext/Lidarr.Plugin.Common/docs/ECOSYSTEM_PARITY_MATRIX.md) for the cross-plugin parity scorecard (30+ axes × 4 plugins).

## Build Commands

**IMPORTANT**: Always use the analyzer suppression flags to avoid StyleCop errors from Lidarr source code.

```bash
# Build the plugin (main project) - RECOMMENDED
dotnet build --configuration Release -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false -p:TreatWarningsAsErrors=false

# Debug build with analyzer suppression
dotnet build --configuration Debug -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false -p:TreatWarningsAsErrors=false

# Restore dependencies and build (full setup)
dotnet restore && dotnet build --configuration Release -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false -p:TreatWarningsAsErrors=false

# Build specific projects
dotnet build Qobuzarr.csproj --configuration Release                    # Main plugin only
dotnet build QobuzCLI/QobuzCLI.csproj --configuration Release          # CLI tool only
dotnet build tests/Qobuzarr.Tests/Qobuzarr.Tests.csproj               # Unit tests
dotnet build tests/QobuzCLI.Tests/QobuzCLI.Tests.csproj               # CLI tests
```

- **NEVER** run git clean ... NEVER!!

**Quick Setup (Recommended)**:
```bash
# Windows PowerShell
.\setup.ps1

# Linux/macOS
chmod +x setup.sh && ./setup.sh

# With automatic plugin deployment to test Lidarr instance
.\setup.ps1 -EnableDeploy
./setup.sh --enable-deploy

# Custom deployment path
.\setup.ps1 -EnableDeploy -DeployPath "C:\Custom\Lidarr\Plugins\Qobuzarr"
./setup.sh --enable-deploy --deploy-path "/custom/lidarr/plugins/qobuzarr"
```

**Quick Build Scripts (New)**:
```bash
# Simple build and deploy
.\build.ps1 --Deploy              # PowerShell
./build.sh --deploy               # Bash

# Release build with deployment
.\build.ps1 Release --Deploy      # PowerShell
./build.sh Release --deploy       # Bash

# Clean build
.\build.ps1 --Clean --Restore     # PowerShell
./build.sh --clean --restore      # Bash

# Show all options
.\build.ps1 --Help                # PowerShell
./build.sh --help                 # Bash
```

## Plugin Deployment

The project includes automatic deployment to test Lidarr instances for faster development iteration.

### Automatic Deployment
```bash
# Enable deployment for Debug builds (will auto-copy to X:\lidarr-hotio-test2\plugins\RicherTunes\Qobuzarr)
dotnet build --configuration Debug -p:EnablePluginDeployment=true -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false

# Custom deployment path
dotnet build --configuration Debug -p:EnablePluginDeployment=true -p:LidarrPluginDeployPath="C:\Custom\Path" -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false

# Using environment variable
set LIDARR_PLUGIN_DEPLOY_PATH=C:\Custom\Path
dotnet build --configuration Debug -p:EnablePluginDeployment=true -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false
```

### Manual Deployment
```bash
# Clean previously deployed plugin
dotnet msbuild -target:CleanDeployedPlugin

# Deploy current build
xcopy /Y /E "bin\*" "X:\lidarr-hotio-test2\plugins\RicherTunes\Qobuzarr\"
```

### Deployment Configuration
- **Default Path**: `X:\lidarr-hotio-test2\plugins\RicherTunes\Qobuzarr`
- **Auto-Deploy**: Enabled for Debug builds when `EnablePluginDeployment=true`
- **Files Copied**: Main DLL, PDB symbols, plugin.json, ML patterns file
- **Environment Override**: Set `LIDARR_PLUGIN_DEPLOY_PATH` to customize default path

## Testing Commands

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

# Run integration tests only
dotnet test --filter Category=Integration

# Run specific test project
dotnet test tests/Qobuzarr.Tests/
dotnet test tests/QobuzCLI.Tests/
```

## CLI Usage (Development/Testing)

```bash
# Build and run CLI
cd QobuzCLI
dotnet build -c Release
dotnet run -- auth login

# Search functionality
dotnet run -- search "Miles Davis Kind of Blue"

# Download operations
dotnet run -- download album <album_id> --output ./Music
dotnet run -- download playlist <playlist_id> --output ./Playlists
```

## Architecture

### Plugin-First Design Philosophy
- **Core principle**: All functionality MUST be implemented in the plugin (`src/`) first
- **CLI role**: The `QobuzCLI/` project is strictly a test wrapper and adapter layer
- **No duplication**: CLI never reimplements plugin functionality, only adapts interfaces
- **Dependency flow**: CLI -> Plugin (never the reverse)

### Project Structure
```
src/                           # Main plugin (Lidarr.Plugin.Qobuzarr.dll)
+-- API/                       # Qobuz API clients and interfaces
+-- Authentication/            # Authentication services and session management
+-- Download/                  # Download client implementation and orchestration
|   +-- Clients/               # QobuzDownloadClient (implements IDownloadClient)
|   +-- Services/              # Download-related services
|   +-- Orchestration/         # Download workflow coordination
+-- Indexers/                  # QobuzIndexer (implements IIndexer) with ML optimization
+-- Models/                    # Data models for Qobuz API and Lidarr integration
+-- Services/                  # Core business logic services
+-- Integration/               # Lidarr integration adapters

QobuzCLI/                      # Test CLI wrapper
+-- Commands/                  # CLI command implementations
+-- Services/Adapters/         # Adapters between CLI and plugin interfaces
+-- Program.cs                 # Entry point

ext/Lidarr/_output/            # Pre-built Lidarr assemblies (ONLY supported method)
```

### Key Components
- **QobuzIndexer** (`src/Indexers/QobuzIndexer.cs`): Implements `HttpIndexerBase<QobuzIndexerSettings>` for Lidarr search integration

  **Intentional bespoke search loop (F6):** `QobuzIndexer` keeps its own capped search loop (around line 207) rather than delegating to Common's `SearchPlanExecutor` accumulate-all executor. This is deliberate: Qobuz must cap over-specific queries so results don't degrade when the catalog returns noise; the per-query cap + artist-only-fallback preservation behaviour is the defining contract. The contract is enforced by `QobuzCappedSearchChainComplianceTests` (subclasses Common's `CappedSearchChainComplianceTestBase`). Do not replace the bespoke loop with `SearchPlanExecutor` without also adopting Common's capped-chain executor variant and keeping the compliance tests green.

- **QobuzDownloadClient** (`src/Download/Clients/QobuzDownloadClient.cs`): Implements `DownloadClientBase<QobuzDownloadSettings>` for Lidarr download integration
- **Plugin Metadata** (`src/Constants/QobuzarrConstants.cs`): Centralized plugin information and constants
- **Authentication Services** (`src/Authentication/`): Handle Qobuz session management
- **ML Optimization** (`src/Indexers/CompiledMLQueryOptimizer.cs`): Pre-compiled ML models for query optimization

## Plugins Branch Protocol Pattern (CORRECT)

The Lidarr plugins branch uses `string Protocol` (not `DownloadProtocol` enum). All working plugins follow this pattern:

```csharp
// src/Download/QobuzarrDownloadProtocol.cs
namespace NzbDrone.Core.Indexers
{
    public class QobuzarrDownloadProtocol : IDownloadProtocol { }
}

// src/Indexers/QobuzIndexer.cs
public class QobuzIndexer : HttpIndexerBase<QobuzIndexerSettings>
{
    public override string Protocol => nameof(QobuzarrDownloadProtocol);
}

// src/Download/Clients/QobuzDownloadClient.cs
public class QobuzDownloadClient : DownloadClientBase<QobuzDownloadSettings>
{
    public override string Protocol => nameof(QobuzarrDownloadProtocol);
}
```

**Key rules**:
- The plugins branch base classes declare `public abstract string Protocol { get; }` (NOT `DownloadProtocol` enum)
- `IDownloadProtocol` interface exists only in the plugins branch
- Always compile against **plugins branch assemblies** -- release assemblies have incompatible signatures
- See `docs/archived/PROTOCOL_INVESTIGATION_HISTORY.md` for the full 2025-08 investigation trail

### Assembly Sources

**Docker extraction** (preferred for CI):
```bash
docker pull ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913
docker create --name temp ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913
docker cp temp:/app/bin/. ext/Lidarr-plugins/_output/
docker rm temp
```

**Plugins branch source** (alternative):
```bash
git clone --depth 1 --branch plugins https://github.com/Lidarr/Lidarr.git ext/Lidarr-source
```

**Assembly version override** (required when building from source): Build scripts automatically patch `AssemblyVersion` in `ext/Lidarr-source/src/Directory.Build.props` to match the target runtime version, preventing `ReflectionTypeLoadException`.

### Build Issues

**StyleCop Analyzer Errors**: The Lidarr source code may trigger StyleCop analyzer errors. These are suppressed in the project configuration, but if you encounter them:
- Always use the build flags: `-p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false -p:TreatWarningsAsErrors=false`
- The `Directory.Build.props` and `ext/.editorconfig` files are configured to suppress these issues
- If issues persist, delete and re-clone the Lidarr source using the setup scripts

## CI/CD (Gitea-primary)

**Workflow**: `.gitea/workflows/ci.yml`. GitHub Actions is out of credits, so CI runs on the Gitea instance; any `.github/workflows/*` are a non-running mirror retained for reference only.

**Jobs**:
- **lint** — fast ecosystem gates (date-parsing, sync-over-async, ecosystem-parity), pwsh-only.
- **verify** — full build + ILRepack package + packaging-closure + deterministic tests (incl. `Qobuzarr.Parity.Tests`) via `pwsh scripts/verify-local.ps1`.

**Approach**: `verify-local.ps1` extracts plugins-branch host assemblies from `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913` (avoids NuGet feed / Central Package Management issues), then builds, packages, runs the packaging-closure check, and the deterministic test projects. The Common submodule is re-pinned **manually** when Common's main advances — there is no scheduled auto-bump on the Gitea-primary copy (see the submodule-pin section above).

## Development Practices

### Security Requirements
- **No hardcoded credentials**: All credentials must use environment variables or secure storage
- **No stub/placeholder data**: Production code paths must connect to real APIs
- **Fail-fast principle**: If real APIs unavailable, fail immediately with clear errors
- **Input validation**: All user inputs must be validated and sanitized

### Code Organization
- **Plugin-first**: Always implement features in `src/` before creating CLI wrappers
- **Interface segregation**: Use dependency injection with clear interface boundaries
- **Error handling**: Use specific exception types (`QobuzApiException`, `QobuzAuthenticationException`)
- **Async patterns**: All I/O operations must be async/await

### File Naming Conventions
- **Plugin files**: Follow Lidarr namespace conventions (`Lidarr.Plugin.Qobuzarr.*`)
- **Interfaces**: Prefix with `I` (e.g., `IQobuzApiClient`)
- **Services**: Suffix with `Service` (e.g., `QobuzAuthenticationService`)
- **Models**: Simple names matching Qobuz API structure (e.g., `QobuzAlbum`, `QobuzTrack`)

## Configuration

### Environment Variables (for development/testing)
```bash
QOBUZ_APP_ID="your_app_id"
QOBUZ_APP_SECRET="your_app_secret"
QOBUZ_EMAIL="your@email.com"        # Optional
QOBUZ_PASSWORD="your_password"      # Optional
QOBUZ_QUALITY="27"                  # 5=MP3-320, 6=FLAC-CD, 7=FLAC-Hi-Res, 27=FLAC-Max
```

### Plugin Configuration
- Configured through Lidarr UI: Settings -> Indexers -> Add -> Qobuzarr
- Settings handled by `QobuzIndexerSettings` and `QobuzDownloadSettings`
- Authentication managed by `QobuzAuthenticationService`

## ML Features

The project includes pre-compiled ML optimization:
- **Query optimization**: `src/Indexers/CompiledMLQueryOptimizer.cs`
- **Pattern learning**: `src/Indexers/ml-baseline-patterns.json`
- **No runtime ML.NET**: Uses pre-trained models to avoid ML.NET dependency in production

## Common Issues

### Assembly Reference Conflicts (MOST COMMON ISSUE)
**Symptoms**:
```
error CS0246: The type or namespace name 'DownloadProtocol' could not be found
error CS1715: type must be 'DownloadProtocol' to match overridden member
```

**Root Cause**: Compiling against release assemblies instead of plugins branch assemblies, or having dual assembly sources.

**Solution**:
1. Use only ONE assembly source (plugins branch assemblies from Docker extraction or source build)
2. Never have both `ext/Lidarr-source/_output` and `ext/Lidarr/_output` -- pick one
3. Protocol must be `string Protocol => nameof(QobuzarrDownloadProtocol)` (plugins branch pattern)

### Other Build Issues
- If "Skipping project... because it was not found": Run `./download-lidarr-assemblies.sh`
- Missing Lidarr assemblies: Ensure `ext/Lidarr/_output/` exists with pre-built DLLs
- Analyzer warnings: Use `Directory.Build.props` settings to suppress non-critical warnings

### Plugin Development
- Plugin discovery: Lidarr automatically discovers classes implementing `IIndexer` and `IDownloadClient`
- DI registration: Services implementing interfaces are auto-registered by Lidarr's DryIoC container
- Testing: Use CLI project to test plugin functionality without full Lidarr installation

## Version Management

- Version is managed in single source of truth: `Qobuzarr.csproj`
- `plugin.json` is auto-generated from `plugin.json.template` during build
- Assembly version format must match Lidarr requirements (x.x.x.x format)

## Development Quality Tools

### Pre-commit Hooks

The project includes automated pre-commit hooks that run essential checks before each commit:

**Setup**: Pre-commit hooks are automatically available in `.git/hooks/pre-commit` (executable)

**Checks performed**:
- **Build artifact prevention**: Blocks `.dll`, `.pdb`, `.exe`, `bin/`, `obj/` files from being committed
- **Secret detection**: Scans for hardcoded credentials in code files
- **Code quality validation**: Basic syntax checks, TODO/FIXME detection, Console.WriteLine warnings
- **JSON validation**: Validates `plugin.json` if modified
- **Package management**: Alerts about Directory.Packages.props changes

**Usage**:
```bash
# Hooks run automatically on commit
git commit -m "your changes"

# Manual testing
.git/hooks/pre-commit

# Skip hooks (emergency only)
git commit --no-verify -m "emergency fix"
```

### Centralized Package Management

The project uses [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) for consistent dependency versions across all projects.

**Configuration**: `Directory.Packages.props` manages all package versions centrally

**Migration Tools**:
```bash
# Preview changes (safe)
./migrate-to-central-packages.sh --dry-run
.\migrate-to-central-packages.ps1 -DryRun

# Apply migration
./migrate-to-central-packages.sh
.\migrate-to-central-packages.ps1
```

## Troubleshooting

### ReflectionTypeLoadException -- Version Mismatch

**Symptoms**: Lidarr fails to start with "Could not load file or assembly 'Lidarr.Core, Version=10.0.0.xxxxx'"

**Root Cause**: Plugin compiled against development Lidarr versions but runtime expects release versions.

**Solution**: Build scripts automatically override `AssemblyVersion` in `Directory.Build.props` to match the target Lidarr runtime version. If building manually, patch the version before compiling.

### Plugin Not Loading

**Check**: Verify plugin files in Lidarr plugins directory:
- `Lidarr.Plugin.Qobuzarr.dll` - Main assembly
- `plugin.json` - Plugin manifest
- Both should have recent timestamps matching your last build

**Restart**: Always restart Lidarr after plugin deployment

## Local Verification

Run the merge-critical verification pipeline locally before pushing CI-sensitive changes:

```bash
pwsh scripts/verify-local.ps1                    # Full pipeline (extract + build + package + closure + E2E)
pwsh scripts/verify-local.ps1 -SkipExtract       # Fast rerun (reuse cached host assemblies)
pwsh scripts/verify-local.ps1 -SkipTests         # Build + packaging closure only
pwsh scripts/verify-local.ps1 -NoRestore         # Skip dotnet restore (fast iteration)
pwsh scripts/verify-local.ps1 -IncludeSmoke      # + Docker smoke test (mounts plugin in Lidarr)
```

**Prerequisites**: PowerShell 7+ (`pwsh`), .NET 8 SDK, Docker (for extract/smoke stages).

The script delegates to `ext/Lidarr.Plugin.Common/scripts/local-ci.ps1`, which orchestrates the same gates as CI: host assembly extraction with .NET 8 + FV 9.5.4 guardrails, plugin packaging via `New-PluginPackage`, and packaging closure validation via `generate-expected-contents.ps1 -Check`.

## Docker E2E Harness (wave 22b)

A runnable end-to-end harness boots a real Lidarr container, mounts the merged
Qobuzarr plugin DLL, waits for the API, and asserts plugin liveness against the
Lidarr REST API. Built on common's lifted `LidarrContainerFixture` (wave 22a) —
this plugin contributes only ~80 lines of per-plugin glue.

### Run locally

```powershell
# One-shot (builds plugin via verify-local.ps1, then runs the smoke matrix)
pwsh scripts/e2e.ps1

# Re-run without rebuilding (merged DLL already at bin/)
pwsh scripts/e2e.ps1 -SkipBuild

# Run a single test
pwsh scripts/e2e.ps1 -Filter 'FullyQualifiedName~Indexer_Test'
```

If Docker Desktop isn't running the tests **skip gracefully**.

### Pinned image and per-plugin constants

| Knob | Value |
|------|-------|
| Image | `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913` |
| Container name | `qobuzarr-e2e` |
| Host port | `8692` (avoids tidalarr `8690` / applemusicarr `8691`) |
| Mount path | `/config/plugins/RicherTunes/Qobuzarr` |
| Mounted DLL | `bin/Lidarr.Plugin.Qobuzarr.dll` (ILRepacked merged output) |
| Schema match substring | `"Qobuz"` |

### ILRepack interaction (critical)

The harness mounts the **merged** plugin DLL (where `Lidarr.Plugin.Common`
types are internalized) — that's what Lidarr loads in production. The
test project itself keeps `PluginPackagingDisable=true;OutputPath=bin-tests/`
on its `<ProjectReference>` (Phase 6 fix) so test compilation resolves against
the standalone Common assembly. The fixture's `FindPluginDll` deliberately
points at `bin/`, **not** `bin-tests/`. Don't conflate the two: tests need the
un-merged DLL for type identity; Lidarr needs the merged DLL.

Per-plugin glue lives in `tests/Qobuzarr.Tests/Runtime/`:

- `QobuzarrLidarrContainerFixture.cs` — subclasses common's fixture and
  populates `LidarrContainerOptions`; defines `[CollectionDefinition]`.
- `DockerE2ETests.cs` — four `[SkippableFact]`s delegating to the
  smoke-assertion extension methods on the fixture
  (`AssertPluginAppearsInIndexerSchemaAsync`,
  `AssertPluginAppearsInDownloadClientSchemaAsync`,
  `AssertIndexerTestReturnsSensibleFailureAsync`,
  `AssertDownloadClientTestReturnsSensibleFailureAsync`).

## Flaky Tests Policy

**Flaky tests are priority tech debt that must be paid immediately.** A test that passes sometimes and fails sometimes erodes trust in the entire test suite. When a flaky test is discovered:

1. **Fix it before starting new feature work** -- flaky tests block reliable CI
2. **Document the root cause** in a commit message so the pattern is not repeated
3. **Never skip or disable** a flaky test without a tracking issue

### Known Flaky Tests (Qobuzarr)

_None outstanding. The previous entries below were resolved either by my changes
or by ambient refactors during the May 2026 wave-17 / wave-18 work. Verified green
across 3+ consecutive `dotnet test --no-build` iterations on 2026-05-25._

### Resolved Flakes (May 2026 verification)

| Test | Root Cause | Resolution |
|------|-----------|-----------|
| 6x `LidarrDecisionEngineTests.*` | NSubstitute mocking non-virtual members | Resolved during refactor — currently passes 3/3 stress iterations |
| `AlbumEditionLidarrIntegrationTests.ParsedAlbumInfo_WithUnicodeVersions_*` | Unicode/diacritical stripping logic | Resolved during refactor — currently passes 3/3 stress iterations |
| `AlbumEditionLidarrIntegrationTests.AlbumRepository_FindByTitle_WithDifferentEditions_*` | GUID collision for editions | Resolved during refactor — currently passes 3/3 stress iterations |
| `PluginPackagingTests.PluginFluentValidationReference_ShouldMatch_HostVersion` | FluentValidation version 9 vs 11 mismatch | Resolved during refactor — currently passes |
| `MLOptimizationRegressionTests.ConcurrentPredictions_MaintainPerformance` | Latency threshold 20ms too tight for CI | Threshold is now `TARGET_PREDICTION_TIME_MS * 10` = 100ms; currently passes |
| `QobuzAuthenticationServiceCovTests.ClearAuthenticationCache_ClearsStoredSession` <br> `QobuzAuthenticationServiceTests.GetCachedSession_WithExpiredSession_ShouldReturnNull` | Two test classes shared `_persistentStore`'s default file path | Fixed May 2026 in `ef73d9f` by adding `internal` constructor overload with `sessionFilePath` parameter; each test instance generates a `Path.GetTempPath()/qobuzarr-test-{Guid}.session.json` and deletes it in `Dispose()`. `[Collection("QobuzAuthentication")]` retained. 5 stress iterations green post-fix. |
| `QobuzAppSecretLogScrubTests.ExtractAppSecret_OnSuccess_NeverLogsRaw{Seed,InfoOrExtras}` (2 tests) | **Global NLog state clobbered by parallel sibling tests.** `MLPerformanceMetricsTests`, `MLPerformanceMetricsLogGateTests`, and `DownloadProgressTrackerCovTests` set `LogManager.Configuration` in their ctors (and nulled it / called `LogManager.Shutdown()` in teardown), wiping the shared `testMemory` capture target the scrub tests read via `NLogTestLogger.GetLoggedMessages()`. Deterministic in the full suite; passed in isolation. Common's `NLogTestLogger` was already correct (the #544 `CreateNullLogger` isolation fix is in the pin) — the bug was purely local. | Fixed May 2026 (`integrate/common-consolidation`): all three siblings now use `NLogTestLogger.CreateNullLogger()` (Common's isolated-`LogFactory` null logger) and never touch global NLog state. None of them assert on captured logs, so a no-op logger suffices. Verified green across 2 consecutive full-suite runs (0 failed / 2448 passed). **Lesson: a test must never assign `LogManager.Configuration` or call `LogManager.Shutdown()` — use `NLogTestLogger.CreateNullLogger()` for a throwaway logger, or an isolated `LogFactory` when it needs to assert on its own captured output.** |

### Resolved Bugs (Wave 1 + Wave 2, Mar 26 2026)

| Bug | Root Cause | Resolution |
|-----|-----------|------------|
| `QobuzAlbum.GetGenre()` null-ref | `GenresList` can be null after JSON deserialization; `.FirstOrDefault()` threw NRE | Wave 1: guarded with `GenresList?.FirstOrDefault() ?? "Unknown"` (PR #243) |
| `QobuzAlbum.GetAllArtistNames()` null-ref | `Artists` collection or individual elements null from JSON | Wave 2: null guards on collection and elements (PR #244) |
| `QobuzTrack.GetFullTitle()` null-ref | `Title` null; `.Contains()` threw NRE | Wave 2: fallback to "Unknown Track" (PR #244) |
| `QobuzSearchResultContainer.HasMoreResults` / `GetNextOffset()` null-ref | `Items` null from JSON deserialization | Wave 2: guarded with `?.Count ?? 0` (PR #244) |

## Ecosystem consolidation & parity discipline

This plugin is one of five copy-paste-adjacent Lidarr streaming plugins (amazonmusicarr, applemusicarr,
tidalarr, qobuzarr, brainarr) sharing `Lidarr.Plugin.Common`. **Every bug here is likely a bug class** present
in the sibling plugins too. Before shipping a fix to any shared-surface concern (auth/retry, rate-limit /
Retry-After, catalog→ReleaseInfo field mapping, path/SSRF guards, token store, pagination, date/number
parsing): **sweep the other plugins + Common for the same pattern, fix every instance, and push shared logic
down into Common** (plugins adopt it via a thin DI subclass; the out-of-tree DRM seam stays plugin-owned +
public — never consolidated, because ILRepack internalizes Common in the merged DLL). Common changes go via an
**isolated-worktree PR from origin/main**, must re-pin `ext-common-sha.txt`, and must keep this plugin's parity
tests green (the parity matrix is a contract). Verify the actual mechanism before assuming a class sweeps —
raw-JSON alias-probing plugins and typed-DTO plugins are vulnerable to different bug classes.

**Canonical rules:** `ext/Lidarr.Plugin.Common/AGENTS.md` → "Ecosystem Consolidation & Parity Discipline".
