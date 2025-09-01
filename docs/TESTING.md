Testing Qobuzarr
=================

Quick commands
- Unit + fast integration (default):
  - dotnet test Qobuzarr.sln --settings tests/Default.runsettings -v minimal
- Full suite with live Lidarr:
  - Set env vars: LIDARR_URL, LIDARR_API_KEY
  - Optionally QOBUZ_APP_ID, QOBUZ_APP_SECRET, QOBUZ_EMAIL, QOBUZ_PASSWORD
  - Run: dotnet test Qobuzarr.sln -v minimal

Categories
- Category=LiveIntegration: Tests that talk to a live Lidarr instance; skipped by default via runsettings.
- Category=Slow: Stress/perf tests; skipped by default via runsettings (same filter excludes them if desired; adjust TestCaseFilter).

Skipping live tests automatically
- Live tests skip (do not fail) when required env vars are missing or endpoint is unreachable. Messages indicate what to set.

Guidelines
- Keep per-test timeouts short (≤10s) and avoid unbounded waits.
- Prefer collection fixtures for shared heavy setup; keep unit tests parallel.
- Scale stress loops with an env flag for nightly runs rather than PRs.

