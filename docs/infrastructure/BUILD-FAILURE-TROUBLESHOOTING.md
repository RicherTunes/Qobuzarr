# Build Failure Troubleshooting Guide

## Overview

This guide helps diagnose and resolve build failures in the Qobuzarr CI/CD pipeline. The project uses multiple build methods for maximum compatibility validation.

## CI Status

**Primary CI**: Gitea (`.gitea/workflows/ci.yml`) — three jobs:

- **`CI / secret-scan`**: Gitleaks scan with pinned archive checksum verification.
- **`CI / lint`**: Fast ecosystem gates (date-parsing, sync-over-async, ecosystem-parity scripts from Common).
- **`CI / verify`**: Full pipeline — host-assembly extraction from Docker, build, ILRepack package, packaging-closure check, deterministic test suite — via `pwsh scripts/verify-local.ps1`.

Check CI status for a commit or PR in the Gitea Actions UI on the self-hosted instance.

## Common Build Issues & Solutions

### **Issue 1: Docker Build Method Failures**

**Symptoms**:

- Docker build fails with assembly errors
- Missing assembly references in temporary project
<!-- TODO(docval): Qobuzarr.Docker.csproj not found in codebase as of 2026-05-31 -->

**Root Causes**:

1. **Missing Dependencies**: Temporary project file missing new package references
2. **Assembly Extraction**: Docker container assemblies not properly extracted
3. **Version Mismatches**: Plugin dependencies not matching Docker assemblies

**Solutions**:

**1. Update Temporary Project File**:

```xml
<!-- Always include ALL current dependencies in Docker build -->
<PackageReference Include="Serilog" />
<PackageReference Include="Serilog.Sinks.File" />
<PackageReference Include="Serilog.Formatting.Compact" />
```

**2. Verify Docker Assembly Extraction**:

```bash
# Check assemblies were extracted
if [ ! -d "ext/Lidarr-docker/_output/net8.0" ]; then
  echo "❌ Error: Docker assemblies not extracted"
  exit 1
fi
```

**3. Comprehensive Error Handling**:

```bash
# Build with error checking
if ! dotnet build Qobuzarr.csproj; then
  echo "❌ Docker build failed - check dependencies"
  exit 1
fi
```

### **Issue 2: Documentation Deployment Failures**

**Symptoms**:

- "GitHub Pages not enabled" errors
- "HttpError: Not Found" when setting up Pages

**Root Cause**:
GitHub Pages feature not enabled in repository settings

**Solutions**:

**1. Enable GitHub Pages** (Repository Owner):

- Go to Repository Settings → Pages
- Set Source to "GitHub Actions"
- Save configuration

**2. Robust Workflow** (Already Implemented):

```yaml
- name: Setup Pages
  continue-on-error: true  # Don't fail entire workflow
  
- name: Deploy to GitHub Pages
  if: steps.pages-setup.outcome == 'success'  # Only deploy if Pages enabled
```

**3. Informative Fallback**:

```bash
if [ pages not enabled ]; then
  echo "📝 Documentation built successfully, Pages setup pending"
  echo "🔧 Enable in repository settings when ready"
fi
```

### **Issue 3: Assembly Version Conflicts**

**Symptoms**:

- TagLibSharp binding redirect warnings
- Assembly version mismatch errors

**Root Cause**:
Multiple TagLibSharp versions in dependency chain

**Solution**:

```xml
<!-- Already handled in main project -->
<PackageReference Include="TagLibSharp-Lidarr" Version="2.2.0.27" />
```

**Note**: Warnings are expected and don't affect functionality.

### **Issue 4: Sentry API Warnings**

**Symptoms**:

- "Sentry API request failed" warnings during Lidarr source builds

**Root Cause**:
Lidarr source code includes Sentry integration that tries to contact Sentry servers

**Solution**:

- **Status**: ✅ **Harmless** - warnings only, don't affect build
- **Impact**: None on plugin functionality
- **Action**: No action needed - expected behavior

## Build Method Comparison

### **Primary CI: Gitea (`CI / secret-scan` + `CI / lint` + `CI / verify`)**

