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

| Test | Condition to unskip |
|------|---------------------|
| `[TRACKING RED] RunAnalyzersDuringBuild=false is not present in any workflow` | Phase 1.3: Warning count < 50 and flags removed from CI |
| `[TRACKING RED] EnableNETAnalyzers=false is not present in any workflow` | Phase 1.3: Same |
| `[TRACKING RED] TreatWarningsAsErrors=false is not present in any workflow` | Phase 1.3: All remaining warnings suppressed via NoWarn |

**Related:** `docs/ANALYZER_BASELINE.md`

---

## Previously RED — Now GREEN

| Test | Fixed in | How |
|------|----------|-----|
| `ExecuteRequestAsync_WithPreRequestHandler_ShouldUseHandlerMethods` (line 777) | Phase 1 | Moq Callback arity updated: `Callback<HttpRequest>` → `Callback<HttpRequest, CancellationToken>` |
| `ExecuteRequestAsync_WithParameterContainingWhitespace_ShouldTrimValue` (line 1039) | Phase 1 | Same Moq fix |

**Related:** `docs/SKIPPED_TESTS.md`
