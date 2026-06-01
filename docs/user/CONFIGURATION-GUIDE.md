# Qobuzarr Configuration Guide

This guide covers all configuration options for the Qobuzarr plugin, including initial setup, authentication methods, and advanced settings.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Installation](#installation)
- [Authentication Setup](#authentication-setup)
- [Indexer Configuration](#indexer-configuration)
- [Download Client Configuration](#download-client-configuration)
- [Advanced Settings](#advanced-settings)
- [Troubleshooting](#troubleshooting)

## Prerequisites

Before configuring Qobuzarr, ensure you have:

1. **Lidarr v3.0.0+ (plugins branch)** running on the plugins branch
2. **Qobuz Account** with active subscription
   - Studio tier: CD quality (FLAC 16-bit/44.1kHz)
   - Sublime tier: Hi-Res quality (up to 24-bit/192kHz)
3. **.NET 8.0 Runtime** installed
4. **Network Access** to Qobuz API endpoints

## Installation

### Docker Installation (Recommended)

```bash
# Using hotio's plugins image
docker pull ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913

# Run with plugin support
docker run -d \
  --name=lidarr \
  -e PUID=1000 \
  -e PGID=1000 \
  -e TZ=America/New_York \
  -p 8686:8686 \
  -v /path/to/config:/config \
  -v /path/to/music:/music \
  -v /path/to/downloads:/downloads \
  ghcr.io/hotio/lidarr:pr-plugins
```

### Manual Installation

1. Download the latest release from [GitHub Releases](https://github.com/richertunes/qobuzarr/releases)
2. Extract `Lidarr.Plugin.Qobuzarr.dll` to your Lidarr plugins directory:
   - Linux: `/config/plugins/`
   - Windows: `%ProgramData%\Lidarr\plugins\`
   - macOS: `~/.config/Lidarr/plugins/`
3. Restart Lidarr

### Verifying Installation

1. Navigate to **System → Plugins** in Lidarr
2. Confirm "Qobuzarr" appears in the plugin list
3. Check the plugin status is "Loaded"

## Authentication Setup

Qobuzarr supports two authentication methods:

### Method 1: Email & Password Authentication

This is the simplest method for most users.

1. Navigate to **Settings → Indexers**
2. Click **Add Indexer** → **Qobuz**
3. Configure authentication:

```yaml
Authentication Method: Email & Password
Email: your-email@example.com
Password: your-password
```

**Note:** Your password is MD5 hashed before transmission to Qobuz.

### Method 2: User ID & Token Authentication

For advanced users who want to use existing auth tokens.

1. Obtain your User ID and Auth Token (see [Getting Auth Tokens](#getting-auth-tokens))
2. Configure in Lidarr:

```yaml
Authentication Method: User ID & Token
User ID: 12345678
Auth Token: your-auth-token-here
```

### Getting Auth Tokens

#### Using Browser Developer Tools

1. Log into [play.qobuz.com](https://play.qobuz.com)
2. Open Developer Tools (F12)
3. Go to Application/Storage → Local Storage
4. Find `user.id` and `user.userAuthToken`

#### Using QobuzCLI

```bash
# Authenticate and display tokens
qobuzcli auth --email your@email.com --password yourpass --show-token
```

## Indexer Configuration

### Basic Settings

| Setting | Default | Description |
|---------|---------|-------------|
| **Name** | Qobuz | Display name in Lidarr |
| **Enable RSS** | No | RSS sync not supported |
| **Enable Automatic Search** | Yes | Allow automatic searches |
| **Enable Interactive Search** | Yes | Allow manual searches |

### Search Settings

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| **Maximum Search Results** | 100 | 10–500 | Results fetched per search query |
| **Include Singles & EPs** | No | — | Include singles and EPs in results |
| **Include Compilations** | Yes | — | Include compilation / Various Artists albums |
| **Pre-release Window (days)** | 0 | 0–30 | Include albums up to N days before official release *(advanced)* |

### Query Optimization *(Performance section)*

| Setting | Options | Description |
|---------|---------|-------------|
| **Query Optimization** | Disabled · Query Intelligence · ML Prediction | Reduces API calls. "Query Intelligence" saves ~35%; "ML Prediction" saves ~49% (experimental). Default: Query Intelligence |
| **ML Model Type** | Baseline · Personal · Hybrid | Model selection when using ML Prediction mode. "Baseline" is pre-trained; others require manual training *(advanced)* |

### Concurrency *(advanced)*

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| **Concurrency Mode** | Adaptive | Adaptive / Fixed / Manual | How to manage parallel API operations |
| **Fixed Concurrency Level** | 4 | 1–16 | Parallel operations in Fixed mode |
| **Minimum Concurrency** | 1 | 1–8 | Adaptive floor — system won't go below this |
| **Maximum Concurrency** | 8 | 2–16 | Adaptive ceiling — system won't exceed this |
| **Target Response Time (ms)** | 1000 | 500–5 000 | Adaptive: increases concurrency when faster |
| **Maximum Response Time (ms)** | 5000 | 1 000–10 000 | Adaptive: reduces concurrency when slower |

### Advanced Matching *(advanced)*

| Setting | Default | Description |
|---------|---------|-------------|
| **Match Confidence Threshold** | 0.8 | Minimum score (0–1) for accepting results |
| **Hybrid Search Threshold** | 0.6 | Confidence below which extra strategies activate |
| **Qobuz Subscription** | Unknown | Helps optimise quality selection (Unknown / Free / Sublime / Premier) |
| **Locale (optional)** | *(empty)* | Locale for localised results (e.g. `en_US`, `fr_FR`) |

### API Settings *(advanced)*

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| **API Rate Limit** | 60/min | 1–300 | Maximum requests per minute |
| **Cache Duration (minutes)** | 5 | 0–60 | Search-result cache TTL (0 = disabled) |
| **Connection Timeout (seconds)** | 30 | 5–300 | API request timeout |

### Example Configuration

```yaml
# Optimal configuration for most users
Name: Qobuz
Authentication Method: Email & Password
Email: user@example.com
Password: ********
Maximum Search Results: 100
Include Singles & EPs: No
Include Compilations: Yes
Query Optimization: Query Intelligence
Qobuz Subscription: Unknown
```

## Download Client Configuration

The download client is fully implemented. Configure it under **Settings → Download Clients → Add → Qobuzarr**.

### Storage

| Setting | Default | Description |
|---------|---------|-------------|
| **Download Path** | *(required)* | Root folder for downloads; Lidarr imports from here |
| **Create Album Folders** | Yes | Organise into Artist/Album folder structure |

### Quality

| Setting | Options | Description |
|---------|---------|-------------|
| **Audio Quality** | MP3 320 · FLAC CD (16/44.1) · FLAC Hi-Res (24/96) · FLAC Hi-Res (24/192) | Preferred quality; falls back automatically if unavailable |

> **Note:** 24-bit/192 kHz (format 27) is purchase-only on Qobuz; streaming tops out at 24/96. The plugin falls back to 96 kHz or CD quality.

### Performance

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| **Concurrency Mode** | Adaptive | Adaptive / Fixed / Manual | How to manage parallel track downloads |
| **Fixed Concurrent Downloads** | 3 | 1–10 | Tracks downloaded at once in Fixed mode |
| **Minimum Downloads** | 1 | 1–5 | Adaptive floor *(advanced)* |
| **Maximum Downloads** | 6 | 2–10 | Adaptive ceiling *(advanced)* |
| **Target Response Time (ms)** | 1000 | 500–3 000 | Adaptive: increases concurrency when faster *(advanced)* |

### Reliability

| Setting | Default | Description |
|---------|---------|-------------|
| **Minimum Success Rate (%)** | 80 | Tracks that must succeed for the album to pass (0–100%) |
| **Skip Preview Tracks** | Yes | Skip 30-second preview/sample tracks |
| **Count Previews as Failures** | No | Count skipped previews as failures in the success rate *(advanced)* |
| **Enable Quality Fallback** | Yes | Allow lower quality when requested format is unavailable *(advanced)* |

### Metadata *(advanced)*

| Setting | Default | Description |
|---------|---------|-------------|
| **Save Synced Lyrics** | Yes | Save a `.lrc` file alongside each track when lyrics are available |
| **Use LRCLIB for Lyrics** | No | Fall back to the public LRCLIB service (lrclib.net) for lyrics |

## Advanced Settings

### API Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| **App ID** | *(auto-detected)* | Qobuz application ID — leave empty for automatic dynamic extraction from the web player |
| **App Secret** | (built-in) | Application secret |
| **API Timeout** | 30s | Request timeout |
| **Rate Limit** | 60/min | Initial rate limit (adaptive system adjusts to 500+/min) |

### Query Intelligence Configuration

Query Intelligence is controlled through the **Query Optimization** dropdown in indexer settings (see *Query Optimization* table above). No environment variables are required — all tuning is done via the Lidarr UI.

### Concurrency and Rate Limiting

Adaptive concurrency and rate limiting are built-in and configured through the indexer and download-client UI (see the *Concurrency* tables above). No environment variables are needed.

### Caching

Search-result caching is controlled by the **Cache Duration (minutes)** setting in the indexer's advanced API section (see *API Settings* table above).

### Logging

Use Lidarr's global log level (**Settings → General → Log Level**) to control verbosity. The plugin does not expose separate log-level or API-logging settings.

## Quality Profile Mapping

Qobuzarr automatically maps Qobuz qualities to Lidarr profiles:

| Qobuz Format | Format ID | Lidarr Quality | Bitrate/Sample Rate |
|--------------|-----------|----------------|---------------------|
| MP3 V0 | 1 | MP3-VBR-V0 | ~245 kbps VBR |
| MP3 320 | 5 | MP3-320 | 320 kbps CBR |
| FLAC CD | 6 | FLAC | 16-bit/44.1kHz |
| FLAC Hi-Res | 7 | FLAC 24bit | 24-bit/96kHz |
| FLAC Hi-Res | 27 | FLAC 24bit | 24-bit/192kHz |

## Environment Variables

Override default settings using environment variables:

```bash
# Override app credentials (optional — leave empty to auto-detect from web player)
QOBUZ_APP_ID=your_app_id
QOBUZ_APP_SECRET=your_app_secret
```

## Configuration Files

### Location

Configuration is stored in Lidarr's config directory:

- Linux: `/config/config.xml`
- Windows: `%ProgramData%\Lidarr\config.xml`
- macOS: `~/.config/Lidarr/config.xml`

### Backup

Always backup your configuration before making changes:

```bash
cp /config/config.xml /config/config.xml.backup
```

## Troubleshooting

### Authentication Issues

1. **"Invalid credentials" error**
   - Verify email/password are correct
   - Check if account is active on qobuz.com
   - Try re-entering credentials

2. **"Session expired" errors**
   - Sessions last 24 hours
   - Click "Test" to re-authenticate
   - Check system time is correct

3. **"403 Forbidden" errors**
   - App ID/Secret may be invalid
   - Try default credentials
   - Contact support if persists

### Search Issues

1. **No results found**
   - Try broader search terms
   - Remove special characters
   - Check genre/year filters
   - Verify quality settings

2. **Incomplete results**
   - Increase search limit
   - Check subscription tier
   - Some content may be region-locked

3. **Slow searches**
   - Reduce search limit
   - Check network connectivity
   - Review proxy settings

### Connection Issues

1. **Timeout errors**
   - Increase API timeout
   - Check firewall settings
   - Verify DNS resolution

2. **Rate limiting**
   - Reduce concurrent searches
   - Check rate limit setting
   - Wait before retrying

### Enable Debug Logging

For detailed troubleshooting:

1. Set indexer log level to "Debug"
2. Reproduce the issue
3. Check logs at:
   - Linux: `/config/logs/lidarr.txt`
   - Windows: `%ProgramData%\Lidarr\logs\lidarr.txt`

### Common Log Entries

```log
# Successful authentication
[Info] QobuzAuthenticationService: Successfully authenticated with Qobuz API using email/password

# Search executed
[Debug] QobuzIndexer: Qobuz search: https://www.qobuz.com/api.json/0.2/album/search?query=...

# Cache hit
[Debug] QobuzApiClient: Returning cached response for /album/search

# Rate limited
[Warn] QobuzApiClient: Rate limited, waiting 60 seconds
```

## Best Practices

1. **Use Email Authentication** unless you have specific requirements
2. **Set Appropriate Search Limits** - 100 is usually sufficient
3. **Configure Quality Filters** to match your Lidarr profiles
4. **Enable Caching** to reduce API calls
5. **Monitor Logs** during initial setup
6. **Backup Configuration** before major changes

## Support

For additional help:

1. Check [GitHub Issues](https://github.com/richertunes/qobuzarr/issues)
2. Review [FAQ](https://github.com/richertunes/qobuzarr/wiki/FAQ)
3. Join [Discord Support](https://discord.gg/lidarr)
4. Enable debug logging and share logs when reporting issues
