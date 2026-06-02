# Configuration Guide

Quick-start configuration for Qobuzarr. For the complete settings reference (download client, concurrency, advanced matching, and all field-level detail), see the canonical **[docs/user/CONFIGURATION-GUIDE.md](../docs/user/CONFIGURATION-GUIDE.md)**.

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
   - Navigate to **Application → Local Storage → `https://play.qobuz.com`**
   - Copy values for:
     - `user.id` (numeric user ID)
     - `user.userAuthToken` (long string token)

2. **Configure Token Authentication**:

   ```
   Authentication Method: User ID & Token
   User ID: 12345678
   Auth Token: your-long-auth-token-string
   ```

### App ID and App Secret (Optional)

App ID and App Secret are **advanced fields** — leave them empty and the plugin will automatically extract credentials from the Qobuz web player. Only set them if you have custom API credentials.

## 🔍 Indexer Configuration

### Basic Settings

| Setting | Default | Description |
|---------|---------|-------------|
| **Name** | Qobuz | Display name in Lidarr |
| **Enable RSS** | No | RSS feeds not supported by Qobuz API |
| **Enable Automatic Search** | Yes | Allow automatic album searches |
| **Enable Interactive Search** | Yes | Allow manual searches from Lidarr UI |

### Search Settings

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| **Maximum Search Results** | 100 | 10–500 | Results fetched per search query |
| **Include Singles & EPs** | No | — | Include singles and EPs |
| **Include Compilations** | Yes | — | Include compilation albums |
| **Pre-release Window (days)** | 0 | 0–30 | Include albums up to N days before release *(advanced)* |

### Query Optimization

| Setting | Options | Description |
|---------|---------|-------------|
| **Query Optimization** | Disabled · Query Intelligence · ML Prediction | Reduces API calls (~35% or ~49%). Default: Query Intelligence |
| **ML Model Type** | Baseline · Personal · Hybrid | Model when using ML Prediction *(advanced)* |
| **Country/Region** | *(text)* | Your country code (e.g. US, GB, FR) — must match Qobuz account region |
| **Locale** | *(text)* | Locale for localised results (e.g. en_US) *(advanced)* |
| **Match Confidence Threshold** | 0–1 | Minimum confidence for accepting results. Default: 0.8 *(advanced)* |
| **Hybrid Search Threshold** | 0–1 | Confidence below which additional strategies activate. Default: 0.6 *(advanced)* |
| **Qobuz Subscription** | Unknown · Studio · Sublime+ | Helps optimise quality selection. Default: Unknown (auto-detect) |

For concurrency, advanced matching, and API settings, see the [full Configuration Guide](../docs/user/CONFIGURATION-GUIDE.md).

## 📥 Download Client Configuration

Add the download client under **Settings → Download Clients → Add → Qobuzarr**.

| Setting | Default | Description |
|---------|---------|-------------|
| **Download Path** | *(required)* | Root folder for downloads |
| **Create Album Folders** | Yes | Organise into Artist/Album structure |
| **Audio Quality** | FLAC CD | MP3 320 / FLAC CD / FLAC 96 / FLAC 192 |
| **Minimum Success Rate (%)** | 80 | Album passes when this % of tracks succeed |
| **Skip Preview Tracks** | Yes | Skip 30-second samples |
| **Count Previews as Failures** | No | Count skipped previews as failures in success rate *(advanced)* |
| **Enable Quality Fallback** | Yes | Allow lower quality if requested format unavailable. Disable for strict HiRes-or-nothing *(advanced)* |
| **Save Synced Lyrics** | Yes | Save a .lrc lyrics file alongside each track *(advanced)* |
| **Use LRCLIB for Lyrics** | No | Fall back to public LRCLIB service for lyrics *(advanced)* |

For performance, reliability, and metadata settings (concurrency, quality fallback, lyrics), see the [full Configuration Guide](../docs/user/CONFIGURATION-GUIDE.md).

## 🔧 Environment Variables

The only supported environment variables are optional App ID / App Secret overrides:

```bash
QOBUZ_APP_ID=your_app_id          # Optional; leave empty for auto-detection
QOBUZ_APP_SECRET=your_app_secret  # Optional; leave empty for auto-detection
```

All other settings are configured through the Lidarr UI. There are no environment variables for query optimisation, rate limiting, caching, or logging.

## 🔍 Logging and Debugging

Use Lidarr's global log level (**Settings → General → Log Level**) to control verbosity. The plugin does not expose separate log-level or API-logging settings.

## 🆘 Troubleshooting Configuration

### Common Issues

#### Authentication Failures

- Verify email and password are correct
- Check account status on qobuz.com
- Test with the other authentication method

#### Poor Search Performance

- Ensure Query Optimization is set to "Query Intelligence" or "ML Prediction"
- Reduce "Maximum Search Results" to 50
- Check the concurrency settings (Adaptive mode recommended)

#### No Search Results

- Try broader search terms
- Verify your Qobuz subscription is active
- Check that content is available in your region

### Getting Help

1. **Review [[Troubleshooting]] guide**
2. **Check [GitHub Issues](https://github.com/RicherTunes/qobuzarr/issues)**
3. **Enable debug logging and share relevant logs**
4. **Join community discussions**

---

*Next: [[Troubleshooting]]*
