# Installation Guide

This guide provides comprehensive instructions for installing Qobuzarr in various environments.

## 📋 Prerequisites

### System Requirements

- **Lidarr**: v3.0.0 or higher (plugins branch — `pr-plugins-3.x`)
- **.NET Runtime**: 8.0 or higher
- **Operating System**: Linux, Windows, or macOS
- **Memory**: Minimum 512MB RAM available
- **Storage**: 100MB for plugin files, additional space for downloads

### Qobuz Account Requirements

- **Qobuz Subscription**: Active subscription required
  - **Studio**: CD quality FLAC (16-bit/44.1kHz)
  - **Sublime+**: Hi-Res FLAC (up to 24-bit/192kHz)
- **Valid Credentials**: Email and password for authentication

### Network Requirements

- **Internet Connection**: Stable broadband connection
- **API Access**: Outbound access to `*.qobuz.com`
- **Ports**: No inbound ports required

## 🐳 Docker Installation (Recommended)

### Using Hotio's Pre-built Image

The simplest way to run Qobuzarr is using Hotio's Lidarr image with plugin support:

```bash
# Pull the plugins-enabled image
docker pull ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913

# Run with Qobuzarr support
docker run -d \
  --name=lidarr-qobuzarr \
  -e PUID=1000 \
  -e PGID=1000 \
  -e TZ=America/New_York \
  -p 8686:8686 \
  -v /path/to/config:/config \
  -v /path/to/music:/music \
  -v /path/to/downloads:/downloads \
  --restart unless-stopped \
  ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913
```

### Docker Compose Setup

Create a `docker-compose.yml` file:

```yaml
version: '3.8'
services:
  lidarr:
    image: ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913
    container_name: lidarr-qobuzarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=America/New_York
    volumes:
      - /path/to/config:/config
      - /path/to/music:/music
      - /path/to/downloads:/downloads
    ports:
      - 8686:8686
    restart: unless-stopped
```

Start the services:

```bash
docker-compose up -d
```

### Manual Plugin Installation in Docker

If using a standard Lidarr image, install the plugin manually:

```bash
# Download the latest *-net8.0.zip release asset from:
# https://github.com/RicherTunes/qobuzarr/releases/latest

# Copy to container
docker cp qobuzarr-VERSION-net8.0.zip lidarr-container:/tmp/
# Plugin DLLs MUST live under /config/plugins/<Owner>/<Name>/ — Lidarr's loader scans
# /config/plugins/RicherTunes/Qobuzarr/ for Lidarr.Plugin.*.dll; a flat /config/plugins/ is NOT scanned.
docker exec lidarr-container mkdir -p /config/plugins/RicherTunes/Qobuzarr
docker exec lidarr-container unzip /tmp/qobuzarr-VERSION-net8.0.zip -d /config/plugins/RicherTunes/Qobuzarr/
docker restart lidarr-container
```

## 🖥️ Native Installation

### Linux Installation

#### Debian/Ubuntu

```bash
# Install .NET 8.0 runtime
sudo apt update
sudo apt install -y dotnet-runtime-8.0

# Create plugins directory
sudo mkdir -p /var/lib/lidarr/plugins

# Download the latest *-net8.0.zip release asset, then install it
cd /tmp
sudo unzip qobuzarr-VERSION-net8.0.zip -d /var/lib/lidarr/plugins/RicherTunes/Qobuzarr/

# Set permissions
sudo chown -R lidarr:lidarr /var/lib/lidarr/plugins
sudo chmod -R 755 /var/lib/lidarr/plugins

# Restart Lidarr
sudo systemctl restart lidarr
```

#### CentOS/RHEL/Fedora

```bash
# Install .NET 8.0 runtime
sudo dnf install -y dotnet-runtime-8.0

# Create plugins directory
sudo mkdir -p /var/lib/lidarr/plugins

# Download the latest *-net8.0.zip release asset, then install it
cd /tmp
sudo unzip qobuzarr-VERSION-net8.0.zip -d /var/lib/lidarr/plugins/RicherTunes/Qobuzarr/

# Set permissions
sudo chown -R lidarr:lidarr /var/lib/lidarr/plugins

# Restart Lidarr
sudo systemctl restart lidarr
```

#### Arch Linux

```bash
# Install .NET runtime
sudo pacman -S dotnet-runtime

# Install plugin
# Download the latest *-net8.0.zip release asset first.
sudo unzip qobuzarr-VERSION-net8.0.zip -d /usr/share/lidarr/plugins/RicherTunes/Qobuzarr/
sudo systemctl restart lidarr
```

### Windows Installation

#### Using PowerShell (Administrator)

```powershell
# Download the latest *-net8.0.zip release asset from:
# https://github.com/RicherTunes/qobuzarr/releases/latest

# Extract to plugins directory
$pluginPath = "$env:ProgramData\Lidarr\plugins\RicherTunes\Qobuzarr"
New-Item -ItemType Directory -Force -Path $pluginPath
Expand-Archive -Path "$env:TEMP\qobuzarr-VERSION-net8.0.zip" -DestinationPath $pluginPath -Force

# Restart Lidarr service
Restart-Service -Name "Lidarr"
```

#### Manual Installation

