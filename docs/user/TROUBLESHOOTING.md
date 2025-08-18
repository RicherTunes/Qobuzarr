# Qobuzzarr Troubleshooting Guide

This guide helps diagnose and resolve common issues with the Qobuzzarr plugin.

## Table of Contents

- [Quick Diagnostics](#quick-diagnostics)
- [Authentication Issues](#authentication-issues)
- [Search Problems](#search-problems)
- [Download Issues](#download-issues)
- [Plugin Loading Errors](#plugin-loading-errors)
- [Performance Issues](#performance-issues)
- [API Errors](#api-errors)
- [Log Analysis](#log-analysis)
- [Advanced Debugging](#advanced-debugging)

## Quick Diagnostics

### Plugin Health Check

1. **Verify Plugin is Loaded**
   - Navigate to **System → Plugins**
   - Check "Qobuzzarr" appears with status "Loaded"
   - Note the version number

2. **Test Connection**
   - Go to **Settings → Indexers → Qobuz**
   - Click "Test" button
   - Should show "Test successful"

3. **Check Logs**
   ```bash
   # Linux
   tail -f /config/logs/lidarr.txt | grep -i qobuz
   
   # Windows
   Get-Content "C:\ProgramData\Lidarr\logs\lidarr.txt" -Tail 50 | Select-String "qobuz"
   ```

## Authentication Issues

### "Invalid Credentials" Error

**Symptoms:**
- Test connection fails with "Invalid credentials"
- Search returns no results
- Log shows "401 Unauthorized"

**Solutions:**

1. **Verify Credentials**
   ```bash
   # Test with QobuzCLI
   qobuzcli auth --email your@email.com --password yourpass
   ```

2. **Check Password Format**
   - Ensure no special characters are causing issues
   - Try resetting password on qobuz.com
   - Password is MD5 hashed - verify encoding

3. **Account Status**
   - Log into qobuz.com to verify account is active
   - Check subscription hasn't expired
   - Verify no regional restrictions

### "Session Expired" Errors

**Symptoms:**
- Works initially then fails after ~24 hours
- "Session expired" in logs
- Intermittent authentication failures

**Solutions:**

1. **Force Re-authentication**
   ```bash
   # Clear session cache
   rm /config/qobuz_session.cache
   ```

2. **Update Settings**
   - Click "Test" in indexer settings
   - Save settings again
   - Restart Lidarr if needed

### Token Authentication Issues

**Symptoms:**
- Token auth fails but email/password works
- "Invalid user ID or token" errors

**Solutions:**

1. **Get Fresh Tokens**
   ```javascript
   // In browser console on play.qobuz.com
   localStorage.getItem('user.id')
   localStorage.getItem('user.userAuthToken')
   ```

2. **Verify Token Format**
   - User ID should be numeric (e.g., "12345678")
   - Auth token is alphanumeric string
   - No extra spaces or quotes

## Search Problems

### No Search Results

**Symptoms:**
- Searches return empty
- "No results found" for known albums
- Works on qobuz.com but not in Lidarr

**Solutions:**

1. **Simplify Search Terms**
   ```
   Bad:  "Pink Floyd - The Dark Side of the Moon (1973)"
   Good: "Pink Floyd Dark Side Moon"
   ```

2. **Check Search Settings**
   - Increase "Search Result Limit"
   - Disable genre filtering
   - Remove year restrictions

3. **Test Search Strategies**
   ```bash
   # Test different search approaches
   qobuzcli search "artist:Pink Floyd"
   qobuzcli search "album:Dark Side"
   qobuzcli search "Pink Floyd" --fuzzy
   ```

### Incomplete Results

**Symptoms:**
- Missing known albums
- Only showing some formats
- Regional content missing

**Solutions:**

1. **Adjust Quality Filters**
   - Set "Minimum Quality" to "Any"
   - Enable all format options
   - Check subscription tier supports quality

2. **Regional Availability**
   - Some content is region-locked
   - Use VPN if needed (check Qobuz ToS)
   - Verify account region matches content

### Search Timeout

**Symptoms:**
- "Request timeout" errors
- Searches take >30 seconds
- Intermittent failures

**Solutions:**

1. **Reduce Search Load**
   ```yaml
   Search Result Limit: 50  # Reduce from 100
   Include Singles: No
   Include Compilations: No
   ```

2. **Check Network**
   ```bash
   # Test API connectivity
   curl -I https://www.qobuz.com/api.json/0.2/album/search
   ```

## Download Issues

*Note: Download client is still in development*

### Common Download Problems

1. **Queue Stuck**
   - Check queue status in UI
   - Clear failed items
   - Restart download service

2. **Slow Downloads**
   - Check bandwidth limits
   - Reduce concurrent downloads
   - Verify network speed

3. **Metadata Issues**
   - Enable "Embed Metadata" option
   - Check TagLib compatibility
   - Verify file permissions

## Plugin Loading Errors

### "Could not load plugin" Error

**Symptoms:**
- Plugin doesn't appear in list
- Error in logs about loading assembly
- "Missing dependency" errors

**Solutions:**

1. **Verify Installation**
   ```bash
   # Check plugin file exists
   ls -la /config/plugins/Lidarr.Plugin.Qobuz.dll
   ```

2. **Check .NET Version**
   ```bash
   dotnet --version  # Should be 6.0 or higher
   ```

3. **Dependency Issues**
   - Ensure ILRepack merged all dependencies
   - Check for conflicting versions
   - Review assembly binding redirects

### "Plugin version incompatible"

**Symptoms:**
- Plugin loads but features missing
- "Minimum version not met" warning

**Solutions:**

1. **Update Lidarr**
   ```bash
   # Pull latest plugins branch
   docker pull ghcr.io/hotio/lidarr:pr-plugins
   ```

2. **Verify Compatibility**
   - Check plugin targets correct Lidarr version
   - Review API changes in Lidarr

## Performance Issues

### Query Intelligence Not Working

**Symptoms:**
- API calls not reduced (should see ~50% reduction)
- Searches still making 3 API calls per album
- No performance improvement after v0.0.3

**Diagnosis:**
```bash
# Check if Query Intelligence is enabled
echo $QOBUZ_QUERY_INTELLIGENCE  # Should return "true"

# Enable debug logging to see classifications
export QOBUZ_DEBUG_QUERIES="true"

# Test with CLI
cd QobuzCLI
dotnet run -- search "Pink Floyd The Wall" --debug
```

**Expected Debug Output:**
```
Query complexity for 'Pink Floyd - The Wall': Simple
Optimized queries: 1 (reduced from 3, 66.7% reduction)
API calls saved: 2
```

**Solutions:**

1. **Verify Query Intelligence is Enabled**
   ```bash
   # Enable Query Intelligence
   export QOBUZ_QUERY_INTELLIGENCE="true"
   
   # Restart Lidarr after environment change
   sudo systemctl restart lidarr
   ```

2. **Check Classification Logic**
   ```bash
   # Test various complexity scenarios
   dotnet run -- analyze-complexity --artist "Pink Floyd" --album "The Wall"        # Should be Simple
   dotnet run -- analyze-complexity --artist "AC/DC" --album "Back in Black"       # Should be Complex
   dotnet run -- analyze-complexity --artist "Various Artists" --album "Hits 2024" # Should be Complex
   ```

3. **Validate Performance Metrics**
   ```bash
   # Measure actual API call reduction
   dotnet run -- test-performance --albums 20 --measure-reduction
   
   # Compare with/without optimization
   export QOBUZ_QUERY_INTELLIGENCE="false"
   dotnet run -- test-performance --albums 20 --baseline
   ```

### Slow Searches Despite Query Intelligence

**Symptoms:**
- Query Intelligence enabled but searches still slow
- No significant performance improvement
- API calls reduced but response time unchanged

**Solutions:**

1. **Check Adaptive Rate Limiting**
   ```bash
   # Ensure adaptive rate limiting is enabled
   export QOBUZ_ADAPTIVE_RATE_LIMITING="true"
   
   # Allow higher maximum rate
   export QOBUZ_MAX_RATE="500"
   ```

2. **Monitor Rate Limiting Effectiveness**
   ```bash
   # Enable rate limiting debug logging
   export QOBUZ_DEBUG_RATE_LIMITING="true"
   
   # Test performance scaling
   dotnet run -- test-performance --albums 50 --show-rate-scaling
   ```

3. **Optimize Combined Systems**
   ```bash
   # Both optimizations enabled (recommended)
   export QOBUZ_QUERY_INTELLIGENCE="true"      # 49.83% API reduction
   export QOBUZ_ADAPTIVE_RATE_LIMITING="true"  # 93x performance improvement
   ```

### Unexpected Query Classifications

**Symptoms:**
- Simple albums classified as Complex
- Unicode artists not handled correctly
- Various Artists not optimized as expected

**Diagnosis:**
```bash
# Enable debug logging
export QOBUZ_DEBUG_QUERIES="true"

# Test problematic cases
dotnet run -- analyze-complexity --artist "Björk" --album "Homogenic"
dotnet run -- analyze-complexity --artist "Sigur Rós" --album "Ágætis byrjun"
```

**Understanding Classifications:**

**Simple Cases (1 query):**
- "Pink Floyd" + "The Wall" → Score: 0 (no complexity factors)
- "Elton John" + "Goodbye Yellow Brick Road" → Score: 1 (long string)

**Medium Cases (2 queries):**
- "The Beatles" + "Abbey Road" → Score: 2 ("The" prefix + moderate complexity)

**Complex Cases (3 queries):**
- "AC/DC" + "Back in Black" → Score: 5+ (special characters)
- "Various Artists" + "Top Hits" → Score: 6+ (compilation indicator)
- "Björk" + "Homogenic" → Score: 4+ (Unicode characters)

**Solutions:**

1. **Trust Conservative Design**
   - Unicode artists like "Björk" may classify as Simple - this is acceptable
   - Conservative behavior prioritizes quality over aggressive optimization
   - Complex cases maintain current search quality

2. **Custom Thresholds (Advanced)**
   ```bash
   # More aggressive optimization (may impact quality)
   export QOBUZ_SIMPLE_THRESHOLD="2"    # More albums classified as simple
   export QOBUZ_MEDIUM_THRESHOLD="5"    # Fewer albums classified as complex
   
   # More conservative optimization (less API reduction but higher quality)
   export QOBUZ_SIMPLE_THRESHOLD="0"    # Fewer albums classified as simple
   export QOBUZ_MEDIUM_THRESHOLD="3"    # More albums classified as complex
   ```

3. **Report Edge Cases**
   - If you find consistently incorrect classifications, report them
   - Include the debug output and expected vs actual behavior
   - Help improve the classification algorithm

### Legacy Performance Issues

**Symptoms:**
- Searches take >10 seconds
- UI becomes unresponsive
- High CPU usage

**Solutions:**

1. **Enable All Performance Optimizations**
   ```bash
   # Enable both major optimizations
   export QOBUZ_QUERY_INTELLIGENCE="true"      # 49.83% API reduction
   export QOBUZ_ADAPTIVE_RATE_LIMITING="true"  # 93x performance improvement
   ```

2. **Enable Caching**
   ```yaml
   Enable Response Cache: Yes
   Search Cache Duration: 10 minutes
   ```

3. **Optimize Queries** (largely automated by Query Intelligence)
   - Query Intelligence handles optimization automatically
   - Use specific search terms when possible
   - Limit result count for testing

### Memory Usage

**Symptoms:**
- Lidarr memory usage increases
- Out of memory errors
- Cache growth unbounded

**Solutions:**

1. **Clear Cache**
   ```bash
   # Restart Lidarr to clear memory cache
   docker restart lidarr
   ```

2. **Adjust Cache Settings**
   - Reduce cache duration
   - Limit cache size
   - Monitor memory usage

## API Errors

### Rate Limiting (429)

**Symptoms:**
- "Too many requests" errors
- Searches failing after multiple attempts
- "Rate limit exceeded" in logs

**Solutions:**

1. **Reduce Request Rate**
   ```yaml
   API Rate Limit: 30  # Reduce from 60
   ```

2. **Check for Loops**
   - Disable RSS if not needed
   - Reduce search frequency
   - Check for duplicate indexers

### Server Errors (500)

**Symptoms:**
- "Internal server error" responses
- Intermittent failures
- Works sometimes but not others

**Solutions:**

1. **Wait and Retry**
   - Qobuz may have temporary issues
   - Check https://status.qobuz.com
   - Try again in 5-10 minutes

2. **Report Persistent Issues**
   - Note exact error and time
   - Check if specific to certain content
   - Report to plugin maintainers

## Log Analysis

### Enable Debug Logging

1. **Indexer Specific**
   ```yaml
   Log Level: Debug
   Log API Requests: Yes
   Log API Responses: Yes  # Careful - verbose!
   ```

2. **Global Lidarr**
   ```bash
   # Edit config.xml
   <LogLevel>Debug</LogLevel>
   ```

### Important Log Patterns

```log
# Successful authentication
[Info] QobuzAuthenticationService: Successfully authenticated with Qobuz API

# Search execution
[Debug] QobuzIndexer: Executing search for 'Pink Floyd'
[Debug] QobuzApiClient: GET /album/search?query=Pink+Floyd&limit=100

# Cache hit
[Debug] QobuzApiClient: Returning cached response for /album/search

# Rate limiting
[Warn] QobuzApiClient: Rate limited, waiting 60 seconds

# Errors
[Error] QobuzAuthenticationService: Authentication failed: Invalid credentials
[Error] QobuzParser: Failed to parse response: Unexpected format
```

### Log File Locations

- **Linux**: `/config/logs/lidarr.txt`
- **Windows**: `C:\ProgramData\Lidarr\logs\lidarr.txt`
- **Docker**: `/config/logs/lidarr.txt` (inside container)
- **macOS**: `~/.config/Lidarr/logs/lidarr.txt`

## Advanced Debugging

### Using QobuzCLI for Testing

```bash
# Test authentication
qobuzcli auth --show-token

# Test specific album
qobuzcli search --album-id 0060254735439

# Test with debug output
qobuzcli --debug search "test query"

# Export results for analysis
qobuzcli search "Pink Floyd" --output json > results.json
```

### Network Debugging

```bash
# Test API endpoint directly
curl -v "https://www.qobuz.com/api.json/0.2/album/search?query=test&app_id=285473059"

# Check DNS resolution
nslookup www.qobuz.com

# Test with different DNS
echo "nameserver 8.8.8.8" > /etc/resolv.conf
```

### Database Queries

```sql
-- Check indexer configuration
SELECT * FROM Indexers WHERE Implementation = 'Qobuz';

-- View recent searches
SELECT * FROM History WHERE EventType = 'AlbumSearched' 
ORDER BY Date DESC LIMIT 50;

-- Check download queue
SELECT * FROM DownloadHistory WHERE DownloadClient = 'Qobuz'
ORDER BY Date DESC;
```

## Getting Help

### Before Reporting Issues

1. **Search Existing Issues**
   - Check [GitHub Issues](https://github.com/richertunes/qobuzarr/issues)
   - Look for similar problems
   - Review closed issues too

2. **Gather Information**
   - Lidarr version
   - Plugin version
   - OS and architecture
   - Relevant log excerpts
   - Steps to reproduce

3. **Create Minimal Test Case**
   - Specific search that fails
   - Exact error message
   - Screenshots if UI-related

### Reporting Template

```markdown
**Environment:**
- Lidarr Version: 2.0.0.1234
- Plugin Version: 1.0.0
- OS: Ubuntu 22.04
- Install Method: Docker

**Issue Description:**
[Clear description of the problem]

**Steps to Reproduce:**
1. Go to Settings → Indexers
2. Search for "Pink Floyd"
3. See error

**Expected Behavior:**
Results should include "The Dark Side of the Moon"

**Actual Behavior:**
No results returned, error in logs

**Logs:**
```
[Relevant log entries]
```

**Additional Context:**
[Any other relevant information]
```

## Common Fixes Summary

| Issue | Quick Fix |
|-------|-----------|
| Auth fails | Re-enter credentials, check account |
| No results | Simplify search terms |
| Timeout | Reduce result limit |
| Rate limit | Lower request rate |
| Plugin missing | Check installation path |
| Slow search | Enable caching |
| Memory issues | Restart Lidarr |

## Prevention Tips

1. **Regular Maintenance**
   - Clear cache monthly
   - Update plugin regularly
   - Monitor log file size

2. **Optimal Settings**
   - Use reasonable search limits
   - Enable caching
   - Set appropriate quality filters

3. **Monitoring**
   - Watch for repeated errors
   - Track search success rate
   - Monitor API usage