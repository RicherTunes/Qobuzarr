# Skipped Tests Register

**File:** `tests/Qobuzarr.Tests/QobuzApiClientCovTests.cs`
**Last updated:** 2026-05-23 (Phase 1)

## Summary

Originally 4 skipped tests. 2 were fixed in Phase 1 by updating Moq Callback arity.
2 remain skipped pending deeper refactoring.

---

## Fixed in Phase 1 (unskipped)

### Test 1 — Line 777 (was skipped)
| Field | Value |
|-------|-------|
| Test name | `ExecuteRequestAsync_WithPreRequestHandler_ShouldUseHandlerMethods` |
| Original skip reason | `"Moq callback signature mismatch after plugins-branch assembly swap"` |
| Root cause | `.Callback<HttpRequest>(req => ...)` used on `ExecuteAsync(HttpRequest, CancellationToken)` — Moq requires Callback arity to match method arity |
| Fix applied | Changed to `.Callback<HttpRequest, CancellationToken>((req, _) => capturedRequest = req)` |
| Owner | RicherTunes |
| Status | **FIXED — unskipped 2026-05-23** |

### Test 2 — Line 1039 (was skipped)
| Field | Value |
|-------|-------|
| Test name | `ExecuteRequestAsync_WithParameterContainingWhitespace_ShouldTrimValue` |
| Original skip reason | `"Moq callback signature mismatch after plugins-branch assembly swap"` |
| Root cause | Same Moq Callback arity issue as Test 1 |
| Fix applied | Same fix — `.Callback<HttpRequest, CancellationToken>((req, _) => capturedRequest = req)` |
| Owner | RicherTunes |
| Status | **FIXED — unskipped 2026-05-23** |

---

## Still Skipped

### Test 3 — Line 835
| Field | Value |
|-------|-------|
| Test name | `ExecuteRequestAsync_WithTokenManager_ShouldGetValidSession` |
| Skip reason | `"StreamingTokenManager constructor changed after plugins-branch assembly swap"` |
| Root cause | `StreamingTokenManager<TSession, TCredentials>` is a concrete class from `Lidarr.Plugin.Common`. Its constructor signature changed between Common versions. `Substitute.For<StreamingTokenManager<...>>()` (NSubstitute) requires a parameterless or proxy-compatible constructor. The current constructor requires injected dependencies that are not easily mocked. |
| Owner | RicherTunes |
| Removal condition | Rewrite test to use the real `StreamingTokenManager` with fake dependencies injected via constructor, OR extract an `IStreamingTokenManager<TSession, TCredentials>` interface that is mockable without concrete class constraints. Coordinate with `lidarr.plugin.common` changelog before updating. |

### Test 4 — Line 886
| Field | Value |
|-------|-------|
| Test name | `ExecuteRequestAsync_WithCachedResponse_ShouldReturnCachedValue` |
| Skip reason | `"IQobuzResponseCache is no longer the cache layer driven by GET requests. Phase 3 (commit b9c6344) migrated GETs to Lidarr.Plugin.Common's CachingHttpExecutor..."` |
| Root cause | The test mocks `IQobuzResponseCache.Get<T>()` and expects it to short-circuit the HTTP request path. After the Phase 3 migration, GETs go through `CachingHttpExecutor` (from Common) which has its own cache layer wired through `StreamingApiRequestBuilder`. Mocking the old `IQobuzResponseCache` no longer intercepts the request path; the test then hits `LidarrHttpClientInvoker.SendAsync` with no real HTTP client wired, causing NullReferenceException. |
| Owner | RicherTunes |
| Removal condition | Rewrite the test against the `CachingHttpExecutor` surface: mock `ICachingHttpExecutor` or use the TestKit's `FakeCachingHttpExecutor`, configure a cache hit, and assert the downstream HTTP call is skipped. This requires understanding the Common v2+ executor API — review `lidarr.plugin.common` TestKit before attempting. |
