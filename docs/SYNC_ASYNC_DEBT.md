# Sync-over-Async Debt Register

**Phase:** 1 — tracked for Phase 1.1 resolution
**Owner:** RicherTunes

## Summary

Three private methods in `QobuzAuthenticationService` call `FileTokenStore.{Load,Save,Clear}Async()`
via `.GetAwaiter().GetResult()` inside `lock {}` blocks, violating the sync-over-async anti-pattern.
This blocks a thread-pool thread for each disk persist/load/clear operation.

`FileTokenStore<TSession>` (from `Lidarr.Plugin.Common`) exposes only an async surface
(`LoadAsync`, `SaveAsync`, `ClearAsync`) — no sync overloads. Converting the three helper methods
to `async Task` would require making their callers async as well, which ripples into the
`IQobuzAuthenticationService` public interface and all registered callers. That ripple exceeds
Phase 1 scope.

## Affected Locations

| File | Line | Method | Async call blocked |
|------|------|--------|--------------------|
| `src/Authentication/QobuzAuthenticationService.cs` | ~502 | `TryLoadPersistedSession()` | `_persistentStore.LoadAsync()` |
| `src/Authentication/QobuzAuthenticationService.cs` | ~522 | `TryPersistSession(QobuzSession)` | `_persistentStore.SaveAsync(...)` |
| `src/Authentication/QobuzAuthenticationService.cs` | ~540 | `TryClearPersistedSession()` | `_persistentStore.ClearAsync()` |

## Why Not Fixed in Phase 1

Converting these methods requires making three public sync methods (`GetCachedSession`,
`StoreSession`, `ClearSession`) async, which touches `IQobuzAuthenticationService`,
`IStreamingAuthenticationService<TSession, TCredentials>`, and any registered callers — a ripple
of more than 3 callers. Per Phase 1 rules, the conversion is deferred.

## Risk

- Thread-pool thread blocked for the duration of a disk I/O operation (typically <50ms on SSD,
  potentially longer on slow/cold storage or Windows CI runners).
- The `lock {}` wrapper prevents concurrency issues but amplifies the blocking risk on high-load
  paths.
- Deadlock risk is low because the calling code does not hold any other locks, but is non-zero
  in edge cases where Lidarr's host I/O thread pool is exhausted.

## Resolution Plan (Phase 1.1)

1. Add `GetCachedSessionAsync`, `StoreSessionAsync`, `ClearSessionAsync` to `IQobuzAuthenticationService`.
2. Keep legacy sync methods as shims calling `GetAwaiter().GetResult()` on the new async versions
   (so existing callers keep compiling).
3. Migrate callers of the sync shims to the async API.
4. Remove the sync shims in Phase 2.

## Tracking Tests

`tests/Qobuzarr.Tests/Authentication/QobuzAuthenticationServiceAsyncTests.cs` contains three
`[Fact(Skip = "TRACKING RED ...")]` tests that will turn GREEN once Phase 1.1 is applied.
See `docs/PHASE1_TRACKING_REDS.md` for the full list of tracking-RED tests.
