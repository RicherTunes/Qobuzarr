# Build Failure Troubleshooting Guide

## Overview

This guide helps diagnose and resolve build failures in the Qobuzarr CI/CD pipeline. The project uses multiple build methods for maximum compatibility validation.

## Build Method Status ✅

### **✅ Primary Build Methods (Always Working)**

- **CI (Simple)**: ✅ Main build pipeline - uses source assemblies
- **Validate**: ✅ Quick validation build
- **Build Plugin (TypNull's Method)**: ✅ Alternative assembly method

### **🔧 Secondary Build Methods (May Need Fixes)**

- **Build Plugin (Docker Method)**: Uses Docker-extracted assemblies
- **Deploy Documentation**: Requires GitHub Pages enabled

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

### **Primary Method: CI (Simple)**

```yaml
# Uses: ext/Lidarr-source assemblies (source build)
# Status: ✅ Always works
# Purpose: Main development and release pipeline
```

**Advantages**:

- ✅ Fastest build time
- ✅ Most reliable
- ✅ Includes all source code
- ✅ Full analyzer support

**When to Use**: Default for all development

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

### **Step 1: Check Primary Build Status**

```bash
# Main build should always work
gh run list --workflow="CI (Simple)" --limit 1
```

If primary build fails → **Critical issue** (fix immediately)
If only alternative builds fail → **Enhancement issue** (fix when convenient)

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

```bash
# Test specific workflow
gh workflow run "Build Plugin (Docker Method)"
gh run watch

# Check all workflows status
gh run list --limit 5
```

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

**Status**: Robust build pipeline with multiple fallback methods  
**Primary Build**: ✅ Always reliable (CI Simple)  
**Alternative Builds**: 🔧 Enhanced with error handling and troubleshooting
