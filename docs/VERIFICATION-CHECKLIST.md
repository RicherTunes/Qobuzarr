# Qobuzarr Plugin Verification Checklist

## Root Cause & Research

- [ ] Identified root cause, not symptoms
- [ ] Researched Lidarr plugin architecture patterns (TrevTV's approach)
- [ ] Analyzed working plugins (Tidal, Deezer) for best practices
- [ ] Verified against Qobuz API documentation
- [ ] Checked Lidarr version compatibility (2.13.2.4686)

## Architecture & Design

- [ ] Plugin-first implementation (no business logic in CLI)
- [ ] Uses correct Lidarr base classes (HttpIndexerBase, DownloadClientBase)
- [ ] Proper DI with Lidarr's DryIoC container
- [ ] No duplicate code between plugin and CLI
- [ ] Follows established plugin patterns from TrevTV

## Solution Quality

- [ ] CLAUDE.md compliant build commands used
- [ ] Assembly version override applied (2.13.2.4686)
- [ ] No hardcoded credentials or API keys
- [ ] ML optimization properly integrated
- [ ] 100% complete implementation (not partial)

## Build System Stability

- [ ] Build scripts pinned to specific tool versions (.NET SDK 8.0.x)
- [ ] Lidarr source commit hash hardcoded (aa7b63f2e13351f54a31d780d6a7b93a2411eaec)
- [ ] Directory.Build.props version override automated in scripts
- [ ] NuGet package sources explicitly configured (no conflicts)
- [ ] ILRepack version pinned in Directory.Packages.props
- [ ] MSBuild properties documented and version-controlled
- [ ] Build works on Windows, Linux, and macOS
- [ ] CI/CD uses exact same build commands as local

## Dependency & Version Management

- [ ] All NuGet packages pinned to exact versions
- [ ] Directory.Packages.props centralized management working
- [ ] No floating version wildcards (*) in dependencies
- [ ] Newtonsoft.Json version matches Lidarr's (13.0.3)
- [ ] Microsoft.Extensions.* versions compatible with Lidarr
- [ ] No dependency conflicts between plugin and Lidarr
- [ ] Package restore sources properly configured
- [ ] Vulnerable package scanner configured (Dependabot/Snyk)

## Breaking Change Prevention

- [ ] Lidarr API changes monitored (new releases checked)
- [ ] Qobuz API changes monitored (deprecation notices)
- [ ] .NET runtime compatibility verified (8.0 LTS)
- [ ] Build script changes tested before commit
- [ ] Middleware version upgrades tested in isolation
- [ ] Interface changes detected by compilation
- [ ] Assembly binding redirects avoided (ILRepack instead)
- [ ] Plugin.json schema changes tracked

## Security & Safety

- [ ] Qobuz credentials stored securely (no plaintext)
- [ ] API keys managed through settings, not code
- [ ] Session tokens properly handled and refreshed
- [ ] No sensitive data in logs or error messages
- [ ] Input sanitization for search queries
- [ ] No secrets in git history (BFG/filter-branch if needed)
- [ ] Pre-commit hooks block credential commits
- [ ] HTTPS only for all API calls

## Lidarr Integration

- [ ] IIndexer interface properly implemented
- [ ] IDownloadClient interface properly implemented
- [ ] Settings UI works in Lidarr web interface
- [ ] Plugin.json manifest correctly configured
- [ ] Assembly versions match target Lidarr runtime
- [ ] Constructor injection matches base class expectations
- [ ] Plugin loads without reflection errors
- [ ] Settings persist across Lidarr restarts

## Qobuz API Integration

- [ ] Authentication flow working (login/token refresh)
- [ ] Search functionality returns correct results
- [ ] Album/track metadata properly mapped to Lidarr models
- [ ] Quality levels correctly handled (5=MP3, 6=FLAC-CD, 7=Hi-Res, 27=Max)
- [ ] Download URLs properly generated with tokens
- [ ] API rate limits respected (no 429 errors)
- [ ] Geographic API endpoints handled correctly
- [ ] Token expiry handled with automatic refresh

## Build & Deployment

- [ ] Builds with analyzer suppression flags
- [ ] ILRepack enabled for dependency merging
- [ ] Auto-deployment to test instance works
- [ ] No StyleCop errors from Lidarr source
- [ ] CI/CD pipeline passes all checks
- [ ] Build artifacts properly generated
- [ ] Symbol files (PDB) included for debugging
- [ ] Clean build from fresh clone works

## Testing & Validation

- [ ] Unit tests cover core functionality
- [ ] Integration tests for Qobuz API
- [ ] Manual testing in Lidarr instance performed
- [ ] Search returns expected results
- [ ] Downloads complete successfully
- [ ] Tests run in CI/CD pipeline
- [ ] Mock data doesn't leak into production
- [ ] Test coverage > 70%

## Performance & Optimization

- [ ] ML query optimizer functioning
- [ ] API rate limiting respected
- [ ] Efficient caching implemented
- [ ] No memory leaks or resource issues
- [ ] Response times acceptable (<2s for searches)
- [ ] Database queries optimized (if applicable)
- [ ] HTTP connection pooling configured
- [ ] Async/await used properly (no .Result blocking)

## Error Handling & Resilience

- [ ] Specific exception types used (QobuzApiException, etc.)
- [ ] Graceful degradation on API failures
- [ ] Clear error messages for configuration issues
- [ ] Retry logic for transient failures
- [ ] Proper logging at appropriate levels
- [ ] Circuit breaker for API outages
- [ ] Timeout handling for hung requests
- [ ] Crash recovery without data loss

## Operational Monitoring

- [ ] Health check endpoint working
- [ ] Metrics exposed for monitoring
- [ ] Log levels configurable
- [ ] Performance counters available
- [ ] Error rates tracked
- [ ] API call success/failure rates logged
- [ ] Download completion rates monitored
- [ ] Resource usage within limits

## Qobuzarr-Specific Validation

- [ ] Edition detection working (Deluxe, Remastered, etc.)
- [ ] Multi-disc albums handled correctly
- [ ] Artist name variations normalized
- [ ] Quality preference logic functioning
- [ ] Geographic restrictions handled (app_id/secret per region)
- [ ] Track numbering preserved correctly
- [ ] Compilation albums properly identified
- [ ] Release date parsing accurate
- [ ] Cover art URLs generated correctly
- [ ] Download progress reporting to Lidarr
- [ ] Various artist albums handled
- [ ] Special characters in titles handled
- [ ] Unicode/UTF-8 support verified

## Cross-Platform Compatibility

- [ ] Windows path separators handled
- [ ] Linux case-sensitivity considered  
- [ ] macOS special folders respected
- [ ] Docker container support verified
- [ ] File permissions handled correctly
- [ ] Line ending differences (CRLF/LF) managed
- [ ] Time zone handling consistent
- [ ] Locale/culture independent

## Documentation & Maintenance

- [ ] CLAUDE.md instructions current and accurate
- [ ] Build scripts (setup.ps1/sh, build.ps1/sh) working
- [ ] Pre-commit hooks catching issues
- [ ] Central package management configured
- [ ] Version management automated
- [ ] README has quick start guide
- [ ] Troubleshooting guide updated
- [ ] API documentation generated

## Known Issues Resolution

- [ ] ReflectionTypeLoadException fixed (assembly version override)
- [ ] Plugin discovery by Lidarr confirmed
- [ ] No missing dependencies at runtime
- [ ] Constructor signatures match base classes
- [ ] ILocalizationService properly injected
- [ ] Directory.Build.props conflicts resolved
- [ ] NuGet NU1507/NU1008 errors prevented
- [ ] NETSDK1045 compatibility fixed

## Regression Testing

- [ ] Previous bugs have test coverage
- [ ] Version upgrade path tested
- [ ] Rollback procedure documented
- [ ] Data migration tested (if applicable)
- [ ] Config file compatibility maintained
- [ ] API backward compatibility verified
- [ ] Plugin upgrade doesn't break settings
- [ ] Database schema changes handled

## Environment-Specific Checks

- [ ] Development environment setup documented
- [ ] Test environment matches production
- [ ] CI environment properly configured
- [ ] Local build matches CI build
- [ ] Environment variables documented
- [ ] Secrets management configured
- [ ] Proxy support tested (if applicable)
- [ ] Firewall rules documented

## Future-Proofing

- [ ] Lidarr v3 compatibility considered
- [ ] .NET 9 migration path planned
- [ ] Qobuz API v2 changes anticipated
- [ ] Deprecation warnings addressed
- [ ] Technical debt documented
- [ ] Upgrade strategy defined
- [ ] Breaking change communication plan
- [ ] Sunset timeline for old versions

## ANALYZE ALL ITEMS IN THIS CHECKLIST ONE BY ONE. ACHIEVE 100% COVERAGE. DO NOT MISS A SINGLE ITEM.

## Process: READ → RESEARCH → ANALYZE ROOT CAUSE → CHALLENGE → THINK → RESPOND

## Critical Success Factors

1. **Assembly Version MUST be 2.13.2.4686** - Override in build
2. **Use exact Lidarr commit aa7b63f2e13351f54a31d780d6a7b93a2411eaec**
3. **Always suppress analyzers with build flags**
4. **Never implement business logic in CLI - plugin only**
5. **Follow TrevTV's proven patterns exactly**
6. **Pin ALL versions - no wildcards or floating versions**
7. **Test build changes in isolation before committing**
8. **Monitor upstream breaking changes proactively**