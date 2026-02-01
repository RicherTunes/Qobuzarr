# Installation Guide

This guide will walk you through installing and setting up Qobuzarr for use with Lidarr.

## Prerequisites

### System Requirements
- **Lidarr**: Version 2.13.0 or higher
- **.NET Runtime**: .NET 6.0 or higher
- **Qobuz Account**: Active subscription (Studio Premier recommended)
- **Storage**: At least 100MB free space for plugin and cache

### Before You Begin
1. **Backup your Lidarr configuration** before installing any plugins
2. **Stop Lidarr** during installation to prevent conflicts
3. **Verify your Qobuz subscription** includes the quality levels you want to download

## Installation Methods

### Method 1: GitHub Release (Recommended)

1. **Download the Latest Release**
   Visit the [GitHub releases page](https://github.com/RicherTunes/qobuzarr/releases) and download the latest Qobuzarr.zip file.

2. **Extract the Plugin**
   Extract the ZIP file to your Lidarr plugins directory:

   **Windows**:
   ```cmd
   mkdir "%APPDATA%\Lidarr\plugins\RicherTunes\Qobuzarr\"
   drag and drop files from ZIP to this directory
   ```

   **Linux**:
   ```bash
   mkdir -p ~/.config/lidarr/plugins/RicherTunes/Qobuzarr/
   unzip Qobuzarr.zip -d ~/.config/lidarr/plugins/RicherTunes/Qobuzarr/
   ```

   **Docker**:
   ```bash
   unzip Qobuzarr.zip -d /config/plugins/RicherTunes/Qobuzarr/
   ```

### Method 2: Build from Source (For Developers)

For developers who want the latest features:

```bash
# Clone the repository
git clone https://github.com/RicherTunes/qobuzarr.git
cd qobuzarr

# Build the plugin
dotnet build --configuration Release -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false

# Copy to Lidarr plugins directory
cp -r bin/Release/net6.0/* ~/.config/lidarr/plugins/RicherTunes/Qobuzarr/
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
       └── RicherTunes/
           └── Qobuzarr/
               ├── Lidarr.Plugin.Qobuzarr.dll
               ├── plugin.json
               └── [other plugin files]
   ```

3. **Copy plugin files**:
   - Extract all files from the release archive
   - Copy to the `plugins/RicherTunes/Qobuzarr/` directory
   - Ensure `Lidarr.Plugin.Qobuzarr.dll` and `plugin.json` are present

## Initial Configuration

### 1. Start Lidarr

After copying the plugin files, start Lidarr and check the logs for successful plugin loading:

```
[Info] PluginLoader: Loading plugin Qobuzarr from plugins/RicherTunes/Qobuzarr/plugin.json
[Info] QobuzarrModule: Qobuzarr plugin initialized successfully
```

### 2. Configure Indexer

1. Navigate to **Settings** → **Indexers**
2. Click **Add** → **Qobuzarr**
3. Configure the required settings:
   - **App ID**: Your Qobuz application ID
   - **App Secret**: Your Qobuz application secret
   - **Email**: Your Qobuz account email
   - **Password**: Your Qobuz account password
   - **Quality**: Audio quality (default: 27 for FLAC-Max)

### 3. Configure Download Client

1. Navigate to **Settings** → **Download Clients**
2. Click **Add** → **Qobuzarr**
3. Configure download settings:
   - **Name**: Client name (e.g., "Qobuzarr Downloader")
   - **Priority**: Priority in download client list
   - **Download Path**: Location to save downloaded files

## Quality Configuration

| Quality ID | Format | Description |
|------------|--------|-------------|
| 5 | MP3 320kbps | Standard quality |
| 6 | FLAC CD | CD quality |
| 7 | FLAC Hi-Res | Hi-Res quality |
| 27 | FLAC Studio | Studio quality (Studio Premier required) |

## Verification

### Test the Installation

1. **Check Plugin Status**:
   - Go to **System** → **Status**
   - Look for "Qobuzarr" in the plugins section

2. **Test Indexer**:
   - Go to **Wanted** → **Search**
   - Perform a manual search for a known album
   - Check for Qobuzarr results in the search results

3. **Test Download Client**:
   - Add an album to your library
   - Monitor the download progress
   - Verify files are downloaded to the configured path

### Troubleshooting

**Plugin Not Loading**:
- Check Lidarr logs for error messages
- Ensure files are in the correct directory
- Verify file permissions (Linux/macOS)

**Authentication Errors**:
- Verify Qobuz credentials are correct
- Check subscription includes desired quality levels
- Try re-authenticating

**Permission Errors (Linux/macOS)**:
```bash
sudo chown -R lidarr:lidarr ~/.config/lidarr/plugins/
sudo chmod -R 644 ~/.config/lidarr/plugins/RicherTunes/Qobuzarr/*
sudo chmod 755 ~/.config/lidarr/plugins/RicherTunes/Qobuzarr/
```

## Next Steps

After successful installation:

1. **[Configuration Guide](Configuration-Guide.md)** - Set up advanced options
2. **[Usage Examples](Usage-Examples.md)** - Learn common workflows
3. **[CLI Usage](CLI-Usage.md)** - Explore command-line features
4. **[Troubleshooting](Troubleshooting.md)** - Solve common issues

---

**Installation Complete!** Next, configure your settings and start using Qobuzarr with your music library.