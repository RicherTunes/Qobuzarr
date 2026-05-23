# Changelog

All notable changes to Qobuzarr will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.5.0] - 2026-05-23

### Liveness fix

- **Sync-over-async deadlock in `QobuzIndexerAdapter.RecordAuthOutcomeFromException` removed.** The previous wave's auth-gate wire-up called `_authGate.Handler.HandleFailureAsync(...).AsTask().GetAwaiter().GetResult()` from inside async catch blocks — deadlocks the calling thread whenever `IAuthFailureHandler.HandleFailureAsync` is implemented (or wrapped) in a way that captures the synchronization context. The default handler doesn't capture, so the deadlock didn't fire in practice; but it's a foot-gun loaded for the next person who implements a custom handler. Method renamed `RecordAuthOutcomeFromExceptionAsync`, returns `Task`, called with `await … ConfigureAwait(false)`. Pure liveness fix; gate behavior on the happy path is unchanged.

### Common library bump

- Bumps `ext/Lidarr.Plugin.Common` from `431fe97` (between v1.7.1 and v1.8.0) to **v1.9.1**. Picks up `AuthFailureGate`, `SecureMemory`, `PagedResponseValidator`, `Conservative rate-limit profile`, `HttpExceptionClassifier`, testkit-lifted plugin contracts, `Lidarr.Plugin.*.dll` naming enforcement.

### Auth-failure gate adoption

- `QobuzarrStreamingPlugin.ConfigureServices` registers `AuthFailureDelegatingHandler` + `AuthFailureGate` (60s probe interval).
- `CredentialValidator` + `TokenRefresher` + `AuthTokenManager` drive the gate (record failure on 401/403, signal success on recovery).
- `QobuzIndexerAdapter.SearchAlbumsAsync` short-circuits at the top when the gate is latched bad and no probe slot is available — closes the search-loop hammering family.
- New `QobuzAuthFailureDelegatingHandlerWireUpTests` pins the full handler chain.

### Pagination + dedup

