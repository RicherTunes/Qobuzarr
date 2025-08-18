# Qobuzzarr Configuration Guide

This guide covers all configuration options for the Qobuzzarr plugin, including initial setup, authentication methods, and advanced settings.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Installation](#installation)
- [Authentication Setup](#authentication-setup)
- [Indexer Configuration](#indexer-configuration)
- [Download Client Configuration](#download-client-configuration)
- [Advanced Settings](#advanced-settings)
- [Troubleshooting](#troubleshooting)

## Prerequisites

Before configuring Qobuzzarr, ensure you have:

1. **Lidarr v2.0+** running on the plugins branch
2. **Qobuz Account** with active subscription
   - Studio tier: CD quality (FLAC 16-bit/44.1kHz)
   - Sublime tier: Hi-Res quality (up to 24-bit/192kHz)
3. **.NET 6.0 Runtime** installed
4. **Network Access** to Qobuz API endpoints

## Installation

### Docker Installation (Recommended)

```bash
# Using hotio's plugins image
docker pull ghcr.io/hotio/lidarr:pr-plugins

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
2. Extract `Lidarr.Plugin.Qobuz.dll` to your Lidarr plugins directory:
   - Linux: `/config/plugins/`
   - Windows: `%ProgramData%\Lidarr\plugins\`
   - macOS: `~/.config/Lidarr/plugins/`
3. Restart Lidarr

### Verifying Installation

1. Navigate to **System → Plugins** in Lidarr
2. Confirm "Qobuzzarr" appears in the plugin list
3. Check the plugin status is "Loaded"

## Authentication Setup

Qobuzzarr supports two authentication methods:

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
| **Search Result Limit** | 100 | 10-500 | Maximum results per search |
| **Include Singles** | No | - | Include single releases in results |
| **Include Compilations** | Yes | - | Include compilation albums |
| **Include Live Albums** | Yes | - | Include live recordings |

### Quality Filters

| Setting | Options | Description |
|---------|---------|-------------|
| **Minimum Quality** | Any, MP3-320, FLAC, Hi-Res | Exclude lower qualities |
| **Maximum Quality** | Any, MP3-320, FLAC, Hi-Res | Exclude higher qualities |

### Genre Filtering

Select specific genres to search within:

```yaml
Preferred Genre: [Dropdown]
  - All Genres (default)
  - Jazz
  - Classical
  - Rock
  - Pop
  - Electronic
  - Hip-Hop
  - Folk
  - Blues
  - Country
  - World Music
```

### Date Filtering

| Setting | Default | Description |
|---------|---------|-------------|
| **Minimum Year** | - | Exclude releases before this year |
| **Maximum Year** | - | Exclude releases after this year |

### Example Configuration

```yaml
# Optimal configuration for most users
Name: Qobuz
Authentication Method: Email & Password
Email: user@example.com
Password: ********
Search Result Limit: 100
Include Singles: No
Include Compilations: Yes
Include Live Albums: Yes
Minimum Quality: FLAC
Preferred Genre: All Genres
```

## Download Client Configuration

*Note: Download client functionality is currently in development*

### Planned Settings

| Setting | Default | Description |
|---------|---------|-------------|
| **Download Directory** | - | Where to save downloads |
| **Concurrent Downloads** | 5 | Parallel track downloads |
| **Bandwidth Limit** | Unlimited | Max download speed (MB/s) |
| **File Naming Pattern** | `{Artist} - {Album} ({Year})/{Track} - {Title}` | |
| **Embed Metadata** | Yes | Add ID3/Vorbis tags |
| **Download Artwork** | Yes | Save album artwork |
| **Create M3U Playlist** | No | Generate playlist file |

## Advanced Settings

### API Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| **App ID** | 285473059 | Qobuz application ID |
| **App Secret** | (built-in) | Application secret |
| **API Timeout** | 30s | Request timeout |
| **Rate Limit** | 60/min | Initial rate limit (adaptive system adjusts to 500+/min) |

### Query Intelligence Configuration ⚡

**Query Intelligence provides 49.83% API call reduction automatically - enabled by default!**

| Setting | Environment Variable | Default | Description |
|---------|---------------------|---------|-------------|
| **Enable Query Intelligence** | `QOBUZ_QUERY_INTELLIGENCE` | `true` | Master switch for optimization system |
| **Debug Query Classification** | `QOBUZ_DEBUG_QUERIES` | `false` | Log complexity classifications |
| **Simple Threshold** | `QOBUZ_SIMPLE_THRESHOLD` | `1` | Threshold for simple classification (experts only) |
| **Medium Threshold** | `QOBUZ_MEDIUM_THRESHOLD` | `4` | Threshold for medium classification (experts only) |

#### Configuration Examples

**Default (Recommended)**
```bash
# Query Intelligence enabled with conservative settings
QOBUZ_QUERY_INTELLIGENCE="true"      # 49.83% API reduction
QOBUZ_DEBUG_QUERIES="false"          # Clean logs
```

**Debug Mode**
```bash
# Enable debug logging to understand classifications
QOBUZ_QUERY_INTELLIGENCE="true"
QOBUZ_DEBUG_QUERIES="true"           # Log all complexity decisions
```

**Custom Thresholds (Advanced Users Only)**
```bash
# More aggressive optimization (may impact quality)
QOBUZ_SIMPLE_THRESHOLD="2"           # More albums classified as simple
QOBUZ_MEDIUM_THRESHOLD="5"           # Fewer albums classified as complex

# More conservative optimization (less API reduction)
QOBUZ_SIMPLE_THRESHOLD="0"           # Fewer albums classified as simple  
QOBUZ_MEDIUM_THRESHOLD="3"           # More albums classified as complex
```

**Performance vs Quality Trade-offs**

| Configuration | API Reduction | Quality Impact | Use Case |
|---------------|---------------|----------------|----------|
| **Default** | 49.83% | 1.515% loss | **Recommended for all users** |
| **Aggressive** | 55%+ | 3-5% loss | High-volume, quality-tolerant |
| **Conservative** | 35-40% | <1% loss | Quality-critical applications |
| **Disabled** | 0% | 0% loss | Troubleshooting only |

### Adaptive Rate Limiting Configuration ⚡

**Adaptive Rate Limiting provides 93x performance improvement automatically - enabled by default!**

| Setting | Environment Variable | Default | Description |
|---------|---------------------|---------|-------------|
| **Enable Adaptive Rate Limiting** | `QOBUZ_ADAPTIVE_RATE_LIMITING` | `true` | Master switch for adaptive system |
| **Initial Rate** | `QOBUZ_INITIAL_RATE` | `60/min` | Starting request rate |
| **Maximum Rate** | `QOBUZ_MAX_RATE` | `500/min` | Maximum request rate |
| **Rate Increase Factor** | `QOBUZ_RATE_INCREASE_FACTOR` | `1.2` | Rate increase multiplier |
| **Success Threshold** | `QOBUZ_SUCCESS_THRESHOLD` | `20` | Successful requests before rate increase |

#### Configuration Examples

**Default (Recommended)**
```bash
# Adaptive rate limiting with conservative start
QOBUZ_ADAPTIVE_RATE_LIMITING="true"   # 93x performance improvement
QOBUZ_INITIAL_RATE="60"               # Conservative start
QOBUZ_MAX_RATE="500"                  # Safe maximum
```

**Aggressive Performance**
```bash
# Faster initial rate, higher maximum
QOBUZ_INITIAL_RATE="120"              # Start faster
QOBUZ_MAX_RATE="600"                  # Higher ceiling
QOBUZ_RATE_INCREASE_FACTOR="1.5"      # Faster scaling
```

**Conservative Performance**
```bash
# Slower scaling for stability
QOBUZ_INITIAL_RATE="30"               # Very conservative start
QOBUZ_MAX_RATE="300"                  # Lower ceiling
QOBUZ_RATE_INCREASE_FACTOR="1.1"      # Gradual scaling
```

### Caching Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| **Enable Response Cache** | Yes | Cache API responses |
| **Search Cache Duration** | 5 min | Search result cache TTL |
| **Album Cache Duration** | 1 hour | Album details cache TTL |
| **Artist Cache Duration** | 24 hours | Artist info cache TTL |

### Network Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| **Use Proxy** | No | Route through HTTP proxy |
| **Proxy URL** | - | Proxy server address |
| **Proxy Username** | - | Proxy authentication |
| **Proxy Password** | - | Proxy authentication |

### Logging Configuration

| Setting | Options | Description |
|---------|---------|-------------|
| **Log Level** | Info | Debug, Info, Warn, Error |
| **Log API Requests** | No | Log all API calls |
| **Log API Responses** | No | Log full responses |

## Quality Profile Mapping

Qobuzzarr automatically maps Qobuz qualities to Lidarr profiles:

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
# Override app credentials
QOBUZ_APP_ID=your_app_id
QOBUZ_APP_SECRET=your_app_secret

# Enable debug logging
QOBUZ_LOG_LEVEL=Debug

# Set custom cache directory
QOBUZ_CACHE_DIR=/path/to/cache
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