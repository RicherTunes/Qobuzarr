# Qobuz CLI request-timeout isolation — 2026-09-06

## Scope and integration status

Branch: `fix/cli-request-timeout-isolation-20260906`, based on Gitea main `d0d130fd1caae92c29375c1782556d99bb883424`.

Worktree: `C:/Users/Alexandre/.devspace/worktrees/qobuzarr-0f7e33a8`.

This is a review-only candidate. No main/master merge, release, Common implementation change, consumer repin or alteration of the original checkout is part of this work. The existing Common gitlink and sentinel remain `771bd3a18efd712cace51f949eac7823d29d247e`. Common PR #130 is separate work and was not promoted by this pass.

## Reproduced problem

`QobuzCLI/Services/Adapters/CliHttpClientAdapter.cs` assigned a per-call timeout to its caller-owned `HttpClient.Timeout`. The first call changed the policy for unrelated calls, and a later call with any explicit timeout threw `InvalidOperationException` because HttpClient configuration is frozen after use. Concurrent calls could consequently fail before reaching the transport; a short timeout also became the shared client's default.

The adapter now creates and disposes a request-local CancellationTokenSource and passes its token to the existing `GetStringAsync` overload. There is no new retry policy, HTTP client, connection pool, serialization layer or shared mutable timer state.

**Deliberate contract:** a per-call deadline can shorten, but cannot extend or disable, the owning client's configured timeout. Null or exact `Timeout.InfiniteTimeSpan` adds no per-call deadline. Validation retains the original accepted interval: strictly positive through `int.MaxValue` milliseconds inclusive, or the exact infinite sentinel. Other negative values, zero and larger durations fail before sending. Exception parameter names are not asserted as an unchanged legacy contract.

Newtonsoft.Json deserialization, JsonProperty mappings, JSON null, malformed-response exceptions, HTTP-status errors and one transport attempt remain unchanged. The owning HttpClient is never disposed or reconfigured. Synchronous JSON deserialization is not forcibly interrupted by the HTTP deadline; requests that ignore cancellation are not forcibly terminated.

## Shared reuse and cross-plugin survey

Tests use **Lidarr.Plugin.Common.TestKit.Testing.FakeTimeProvider**, already available through the test project's existing references. The public adapter constructor still takes only HttpClient; an internal TimeProvider constructor and CLI test-friend declaration permit explicit clock advancement without expanding the public API or adding packages.

A Gitea-main search of production timeout assignments across Common and all five plugins found the runtime mutation in this Qobuz adapter. The other Qobuz and Tidal assignments configure clients through construction/DI registration. Brain's matching `LlmErrorCode.Timeout =>` is a switch arm, not an assignment. No equivalent runtime mutation was found in the searched Apple/Amazon/Common roots. This is a bounded source survey, not a blanket claim about all possible client configuration patterns.

Qobuz's host adapter uses Lidarr's transport and a request-specific host timeout. It was not substituted with Common's System.Text.Json helper, and Common's retry executors were not inserted into this single-attempt Newtonsoft adapter. Reusing an inappropriate helper would change the transport/serializer contract rather than reduce meaningful duplication.

## Test evidence

All accepted receipts are local to this worktree under `artifacts/cli-timeout`.

- Initial test-only run: 17 failures / 7 passes. One incorrect JSON exception expectation and a custom body fixture that did not cooperate with the actual stream-read path were test defects, not production findings.
- Corrected, still-pre-fix run: **16 failures / 8 passes**, `red/cli-timeout-verified-red.trx`. No production change preceded this receipt.
- First implementation: 24 passing cases.
- Independent review found two timing-oracle weaknesses. The final isolation test starts and confirms the unaffected request first, then the expiring request; fake time advances only after both have entered the transport. The body-read test waits for copying to start before advancing time.
- Final focused set: **27 passed / 0 failed / 0 skipped**, `green/cli-timeout-deterministic-green.trx`.
- Ten repeated runs: **270 passed**, `repeated/repeat-1.trx` through `repeat-10.trx`.
- Two owning-client timeout tests intentionally exercise native .NET timers; they assert eventual cancellation, not an exact elapsed duration. A watchdog TimeoutException cannot satisfy their OperationCanceledException assertions. Pending synthetic work is released/cancelled and joined in finally blocks.

Fresh shared local validation completed through `scripts/verify-local.ps1 -SkipExtract`:

| Project | Passed | Failed | Existing skips |
| --- | ---: | ---: | ---: |
| Qobuzarr.Tests | 2,403 | 0 | 7 |
| Qobuzarr.Parity.Tests | 1,019 | 0 | 0 |
| Minimal.Tests | 194 | 0 | 0 |
| QobuzCLI.Tests | 181 | 0 | 0 |
| Total | 3,797 | 0 | 7 |

Production build, packaging and packaging closure passed; enforced warning budget was 768/1000 with no budget increase or suppression changes. Host assemblies were extracted from the configured .NET 8 Lidarr Docker image earlier in this worktree and reused. The package at this stage was built from a working-tree candidate, not represented as a release artifact with final-commit provenance.

Targeted whitespace verification of the production adapter and test file, plus `git diff --check`, passed. A prior combined validation/smoke invocation returned connector HTTP 502 without an accepted completion receipt; it is not counted as a pass or Docker runtime proof.

## Review boundary

The initial independent source review is `artifacts/cli-timeout/independent-review.md`. Verdict: **CHANGES_REQUIRED for two test weaknesses**, with no introduced production defect identified in that reviewed version. Those weaknesses were corrected and the full pipeline and repeated cases passed afterward.

The follow-up independent review invocation was blocked before execution. **There is no independent APPROVE verdict for the final TimeProvider-enabled version.** Parent inspection and local tests do not substitute for that missing review. The changelog update attempt was also blocked; CHANGELOG.md remains unchanged and release documentation is still an integration gate.

No live Qobuz credentials, real catalog calls, protected downloads, completed imports, five-plugin coexistence or soak acceptance is claimed. The new work has not been merged into main/master.

## Primary API reference

The .NET 8 HttpClient implementation establishes the accepted timeout range, frozen-after-use property behavior and per-request token flow through body consumption:

`https://raw.githubusercontent.com/dotnet/runtime/v8.0.0/src/libraries/System.Net.Http/src/System/Net/Http/HttpClient.cs`