```yaml
# Workflow: .gitea/workflows/ci.yml
# lint job: ecosystem gates (date-parsing, sync-over-async, ecosystem-parity)
# verify job: Docker-extract → build → ILRepack → closure check → tests
# Local equivalent: pwsh scripts/verify-local.ps1
```

**Advantages**:

- ✅ Self-hosted runner — no billing limits
- ✅ Same local-ci.ps1 pipeline developers use locally
- ✅ Docker-extracted host assemblies match production runtime
- ✅ Packaging-closure check prevents missing-DLL deploy bugs

**When to Use**: Default — runs automatically on every push and PR

### **Alternative Method: Docker**

```yaml
# Uses: Docker-extracted assemblies from hotio/lidarr:pr-plugins
# Status: 🔧 Requires maintenance
# Purpose: Plugins branch compatibility validation
```

**Advantages**:

- ✅ Tests plugins branch compatibility
- ✅ Uses actual runtime assemblies
- ✅ Validates deployment compatibility

**Maintenance Required**:

- Keep temporary project file synchronized with main project dependencies
- Update Docker image version when new Lidarr releases available

**When to Use**: Plugins branch compatibility validation

### **Alternative Method: TypNull's Method**

```yaml
# Uses: Pre-built assemblies with specific commit
# Status: ✅ Works reliably
# Purpose: Compatibility validation
```

**Advantages**:

- ✅ Proven reliable approach
- ✅ Good fallback method
- ✅ Specific commit pinning

**When to Use**: Backup build method

## Troubleshooting Steps

### **Step 1: Check Primary CI Status**

Check the Gitea Actions UI for the commit or PR — `CI / secret-scan`, `CI / lint`, and `CI / verify` must all be green.

To reproduce locally (same pipeline CI runs):

```powershell
pwsh scripts/verify-local.ps1
```

If `CI / verify` fails → **Critical issue** (fix immediately); `CI / lint` failure is also critical.
If `CI / verify` times out during assembly extraction → Docker daemon unreachable on runner (infrastructure issue, not a code bug).

### **Step 2: Identify Failure Type**

**Compilation Errors**:

- Check for missing dependencies
- Verify assembly references
- Look for API compatibility issues

**Infrastructure Errors**:

- Check Docker availability
- Verify GitHub Pages configuration
- Check permission issues

### **Step 3: Apply Appropriate Fix**

**For Docker Build**:

1. Update temporary project file with new dependencies
2. Verify Docker image availability
3. Test assembly extraction process

**For Documentation**:

1. Enable GitHub Pages in repository settings
2. Verify workflow permissions
3. Test manual deployment

### **Step 4: Verify Fix**

Run the full local pipeline (same as Gitea's `CI / verify` job):

```powershell
pwsh scripts/verify-local.ps1
```

For a faster rerun when host assemblies are already extracted:

```powershell
pwsh scripts/verify-local.ps1 -SkipExtract
```

Then push; confirm `CI / secret-scan`, `CI / lint`, and `CI / verify` are all green in the Gitea Actions UI.

## Maintenance Schedule

### **Regular Maintenance**

- **Monthly**: Update Docker image version to latest Lidarr
- **When Dependencies Change**: Update temporary Docker project file
- **When Lidarr Updates**: Verify compatibility across all build methods

### **Monitoring**

- Watch for new build failures in any method
- Keep documentation deployment working
- Maintain alternative build methods for robustness

## Emergency Procedures

### **If All Builds Fail**

1. Check main `Qobuzarr.csproj` for syntax errors
2. Verify `Directory.Packages.props` package versions
3. Test local build: `dotnet build Qobuzarr.csproj`
4. Rollback recent changes if necessary

### **If Only Alternative Builds Fail**

1. Document the failure (this guide)
2. Fix when convenient (not critical)
3. Ensure primary build still works

---

**Primary CI**: Gitea (`.gitea/workflows/ci.yml`) — `CI / secret-scan` + `CI / lint` + `CI / verify`
**Local equivalent**: `pwsh scripts/verify-local.ps1`
