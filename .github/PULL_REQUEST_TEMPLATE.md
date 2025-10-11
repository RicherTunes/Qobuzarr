## Summary

Short PR to stabilize CI test runs, fix the CS1737 compile error, and document the fast vs. full test profiles.

## Changes

- fix(build/tests): stop watch-loop churn by generating `plugin.json` only on Pack/Publish, not general Build
- fix(download): TrackDownloadService constructor order (required before optional) to resolve CS1737
- fix(client): revert `QobuzDownloadClient` to known-good snapshot to remove string literal/formatting corruption
- fix(services): align `IStreamUrlProvider` with `IReadOnlyList<int>` probe signature; import `System.Collections.Generic`
- fix(stream): resolve `QobuzSubscriptionTier` ambiguity (namespace-qualified); use cache `.Find(...)`
- fix(logging): route `TrackDownloadService` logging through `IQobuzLogger` via `NLogAdapter`
- fix(io): replace missing helpers with `_diskProvider`/`FileInfo` safe checks
- docs(tests): add tests/README for fast/full/live guidance; warn against `dotnet watch test`
- ci: add GitHub Actions workflow for fast suite on PRs + manual full/full-live

## Rationale

The primary cause of multi-hour test runs was a build-trigger loop (plugin.json rewritten every build) interacting with `dotnet watch` or multi-project test runs. Moving manifest generation to Pack/Publish eliminates the loop and makes local/CI runs deterministic.

## Validation

- local build: `dotnet build -c Release` (OK)
- fast tests: `./tests/run-tests.ps1 -Configuration Release` (~20s, all green)
- targeted: `dotnet test tests/Qobuzarr.Tests/Qobuzarr.Tests.csproj -c Release --settings tests/Default.runsettings --no-build` (OK)

## Reviewer Checklist

- [ ] Solution builds on your machine (Release)
- [ ] Fast suite green with Default.runsettings
- [ ] Optional: trigger Actions "Qobuzarr CI" with `suite=full` (no live)
- [ ] Optional: set secrets and run `suite=full-live` (requires LIDARR_URL/API_KEY)
- [ ] Sanity check TrackDownloadService logs still appear in host logs
- [ ] Confirm no functional regression in CLI smoke (basic command help works)

## Follow-ups (tracked separately)

- Resolve .NET 6 EOL warnings (migrate to .NET 8)
- Reduce package version conflicts in test projects
- Add a small smoke test for LoggerExtensions props path

