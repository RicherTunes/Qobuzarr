# Tech Debt Tracker

Purpose: keep a single, accurate view of what we changed, why, and what’s next. This replaces scattered, outdated notes that referenced folders no longer present.

## Completed (Oct 14, 2025)

- Default to Lidarr release branch locally for broader dev compatibility; CI still flips to plugins branch when available.
  - Qobuzarr.csproj: `UsePluginsBranch` default set to `false` and a build log line added to make selection explicit.
- Re‑enabled analyzers for `src/` and `tests/` only; `ext/**` remains suppressed.
  - Added `src/Directory.Build.props` and `tests/Directory.Build.props` with `TreatWarningsAsErrors=true` (docs warnings excluded).
- Prevented string similarity ambiguity with the common library.
  - Added global alias `CommonStringSimilarity` to point at `Lidarr.Plugin.Common.Utilities.StringSimilarity`.

## In Progress / Next

- Rename `src/Abstractions/IQobuzHttpClient` to `IPluginHttpClient` to remove naming collision with `src/API/Http/IQobuzHttpClient`.
- Extract Qobuz‑specific helpers from `src/Utilities/StringSimilarity.cs` into explicit types (`TitleNormalizer`, `TrackSimilarityScorer`) and route generic similarity to the common lib.
- Gradually remove nullable suppressions by fixing call sites (drop `CS860x/CS862x/CS1998` from `NoWarn`).

## Notes

- External code under `ext/**` continues to have analyzers disabled to avoid churn on upstream sources.
- ILRepack remains disabled until we have a documented need and a green, reproducible pipeline.

