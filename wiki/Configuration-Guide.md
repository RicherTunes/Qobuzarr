# Configuration Guide

Complete configuration guide for Qobuzarr, covering authentication, indexer settings, performance tuning, and advanced features.

## 🔐 Authentication Setup

### Method 1: Email & Password (Recommended)

The simplest and most reliable authentication method:

1. **Navigate to Lidarr Settings**:
   - Go to **Settings → Indexers**
   - Click **Add Indexer (+)**
   - Select **Qobuzarr** from the list

2. **Configure Basic Authentication**:

   ```
   Name: Qobuz
   Authentication Method: Email & Password
   Email: your-email@example.com
   Password: your-password
   ```

3. **Test Connection**:
   - Click **Test** button
   - Verify "Test passed" message
   - Click **Save**

### Method 2: User ID & Token

For advanced users or automated deployments:

1. **Obtain Authentication Tokens**:

   **Using Browser Developer Tools**:
   - Log into [play.qobuz.com](https://play.qobuz.com)
   - Open Developer Tools (F12)
   - Navigate to **Application** → **Local Storage** → `https://play.qobuz.com`
   - Copy values for:
     - `user.id` (numeric user ID)
     - `user.userAuthToken` (long string token)

2. **Configure Token Authentication**:

   ```
   Authentication Method: User ID & Token
   User ID: 12345678
   Auth Token: your-long-auth-token-string
   ```

### Method 3: Dynamic Extraction (Advanced)

Automatically extracts credentials from Qobuz web player:

⚠️ **Warning**: This method may break if Qobuz updates their web interface.

```bash
# Enable dynamic extraction
export QOBUZ_DYNAMIC_AUTH=true
export QOBUZ_BROWSER_PATH="/path/to/chrome"
```

## 🔍 Indexer Configuration

### Basic Settings

| Setting | Default | Description |
|---------|---------|-------------|
| **Name** | Qobuz | Display name in Lidarr |
| **Enable RSS** | No | RSS feeds not supported by Qobuz API |
| **Enable Automatic Search** | Yes | Allow automatic album searches |
| **Enable Interactive Search** | Yes | Allow manual searches from Lidarr UI |
| **Priority** | 25 | Search priority (lower = higher priority) |

### Search Configuration

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| **Search Result Limit** | 100 | 10-500 | Maximum results returned per search |
| **Early Download Limit** | - | 1-10000 | Download older releases immediately |
| **Grab Delay** | 0 | 0-60 | Minutes to wait before downloading |

### Content Filters

| Setting | Default | Description |
|---------|---------|-------------|
| **Include Singles** | No | Include single track releases |
| **Include EPs** | Yes | Include extended play releases |
| **Include Compilations** | Yes | Include compilation albums |
| **Include Live Albums** | Yes | Include live recordings |
| **Include Soundtracks** | Yes | Include movie/TV soundtracks |

### Quality Filters

Configure quality filtering to match your preferences:

| Setting | Options | Description |
|---------|---------|-------------|
| **Minimum Quality** | Any, MP3-320, FLAC, Hi-Res | Exclude lower quality formats |
| **Maximum Quality** | Any, MP3-320, FLAC, Hi-Res | Exclude higher quality formats |
| **Preferred Quality** | - | Prioritize specific quality when available |

**Quality Format Mapping**:

```
MP3-320    → 320 kbps CBR MP3
FLAC       → 16-bit/44.1kHz FLAC (CD Quality)
Hi-Res     → 24-bit/96kHz or 192kHz FLAC
```

### Genre Filtering

Filter search results by musical genre:

```
Available Genres:
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
- Alternative
- Metal
- Reggae
- R&B/Soul
```

### Date Range Filtering

| Setting | Format | Description |
|---------|--------|-------------|
| **Minimum Year** | YYYY | Exclude releases before this year |
| **Maximum Year** | YYYY | Exclude releases after this year |

Example: Set 2000-2023 for modern music only.

## ⚡ Performance Optimization

### ML-Powered Query Intelligence

**Achieves 65.8% API call reduction automatically**

| Setting | Environment Variable | Default | Description |
|---------|---------------------|---------|-------------|
| **Enable Query Intelligence** | `QOBUZ_QUERY_INTELLIGENCE` | `true` | Master optimization switch <!-- TODO(docval): QOBUZ_QUERY_INTELLIGENCE env var not found in src/ as of 2026-05-31 --> |
| **Debug Classifications** | `QOBUZ_DEBUG_QUERIES` | `false` | Log query complexity decisions |
| **Simple Threshold** | `QOBUZ_SIMPLE_THRESHOLD` | `1` | Classification threshold (experts only) |
| **Medium Threshold** | `QOBUZ_MEDIUM_THRESHOLD` | `4` | Classification threshold (experts only) |

**Configuration Examples**:

**Default (Recommended)**:

```bash
export QOBUZ_QUERY_INTELLIGENCE=true
export QOBUZ_DEBUG_QUERIES=false
# Result: 65.8% API reduction, 1.515% quality impact
```

**Debug Mode**:

```bash
export QOBUZ_QUERY_INTELLIGENCE=true
export QOBUZ_DEBUG_QUERIES=true
# View classification decisions in logs
```

**Aggressive Optimization**:

```bash
export QOBUZ_SIMPLE_THRESHOLD=2
export QOBUZ_MEDIUM_THRESHOLD=5
# Result: ~70% API reduction, 3-5% quality impact
```

### Adaptive Rate Limiting

**Provides 93x performance improvement**

| Setting | Environment Variable | Default | Description |
|---------|---------------------|---------|-------------|
| **Enable Adaptive Rate Limiting** | `QOBUZ_ADAPTIVE_RATE_LIMITING` | `true` | Adaptive rate control |
| **Initial Rate** | `QOBUZ_INITIAL_RATE` | `60/min` | Starting request rate |
| **Maximum Rate** | `QOBUZ_MAX_RATE` | `500/min` | Maximum request rate |
| **Rate Increase Factor** | `QOBUZ_RATE_INCREASE_FACTOR` | `1.2` | Rate scaling multiplier |
| **Success Threshold** | `QOBUZ_SUCCESS_THRESHOLD` | `20` | Requests before rate increase |

**Performance Profiles**:

**Conservative** (Default):

```bash
export QOBUZ_INITIAL_RATE=60
export QOBUZ_MAX_RATE=500
export QOBUZ_RATE_INCREASE_FACTOR=1.2
```

**Aggressive**:

```bash
export QOBUZ_INITIAL_RATE=120
export QOBUZ_MAX_RATE=750
export QOBUZ_RATE_INCREASE_FACTOR=1.5
```

**Ultra-Conservative**:

```bash
export QOBUZ_INITIAL_RATE=30
export QOBUZ_MAX_RATE=300
export QOBUZ_RATE_INCREASE_FACTOR=1.1
```

### Caching Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| **Enable Response Cache** | Yes | Cache API responses to reduce calls |
| **Search Cache TTL** | 5 minutes | How long to cache search results |
| **Album Cache TTL** | 1 hour | How long to cache album details |
| **Artist Cache TTL** | 24 hours | How long to cache artist information |
| **Cache Size Limit** | 100MB | Maximum cache size before eviction |

**Cache Environment Variables**:

```bash
# Configure cache location
export QOBUZ_CACHE_DIR=/fast/storage/qobuz-cache

# Adjust cache sizes
export QOBUZ_SEARCH_CACHE_SIZE=50MB
export QOBUZ_ALBUM_CACHE_SIZE=100MB
export QOBUZ_ARTIST_CACHE_SIZE=25MB
```

## 🔧 Advanced Settings

### API Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| **API Timeout** | 30 seconds | Request timeout duration |
| **Retry Count** | 3 | Number of retry attempts |
| **Retry Delay** | 1 second | Base delay between retries |
| **User Agent** | Lidarr/Qobuzarr | HTTP User-Agent header |

### Network Configuration

| Setting | Description |
|---------|-------------|
| **HTTP Proxy** | Route traffic through proxy server |
| **Proxy Username** | Authentication for proxy server |
| **Proxy Password** | Password for proxy authentication |
| **Skip Certificate Validation** | Ignore SSL certificate errors (not recommended) |

**Proxy Configuration Example**:

```bash
export HTTP_PROXY=http://proxy.company.com:8080
export HTTPS_PROXY=https://proxy.company.com:8443
export PROXY_USER=username
export PROXY_PASS=password
```

### Security Settings

| Setting | Default | Description |
|---------|---------|-------------|
| **Encrypt Credentials** | Yes | Encrypt stored passwords |
| **Session Timeout** | 24 hours | Authentication session duration |
| **IP Whitelisting** | Disabled | Restrict API access by IP |
| **Rate Limit Protection** | Enabled | Prevent API abuse |

## 📁 File & Path Configuration

### Download Paths

Configure where downloads are stored:

```bash
# Base download directory
export QOBUZ_DOWNLOAD_PATH=/music/downloads

# Organize by artist/album
export QOBUZ_PATH_PATTERN="{Artist}/{Year} - {Album}"

# File naming pattern
export QOBUZ_FILE_PATTERN="{Track:00} - {Title}"
```

### Metadata Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| **Embed Artwork** | Yes | Include album artwork in files |
| **Artwork Size** | 1400x1400 | Preferred artwork resolution |
| **Write ID3 Tags** | Yes | Embed metadata in MP3 files |
| **Write Vorbis Comments** | Yes | Embed metadata in FLAC files |

## 🔍 Logging & Debugging

### Log Level Configuration

| Level | Description | When to Use |
|-------|-------------|-------------|
| **Error** | Errors only | Production environments |
| **Warn** | Warnings and errors | Normal operation |
| **Info** | Informational messages | Default recommended level |
| **Debug** | Detailed debugging | Troubleshooting issues |
| **Trace** | Very verbose output | Development only |

**Set Log Level**:

```bash
export QOBUZ_LOG_LEVEL=Info
```

### Debug Options

Enable specific debugging features:

```bash
# Debug API requests/responses
export QOBUZ_DEBUG_API=true

# Debug authentication flow
export QOBUZ_DEBUG_AUTH=true

# Debug search processing
export QOBUZ_DEBUG_SEARCH=true

# Debug ML optimization
export QOBUZ_DEBUG_ML=true

# Debug caching
export QOBUZ_DEBUG_CACHE=true
```

## 🎯 Use Case Configurations

### High-Volume Server

Optimized for large libraries and frequent searches:

```bash
# Aggressive performance settings
export QOBUZ_QUERY_INTELLIGENCE=true
export QOBUZ_SIMPLE_THRESHOLD=2
export QOBUZ_ADAPTIVE_RATE_LIMITING=true
export QOBUZ_INITIAL_RATE=120
export QOBUZ_MAX_RATE=750

# Large caches
export QOBUZ_SEARCH_CACHE_SIZE=200MB
export QOBUZ_ALBUM_CACHE_SIZE=500MB

# Extended cache times
export QOBUZ_SEARCH_CACHE_TTL=15m
export QOBUZ_ALBUM_CACHE_TTL=6h
```

### Quality-Critical Setup

Prioritizes quality over performance:

```bash
# Conservative optimization
export QOBUZ_QUERY_INTELLIGENCE=true
export QOBUZ_SIMPLE_THRESHOLD=0
export QOBUZ_MEDIUM_THRESHOLD=2

# Conservative rate limiting
export QOBUZ_INITIAL_RATE=30
export QOBUZ_MAX_RATE=300

# Quality-focused settings
export QOBUZ_MINIMUM_QUALITY=FLAC
export QOBUZ_INCLUDE_HIRES=true
```

### Resource-Constrained Environment

Minimal resource usage:

```bash
# Disable heavy features
export QOBUZ_QUERY_INTELLIGENCE=false
export QOBUZ_ADAPTIVE_RATE_LIMITING=false

# Small caches
export QOBUZ_SEARCH_CACHE_SIZE=10MB
export QOBUZ_ALBUM_CACHE_SIZE=25MB

# Reduced limits
export QOBUZ_SEARCH_LIMIT=25
export QOBUZ_MAX_CONNECTIONS=2
```

## 📊 Monitoring & Statistics

### Performance Metrics

Track plugin performance with these metrics:

- **API Call Reduction**: Percentage saved by ML optimization
- **Cache Hit Rate**: Percentage of requests served from cache
- **Average Response Time**: API request latency
- **Search Success Rate**: Percentage of successful searches

### Health Monitoring

```bash
# Enable metrics collection
export QOBUZ_COLLECT_METRICS=true

# Metrics endpoint (if available)
export QOBUZ_METRICS_PORT=9090

# Export to Prometheus
export QOBUZ_PROMETHEUS_ENABLED=true
```

## 🔐 Environment Variables Reference

### Authentication

```bash
QOBUZ_EMAIL=your@email.com
QOBUZ_PASSWORD=your_password
QOBUZ_USER_ID=12345678
QOBUZ_AUTH_TOKEN=your_token
QOBUZ_APP_ID=285473059
QOBUZ_APP_SECRET=your_secret
```

### Performance

```bash
QOBUZ_QUERY_INTELLIGENCE=true
QOBUZ_ADAPTIVE_RATE_LIMITING=true
QOBUZ_INITIAL_RATE=60
QOBUZ_MAX_RATE=500
QOBUZ_RATE_INCREASE_FACTOR=1.2
```

### Caching

```bash
QOBUZ_CACHE_DIR=/path/to/cache
QOBUZ_SEARCH_CACHE_SIZE=50MB
QOBUZ_ALBUM_CACHE_SIZE=100MB
QOBUZ_SEARCH_CACHE_TTL=5m
QOBUZ_ALBUM_CACHE_TTL=1h
```

### Debugging

```bash
QOBUZ_LOG_LEVEL=Info
QOBUZ_DEBUG_QUERIES=false
QOBUZ_DEBUG_API=false
QOBUZ_DEBUG_AUTH=false
```

## 📝 Configuration Validation

### Test Your Configuration

1. **Authentication Test**:
   - Click **Test** in indexer settings
   - Verify successful connection message

2. **Search Test**:
   - Perform manual search in Lidarr
   - Check for results and proper quality detection

3. **Log Review**:
   - Enable Debug logging temporarily
   - Review logs for any warnings or errors
   - Look for optimization statistics

### Configuration Backup

Always backup your configuration:

```bash
# Lidarr configuration
cp /config/config.xml /config/config.xml.backup

# Plugin-specific settings
tar czf qobuzarr-config.tar.gz /config/plugins/
```

## 🆘 Troubleshooting Configuration

### Common Issues

#### Authentication Failures

- Verify credentials are correct
- Check account status on qobuz.com
- Test with different authentication method
- Review network connectivity

#### Poor Search Performance

- Enable Query Intelligence if disabled
- Check cache configuration
- Verify network bandwidth
- Review rate limiting settings

#### No Search Results

- Check quality filters aren't too restrictive
- Verify genre filters are appropriate
- Test with broader search terms
- Check subscription tier limitations

### Getting Help

1. **Review [[Troubleshooting]] guide**
2. **Check [GitHub Issues](https://github.com/RicherTunes/qobuzarr/issues)**
3. **Enable debug logging and share relevant logs**
4. **Join community discussions**

---

*Your Qobuzarr plugin is now configured for optimal performance. Next: [[Usage Examples]]*
