# PR Closure Messages

## PR #7: Expand Qobuzarr Test Coverage
**Action**: Close
**Message**:
```
Thank you for your contribution. However, this PR has compilation errors and makes massive changes (15K+ additions, 29K+ deletions) that remove important documentation without justification. 

The test additions are valuable, but they need to:
1. Compile without errors
2. Not remove existing documentation
3. Be submitted in smaller, reviewable chunks

Please consider resubmitting specific test improvements in smaller PRs that don't break the build.
```

---

## PR #19: Optimize CI/CD Pipeline
**Action**: Close
**Message**:
```
Thank you for your contribution. Unfortunately, this PR completely breaks the build by introducing a telemetry system with incompatible Microsoft.Extensions dependencies.

Issues found:
- Telemetry system doesn't compile in Lidarr plugin environment
- Over-engineered solution (1000+ lines) for minimal benefit
- Unproven performance claims

If CI/CD optimization is needed, please focus on simpler, working improvements that don't break the build.
```

---

## PR #21: Fix build errors and test coverage
**Action**: Close
**Message**:
```
Thank you for your contribution. While the test coverage additions are valuable, this PR doesn't actually fix the build errors as claimed.

Issues found:
- Uses wrong Protocol implementation (DownloadProtocol.Usenet instead of string)
- Removes 21,000+ lines of documentation without justification
- Build still fails with the proposed changes

The test suite additions could be valuable if submitted separately after fixing the actual compatibility issues.
```

---

## PR #22: Bug Report (DRAFT)
**Action**: Close
**Message**:
```
This PR is misleading - it claims to be a bug report but actually adds 30+ new files.

While some bugs identified are real (memory leak in QobuzDownloadItem - now fixed in commit e604ebe), the PR mixes legitimate issues with fabricated details and shouldn't be merged as code.

Bug reports should be submitted as GitHub Issues, not PRs. The real issues have been addressed or tracked separately.
```

---

## PR #23, #24, #27: Test/CI Duplicates (DRAFT)
**Action**: Close
**Message**:
```
This appears to be a duplicate of PR #[7/19] with similar issues. Closing to reduce noise.

Please coordinate to submit a single, working PR rather than multiple drafts with the same problems.
```

---

## PR #26: Security Audit Report (DRAFT)
**Action**: Close with consideration
**Message**:
```
Thank you for the security audit. The issues identified appear legitimate and valuable.

However:
- External attribution should be removed
- This should be an Issue or Discussion, not a PR
- Some recommendations may be over-engineered for a Lidarr plugin

The valuable security improvements have been tracked as separate issues for implementation.
```

---

## PR #28: CI/CD Documentation (DRAFT)
**Action**: Close
**Message**:
```
Thank you for the comprehensive documentation. However, this is premature optimization.

The project needs a working build before optimizing CI/CD. The practices documented are good but:
- Build is currently broken and needs fixing first
- Many suggestions don't apply to Lidarr plugin deployment model
- Documentation-only PRs should be discussed first

Please focus on fixing actual build issues before optimization documentation.
```