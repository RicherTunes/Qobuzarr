# Analyzer Warning Baseline — Phase 1

**Captured:** 2026-05-23
**Build command:** `dotnet build Qobuzarr.csproj --configuration Release -p:RunAnalyzersDuringBuild=true -p:EnableNETAnalyzers=true -p:TreatWarningsAsErrors=false`
**Total warnings:** 863

## Warning Counts by Category

| Rule | Count | Description | Fix effort |
|------|-------|-------------|------------|
| CA1305 | 546 | `Logger.*` / `ToString()` calls need `IFormatProvider` | Medium — NLog's Logger API doesn't accept IFormatProvider; suppress with comment |
| CA1822 | 322 | Members can be marked `static` | Low — mechanical fix, but large surface area |
| CA1860 | 178 | Prefer `Count == 0` over `.Any()` | Low — mechanical |
| SYSLIB1045 | 106 | Use `[GeneratedRegex]` for compile-time regex | Medium — requires partial classes |
| CA1805 | 98 | Do not initialize fields to default value | Low — mechanical |
| CA1304 | 92 | String comparison lacks `StringComparison` | Medium |
| CA1311 | 92 | `ToUpper`/`ToLower` without culture | Low — mechanical |
| CA1848 | 34 | Use `LoggerMessage` delegates | High — large refactor |
| CA1862 | 34 | Use `StringComparison` overload | Low — mechanical |
| CA1847 | 26 | Use `char` literal instead of string literal | Low — mechanical |
| CA1310 | 24 | Specify `StringComparison` for correctness | Medium |
| CA1510 | 22 | Use `ArgumentNullException.ThrowIfNull` | Low — mechanical |
| CA1859 | 16 | Use concrete type for improved perf | Medium — interface dependency changes |
| CA1854 | 14 | Prefer `TryGetValue` over `ContainsKey` + indexer | Low — mechanical |
| CA1816 | 14 | `GC.SuppressFinalize` missing | Low — mechanical |
| CA1866 | 12 | Use `char`-overload of `string.StartsWith` | Low — mechanical |
| CS0618 | 12 | Obsolete `Lidarr.Plugin.Qobuzarr.Utilities.HashingUtility` calls | Low — Task F migration |
| CA1845 | 10 | Use `AsSpan` or `AsMemory` | Medium |
| CA1716 | 10 | Reserved keyword identifiers | High — public API change |
| CA1865 | 8 | Use `char`-overload of `string.EndsWith` | Low — mechanical |
| CA1513 | 8 | Use `ObjectDisposedException.ThrowIf` | Low — mechanical |
| CA1869 | 6 | Cache `JsonSerializerOptions` | Medium |
| CA1001 | 6 | Disposable field not disposed | High — correctness risk |
| CA1725 | 6 | Parameter names must match base | Low |
| CA1861 | 4 | Constant in array argument | Low |
| CA1850 | 4 | Use `HashData` instead of `ComputeHash` | Low — mechanical |
| CA1416 | 4 | Platform-specific API | Low — already guarded |
| CA5351 | 4 | MD5 is broken — cryptographic use | Informational — Qobuz API requires MD5 |
| CA1835 | 4 | Use `Memory<T>` overload for streams | Medium |
| RS0025 | 2 | Missing `#nullable enable` | Low |
| CA1825 | 2 | Avoid zero-length array allocations | Low |
| CA2012 | 2 | `ValueTask` misuse | High — correctness risk |
| CA1068 | 2 | `CancellationToken` parameter order | Low |
| MSB3836 | 2 | NuGet version constraint | Low |
| CA1720 | 2 | Identifier contains type name | Low |

## Decision

**863 warnings > 50 threshold.** The analyzer-disable CI flags (`-p:RunAnalyzersDuringBuild=false
-p:EnableNETAnalyzers=false -p:TreatWarningsAsErrors=false`) are NOT removed in Phase 1.

## Phased Plan to Clear Warnings

### Phase 1.1 — Quick wins (≤ 50 warnings remaining target)
Focus on mechanical, zero-risk rules that can be bulk-fixed:

1. **CA1822** (322): Mark instance methods `static` where they access no instance data.
2. **CA1860** (178): Replace `.Any()` with `.Count == 0` guards.
3. **CA1805** (98): Remove explicit initialization to default values.
4. **CA1311** (92): Add `InvariantCulture` to `ToUpper`/`ToLower`.
5. **CA1510** (22): Use `ArgumentNullException.ThrowIfNull`.
6. **CA1816** (14): Add `GC.SuppressFinalize` in `Dispose()`.
7. **CA1866/CA1865** (20): Use `char` overloads.
8. **CS0618** (12): Complete HashingUtility caller migration (Task F).

Estimated reduction: ~758 warnings → ~105 remaining.

### Phase 1.2 — String/culture rules
9. **CA1305/CA1304/CA1310** (662 combined): Suppress `CA1305` for NLog `Logger` calls
   (NLog API does not accept `IFormatProvider`) with targeted `#pragma warning disable CA1305`
   comments in the logging sites, or add to `NoWarn` for logging namespaces. Fix remaining
   `CA1304`/`CA1310` mechanically.

Estimated reduction after Phase 1.2: ~105 → ~50 remaining.

### Phase 1.3 — Enable analyzers in CI
Once warnings < 50:
1. Remove `-p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false` from CI.
2. Keep `-p:TreatWarningsAsErrors=false` until all remaining warnings are NoWarn'd.
3. Add remaining suppressions to `Qobuzarr.csproj` `<NoWarn>` with comments.
4. Enable `-warnaserror` for new code only (incremental ratchet).

### Phase 2 — Correctness-risk rules
- **CA1001** (6 — undisposed fields), **CA2012** (2 — ValueTask misuse): Fix before enabling
  `-warnaserror` as these are correctness risks, not style issues.
- **SYSLIB1045** (106 — GeneratedRegex): Requires partial classes; schedule as a separate PR.

## Tracking Test

`scripts/tests/analyzer-flags.Tests.ps1` contains a `[Fact]`-equivalent Pester test that
stays RED until the CI analyzer flags are removed. See `docs/PHASE1_TRACKING_REDS.md`.
