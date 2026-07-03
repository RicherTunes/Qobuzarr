# Tech Debt Tracker

Purpose: keep a single, accurate view of what we changed, why, and what’s next. This replaces scattered, outdated notes that referenced folders no longer present.

## Completed (Mar 26, 2026)

- **Wave 4 root-doc cleanup & ILRepack documentation.**
  - Moved 15 stale investigation/analysis markdown files from repo root to `docs/archived/`: `CLI-FRAMEWORK-DEMO.md`, `CONSOLIDATION-STRATEGY.md`, `DISABLED-SERVICES-ANALYSIS.md`, `DRYIOC-RESOLUTION.md`, `FRAMEWORK-SUCCESS-PROOF.md`, `LIBRARY-MIGRATION-ANALYSIS.md`, `MIGRATION-PRIORITY-PLAN.md`, `QUEUE-TESTS-SUMMARY.md`, `SAFE-TECH-DEBT-PLAN.md`, `SERVICE-CONSOLIDATION-PLAN.md`, `SHARED-COMPONENT-IMPLEMENTATION.md`, `TECH-DEBT-ANALYSIS-2025-08-28.md`, `TECH-DEBT-ANALYSIS.md`, `TECH-DEBT-INVENTORY.md`, `TECH-DEBT-RESOLUTION-PLAN.md`.
  - Documented ILRepack disabled status in `Directory.Build.props` with rationale (double-merge risk) and re-enable criteria.

- **Wave 2 null-safety audit** across model classes.
  - `QobuzAlbum.GetAllArtistNames()`: guarded `Artists` collection against null (JSON deserialization can override default initializer); also added null-check on individual artist elements.
  - `QobuzTrack.GetFullTitle()`: guarded `Title` against null with fallback to "Unknown Track", preventing `NullReferenceException` in `.Contains()` call.
  - `QobuzSearchResultContainer<T>.HasMoreResults` / `GetNextOffset()`: guarded `Items` against null with `?.Count ?? 0`.
  - Wave 1 `QobuzAlbum.GetGenre()` null-ref fix (already in main) confirmed resolved.

## Completed (Oct 14, 2025)

- Default to Lidarr release branch locally for broader dev compatibility; CI still flips to plugins branch when available.
  - Qobuzarr.csproj: `UsePluginsBranch` default set to `false` and a build log line added to make selection explicit.
- Re‑enabled analyzers for `src/` and `tests/` only; `ext/**` remains suppressed.
  - Added `src/Directory.Build.props` and `tests/Directory.Build.props` with `TreatWarningsAsErrors=true` (docs warnings excluded).
- Prevented string similarity ambiguity with the common library.
  - Added global alias `CommonStringSimilarity` to point at `Lidarr.Plugin.Common.Utilities.StringSimilarity`.

## In Progress / Next

- Rename `src/Abstractions/IQobuzHttpClient` to `IPluginHttpClient` to remove naming collision with `src/API/Http/IQobuzHttpClient`.
- Extract Qobuz-specific helpers from `src/Utilities/StringSimilarity.cs` into explicit types (`TitleNormalizer`, `TrackSimilarityScorer`) and route generic similarity to the common lib.
- Gradually remove nullable suppressions by fixing call sites (drop `CS860x/CS862x` from root `Directory.Build.props` `NoWarn`).
- CLAUDE.md contains contradictory protocol guidance (both `DownloadProtocol.Unknown` for release assemblies and `string Protocol => nameof(...)` for plugins branch). The file should be pruned to remove obsolete 2025-08 investigation notes and keep only the current plugins-branch pattern.
- **ILRepack**: currently disabled (`ILRepackEnabled=false` in `Directory.Build.props`). Re-enable only in a dedicated PR after a full Gitea/local `verify-local.ps1` pass succeeds with the flag set to `true`.

## Notes

- External code under `ext/**` continues to have analyzers disabled to avoid churn on upstream sources.
- ILRepack remains disabled; rationale and re-enable criteria are documented inline in `Directory.Build.props`.
