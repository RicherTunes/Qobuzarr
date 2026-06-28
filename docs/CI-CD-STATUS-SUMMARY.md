# CI/CD Pipeline Status Summary

## Current CI: Gitea (`.gitea/workflows/ci.yml`)

GitHub Actions is out of credits ecosystem-wide; GitHub is a mirror only.
Primary CI runs on the self-hosted Gitea instance — no billing limits.

### Jobs

**`CI / lint`** (every push + PR):
Runs date-parsing, sync-over-async, and ecosystem-parity gates (PowerShell scripts from Common).

**`CI / verify`** (runs after lint):
Host-assembly extraction from Docker, full build, ILRepack package, packaging-closure check,
deterministic test suite — via `pwsh scripts/verify-local.ps1`.

Both jobs must be green before a PR can merge.

## Local Verification

The `CI / verify` job runs `pwsh scripts/verify-local.ps1`, which delegates to
`ext/Lidarr.Plugin.Common/scripts/local-ci.ps1`. Run the same pipeline locally
before pushing:

```powershell
# Full pipeline (same as CI)
pwsh scripts/verify-local.ps1

# Fast rerun (reuse cached host assemblies)
pwsh scripts/verify-local.ps1 -SkipExtract

# Build + packaging closure only (skip tests)
pwsh scripts/verify-local.ps1 -SkipTests

# + Docker smoke test (mounts plugin in Lidarr container)
pwsh scripts/verify-local.ps1 -IncludeSmoke
```

**Prerequisites**: PowerShell 7+ (`pwsh`), .NET 8 SDK, Docker.

## Troubleshooting

### If `CI / lint` fails

Lint gates run the scripts in `ext/Lidarr.Plugin.Common/scripts/`:

- `lint-date-parsing.ps1` — catches `DateTime.Now` / `DateTime.Today` usage
- `lint-sync-over-async.ps1` — catches `.Result` / `.GetAwaiter().GetResult()` patterns
- `ecosystem-parity-lint.ps1` — checks parity matrix against Common

Run the failing script locally with `-Mode ci` to see the exact violation.

### If `CI / verify` fails

1. **Assembly extraction step**: Docker daemon must be reachable on the runner. If it isn't, this is a runner infrastructure issue.
2. **Build step**: Run `pwsh scripts/verify-local.ps1 -SkipExtract` locally to reproduce.
3. **Packaging-closure step**: The ILRepack output is missing expected DLLs — check `generate-expected-contents.ps1 -Check` output.
4. **Test step**: A deterministic test regressed — run `dotnet test` locally to identify.

### Emergency: both lint and verify fail

1. Check `Qobuzarr.csproj` for syntax errors.
2. Check `Directory.Packages.props` package versions.
3. Test local build: `dotnet build Qobuzarr.csproj`.
4. Rollback recent changes if necessary.

## Maintenance

- **Docker image pin**: `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913` — update when a new Lidarr plugins-branch release is available (search the entire repo for the old tag and update all hits).
- **Common submodule pin**: re-pin manually when Common's `main` advances — see `ext-common-sha.txt` and the submodule-pin section in CLAUDE.md.
- **GitHub mirror**: `.github/` contains `dependabot.yml`, `CODEOWNERS`, `FUNDING.yml`, and other non-workflow files that are still active. The `.github/workflows/` directory is empty — any reference to it as an active CI source is stale.