1. Download the `*-net8.0.zip` asset from [GitHub Releases](https://github.com/RicherTunes/qobuzarr/releases/latest)
2. Extract the zip file
3. Copy contents to `%ProgramData%\Lidarr\plugins\`
4. Restart Lidarr from Services

### macOS Installation

```bash
# Install .NET runtime (if not already installed)
brew install dotnet

# Create plugins directory
mkdir -p ~/.config/Lidarr/plugins

# Download the latest *-net8.0.zip release asset, then install it
cd /tmp
unzip qobuzarr-VERSION-net8.0.zip -d ~/.config/Lidarr/plugins/RicherTunes/Qobuzarr/

# Restart Lidarr
brew services restart lidarr
```

## 📦 Manual Build Installation

For development or custom builds:

### Build from Source

```bash
# Clone the repository
git clone https://github.com/RicherTunes/qobuzarr.git
cd qobuzarr

# Run setup script
chmod +x setup.sh && ./setup.sh

# Build the plugin
./build.sh --deploy
```

### Windows Build

```powershell
# Clone and setup
git clone https://github.com/RicherTunes/qobuzarr.git
cd qobuzarr
.\setup.ps1

# Build
.\build.ps1 -Deploy
```

## ☸️ Kubernetes Installation

### Deployment with Plugin Support

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: lidarr-qobuzarr
  labels:
    app: lidarr
spec:
  replicas: 1
  selector:
    matchLabels:
      app: lidarr
  template:
    metadata:
      labels:
        app: lidarr
    spec:
      containers:
      - name: lidarr
        image: ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913
        ports:
        - containerPort: 8686
        env:
        - name: PUID
          value: "1000"
        - name: PGID
          value: "1000"
        - name: TZ
          value: "UTC"
        volumeMounts:
        - name: config
          mountPath: /config
        - name: music
          mountPath: /music
        - name: downloads
          mountPath: /downloads
      volumes:
      - name: config
        persistentVolumeClaim:
          claimName: lidarr-config
      - name: music
        persistentVolumeClaim:
          claimName: lidarr-music
      - name: downloads
        persistentVolumeClaim:
          claimName: lidarr-downloads
---
apiVersion: v1
kind: Service
metadata:
  name: lidarr-service
spec:
  selector:
    app: lidarr
  ports:
  - protocol: TCP
    port: 8686
    targetPort: 8686
  type: LoadBalancer
```

Apply the configuration:

```bash
kubectl apply -f lidarr-qobuzarr.yaml
```

## 🏥 Installation Verification

### Check Plugin Loading

1. **Navigate to Lidarr Web UI**: `http://your-server:8686`
2. **Go to System → Status**
3. **Verify plugin status**:
   - Plugin should appear in the list
   - Status should be "Loaded" or "Active"
   - No error messages

### Plugin File Verification

Ensure these files exist in your plugins directory:

```
plugins/
├── Lidarr.Plugin.Qobuzarr.dll      # Main plugin assembly
├── plugin.json                     # Plugin manifest
├── ml-baseline-patterns.json       # ML optimization patterns
└── Newtonsoft.Json.dll             # Dependencies (if required)
```

### Test API Connection

1. **Go to Settings → Indexers**
2. **Add New Indexer → Qobuzarr**
3. **Configure authentication**
4. **Click "Test" button**
5. **Verify successful connection**

## 🔧 Post-Installation Configuration

### Initial Setup Steps

1. **Configure Authentication**: See [[Configuration Guide]] for detailed setup
2. **Set Quality Preferences**: Configure quality filters in indexer settings
3. **Test Search Functionality**: Perform a manual search to verify operation
4. **Configure Download Client** (if available): Set up download preferences

### Environment Variables

No environment variables are required for production use. All settings are configured through the Lidarr UI. The only supported environment variables are optional App ID / App Secret overrides — see the [Configuration Guide](Configuration-Guide.md) for details.

## 🐛 Troubleshooting Installation

### Common Issues

#### Plugin Not Loading

```bash
# Check file permissions
ls -la /config/plugins/
# Should be readable by Lidarr user

# Check .NET runtime
dotnet --version
# Should be 8.0 or higher (plugin targets net8.0)
```

#### Missing Dependencies

```bash
# Verify all required files are present
ls -la /config/plugins/Lidarr.Plugin.Qobuzarr.*

# Check Lidarr logs for missing assembly errors
tail -f /config/logs/lidarr.txt
```

#### Permission Errors

```bash
# Fix ownership (Linux)
sudo chown -R lidarr:lidarr /config/plugins/
sudo chmod -R 755 /config/plugins/
```

### Log Analysis

Enable debug logging to diagnose issues:

1. **Go to Settings → General**
2. **Set Log Level to "Debug"**
3. **Restart Lidarr**
4. **Check logs**: `/config/logs/lidarr.txt`

Look for these log entries:

```
[Info] PluginService: Loading plugin: Qobuzarr
[Info] QobuzarrPlugin: Plugin initialized successfully
[Error] PluginService: Failed to load plugin: [error details]
```

### Getting Help

If you encounter issues:

1. **Check [[Troubleshooting]] wiki page**
2. **Review [GitHub Issues](https://github.com/RicherTunes/qobuzarr/issues)**
3. **Join [Discord Support](https://discord.gg/lidarr)**
4. **Create new issue with logs**

## 🚀 Next Steps

After successful installation:

1. **[[Configuration Guide]]** - Configure authentication and settings
2. **[[API Reference]]** - Understand plugin capabilities

## 📈 Performance Optimization

### Resource Allocation

Recommended system resources:

- **Memory**: 1GB+ RAM for optimal performance
- **CPU**: 2+ cores recommended for concurrent operations
- **Storage**: SSD recommended for cache performance
- **Network**: 10+ Mbps for smooth streaming downloads

### Cache Configuration

Caching is built in and configured automatically. No environment variables are needed — cache behaviour can be adjusted through the Lidarr UI (see **Settings → Indexers → Qobuzarr**).

---

*Installation complete! Your Qobuzarr plugin is now ready for configuration and use.*
