# Qobuzarr Upgrade Guide

This guide covers upgrading Qobuzarr between versions.

## General Upgrade Process

1. **Backup your settings** - Export or screenshot your current configuration
2. **Download the new version** from [Releases](https://github.com/RicherTunes/qobuzarr/releases)
3. **Stop Lidarr** before replacing files
4. **Replace plugin files** in your plugins directory
5. **Start Lidarr** and verify the plugin loads
6. **Test connection** in Settings > Indexers > Qobuzarr

## Version-Specific Notes

### Upgrading to v0.1.x from v0.0.x

**New Features:**

- ML-powered Query Intelligence for smarter searches
- Improved caching (94.7% hit rate)
- Better error messages

**Settings Changes:**

- New "Enable Query Intelligence" option (enabled by default)
- New confidence threshold settings

**Action Required:** None - existing settings preserved

### Upgrading from Early Alpha (v0.0.1-v0.0.10)

**Changes:**

- Moved to .NET 8.0 runtime
- New plugin structure
- Improved authentication handling

**Action Required:**

1. Remove old plugin files completely
2. Install fresh from release
3. Re-enter Qobuz credentials

## Settings Migration

### Automatic Migration

Most settings migrate automatically. After upgrade:

1. Check Settings > Indexers > Qobuzarr
2. Verify App ID and App Secret are preserved
3. Test the connection

### Manual Migration Required

If upgrading from very old versions:

1. Note current App ID, App Secret, and quality setting
2. After upgrade, re-enter in plugin settings

## Query Intelligence Migration

When upgrading to versions with Query Intelligence:

1. **First run** will be slightly slower as patterns are learned
2. **Cache builds over time** - give it 24-48 hours of normal use
3. **Monitor API usage** - should see ~65% reduction after learning

### Disabling Query Intelligence

If you experience issues:

1. Settings > Indexers > Qobuzarr
2. Set "Enable Query Intelligence" to No
3. Save and test

## Troubleshooting Upgrades

### Plugin Not Loading After Upgrade

1. **Verify file extraction:**

   ```
   plugins/RicherTunes/Qobuzarr/
   ├── Lidarr.Plugin.Qobuzarr.dll
   ├── plugin.json
   └── [other dependencies]
   ```

2. Check Lidarr logs at System > Logs
3. Ensure Lidarr 2.13.0+ is installed

### "Could not load assembly" Error

Version mismatch with Lidarr:

1. Check required Lidarr version in release notes
2. Upgrade Lidarr if needed
3. Or downgrade to compatible Qobuzarr version

### Authentication Lost After Upgrade

1. Go to Settings > Indexers > Qobuzarr
2. Re-enter App ID and App Secret
3. If using tokens, extract fresh tokens from browser
4. Test connection

### Search Quality Degraded

After upgrading to Query Intelligence versions:

1. Let the ML patterns stabilize (24-48 hours)
2. Check if "Query Optimization" is set to "Query Intelligence" or higher
3. Try clearing the cache: Settings > General > Clear Cache

## Rollback Procedure

If an upgrade causes problems:

1. **Stop Lidarr**
2. **Remove new files:**

   ```bash
   rm -rf ~/.config/Lidarr/plugins/RicherTunes/Qobuzarr/
   ```

3. **Extract previous version** from backup
4. **Start Lidarr**
5. **Report issue** with logs on [GitHub](https://github.com/RicherTunes/qobuzarr/issues)

## Version Compatibility Matrix

| Qobuzarr | Lidarr Required | .NET Runtime | Notes |
|----------|-----------------|--------------|-------|
| v0.1.x | 2.13.0+ | .NET 8.0 | Current |
| v0.0.14 | 2.13.0+ | .NET 8.0 | Stable |
| v0.0.x | 2.12.0+ | .NET 6.0 | Legacy |

## Breaking Changes Log

| Version | Breaking Change | Migration |
|---------|----------------|-----------|
| v0.0.14 | .NET 8.0 required | Update Lidarr to 2.13+ |
| v0.0.10 | New authentication flow | Re-enter credentials |

## Need Help?

- [Configuration Guide](CONFIGURATION-GUIDE.md)
- [Troubleshooting](TROUBLESHOOTING.md)
- [FAQ](FAQ.md)
- [GitHub Issues](https://github.com/RicherTunes/qobuzarr/issues)
