# src/Compat — Compatibility Stubs

## Purpose

`CommonStubs.cs` provides a minimal set of stub implementations for APIs exported by
`Lidarr.Plugin.Common`. These stubs exist **solely** to allow the project to compile and
run a limited set of tests in environments where the `Lidarr.Plugin.Common` submodule is
not available — for example, external pull requests from contributors who do not have
repository access to the private Common submodule.

## Activation

The stubs are compiled ONLY when the MSBuild property `$(UseCommonStubs)` evaluates to
`true`, which happens automatically in `Qobuzarr.csproj` when the submodule path
`ext/Lidarr.Plugin.Common/src/Lidarr.Plugin.Common.csproj` does not exist:

```xml
<UseCommonStubs Condition="!Exists('ext/Lidarr.Plugin.Common/src/Lidarr.Plugin.Common.csproj')">true</UseCommonStubs>
```

When `UseCommonStubs=true`, the constant `COMMON_STUBS` is added to `DefineConstants` so
that stub implementations can be wrapped in `#if COMMON_STUBS` guards.

## CRITICAL: Production CI MUST NEVER Define COMMON_STUBS

Production CI always has the `Lidarr.Plugin.Common` submodule present. Defining
`COMMON_STUBS` in a production context would silently replace real implementations with
stubs, producing a broken plugin binary.

The following build artifacts MUST NEVER define or reference `COMMON_STUBS` unconditionally:

| File | Policy |
|------|--------|
| `Qobuzarr.csproj` | Only inside `<PropertyGroup Condition="'$(UseCommonStubs)' == 'true'">` |
| `Directory.Build.props` | NEVER — applies to all projects |
| `.github/workflows/*.yml` | NEVER — CI always has the submodule |

A Pester test in `scripts/tests/no-common-stubs-in-prod.Tests.ps1` enforces this policy
automatically and must remain GREEN on every PR.

## What the Stubs Cover

The stubs are NOT feature-complete and are NOT guaranteed to be API-compatible with the
real implementations. They cover only the minimum surface required for a build to succeed.
Tests that depend on real Common behaviour will be skipped or fail under the stub build.

Stub implementations:
- `Lidarr.Plugin.Common.Utilities.Guard` — argument validation helpers
- `Lidarr.Plugin.Common.Utilities.StringSimilarity` — simplified string similarity
- `Lidarr.Plugin.Common.Utilities.RetryUtilities` — empty shell
- `Lidarr.Plugin.Common.Services.Globalization.UnicodeNormalizer` — simplified normalization
- `Lidarr.Plugin.Common.Services.Performance.IUniversalAdaptiveRateLimiter` — interface stub
- `Lidarr.Plugin.Common.Security.Sanitize` — path sanitization stub

## Adding New Stubs

Before adding a stub, confirm that:
1. The API does NOT already exist in a referenced NuGet package.
2. The stub is genuinely needed for the no-submodule build to succeed.
3. The stub is wrapped in `#if COMMON_STUBS` to guarantee it never ships in production.
4. The real implementation is tested separately through the submodule build.
