# lidarr.plugin.common Migration Plan

Aim: Reduce duplication across plugins by moving shared utilities and adapters to our owned shared library `Lidarr.Plugin.Common` (submodule points to main branch in .gitmodules).

## Candidates To Move (Phase 1)
- Utilities
  - `src/Utilities/StringSimilarity.cs`
  - `src/Utilities/QualityFormatter.cs`
  - `src/Utilities/ErrorHandling/ErrorHandlingExtensions.cs`
  - `src/Utilities/HashingUtility.cs`
- Caching
  - `src/Services/Caching/CacheSerializer.cs`
  - `src/Services/Caching/SubstringMatcher.cs` (if reused elsewhere)
- Validation / Input
  - `src/Utilities/LidarrInputValidator.cs`

## Porting Guidelines
- Namespace: `Lidarr.Plugin.Common.*`
- Keep method signatures stable; where Qobuz-specific logic exists, split into `Qobuz*` extension types that remain in this plugin
- Add unit tests alongside Common to preserve behavior
- Mark moved files here as thin forwarders (temporary) or delete once plugin consumes Common

## Steps
1. Implement in Common on `main` with tests
2. Publish preview package or consume via submodule project reference
3. Replace plugin references to local implementations with Common types
4. Delete local copies; keep minor extensions locally if needed

## Acceptance
- No behavior regression in matching, formatting, or serialization paths
- Test suite green; ambiguous type errors eliminated

