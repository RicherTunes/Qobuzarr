> ⚠️ Deprecated — this page is superseded by the canonical wiki. See [Installation Guide](../../../wiki/Installation-Guide.md) (or [docs/](../../) for deep references).

# First Download Guide

This guide will walk you through completing your first successful download with Qobuzarr to verify your installation and configuration.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Pre-Flight Checks](#pre-flight-checks)
- [Method 1: Automatic Download (Recommended)](#method-1-automatic-download-recommended)
- [Method 2: Manual Search and Download](#method-2-manual-search-and-download)
- [Method 3: CLI Testing](#method-3-cli-testing)
- [Verification](#verification)
- [Troubleshooting First Downloads](#troubleshooting-first-downloads)
- [Next Steps](#next-steps)

## Prerequisites

Before attempting your first download, ensure:

- ✅ Qobuzarr is successfully installed
- ✅ Plugin appears in Lidarr settings
- ✅ Indexer is configured and tested
- ✅ Download client is configured
- ✅ Qobuz credentials are valid
- ✅ Download path is writable

If any of these are not complete, return to the [Installation Guide](Installation-Guide.md) or [Configuration Guide](Configuration.md).

## Pre-Flight Checks

### 1. Verify Plugin Status

1. **Check System Status**:
   - Navigate to System → Status
   - Look for "Qobuzarr" in the plugins section
   - Status should show "Loaded" or "Active"

2. **Check Logs**:

   ```bash
   # Look for successful initialization
   grep -i "qobuz" /path/to/lidarr/logs/lidarr.txt
   ```

   Expected entries:

   ```
   [Info] PluginLoader: Loading plugin Qobuzarr
   [Info] QobuzarrModule: Plugin initialized successfully
   [Info] QobuzIndexer: Authentication successful
   ```

### 2. Test Authentication

1. **Test Indexer Connection**:
   - Settings → Indexers → Qobuzarr
   - Click "Test" button
   - Should return "Connection successful"

2. **Test Download Client**:
   - Settings → Download Clients → Qobuzarr
   - Click "Test" button
   - Should confirm client is responsive

### 3. Verify Download Path

Ensure your download path is accessible and writable:

```bash
# Linux/macOS
ls -la /path/to/download/directory
touch /path/to/download/directory/test.txt && rm /path/to/download/directory/test.txt

# Windows
dir "C:\Path\To\Download\Directory"
echo test > "C:\Path\To\Download\Directory\test.txt" && del "C:\Path\To\Download\Directory\test.txt"
```

## Method 1: Automatic Download (Recommended)

This method tests the full integration workflow.

### Step 1: Add an Artist

1. **Navigate to Artists**:
   - Go to Library → Artists
   - Click "Add New Artist"

2. **Search for a Popular Artist**:
   - Search: "Miles Davis"
   - Select an artist with good Qobuz availability
   - Set quality profile appropriately

3. **Add Artist**:
   - Choose your download path
   - Select quality profile
   - Enable "Start search for missing albums"
   - Click "Add Artist"

### Step 2: Monitor Search Process

1. **Check Activity**:
   - Navigate to Activity → Queue
   - Watch for search activity

2. **Expected Workflow**:

   ```
   [Search Started] → [Qobuzarr Search] → [Results Found] → [Download Added] → [Downloading] → [Complete]
   ```

3. **Monitor Logs**:

   ```bash
   tail -f /path/to/lidarr/logs/lidarr.txt | grep -i qobuz
   ```

### Step 3: Verify Download Progress

1. **Activity Monitoring**:
   - Activity → Queue shows download progress
   - Progress should update regularly

2. **File System Check**:

   ```bash
   # Check if files are being created
   find /download/path -name "*.flac" -o -name "*.mp3" | head -10
   ```

## Method 2: Manual Search and Download

For more control over the testing process.

### Step 1: Manual Album Search

1. **Navigate to Wanted**:
   - Go to Wanted → Missing
   - Or add a specific album manually

2. **Perform Manual Search**:
   - Find an album in your library
   - Click the search icon (🔍)
   - Select "Manual Search"

### Step 2: Review Search Results

1. **Check Qobuzarr Results**:
   - Look for results with "Qobuzarr" indexer
   - Note quality levels available
   - Check file sizes and formats

2. **Expected Results Format**:

   ```
   Artist - Album (Year) [FLAC Hi-Res 24-bit/96kHz]
   Size: 850MB | Quality: 7 | Indexer: Qobuzarr
   ```

### Step 3: Download Selection

1. **Choose Download**:
   - Select highest quality available
   - Click download button
   - Confirm download starts

2. **Monitor Download**:
   - Check Activity → Queue
   - Monitor progress percentage
   - Watch for completion

## Method 3: CLI Testing

For advanced users or troubleshooting.

### Step 1: CLI Authentication

```bash
# Navigate to CLI directory
cd QobuzCLI

# Build CLI (if from source)
dotnet build -c Release

# Test authentication
dotnet run -- auth login
```

Expected output:

```
Authentication successful
Session token: abc123...
Token expires: 2024-XX-XX 23:59:59
```

### Step 2: Search Testing

```bash
# Test search functionality
dotnet run -- search "Pink Floyd Dark Side of the Moon" --limit 5

# Expected output shows album results
```

### Step 3: Download Testing

```bash
# Test download (dry run first)
dotnet run -- download album 12345 --output ./test-downloads --dry-run

# Actual download
dotnet run -- download album 12345 --output ./test-downloads
```

Monitor output for:

- Authentication success
- Album metadata retrieval
- Track listing
- Download progress
- File creation

## Verification

### 1. File Verification

After successful download, verify:

```bash
# Check file structure
tree /download/path/Artist/Album/

# Expected structure:
# Artist/
# └── Album (Year)/
#     ├── 01 - Track Name.flac
#     ├── 02 - Track Name.flac
#     ├── cover.jpg
#     └── folder.jpg
```

### 2. Metadata Verification

Check embedded metadata:

```bash
# Using mediainfo (if available)
mediainfo "/path/to/downloaded/track.flac"

# Using ffprobe
ffprobe -v quiet -print_format json -show_format "/path/to/downloaded/track.flac"
```

Expected metadata:

- Artist, Album, Track names
- Year, Genre
- Quality information
- Album art (if enabled)

### 3. Quality Verification

Verify downloaded quality matches request:

```bash
# Check bitrate and sample rate
soxi "/path/to/downloaded/track.flac"

# For FLAC files, should show:
# Sample Rate: 44100 Hz (CD) or 96000 Hz (Hi-Res) or 192000 Hz (Studio)
# Precision: 16-bit (CD) or 24-bit (Hi-Res/Studio)
```

### 4. Lidarr Integration Verification

1. **Library Update**:
   - Check if album shows as "Downloaded" in Lidarr
   - Verify track files are detected
   - Confirm quality matches profile

2. **History Check**:
   - Activity → History
   - Should show successful download entry
   - Note download source as "Qobuzarr"

## Troubleshooting First Downloads

### Common Issues

**No Search Results**

```bash
# Check indexer logs
grep "QobuzIndexer" /path/to/lidarr/logs/lidarr.txt

# Common causes:
# - Authentication failure
# - Search query too specific
# - Album not available on Qobuz
```

**Authentication Failures**

```json
{
  "error": "Authentication failed",
  "details": "Invalid credentials or expired session",
  "troubleshooting": [
    "Verify email/password combination",
    "Check app_id/app_secret validity",
    "Ensure account has active subscription"
  ]
}
```

**Download Failures**

```bash
# Check download client logs
grep "QobuzDownloadClient" /path/to/lidarr/logs/lidarr.txt

# Common causes:
# - Insufficient disk space
# - Permission issues
# - Network connectivity problems
# - Rate limiting
```

**Quality Issues**

```json
{
  "error": "Quality not available",
  "requested": "FLAC 24-bit/192kHz",
  "available": ["FLAC 16-bit/44.1kHz", "MP3 320kbps"],
  "solution": "Upgrade Qobuz subscription or lower quality setting"
}
```

### Debug Mode

Enable debug logging for troubleshooting:

1. **Lidarr Debug Logs**:
   - Settings → General → Log Level → Debug
   - Restart Lidarr
   - Attempt download
   - Check logs for detailed information

2. **CLI Debug Mode**:

   ```bash
   # Enable verbose logging
   dotnet run -- --verbose search "test album"
   ```

### Support Commands

```bash
# Generate system information
dotnet run -- system-info<!-- TODO(docval): system-info command not found in code as of 2026-05-31 -->

# Test all components
dotnet run -- health-check<!-- TODO(docval): health-check command not found in code as of 2026-05-31 -->

# Export configuration (sanitized)
dotnet run -- export-config --sanitize<!-- TODO(docval): export-config command not found in code as of 2026-05-31 -->
```

## Success Indicators

Your first download was successful if:

- ✅ Search returns results from Qobuzarr indexer
- ✅ Download starts and completes without errors  
- ✅ Files are created in correct directory structure
- ✅ Audio quality matches requested format
- ✅ Metadata is properly embedded
- ✅ Lidarr recognizes downloaded files
- ✅ Album shows as "Downloaded" in library

## Next Steps

After successful first download:

1. **[Features Overview](../user-guide/Features-Overview.md)** - Explore advanced features
2. **[Quality Settings](../user-guide/Quality-Settings.md)** - Optimize audio quality
3. **[Performance Tuning](../advanced/Performance-Tuning.md)** - Scale for larger libraries
4. **[CLI Usage](../user-guide/CLI-Usage.md)** - Learn command-line features

## Celebration! 🎉

Congratulations! You've successfully:

- Installed Qobuzarr
- Configured authentication
- Completed your first download
- Verified the integration works

Your Qobuzarr setup is now ready for production use. Enjoy your high-quality music downloads!

---

**Next**: [Features Overview](../user-guide/Features-Overview.md) →