- `QobuzIndexerAdapter.SearchAlbumsAsync` replaced the "not implemented" warning with a 20-page walker that dedupes on album id and preserves accumulated results on mid-page failure (defeats Qobuz's silent-offset-ignored failure mode).

### Log redaction sweep

- Every outbound log statement carrying secret-bearing material in `QobuzApiClient`, `QobuzRequestSigner`, `LidarrApiClient`, and `BridgeQobuzApiClient` now routes through `LogRedactor.Redact` / `RedactException`.
- New `QobuzApiClientLogRedactionTests` asserts redaction at the API client surface.

### Exception shape

- `QobuzApiException` hoisted out of `QobuzApiClient` as a top-level type in `src/Exceptions/`. `StatusCode` field widened to `HttpStatusCode?` for consistency with Common's exception shape. Call sites updated.

### ML indexer + concurrency hardening

- ML-flag-gated path + `HashingUtility` migration. Cache-key normalization uses `ToLowerInvariant` for Turkish-I safety (cosmetic cache cold-start on upgrade, no correctness impact).
- Download + concurrency stack: `BatchProcessor`, `DownloadPolicy`, `QobuzDownloadClient`, `QobuzDownloadItem`, `ConcurrencyManager`, `AdaptiveConcurrencyManager`, `AdaptiveRateLimiter`, `TrackDownloadService`, `QobuzStreamUrlService`, `QobuzStreamAvailabilityService` updated for `RecordAuthFailure` pass-through and tightened concurrency invariants.

### Analyzer baseline

- New `Directory.Build.targets` + `qobuzarr-warning-baseline.txt` + `.editorconfig` addendum documenting CA-rule suppressions. Mechanical CA1805 / CA1304 / CA1866 / CA1510 + `ToLowerInvariant` cleanups across ~30 files.

### Packaging

- Plugin manifest emits `commonVersion: "1.9.1"` from the template (Pester test pins the build-output ↔ template alignment).

### Tests

- 43 / 43 auth-gate + indexer-adapter tests pass. Full Qobuzarr suite green (build clean).

[Full diff](https://github.com/RicherTunes/qobuzarr/compare/v0.1.0...v0.5.0)

## [0.1.0] - 2026-05-23

### Phase 0 + Phase 1 — Ecosystem Alignment and HashingUtility Migration

#### Ecosystem version contract (Phase 0.3)

- Bumped `commonVersion` to `1.8.0` in `plugin.json` to align with Common v1.8.0.
- Parity-lint `VersionContract` check passes (`ecosystem-parity-lint.ps1 -Check VersionContract`).

#### Phase 0 — HashingUtility deprecation cleanup

- Obsoleted local `HashingUtility` pass-through methods (`ComputePasswordMD5Hash`, `ComputeMD5Hash`, `GenerateCacheKey`); call sites now use `Lidarr.Plugin.Common.Utilities.HashingUtility` directly.
- Pass-throughs carry `[Obsolete("... Removal: v0.2.0.", error: false)]` to enable a clean cutover.
- `StreamingApiRequestBuilder` adoption confirmed in Tidal-optimized examples; Qobuz indexer plumbed.
- `FileTokenStore<QobuzSession>` adopted for encrypted session persistence.

#### Phase 1 — docs, ML flag, and security

- Feature flag added for ML-powered query optimization path; `HashingUtility` migration completed in that path.
- Analyzer baseline established (`docs/ANALYZER_BASELINE.md`); skipped-test inventory updated.
- Security hardening backlog added: 11 findings, 2 High severity — see `docs/SECURITY_HARDENING_BACKLOG.md`.
- README augmented with Shared Infrastructure section (Common services consumed, version contract reference).
- Documentation section updated in README with CHANGELOG, CONTRIBUTING, SECURITY, and docs/ links.

---

## [v-next] - GUID Identity Change for Album Editions

### Changed
- **Album GUIDs now incorporate the edition/version string.**
  - Before: `qobuz-{albumId}-{quality}` (e.g., `qobuz-12345-6`)
  - After: `qobuz-{albumId}-{normalizedVersion}-{quality}` (e.g., `qobuz-12345-deluxe-edition-6`)

### Impact
- Albums without editions: **no change** (backward-compatible).
- Albums with editions: new unique GUIDs per edition.
- Lidarr may re-show previously rejected editioned releases as "new".
- No data loss -- existing downloads are unaffected.

### Recommendation
After upgrading, run a manual "Refresh" on any artist with multiple album editions to re-sync.

---

## [0.0.12] - 2025-01-13

### Added
- **GitHub Repository Preparation**: Complete repository cleanup for public release
  - Security audit and credential removal
  - Documentation consolidation
  - GitHub infrastructure setup
  - Version synchronization across all files

### Security
- Removed exposed credentials from test files
- Added comprehensive .env.example templates
- Enhanced .gitignore for sensitive files

### Changed
- Synchronized version numbers across all project files
- Consolidated documentation from 40+ files to essential set
- Updated build dependencies documentation

## [0.0.11] - 2025-01-12

### Added
- **Playlist Download Support**: Complete implementation with M3U8 generation
  - Download entire playlists while preserving track order
  - Automatic M3U8 playlist file creation
  - Concurrent downloads with proper synchronization
- **Label Download Support**: Batch download all albums from record labels
  - Organized by artist folders within label directory
  - Configurable maximum album limits
  - Progress tracking for large label catalogs
- **Extended API Coverage**: Added playlist and label methods to core services
  - QobuzApiService extended with GetPlaylistAsync, GetLabelAsync, etc.
  - QobuzSearchService enhanced with comprehensive playlist/label support
  - Full integration in CLI through PluginHost

### Changed
- **Architecture Improvements**: Strengthened plugin-first architecture
  - CLI now fully delegates to plugin services (no reimplementation)
  - Clean adapter pattern without unnecessary complexity
  - Proper separation of concerns between plugin and CLI

### Fixed
- **Technical Debt Resolution**: Major cleanup of codebase
  - Removed all stub/placeholder implementations
  - Eliminated duplicate code between CLI and plugin
  - Fixed compilation errors in PluginHost
  - Resolved type mismatches in download result models

### Technical
- **Code Quality**: Achieved 7/10 technical debt score
  - No hardcoded/placeholder data in production paths
  - Proper error handling with fail-fast principles
  - Thread-safe operations throughout
  - Clean adapter implementations

## [0.0.10] - 2025-01-11

### Fixed
- **ML Engine Integration**: Fixed dependency injection issue where ML engine wasn't being initialized
  - Added `IPatternLearningEngine` dependency to `QobuzIndexer`
  - Updated `QobuzRequestGenerator` to accept and use ML engine parameter
  - Added `EnableMLPredictions` setting with proper validation requiring Query Intelligence
  - Users will now see "ML Pattern Learning Engine ENABLED" instead of "rule-based optimization only"
- **Network Retry Logic**: Enhanced retry mechanism for network interruption issues
  - Fixed "response ended prematurely" errors with exponential backoff
  - Increased MaxRetries to 5 and RetryDelayMs to 2000ms for better resilience
- **Format ID 27 Support**: Fixed handling of 192kHz quality format
  - Now attempts format_id 27 normally instead of automatically skipping
  - Proper quality fallback chain for better track availability
  - Simplified logging to reduce verbosity

### Added
- **Enhanced User Experience**: Improved ML feature descriptions in settings
  - Added clear, user-friendly help text with emojis and concrete examples
  - Added validation to prevent enabling ML without Query Intelligence
  - Better explanation of benefits and expectations for each feature
- **10M+ Training Data**: Enhanced ML engine with massive training dataset
  - Overnight data collection runs on RTX 3090 24GB for optimal performance
  - Pre-trained models provide immediate benefits without learning period
  - Comprehensive coverage of music genres and edge cases

### Changed
- **Documentation Overhaul**: Comprehensive update to emphasize ML advantages
  - Updated README with clear user benefits and system administrator advantages
  - Enhanced FAQ with ML-specific questions and answers
  - Added performance monitoring examples and configuration guidance
  - Better explanation of what users will see in logs and how to verify features

## [0.0.6] - 2025-02-07

### Fixed
- **Critical Version Mismatch**: Fixed assembly version mismatch preventing plugin from loading
  - Updated Lidarr dependencies from v10.0.0 (development) to v2.13.1 (stable release)
  - Plugin now properly loads in Lidarr v2.13.1.4682
- **Plugin Discovery Issue**: Renamed assembly to match Lidarr's required pattern
  - Changed from "Qobuzarr.dll" to "Lidarr.Plugin.Qobuzarr.dll"
  - Documented this undocumented requirement for future developers

### Changed
- **Version Management**: Consolidated to single source of truth in csproj
  - Removed duplicate version definitions across multiple files
  - Added dynamic plugin.json generation from template during build
  - All classes now read version from assembly at runtime
- **Build Scripts**: Enhanced release scripts with all previous functionality
  - Consolidated build-release.ps1 into new scripts/release.ps1
  - Added deployment, version info generation, and package creation

## [Unreleased]

### Added
- **Advanced Dataset Analysis and Optimization**
  - **65.8% API call reduction** based on 100,000 real album analysis
  - **94.7% cache hit rate** with combined optimization strategies  
  - **Multiple optimization approaches**: Pattern exploitation, context usage, substring caching
  - **Real production test data generation** from actual album patterns
  - **Comprehensive performance projections** with concrete implementation roadmap
- **ML Pattern Learning Engine** ✨
  - **ML.NET-powered adaptive optimization** with online learning
  - **25+ feature extraction** from artist/album characteristics 
  - **Multi-class classification** for query complexity prediction
  - **Continuous improvement** from production usage feedback
  - **Automatic model retraining** every 24 hours or 1000 patterns
  - **Confidence scoring** with intelligent fallback strategies
- **Enhanced Query Intelligence System**
  - Updated simulation with 100,000 album dataset
  - Advanced pattern recognition for live albums, deluxe editions, remasters
  - Context-aware optimization using Lidarr metadata
  - Substring matching for similar artist/album names
- **Production-Ready Integration**
  - All services now use real API integration (no stub/placeholder data)
  - Enhanced CLI export functionality with comprehensive filtering
  - Advanced error handling with exponential backoff retry logic
  - Memory optimization with configurable resource limits

### Changed
- **Major README.md overhaul** with comprehensive project overview
- Query Intelligence enabled by default for immediate performance benefits
- Enhanced architecture documentation with Query Intelligence components
- Improved inline code documentation with performance metrics
- Updated testing procedures to include Query Intelligence validation

### Fixed
- Conservative Query Intelligence classification ensures quality preservation
- Unicode artist handling (Björk, Sigur Rós) with fail-safe behavior
- Complex album handling (Various Artists, special characters) maintains current quality
- Edge case handling for live recordings, classical music, and compilations

## [0.0.3] - 2025-01-30 - Performance Revolution & Technical Debt Cleanup

### 🚀 Major Performance Improvements
- **Adaptive Rate Limiting System**: 93.1x performance improvement (from 4.76 to 443+ searches/minute)
- **Query Intelligence Optimization**: 49.83% API call reduction with 1.515% quality loss
- **Processing Time Revolution**: 23,324 albums from 14+ hours to under 1 hour
- Automatically adjusts request rate based on API responses (60-500 req/min)
- Intelligent detection of rate limits (429 and soft 401 errors)
- Self-optimizing system requiring no manual configuration

### 🏗️ Technical Debt Cleanup
- **Plugin-First Architecture**: CLI now properly uses plugin services via project reference
- **Code Duplication Elimination**: Removed duplicate implementations between CLI and plugin
- **Architectural Alignment**: CLI focuses on test program functionality, plugin contains core logic
- **Comprehensive Documentation**: Added extensive technical documentation suite
- **Testing Framework**: 100% test coverage for new optimization systems

### Added
- `AdaptiveRateLimiter.cs` - Smart rate limiting with automatic adjustment
- `AdaptiveQobuzApiClient.cs` - Decorator pattern wrapper for existing API client
- `QueryComplexityClassifier.cs` - Intelligent query complexity analysis
- `SmartQueryStrategy.cs` - Adaptive query optimization system
- `TestPerformanceCommand.cs` - Performance testing capabilities
- `ImprovedQueueService.cs` - Concurrent download queue processing
- RateLimiterAdapter to properly bridge CLI to plugin's rate limiter
- Comprehensive unit tests for QobuzTrackDownloader and new components

### Changed
- **CLI Architecture**: Refactored to use plugin services (plugin-first architecture)
- Updated `QobuzModule.cs` with new service registrations
- Improved inline code documentation with performance metrics
- Enhanced error messages for better debugging
- Replaced dangerous .Wait() calls with safer GetAwaiter().GetResult() in dispose methods

### Fixed
- Session expiry handling edge cases
- Compilation errors in test projects
- Nullable TimeSpan issues in DownloadQueueStatistics
- Added missing RetryDelaySeconds property to DownloadQueue model
- Resolved namespace conflicts between plugin and CLI
- Fixed DI container registration issues

### Removed
- Duplicate AdaptiveRateLimiter from CLI (now uses plugin's implementation)
- Duplicate QobuzConstants from CLI (now uses plugin's constants)
- Duplicate PluginMetadataService from CLI (now uses plugin's metadata functionality)

### 📊 Performance Testing Results
Extensive testing confirmed API accepts 600+ requests/minute:
- Test 1 (20 searches): 13.1x improvement
- Test 2 (50 searches): 13.2x improvement
- Test 3 (100 searches): 17.4x improvement
- Test 4 (150 searches): 20.7x improvement
- Test 5 (200 searches): 24.4x improvement
- Test 6 (300 searches): Successfully scaled to 500 req/min
- Test 7 (100 searches @ 300 start): **93.1x improvement**

All tests maintained 100% success rate with zero rate limit errors.

## [1.0.0-alpha] - 2025-01-30

### Added
- Initial plugin release with indexer functionality
- Email/password authentication support
- User ID/token authentication support
- Advanced search capabilities with multiple strategies
- Quality detection for MP3, FLAC, and Hi-Res formats
- Response caching with configurable TTL
- Rate limiting with exponential backoff
- Session management with 24-hour caching
- Genre and year filtering options
- Search result limit configuration (10-500)

### Technical Features
- Clean dependency injection architecture
- Comprehensive logging with NLog
- Thread-safe implementations
- Memory-efficient caching
- Automatic request signing for protected endpoints
- ILRepack integration for single DLL distribution

### Known Limitations
- Download client functionality not yet implemented
- No RSS support (not applicable for Qobuz)
- Limited to Qobuz available regions

## [0.5.0-pre] - 2024-12-15

### Added
- Initial project structure
- Basic authentication implementation
- Preliminary API client

### Changed
- Migrated from .NET Core 3.1 to .NET 6.0

## API Breaking Changes

### Version 1.0.0
- Initial release, no breaking changes

## Migration Guides

### From Manual Qobuz Integration to Plugin

1. **Backup Current Configuration**
   ```bash
   cp /config/config.xml /config/config.xml.backup
   ```

2. **Remove Manual Scripts**
   - Delete any custom Qobuz scripts
   - Remove cron jobs for Qobuz imports

3. **Install Plugin**
   - Follow installation guide
   - Configure authentication
   - Test search functionality

4. **Migrate Existing Library**
   - Existing Qobuz albums remain untouched
   - Future searches will use the plugin
   - Consider re-importing for better metadata

## Deprecations

### Planned for 2.0.0
- `AppSecret` parameter will become optional
- Legacy MD5 password hashing may be replaced with more secure method

## Future Roadmap

### Version 1.1.0 (Planned)
- [ ] Download client implementation
- [ ] Queue management system
- [ ] Progress tracking
- [ ] Bandwidth throttling

### Version 1.2.0 (Planned)
- [ ] Playlist support
- [ ] Artist discography sync
- [ ] Smart quality selection

### Version 2.0.0 (Planned)
- [ ] Database-backed cache
- [ ] Advanced filtering options
- [ ] Multi-account support
- [ ] Webhook notifications