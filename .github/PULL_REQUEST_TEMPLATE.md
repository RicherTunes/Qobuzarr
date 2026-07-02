# Pull Request

## Description

<!-- Provide a clear and concise description of what this PR does -->

### Type of Change

<!-- Mark the relevant option with an [x] -->

- [ ] 🐛 Bug fix (non-breaking change which fixes an issue)
- [ ] ✨ New feature (non-breaking change which adds functionality)  
- [ ] 💥 Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] 📚 Documentation update
- [ ] 🏗️ Refactoring (no functional changes)
- [ ] ⚡ Performance improvement
- [ ] 🧪 Test updates
- [ ] 🔧 Build/CI changes
- [ ] 🔒 Security fix

## What does this PR do?

<!-- Explain your changes in detail. Include motivation and context. -->

### Related Issues

<!-- Link any related issues using keywords like "Fixes #123" or "Closes #456" -->

- Fixes #
- Addresses #
- Related to #

## Testing

<!-- Describe the tests you ran and/or added -->

### Test Coverage

- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] Manual testing performed
- [ ] Performance impact assessed
- [ ] Security implications reviewed

### Test Environment

<!-- Provide details about your testing environment -->

- **Lidarr Version**: 
- **Operating System**: 
- **Qobuz Subscription Type**: 
- **Test Scenarios**: 

## Implementation Details

<!-- Provide more technical details about your implementation -->

### Changes Made

<!-- List the main changes -->

- 
- 
- 

### Architecture Impact

<!-- Does this change affect the plugin architecture? -->

- [ ] No architectural changes
- [ ] Changes to plugin-CLI separation
- [ ] New dependencies added
- [ ] API changes
- [ ] Database/storage changes
- [ ] Configuration changes

### Performance Considerations

<!-- Any performance implications? -->

- [ ] No performance impact
- [ ] Performance improvement
- [ ] Potential performance regression (justified because...)
- [ ] Memory usage impact
- [ ] Network usage impact

## Quality Checklist

<!-- Ensure your PR meets quality standards -->

### Code Quality

- [ ] Code follows project coding standards
- [ ] Self-review of code completed
- [ ] Code is properly commented
- [ ] No console.log or debug statements left
- [ ] Error handling implemented appropriately
- [ ] Thread safety considered where applicable

### Plugin Architecture Compliance

- [ ] Core functionality implemented in plugin (`src/`) not CLI
- [ ] CLI only contains UI/CLI-specific features
- [ ] Proper separation of concerns maintained
- [ ] No duplication between plugin and CLI

### Security & Safety

- [ ] No sensitive information exposed in logs
- [ ] Input validation implemented
- [ ] No SQL injection or similar vulnerabilities
- [ ] Secure credential handling
- [ ] Dependencies security reviewed

### Documentation

- [ ] Code documentation updated (if needed)
- [ ] User documentation updated (if needed)  
- [ ] API documentation updated (if needed)
- [ ] CHANGELOG updated (if applicable)

## Screenshots/Recordings

<!-- If applicable, add screenshots or recordings to demonstrate changes -->

## Deployment Notes

<!-- Any special deployment considerations? -->

- [ ] No special deployment needed
- [ ] Database migration required
- [ ] Configuration changes required
- [ ] External service changes
- [ ] Breaking changes that need user communication

## Backwards Compatibility

<!-- How does this affect existing users? -->

- [ ] Fully backwards compatible
- [ ] Minor breaking changes (documented below)
- [ ] Major breaking changes (migration guide provided)

### Breaking Changes Details

<!-- If there are breaking changes, explain them -->

## Attribution

<!-- If building upon TrevTV's work or other contributions, please credit appropriately -->
This PR builds upon the foundational work of [TrevTV's Lidarr.Plugin.Qobuz](https://github.com/TrevTV/Lidarr.Plugin.Qobuz).

## Additional Notes

<!-- Any additional information for reviewers -->

---

## Pre-Merge Verification

### Required (attach evidence or explain skip)
- [ ] Gitea CI is green for `secret-scan`, `lint`, and `verify`
- [ ] `pwsh scripts/verify-local.ps1` succeeds locally for CI-sensitive changes
- [ ] Runtime sandbox tests pass (`--filter "Category=Runtime"`)
- [ ] No new `net6.0` references introduced

### If Common submodule changed
- [ ] Common SHA matches a tagged release (e.g., v1.7.1)
- [ ] Promotion checklist items verified per `ext/Lidarr.Plugin.Common/docs/ECOSYSTEM_PROMOTION_CHECKLIST.md`

### Test Results
- Total: ___ passed, ___ failed, ___ skipped
- Runtime: ___ passed

### Bridge Parity (streaming plugins only)
- [ ] `AddBridgeDefaults()` called in ConfigureServices
- [ ] No silent exception swallowing in indexer/download client paths
