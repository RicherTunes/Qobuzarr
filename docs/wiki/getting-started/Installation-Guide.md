> ⚠️ Deprecated — this page is superseded by the canonical wiki. See [Installation Guide](../../../wiki/Installation-Guide.md) (or [docs/](../../) for deep references).

# Installation Guide

This guide will walk you through installing and setting up Qobuzarr for use with Lidarr.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Installation Methods](#installation-methods)
- [Plugin Installation](#plugin-installation)
- [Initial Configuration](#initial-configuration)
- [Verification](#verification)
- [Next Steps](#next-steps)

## Prerequisites

### System Requirements

- **Lidarr**: Version 2.13.0 or higher
- **.NET Runtime**: .NET 8.0 or higher
- **Qobuz Account**: Active subscription (Studio Premier recommended)
- **Storage**: At least 100MB free space for plugin and cache

### Before You Begin

1. **Backup your Lidarr configuration** before installing any plugins
2. **Stop Lidarr** during installation to prevent conflicts
3. **Verify your Qobuz subscription** includes the quality levels you want to download

## Installation Methods

### Method 1: GitHub Release (Recommended)

1. **Download the Latest Release**

   ```bash
   # Visit GitHub releases page
   https://github.com/RicherTunes/qobuzarr/releases
   
   # Download the latest Qobuzarr.zip file
   ```

2. **Extract the Plugin**

   ```bash
   # Extract to Lidarr plugins directory
   # Windows: %APPDATA%\Lidarr\plugins\Qobuzarr\
   # Linux: ~/.config/Lidarr/plugins/Qobuzarr/
   # Docker: /config/plugins/Qobuzarr/
   ```

### Method 2: Build from Source

For developers or advanced users who want the latest features:

```bash
# Clone the repository
git clone https://github.com/RicherTunes/qobuzarr.git
cd qobuzarr

# Run setup script
# Linux/macOS:
chmod +x setup.sh && ./setup.sh

# Windows PowerShell:
.\setup.ps1

# Build the plugin
dotnet build --configuration Release -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false

# Copy to Lidarr plugins directory
# The build output will be in bin/Release/net8.0/
```

## Plugin Installation

### Standard Installation

1. **Locate your Lidarr data directory**:
   - **Windows**: `%APPDATA%\Lidarr` or `C:\ProgramData\Lidarr`
   - **Linux**: `~/.config/Lidarr` or `/var/lib/lidarr`
   - **Docker**: Usually `/config` inside the container

2. **Create the plugin directory structure**:

   ```
   [Lidarr Data Directory]/
   └── plugins/
       └── Qobuzarr/
           ├── Lidarr.Plugin.Qobuzarr.dll
           ├── plugin.json
           └── [other plugin files]
   ```

3. **Copy plugin files**:
   - Extract all files from the release archive
   - Copy to the `plugins/Qobuzarr/` directory
   - Ensure `Lidarr.Plugin.Qobuzarr.dll` and `plugin.json` are present

### Docker Installation

For Docker-based Lidarr installations:

```bash
# Stop the Lidarr container
docker stop lidarr

# Extract plugin to the mounted config directory
unzip Qobuzarr.zip -d /path/to/lidarr/config/plugins/Qobuzarr/

# Start the Lidarr container
docker start lidarr
```

### Verification of Installation

1. **Check file permissions** (Linux/macOS):

   ```bash
   chmod 644 /path/to/lidarr/config/plugins/Qobuzarr/*
   chmod 755 /path/to/lidarr/config/plugins/Qobuzarr/
   ```

2. **Verify plugin structure**:

   ```
   plugins/Qobuzarr/
   ├── Lidarr.Plugin.Qobuzarr.dll     ✓ Main plugin assembly
   ├── plugin.json                    ✓ Plugin manifest
   ├── Newtonsoft.Json.dll            ✓ Dependencies
   └── [other dependencies]
   ```

## Initial Configuration

### 1. Start Lidarr

After copying the plugin files, start Lidarr and check the logs for successful plugin loading:

```
[Info] PluginLoader: Loading plugin Qobuzarr from plugins/Qobuzarr/plugin.json
[Info] QobuzarrModule: Qobuzarr plugin initialized successfully
```

### 2. Configure Indexer

1. Navigate to **Settings** → **Indexers**
2. Click **Add** → **Qobuzarr**
3. Configure the required settings:

```json
{
  "name": "Qobuzarr",
  "enable": true,
  "appId": "your_qobuz_app_id",
  "appSecret": "your_qobuz_app_secret",
  "email": "your_qobuz_email",
  "password": "your_qobuz_password",
  "audioQuality": "27"
}
```

### 3. Configure Download Client

1. Navigate to **Settings** → **Download Clients**
2. Click **Add** → **Qobuzarr**
3. Configure download settings:

```json
{
  "name": "Qobuzarr Downloader",
  "enable": true,
  "priority": 1,
  "downloadPath": "/music/downloads",
  "audioQuality": "27",
  "enableMetadataEmbedding": true
}
```

### 4. Quality Configuration

Configure quality settings to match your Qobuz subscription:

| Quality ID | Format | Bitrate | Description |
|------------|--------|---------|-------------|
| 5 | MP3 | 320 kbps | Standard quality |
| 6 | FLAC | 16-bit/44.1kHz | CD quality |
| 7 | FLAC | 24-bit/96kHz | Hi-Res quality |
| 27 | FLAC | 24-bit/192kHz | Studio quality |

## Verification

### Test the Installation

1. **Check Plugin Status**:
   - Go to **System** → **Status**
   - Look for "Qobuzarr" in the plugins section

2. **Test Indexer**:
   - Go to **Wanted** → **Search**
   - Perform a manual search
   - Check for Qobuzarr results in the search results

3. **Test Download Client**:
   - Add an album to your library
   - Monitor the download progress
   - Verify files are downloaded to the configured path

### Troubleshooting Installation Issues

**Plugin Not Loading**:

```bash
# Check Lidarr logs for errors
tail -f /path/to/lidarr/logs/lidarr.txt

# Common issues:
# - Missing dependencies (.dll files)
# - Incorrect file permissions
# - Plugin directory in wrong location
```

**Authentication Errors**:

- Verify Qobuz credentials are correct
- Check that your subscription includes the requested quality levels
- Ensure API credentials are valid

**Permission Errors** (Linux/macOS):

```bash
# Fix file permissions
sudo chown -R lidarr:lidarr /path/to/lidarr/config/plugins/
sudo chmod -R 644 /path/to/lidarr/config/plugins/Qobuzarr/*
sudo chmod 755 /path/to/lidarr/config/plugins/Qobuzarr/
```

## Next Steps

After successful installation:

1. **[Configure Settings](Configuration-Guide.md)**: Set up advanced configuration options
2. **[First Use Guide](First-Use.md)**: Learn basic operations and workflows  
3. **[Quality Settings](../user-guide/Quality-Settings.md)**: Optimize audio quality settings
4. **[Performance Tuning](../operations/Performance-Tuning.md)**: Optimize for your library size

## Support

If you encounter issues during installation:

1. Check the **[Troubleshooting Guide](../operations/Troubleshooting.md)**
2. Review the **[FAQ](../user-guide/FAQ.md)** for common questions
3. Search **[GitHub Issues](https://github.com/RicherTunes/qobuzarr/issues)** for similar problems
4. Create a new issue if your problem isn't covered

---

**Next**: [Configuration Guide](Configuration-Guide.md) →
