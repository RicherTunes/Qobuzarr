# Troubleshooting Guide

This document contains solutions to common issues encountered when building, deploying, and using Qobuzarr.

## Table of Contents

### 🔨 Build & Deployment
- [Build Issues](#build-issues)
  - [Missing Lidarr Assemblies](#missing-lidarr-assemblies)
  - [StyleCop Analyzer Errors](#stylecop-analyzer-errors)
  - [ILRepack Failures](#ilrepack-failures)
- [Plugin Loading Issues](#plugin-loading-issues)
  - [Plugin Not Appearing in Lidarr](#plugin-not-appearing-in-lidarr)
  - [Multiple Plugin Instances](#multiple-plugin-instances)
- [Assembly Version Issues](#assembly-version-issues)
  - [ReflectionTypeLoadException](#reflectiontypeloadexception)
  - [Version Mismatch Debugging](#version-mismatch-debugging)

### 🔐 Authentication & API
- [Authentication Problems](#authentication-problems)
  - [Invalid Credentials](#invalid-credentials)
  - [Token Expiration](#token-expiration)
  - [Subscription Tier Issues](#subscription-tier-issues)
- [API Rate Limiting](#api-rate-limiting)
  - [Too Many Requests Errors](#too-many-requests-errors)
  - [Adaptive Rate Limiting](#adaptive-rate-limiting)

### 📥 Downloads & Search
- [Download Issues](#download-issues)
  - [Failed Downloads](#failed-downloads)
  - [Quality Fallback](#quality-fallback)
  - [Incomplete Albums](#incomplete-albums)
- [Search Problems](#search-problems)
  - [No Results Found](#no-results-found)
  - [Poor Search Quality](#poor-search-quality)

### ⚡ Performance & Optimization
- [Performance Problems](#performance-problems)
  - [High Memory Usage](#high-memory-usage)
  - [Slow Search Performance](#slow-search-performance)
  - [Cache Issues](#cache-issues)
- [ML Optimization](#ml-optimization)
  - [Model Loading Issues](#model-loading-issues)
  - [Prediction Accuracy](#prediction-accuracy)

### 🚀 CI/CD & Development
- [CI/CD Issues](#cicd-issues)
  - [GitHub Actions Failures](#github-actions-failures)
  - [Docker Build Problems](#docker-build-problems)
  - [Test Failures](#test-failures)

## Build Issues

### Missing Lidarr Assemblies
**Symptoms**: Build fails with "Could not find Lidarr.Core" or similar assembly reference errors

**Solution**:
```bash
# Download pre-built assemblies (recommended)
./download-lidarr-assemblies.sh --version 2.13.2.4685
.\download-lidarr-assemblies.ps1 -LidarrVersion "2.13.2.4685"

# Then build
./build.sh --deploy
.\build.ps1 -Deploy
```

### StyleCop Analyzer Errors
**Symptoms**: Build fails with SA1xxx analyzer warnings from Lidarr source code

**Solution**: Always use the analyzer suppression flags:
```bash
dotnet build --configuration Release -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false -p:TreatWarningsAsErrors=false
```

### ILRepack Failures
**Symptoms**: "Failed to resolve assembly: 'Lidarr.Core, Version=2.13.2.4686'"

**Solution**: This is expected if Lidarr assemblies aren't present. The main plugin DLL is still built successfully. For production builds, ensure Lidarr assemblies are available.

## Plugin Loading Issues

### Plugin Not Appearing in Lidarr
**Symptoms**: Plugin installed but not showing in Lidarr's indexer/download client lists

**Checklist**:
1. Verify plugin files in correct location:
   - Windows: `C:\ProgramData\Lidarr\plugins\Qobuzarr\`
   - Linux: `/var/lib/lidarr/plugins/Qobuzarr/`
   - Docker: `/config/plugins/Qobuzarr/`

2. Required files present:
   - `Lidarr.Plugin.Qobuzarr.dll`
   - `plugin.json`
   - All dependency DLLs

3. File permissions (Linux/Docker):
   ```bash
   chmod 755 /path/to/plugins/Qobuzarr
   chmod 644 /path/to/plugins/Qobuzarr/*
   ```

4. **Always restart Lidarr** after deploying plugin

### Multiple Plugin Instances
**Symptoms**: Logs show "SecureMLModelLoader initialized" multiple times

**Explanation**: This is normal. Lidarr creates multiple indexer instances for:
- Configuration validation
- Settings UI
- Search operations
- Health checks
- Background tasks

The plugin uses shared services to minimize memory usage.

## Assembly Version Issues

### ReflectionTypeLoadException
**Symptoms**: Lidarr fails with "Could not load file or assembly 'Lidarr.Core, Version=10.0.0.xxxxx'"

**Root Cause**: Plugin compiled against development Lidarr versions but runtime expects release versions

**Solution**:
1. Ensure using correct Lidarr source commit: `aa7b63f2e13351f54a31d780d6a7b93a2411eaec`
2. Build scripts automatically override assembly version to `2.13.2.4686`
3. Verify `ext/Lidarr-source/src/Directory.Build.props` shows:
   ```xml
   <AssemblyVersion>2.13.2.4686</AssemblyVersion>
   ```

**Prevention**: Always use `./build.sh --deploy` or `.\build.ps1 -Deploy` which include automatic version override

### Version Mismatch Debugging
**Check Runtime Version**: 
```bash
# In Lidarr logs, look for:
[Info] Bootstrap: Starting Lidarr - Version 2.13.2.4686
```

**Check Plugin Version**:
```bash
# PowerShell
.\check-assembly.ps1 bin\Lidarr.Plugin.Qobuzarr.dll

# Linux/Mac
monodis --assembly bin/Lidarr.Plugin.Qobuzarr.dll | grep Version
```

**Important**: Runtime and plugin assembly versions must match exactly

## Authentication Problems

### Invalid Credentials
**Symptoms**: "Authentication failed" or "Invalid app_id/app_secret"

**Solutions**:
1. Verify credentials in Lidarr UI settings
2. Check for special characters that need escaping
3. Ensure subscription is active and valid for your region
4. Try token-based authentication instead of email/password

### Session Expiry
**Symptoms**: Downloads work initially then fail after some time

**Solution**: The plugin automatically refreshes sessions. If issues persist:
1. Check "Force Authentication" in settings
2. Clear cached credentials and re-authenticate
3. Verify your Qobuz subscription hasn't expired

## Download Issues

### Slow Download Speeds
**Symptoms**: Downloads are unusually slow

**Troubleshooting**:
1. Check rate limiting settings (Settings → Indexers → Qobuzarr → API Rate Limit)
2. Verify network connectivity to Qobuz CDN
3. Check if other Qobuz clients have similar issues
4. Review concurrent download limits

### Missing Metadata
**Symptoms**: Downloaded files lack proper tags or album art

**Solution**:
1. Enable "Fetch Full Metadata" in settings
2. Check TagLib-Sharp is properly loaded
3. Verify the album exists in Qobuz catalog
4. Try re-downloading with debug logging enabled

## Performance Problems

### High Memory Usage
**Symptoms**: Plugin consumes excessive memory

**Solutions**:
1. Reduce cache size in settings
2. Lower concurrent download limits
3. Disable ML optimization if not needed
4. Check for memory leaks in logs

### Slow Search Results
**Symptoms**: Searches take too long to complete

**Optimizations**:
1. Enable ML query optimization
2. Adjust search timeout settings
3. Use more specific search terms
4. Check API rate limiting isn't too restrictive

## CI/CD Issues

### GitHub Actions Failures

#### Wrong .NET Version
**Symptoms**: NETSDK1045 errors, tool compatibility issues

**Solution**: Use .NET 8.0 in CI:
```yaml
- uses: actions/setup-dotnet@v3
  with:
    dotnet-version: '8.0.x'
```

#### Assembly Resolution Failures
**Symptoms**: Build succeeds locally but fails in CI

**Solution**: Use pre-built assemblies approach:
```yaml
- name: Download Lidarr Assemblies
  run: ./download-lidarr-assemblies.sh --version 2.13.2.4685
  
- name: Build Plugin
  run: ./build.sh --use-prebuilt
```

#### Package Source Conflicts
**Symptoms**: NU1507, NU1008 NuGet errors

**Solution**: Don't try to build Lidarr source in CI. Use pre-built assemblies or reference packages.

## Getting Help

If your issue isn't covered here:

1. **Check logs**: Enable debug logging in Lidarr
2. **Search existing issues**: [GitHub Issues](https://github.com/richertunes/qobuzarr/issues)
3. **Community support**: Lidarr Discord #plugins channel
4. **Report new issues**: Include:
   - Lidarr version
   - Plugin version
   - Full error messages
   - Steps to reproduce

## Known Limitations

- Plugin requires Lidarr 2.13.0 or higher
- Some regions may have limited Qobuz API access
- Hi-Res downloads require appropriate Qobuz subscription tier
- Rate limiting is enforced by Qobuz API

## Quick Fixes Reference

| Problem | Quick Fix |
|---------|-----------|
| Plugin not loading | Restart Lidarr |
| Auth failures | Re-enter credentials |
| Build errors | Use analyzer suppression flags |
| Version mismatch | Use build scripts with auto-override |
| Slow searches | Enable ML optimization |
| Memory issues | Reduce cache size |
| CI failures | Use .NET 8.0 and pre-built assemblies |

---

For additional help, see [CLAUDE.md](CLAUDE.md) for development guidance or [README.md](README.md) for general information.