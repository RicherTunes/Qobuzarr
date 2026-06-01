> ⚠️ Deprecated — this page is superseded by the canonical wiki. See [Configuration Guide](../../../wiki/Configuration-Guide.md) (or [docs/](../../) for deep references).

# Configuration Guide

This guide covers comprehensive configuration of Qobuzarr for optimal performance with your Qobuz subscription and Lidarr setup.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Obtaining Qobuz Credentials](#obtaining-qobuz-credentials)
- [Indexer Configuration](#indexer-configuration)
- [Download Client Configuration](#download-client-configuration)
- [Quality Settings](#quality-settings)
- [Advanced Configuration](#advanced-configuration)
- [Environment Variables](#environment-variables)
- [Troubleshooting Configuration](#troubleshooting-configuration)

## Prerequisites

### Required Information

Before configuring Qobuzarr, gather:

- **Qobuz Email & Password**: Your account credentials
- **Qobuz App ID & Secret**: API credentials (see below)
- **Subscription Type**: Determines available quality levels
- **Download Path**: Where music files will be stored

### Subscription Requirements

| Subscription | Max Quality | Format | Description |
|--------------|-------------|--------|--------------|
| **Hi-Fi** | 16-bit/44.1kHz | FLAC | CD quality |
| **Studio Premier** | 24-bit/192kHz | FLAC | Hi-Res quality |

⚠️ **Important**: Studio Premier subscription required for Hi-Res audio (quality 7 and 27)

## Obtaining Qobuz Credentials

### Method 1: Official API Access (Recommended)

For production use, obtain official API credentials:

1. **Contact Qobuz Developer Support**:
   - Email: <api@qobuz.com>
   - Include: Business use case, expected volume
   - Wait for approval and credential assignment

2. **Alternative Sources** (Community):
   - Check community forums for shared development credentials
   - Use with caution - may have rate limits or restrictions

### Method 2: Extract from Apps

⚠️ **Advanced Users Only**: Extract from legitimate Qobuz applications

```bash
# This method requires technical expertise
# and should only be used for personal, non-commercial purposes
# Always respect Qobuz Terms of Service
```

### Method 3: Environment Variables (Development)

For testing and development:

```bash
export QOBUZ_APP_ID="your_app_id"
export QOBUZ_APP_SECRET="your_app_secret"
export QOBUZ_EMAIL="your@email.com"
export QOBUZ_PASSWORD="your_password"
```

## Indexer Configuration

### Basic Indexer Setup

1. **Navigate to Lidarr Settings**:
   - Settings → Indexers → Add → Qobuzarr

2. **Configure Required Fields**:

   ```json
   {
     "name": "Qobuzarr",
     "enable": true,
     "appId": "your_qobuz_app_id",
     "appSecret": "your_qobuz_app_secret",
     "email": "your_qobuz_email",
     "password": "your_qobuz_password"
   }
   ```

3. **Quality Selection**:

   ```json
   {
     "audioQuality": "27",  // 27=Max, 7=Hi-Res, 6=CD, 5=MP3
     "enableQualityFallback": true
   }
   ```

### Advanced Indexer Settings

```json
{
  "enableRss": true,
  "enableAutomaticSearch": true,
  "enableInteractiveSearch": true,
  "supportsRecentFeed": false,
  "priority": 25,
  "downloadClientId": 0,
  "seedCriteria": {
    "seedRatio": 0,
    "seedTime": 0
  }
}
```

**Field Descriptions**:

- **Priority**: Lower numbers = higher priority (0-50)
- **Enable RSS**: Not applicable for Qobuz (streaming service)
- **Enable Automatic Search**: Allows Lidarr to search automatically
- **Enable Interactive Search**: Manual search capability

### ML Optimization Settings

```json
{
  "enableMLOptimization": true,
  "mlOptimizationLevel": "High",<!-- TODO(docval): mlOptimizationLevel not found in code as of 2026-05-31 - use QueryOptimizationMode instead -->
  "enableQuerySimplification": true,<!-- TODO(docval): enableQuerySimplification not found in code as of 2026-05-31 -->
  "enablePatternLearning": true,<!-- TODO(docval): enablePatternLearning not found in code as of 2026-05-31 -->
  "cacheStrategy": "Aggressive"<!-- TODO(docval): cacheStrategy not found in code as of 2026-05-31 -->
}
```

**Optimization Levels**:

- **Conservative**: Basic optimization, maximum compatibility
- **Balanced**: Good balance of performance and accuracy
- **High**: Maximum optimization (~49% API call reduction)

## Download Client Configuration

### Basic Download Client Setup

1. **Navigate to Download Clients**:
   - Settings → Download Clients → Add → Qobuzarr

2. **Configure Basic Settings**:

   ```json
   {
     "name": "Qobuzarr Downloader",
     "enable": true,
     "host": "localhost",
     "port": 8080,
     "useSsl": false,
     "priority": 1
   }
   ```

3. **Download Configuration**:

   ```json
   {
     "musicDirectory": "/music",<!-- TODO(docval): musicDirectory not found - use DownloadPath instead -->
     "audioQuality": "27",
     "enableMetadataEmbedding": true,<!-- TODO(docval): enableMetadataEmbedding not found in code as of 2026-05-31 --><!-- TODO(docval): enableMetadataEmbedding not found in code as of 2026-05-31 -->
     "enableCoverArtDownload": true<!-- TODO(docval): enableCoverArtDownload not found in code as of 2026-05-31 -->,<!-- TODO(docval): enableCoverArtDownload not found in code as of 2026-05-31 -->
     "fileNamingPattern": "{Artist} - {Album} ({Year}) [{Quality}]"<!-- TODO(docval): fileNamingPattern not found in code as of 2026-05-31 -->
   }
   ```

### Advanced Download Settings

```json
{
  "concurrentDownloads": 3,
  "retryAttempts": 3,
  "retryDelay": 5000,
  "enableProgressReporting": true,
  "enableDuplicateDetection": true,
  "duplicateAction": "Skip",
  "enablePostProcessing": true
}
```

**Performance Settings**:

- **Concurrent Downloads**: Number of simultaneous downloads (1-10)
- **Retry Attempts**: Number of retry attempts on failure
- **Retry Delay**: Delay between retries (milliseconds)

### File Organization

```json
{
  "createArtistFolders": true,
  "createAlbumFolders": true,
  "artistFolderFormat": "{Artist}",
  "albumFolderFormat": "{Album} ({Year})",
  "trackFileFormat": "{TrackNumber:00} - {Title}"
}
```

## Quality Settings

### Quality Hierarchy

Configure quality preferences in Lidarr:

| Priority | Quality ID | Format | Bitrate | Description |
|----------|------------|--------|---------|-------------|
| 1 | 27 | FLAC | 24-bit/192kHz | Studio Master |
| 2 | 7 | FLAC | 24-bit/96kHz | Hi-Res |
| 3 | 6 | FLAC | 16-bit/44.1kHz | CD Quality |
| 4 | 5 | MP3 | 320kbps | High MP3 |

### Quality Profile Configuration

```json
{
  "name": "Qobuz Hi-Res",
  "upgradable": true,
  "cutoff": 7,
  "qualities": [
    {"id": 27, "allowed": true, "name": "FLAC 192kHz"},
    {"id": 7, "allowed": true, "name": "FLAC Hi-Res"},
    {"id": 6, "allowed": true, "name": "FLAC CD"},
    {"id": 5, "allowed": false, "name": "MP3 320"}
  ]
}
```

### Format Configuration

```json
{
  "preferredFormats": {
    "FLAC": 100,
    "MP3": 0
  },
  "formatPriority": [
    "FLAC 24-bit/192kHz",
    "FLAC 24-bit/96kHz", 
    "FLAC 16-bit/44.1kHz",
    "MP3 320kbps"
  ]
}
```

## Advanced Configuration

### Caching Configuration

```json
{
  "enableCaching": true,<!-- TODO(docval): enableCaching not found in code as of 2026-05-31 - use SearchCacheDuration instead -->
  "cacheSize": "500MB",<!-- TODO(docval): cacheSize not found in code as of 2026-05-31 -->
  "cacheTTL": {
    "searchResults": "4h",
    "albumMetadata": "24h",
    "sessionTokens": "24h",
    "mlPatterns": "7d"
  },
  "cacheEvictionStrategy": "LRU"<!-- TODO(docval): cacheEvictionStrategy not found in code as of 2026-05-31 -->
}
```

### Security Configuration

```json
{
  "enableEncryption": true,
  "encryptionKey": "auto-generate",
  "enableSecureStorage": true,
  "sessionTimeout": "24h",
  "enableAuditLogging": true
}
```

### Performance Tuning

```json
{
  "apiRequestTimeout": 30000,
  "maxRetries": 3,
  "rateLimitDelay": 1000,
  "enableConnectionPooling": true,
  "maxConcurrentConnections": 10,
  "enableGzipCompression": true
}
```

### Logging Configuration

```json
{
  "logLevel": "Info",
  "enableFileLogging": true,
  "logRotation": {
    "maxFileSize": "50MB",
    "maxFiles": 5
  },
  "enablePerformanceLogging": true
}
```

## Environment Variables

### Development/Testing Variables

```bash
# Authentication
export QOBUZ_APP_ID="your_app_id"
export QOBUZ_APP_SECRET="your_app_secret"
export QOBUZ_EMAIL="your@email.com"
export QOBUZ_PASSWORD="your_password"

# Quality Settings
export QOBUZ_QUALITY="27"  # 5=MP3-320, 6=FLAC-CD, 7=FLAC-Hi-Res, 27=FLAC-Max

# Performance
export QOBUZ_CACHE_SIZE="500MB"<!-- TODO(docval): QOBUZ_CACHE_SIZE not found in code as of 2026-05-31 -->
export QOBUZ_CONCURRENT_DOWNLOADS="3"<!-- TODO(docval): QOBUZ_CONCURRENT_DOWNLOADS not found in code as of 2026-05-31 -->
export QOBUZ_REQUEST_TIMEOUT="30000"

# Security
export QOBUZ_ENABLE_ENCRYPTION="true"<!-- TODO(docval): QOBUZ_ENABLE_ENCRYPTION not found in code as of 2026-05-31 -->
export QOBUZ_SESSION_TIMEOUT="86400"  # 24 hours in seconds

# Development
export QOBUZ_DEBUG_MODE="false"<!-- TODO(docval): QOBUZ_DEBUG_MODE not found in code as of 2026-05-31 -->
export QOBUZ_LOG_LEVEL="Info"
```

### Production Environment Variables

```bash
# Production settings
export QOBUZ_ENVIRONMENT="Production"
export QOBUZ_ENABLE_TELEMETRY="true"
export QOBUZ_PERFORMANCE_MONITORING="true"

# Security hardening
export QOBUZ_ENFORCE_HTTPS="true"
export QOBUZ_ENABLE_AUDIT_LOGGING="true"
export QOBUZ_ENCRYPT_CACHE="true"

# Performance optimization
export QOBUZ_ENABLE_ML_OPTIMIZATION="true"
export QOBUZ_ML_OPTIMIZATION_LEVEL="High"
export QOBUZ_CACHE_STRATEGY="Aggressive"
```

## Troubleshooting Configuration

### Authentication Issues

```bash
# Test credentials
curl -X POST "https://www.qobuz.com/api.json/0.2/user/login" \
  -d "app_id=YOUR_APP_ID" \
  -d "email=YOUR_EMAIL" \
  -d "password=YOUR_PASSWORD"

# Expected response: {"user_auth_token": "...", ...}
```

### API Connectivity Issues

```bash
# Test API connectivity
telnet www.qobuz.com 443

# Test DNS resolution
nslookup www.qobuz.com

# Test proxy settings (if applicable)
curl -x proxy:port https://www.qobuz.com/api.json/0.2/album/get
```

### Quality Issues

**Subscription Verification**:

```json
{
  "error": "Quality not available",
  "message": "Requested quality requires Studio Premier subscription",
  "available_qualities": [5, 6],
  "requested_quality": 27
}
```

**Solution**: Downgrade quality setting or upgrade subscription

### Performance Issues

```json
{
  "performance_metrics": {
    "api_response_time": "2000ms",  // Should be < 500ms
    "cache_hit_rate": "45%",        // Should be > 90%
    "download_speed": "1MB/s"       // Depends on connection
  },
  "recommendations": [
    "Enable ML optimization",
    "Increase cache size", 
    "Reduce concurrent downloads"
  ]
}
```

### Validation Commands

```bash
# Validate configuration
dotnet run --project QobuzCLI -- validate-config<!-- TODO(docval): validate-config command not found in code as of 2026-05-31 -->

# Test authentication
dotnet run --project QobuzCLI -- auth status

# Test search functionality
dotnet run --project QobuzCLI -- search "Pink Floyd Dark Side of the Moon" --limit 5

# Test download capability
dotnet run --project QobuzCLI -- download album 123456 --dry-run
```

## Configuration Templates

### Minimal Configuration

```json
{
  "indexer": {
    "appId": "required",
    "appSecret": "required", 
    "email": "required",
    "password": "required",
    "audioQuality": "6"
  },
  "downloadClient": {
    "enabled": true,
    "priority": 1
  }
}
```

### Optimal Configuration

```json
{
  "indexer": {
    "appId": "your_app_id",
    "appSecret": "your_app_secret",
    "email": "your@email.com",
    "password": "your_password",
    "audioQuality": "27",
    "enableMLOptimization": true,
    "mlOptimizationLevel": "High"
  },
  "downloadClient": {
    "enabled": true,
    "priority": 1,
    "concurrentDownloads": 3,
    "enableMetadataEmbedding": true,
    "enableCoverArtDownload": true
  },
  "caching": {
    "enableCaching": true,<!-- TODO(docval): enableCaching not found in code as of 2026-05-31 - use SearchCacheDuration instead -->
    "cacheSize": "500MB",<!-- TODO(docval): cacheSize not found in code as of 2026-05-31 -->
    "cacheStrategy": "Aggressive"
  }
}
```

## Next Steps

After configuration:

1. **[First Download Guide](First-Download.md)** - Test your setup
2. **[Features Overview](../user-guide/Features-Overview.md)** - Explore available features
3. **[Performance Tuning](../advanced/Performance-Tuning.md)** - Optimize for your environment
4. **[Troubleshooting](../user-guide/Troubleshooting.md)** - Common issues and solutions

---

**Configuration complete!** Your Qobuzarr plugin should now be ready to use with optimized settings for your environment.
