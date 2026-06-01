# Is Qobuzarr Working? Verification Checklist

Quick checklist to verify your Qobuzarr installation is working correctly.

## Quick Health Check

Run through these checks in order. If any step fails, see the troubleshooting section below.

### 1. Plugin Loaded

- [ ] Go to **Settings > General > About**
- [ ] Look for "Qobuzarr" in the plugins list
- [ ] Version number should match your installed version

**Expected:** Qobuzarr appears in the plugins list with correct version.

### 2. Indexer Visible

- [ ] Go to **Settings > Indexers**
- [ ] Click **Add** (+ button)
- [ ] Search for "Qobuzarr"
- [ ] Qobuzarr should appear in the list

**Expected:** Qobuzarr appears as an available indexer type.

### 3. Download Client Visible

- [ ] Go to **Settings > Download Clients**
- [ ] Click **Add** (+ button)
- [ ] Search for "Qobuzarr"
- [ ] Qobuzarr should appear in the list

**Expected:** Qobuzarr appears as an available download client type.

### 4. Authentication Test

- [ ] Add Qobuzarr as an Indexer
- [ ] Enter your Qobuz **Email** and **Password** (App ID / App Secret are optional — leave empty for automatic detection)
- [ ] Click **Test**

**Expected:** Green checkmark with "Settings validated" message.

### 5. Search Test

- [ ] Go to **Add New** (artist search)
- [ ] Search for a known artist (e.g., "Diana Krall")
- [ ] Results should include Qobuz releases

**Expected:** Search results show albums with Qobuz source indicator.

### 6. Download Test

- [ ] Select an album to download
- [ ] Monitor **Activity > Queue**
- [ ] Check download completes successfully

**Expected:** Album downloads and imports to your library.

### 7. Query Intelligence Check (if enabled)

- [ ] Perform several searches
- [ ] Check System > Logs for "Query Intelligence" messages
- [ ] API usage should decrease over time

**Expected:** Logs show pattern learning and cache hits.

### 8. Log Verification

- [ ] Go to **System > Logs**
- [ ] Filter for "Qobuzarr" or "Qobuz"
- [ ] Look for successful authentication and search messages

**Expected:** Logs show successful API calls without errors.

## Status Indicators

| Indicator | Meaning | Action |
|-----------|---------|--------|
| Green checkmark on Test | Connected to Qobuz | None needed |
| Red X on Test | Authentication failed | Check email/password |
| No search results | API issue or query problem | Check query format |
| Download stuck at 0% | Stream URL issue | Retry download |
| Import failed | File format issue | Check quality settings |

## Quality Verification

### Audio Quality Check

After downloading, verify the quality:

1. Check file properties (bitrate, sample rate, bit depth)
2. Expected for CD Quality: FLAC, 16-bit/44.1kHz
3. Expected for Hi-Res: FLAC, 24-bit/96kHz or 192kHz

### Quality Setting Reference

| Quality Setting | Format | Bit Depth | Sample Rate |
|----------------|--------|-----------|-------------|
| 5 (MP3 320) | MP3 | - | 320 kbps |
| 6 (CD Quality) | FLAC | 16-bit | 44.1 kHz |
| 7 (24-bit/96kHz) | FLAC | 24-bit | Up to 96 kHz |
| 27 (Hi-Res Max) | FLAC | 24-bit | Up to 192 kHz |

## Query Intelligence Verification

If Query Intelligence is enabled:

### Cache Hit Rate Check

1. Go to System > Logs
2. Look for "Cache hit" messages
3. After 24-48 hours, expect 60%+ cache hits

### Pattern Learning Check

1. Search for the same artist multiple times
2. Subsequent searches should be faster
3. Logs show "Using cached pattern"

## Common Issues Quick Fixes

### "Authentication failed"

1. Verify your Qobuz email and password are correct
2. Check if your Qobuz subscription is active
3. Ensure credentials don't have extra spaces

### "No results found"

1. Try a more common search term
2. Check if Query Intelligence is interfering (try disabling)
3. Verify content is available in your region

### "Download failed"

1. Check Activity > History for error details
2. Verify download client is properly configured
3. Some Hi-Res content requires specific subscription tier

### "Quality lower than expected"

1. Verify your Qobuz subscription includes Hi-Res
2. Check quality setting in download client config
3. Not all albums have Hi-Res versions available

### "Query Intelligence not improving"

1. Give it 24-48 hours of normal use
2. Check if enabled in Settings > Indexers > Qobuzarr
3. Try clearing cache and letting it rebuild

## Verification Commands

### Check Lidarr Logs (Docker)

```bash
docker logs lidarr 2>&1 | grep -i qobuz
```

### Verify Qobuz API Access

```bash
# Test basic API connectivity (no auth required)
curl "https://www.qobuz.com/api.json/0.2/track/get?track_id=1"
```

## Still Not Working?

1. **Enable Debug Logging**: Settings > General > Log Level = Debug
2. **Restart Lidarr**: Sometimes required after plugin updates
3. **Disable Query Intelligence**: If searches are failing, try disabling
4. **Check GitHub Issues**: [Qobuzarr Issues](https://github.com/RicherTunes/qobuzarr/issues)
5. **Review Troubleshooting Guide**: [TROUBLESHOOTING.md](TROUBLESHOOTING.md)

## Success Indicators

Your Qobuzarr is working correctly when:

- [ ] Plugin appears in Settings > General > About
- [ ] Indexer test succeeds with green checkmark
- [ ] Download client test succeeds with green checkmark
- [ ] Searches return Qobuz results
- [ ] Downloads complete and import successfully
- [ ] Audio files match expected quality for your subscription
- [ ] Query Intelligence shows improving cache hit rate (if enabled)

---

*Last updated: May 2026*
