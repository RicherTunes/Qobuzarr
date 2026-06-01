> ⚠️ Deprecated — this page is superseded by the canonical wiki. See [Configuration Guide](../wiki/Configuration-Guide.md) (or [docs/](../docs/) for deep references).

# Configuration Guide

This guide covers all configuration options for Qobuzarr, helping you optimize the plugin for your needs.

## Quick Configuration

After installing Qobuzarr, you need to configure it in two places in Lidarr:

1. **Indexer Settings** (for searching music)
2. **Download Client Settings** (for downloading music)

## Indexer Configuration

### Required Settings

These settings are required for Qobuzarr to work:

| Setting | Description |
|---------|-------------|
| **App ID** | Your Qobuz application ID from Qobuz developer portal |
| **App Secret** | Your Qobuz application secret |
| **Email** | Your Qobuz account email |
| **Password** | Your Qobuz account password |

### Optional Settings

| Setting | Default | Description |
|---------|---------|-------------|
| **Quality** | 27 | Audio quality: 5=MP3, 6=FLAC-CD, 7=FLAC-HiRes, 27=FLAC-Max |
| **Priority** | 1 | Priority in indexer list (1=highest) |

## Download Client Configuration

### Required Settings

| Setting | Description |
|---------|-------------|
| **Name** | Descriptive name for the download client |
| **Priority** | Download priority (1=highest) |
| **Download Path** | Where to save downloaded music |

### Optional Settings

| Setting | Default | Description |
|---------|---------|-------------|
| **Enable Metadata Embedding** | true | Embed metadata in audio files |
| **Maximum Simultaneous Limit** | 4 | Max concurrent downloads |

## Advanced Configuration

### Environment Variables

For advanced setups, you can use environment variables:

```bash
# Required
QOBUZ_APP_ID="your_app_id"
QOBUZ_APP_SECRET="your_app_secret"

# Optional
QOBUZ_EMAIL="your@email.com"
QOBUZ_PASSWORD="your_password"
QOBUZ_QUALITY="27"
```

### Quality Settings Guide

| Quality | Format | Bitrate | Best For |
|---------|--------|---------|----------|
| 5 | MP3 | 320 kbps | Small file sizes, portable players |
| 6 | FLAC 16/44.1 | 1,411 kbps | CD quality, general listening |
| 7 | FLAC 24/96 | 2,304 kbps | Hi-Res audio, critical listening |
| 27 | FLAC 24/192 | 3,072 kbps | Studio quality, audiophile systems |

### Authentication Methods

#### Email/Password (Recommended)

- Simple to set up
- Automatically refreshes tokens
- Works with all Qobuz subscription levels

#### Token-based

- More secure for long-running deployments
- Requires manual token refresh
- Use for server deployments

### Network Configuration

#### Proxy Settings

If you need to use a proxy:

```json
{
  "proxy": {
    "host": "proxy.example.com",
    "port": 8080,
    "username": "user",
    "password": "pass"
  }
}
```

#### Timeout Settings

| Timeout | Default | Description |
|---------|---------|-------------|
| Connection Timeout | 30 seconds | Time to establish connection |
| Read Timeout | 120 seconds | Time to read response |
| Overall Timeout | 300 seconds | Total request time |

## Performance Tuning

### Cache Settings

- **Response Cache**: Reduces API calls significantly
- **TTL**: 24 hours (can be adjusted)
- **Size**: 10,000 entries (good for large libraries)

### Rate Limiting

- **Default**: Adaptive based on API responses
- **Maximum**: 60 requests per minute
- **Burst Limit**: 120 requests during peak times

### Memory Optimization

- **Baseline Usage**: ~200MB
- **Peak Usage**: ~500MB during batch operations
- **Recommendation**: 2GB RAM minimum for large libraries

## Security Configuration

### Credential Security

- **Storage**: Encrypted in Lidarr's database
- **Transmission**: Always over HTTPS
- **Logging**: Credentials are never logged

### API Security

- **HTTPS Only**: All communication encrypted
- **Certificate Validation**: Enabled by default
- **Request Signing**: Prevents tampering

### File Security

- **Path Validation**: Prevents directory traversal
- **Filename Sanitization**: Removes dangerous characters
- **Permission Checking**: Validates write permissions

## Docker Configuration

### Docker Compose Example

```yaml
services:
  lidarr:
    image: ghcr.io/hotio/lidarr:latest
    volumes:
      - ./config:/config
      - music:/music
    environment:
      - PUID=1000
      - PGID=1000
      - UMASK=022
    environment_files:
      - qobuzarr.env
```

### Environment File (qobuzarr.env)

```bash
QOBUZ_APP_ID=your_app_id
QOBUZ_APP_SECRET=your_app_secret
QOBUZ_EMAIL=your@email.com
QOBUZ_PASSWORD=your_password
QOBUZ_QUALITY=27
```

## Troubleshooting Configuration

### Common Issues

**Authentication Failed**

- Verify App ID and Secret are correct
- Check Qobuz subscription is active
- Ensure credentials are not expired

**No Search Results**

- Verify indexer is enabled
- Check quality settings match your subscription
- Test API connectivity

**Downloads Failing**

- Check download path permissions
- Verify sufficient disk space
- Check download client priority

### Configuration Validation

Use the CLI to test configuration:

```bash
# Test authentication
qobuz auth login

# Test search
qobuz search "Artist Name"

# Test download
qobuz download album 12345
```

## Backup and Migration

### Backup Configuration

1. Export Lidarr settings
2. Backup plugin configuration files
3. Document custom settings

### Migration Guide

1. Export old configuration
2. Install new version
3. Import configuration
4. Verify all settings

---

For more detailed information, see the [Troubleshooting Guide](Troubleshooting.md) or [Usage Examples](Usage-Examples.md).
