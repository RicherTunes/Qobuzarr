# Phase 1 — Tracking RED Tests

This document lists tests that are intentionally skipped and remain RED until a specific
condition is met. These are not broken tests — they are forward-looking specifications that
document planned work.

Convention: tests are marked `[Fact(Skip = "TRACKING RED ...")]` (xUnit) or
`-Skip:'TRACKING RED ...'` (Pester). They must NEVER be deleted; only unskipped when the
condition is met.

---

## xUnit Tracking REDs

### QobuzAuthenticationServiceAsyncTests — Sync-over-async debt
**File:** `tests/Qobuzarr.Tests/Authentication/QobuzAuthenticationServiceAsyncTests.cs`

| Test | Condition to unskip |
|------|---------------------|
| `StoreSession_WithSlowTokenStore_ShouldNotBlockCallerThread` | Phase 1.1: `TryPersistSession` converted to `async Task` |
| `ClearSession_WithSlowTokenStore_ShouldNotBlockCallerThread` | Phase 1.1: `TryClearPersistedSession` converted to `async Task` |
| `GetCachedSession_WithSlowTokenStore_ShouldNotBlockCallerThread` | Phase 1.1: `TryLoadPersistedSession` converted to `async Task` |

**Related:** `docs/SYNC_ASYNC_DEBT.md`

---

## Pester Tracking REDs

### analyzer-flags.Tests.ps1 — CI analyzer disable flags
**File:** `scripts/tests/analyzer-flags.Tests.ps1`

**Phase 3.2 status:** Tests rewritten and un-skipped. Production build assertions now GREEN.
Remaining blanket-disable checks deferred to Phase 4 (pending ZaiCoding/AuthGate merge).

| Test | Status | Notes |
|------|--------|-------|
| `RunAnalyzersDuringBuild=false is not in production build steps` | GREEN (Phase 3.2) | Production builds now run analyzers |
| `EnableNETAnalyzers=false is not in production build steps` | GREEN (Phase 3.2) | Production builds now run analyzers |
| `Warning count gate step is present in CI workflow` | GREEN (Phase 3.2) | Gate reads qobuzarr-warning-baseline.txt |
| `Security-critical warnaserror flag is present for CA2012` | GREEN (Phase 3.2) | CA2012 promoted to error |
| `[DEFERRED Phase 4] TreatWarningsAsErrors=false is not present in any workflow` | DEFERRED | 29 warnings in in-flight files remain |
| `[DEFERRED Phase 4] RunAnalyzersDuringBuild=false is not in any workflow step` | DEFERRED | Test build steps intentionally keep flag for speed |

**Related:** `docs/ANALYZER_BASELINE.md`

---

## Previously RED — Now GREEN

| Test | Fixed in | How |
|------|----------|-----|
| `ExecuteRequestAsync_WithPreRequestHandler_ShouldUseHandlerMethods` (line 777) | Phase 1 | Moq Callback arity updated: `Callback<HttpRequest>` → `Callback<HttpRequest, CancellationToken>` |
| `ExecuteRequestAsync_WithParameterContainingWhitespace_ShouldTrimValue` (line 1039) | Phase 1 | Same Moq fix |
| `RunAnalyzersDuringBuild=false is not in production build steps` | Phase 3.2 | Flags removed from production CI; Directory.Build.targets suppressions added |
| `EnableNETAnalyzers=false is not in production build steps` | Phase 3.2 | Same |
| `Warning count gate step is present in CI workflow` | Phase 3.2 | Gate added to ci.yml |
| `Security-critical warnaserror flag is present for CA2012` | Phase 3.2 | -warnaserror:CA2012 added to production build |

**Related:** `docs/SKIPPED_TESTS.md`, `docs/ANALYZER_BASELINE.md`
