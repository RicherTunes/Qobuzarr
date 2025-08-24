# GitHub Issues to Create

## Issue #1: Test Suite Compilation Errors
**Priority**: Medium
**Labels**: bug, tests

### Description
The test suite has multiple compilation errors preventing tests from running.

### Errors Found
- FsCheck.Property conversion errors in PropertyBased tests
- Mock setup inconsistencies between Moq and NSubstitute
- Missing extension methods (IsNotNullOrWhiteSpace)
- IndexerFlags.PaidDownload not found
- ParsedAlbumInfo.Year property missing

### Affected Files
- `tests/Qobuzarr.Tests/PropertyBased/AlbumEditionPropertyTests.cs`
- `tests/Qobuzarr.Tests/Integration/AlbumEditionLidarrIntegrationTests.cs`
- `tests/Qobuzarr.Tests/Unit/Models/QobuzAlbumEditionTests.cs`

### Acceptance Criteria
- [ ] All test projects compile without errors
- [ ] Tests can be executed with `dotnet test`

---

## Issue #2: Audit IDisposable Implementations
**Priority**: Low
**Labels**: enhancement, technical-debt

### Description
Several classes implement IDisposable but may have incomplete disposal patterns.

### Classes to Audit
- ServiceIntegrationLayer
- QobuzApiClient
- QobuzAuthenticationService
- Any class using HttpClient or CancellationTokenSource

### Acceptance Criteria
- [ ] All IDisposable classes properly dispose managed resources
- [ ] Disposal patterns follow best practices
- [ ] No resource leaks in long-running operations

---

## Issue #3: Assembly Version Management Complexity
**Priority**: Low
**Labels**: build, documentation

### Description
The project has complex assembly version management due to Lidarr plugins branch compatibility requirements.

### Current Issues
- TagLibSharp version warnings (2.2.0.19 vs 2.2.0.27)
- Plugins branch vs release branch assembly differences
- Version override requirements in build scripts

### Suggested Improvements
- [ ] Document the version requirements clearly
- [ ] Automate version detection and override
- [ ] Add CI checks for version compatibility

---

## Issue #4: Improve Rate Limiting Implementation
**Priority**: Medium
**Labels**: enhancement, api

### Description
Add proper rate limiting for Qobuz API calls to prevent throttling.

### Requirements
- Implement configurable rate limits
- Add exponential backoff on 429 responses
- Track API call metrics
- Respect Qobuz rate limit headers

### Acceptance Criteria
- [ ] Rate limiter prevents API throttling
- [ ] Configurable limits in settings
- [ ] Graceful handling of rate limit errors

---

## Issue #5: Add Certificate Pinning for API Security
**Priority**: Low
**Labels**: enhancement, security

### Description
Implement certificate pinning for Qobuz API calls to prevent MITM attacks.

### Requirements
- Pin Qobuz API certificates
- Implement certificate validation
- Add fallback mechanism for certificate updates
- Log certificate validation failures

### Acceptance Criteria
- [ ] Certificate pinning implemented
- [ ] Graceful fallback on certificate changes
- [ ] Security events logged appropriately