> ⚠️ Partially aspirational (flagged 2026-05-31): Some environment variables, CLI commands, and configuration options described below (e.g., `QOBUZ_*` environment variables) are aspirational design documentation; see inline `<!-- TODO(docval): ... -->` markers for non-existent features. Core troubleshooting guidance for plugin loading, authentication, and downloads is accurate.

# Troubleshooting Guide

Comprehensive guide for diagnosing and resolving issues with Qobuzarr plugin installation, configuration, and operation.

## 🩺 Quick Diagnostics

### Plugin Health Check

**Step 1: Verify Plugin Loading**

1. Navigate to **System → Status** in Lidarr
2. Check **Plugins** section
3. Verify \"Qobuzarr\" appears with status \"Loaded\"
4. Note the version number (should be v0.5.11 or later)

**Step 2: Test Basic Connection**

1. Go to **Settings → Indexers → Qobuzarr**  
2. Click **Test** button
3. Should display \"Test successful\" message

**Step 3: Quick Log Check**

```bash
# Linux/Docker
tail -f /config/logs/lidarr.txt | grep -i qobuz

# Windows
Get-Content \"$env:ProgramData\\Lidarr\\logs\\lidarr.txt\" -Tail 50 | Select-String \"qobuz\"

# Look for these positive indicators:
# [Info] QobuzarrPlugin: Plugin initialized successfully
# [Info] QobuzAuthenticationService: Successfully authenticated
```

### System Requirements Verification

```bash
# Check .NET runtime
dotnet --version
# Should be 8.0 (plugin targets net8.0)

# Check available memory
free -h
# Should have at least 512MB available

# Check disk space
df -h /config
# Should have at least 100MB free
```

## 🔐 Authentication Issues

### Invalid Credentials Error

**Symptoms:**

- Test connection fails with \"Invalid credentials\"
- Search returns empty results  
- Log entries show \"401 Unauthorized\"

**Diagnosis Steps:**

1. **Verify Credentials Outside Plugin**:

   ```bash
   # Using QobuzCLI (if available)
   cd QobuzCLI
   dotnet run -- auth login --email your@email.com
   
   # Or test credentials on qobuz.com directly
   ```

2. **Check Credential Format**:

   ```csharp
   // Ensure email is properly formatted
   Email: user@domain.com (not user@domain)
   
   // Password should not contain:
   - Leading/trailing spaces
   - Special encoding characters
   - Non-ASCII characters
   ```

**Solutions:**

1. **Reset Credentials**:
   - Log into qobuz.com and verify account access
   - Reset password if necessary
   - Re-enter credentials in Lidarr settings
   - Click \"Test\" to verify

2. **Check Account Status**:
   - Verify Qobuz subscription is active
   - Check for regional restrictions
   - Ensure account wasn't suspended

3. **Advanced Troubleshooting**:<!-- TODO(docval): QOBUZ_LOG_LEVEL, QOBUZ_DEBUG_AUTH environment variables not found in codebase as of 2026-05-31. -->

   ```bash
   # Enable debug logging
   export QOBUZ_LOG_LEVEL=Debug
   export QOBUZ_DEBUG_AUTH=true

   # Restart Lidarr and check logs
   systemctl restart lidarr
   tail -f /config/logs/lidarr.txt | grep -A5 -B5 \"auth\"
   ```

### Session Expired Issues

**Symptoms:**

- Plugin works initially, fails after 24-48 hours
- \"Session expired\" or \"Token invalid\" in logs
- Intermittent authentication failures

**Solutions:**

1. **Force Session Refresh**:

   ```bash
   # Clear cached session data
   rm -f /config/plugins/qobuz_session.cache
   
   # In Lidarr UI: Settings → Indexers → Qobuz → Test → Save
   ```

2. **Configure Auto-Refresh** (if available):<!-- TODO(docval): QOBUZ_AUTO_REFRESH_SESSION, QOBUZ_SESSION_REFRESH_THRESHOLD environment variables not found in codebase as of 2026-05-31. -->

   ```bash
   # Environment variable for session management
   export QOBUZ_AUTO_REFRESH_SESSION=true
   export QOBUZ_SESSION_REFRESH_THRESHOLD=3600  # 1 hour before expiry
   ```

### Dynamic Authentication Issues

**Symptoms:**

- Dynamic credential extraction fails
- \"Failed to extract app credentials\" errors
- Browser automation issues

**Solutions:**

1. **Manual App Credentials**:<!-- TODO(docval): QOBUZ_APP_ID, QOBUZ_APP_SECRET environment variables not found in codebase as of 2026-05-31. -->

   ```bash
   # Set manual app credentials instead of dynamic
   export QOBUZ_APP_ID=\"285473059\"
   export QOBUZ_APP_SECRET=\"your_manual_secret\"
   ```

2. **Browser Path Configuration**:<!-- TODO(docval): QOBUZ_BROWSER_PATH, QOBUZ_BROWSER_ARGS environment variables not found in codebase as of 2026-05-31. -->

   ```bash
   # Specify browser path for dynamic extraction
   export QOBUZ_BROWSER_PATH=\"/usr/bin/chromium-browser\"
   export QOBUZ_BROWSER_ARGS=\"--headless --no-sandbox\"
   ```

## 🔍 Search Issues

### No Search Results

**Symptoms:**

- Manual searches return empty results
- Automatic searches find no releases
- \"No results found\" in activity logs

**Diagnosis:**

1. **Check Search Parameters**:

   ```bash
   # Test with simple, known albums
   # Try: \"Beatles\" / \"Abbey Road\"
   # Try: \"Miles Davis\" / \"Kind of Blue\"
   ```

2. **Verify Quality Filters**:
   - Remove minimum/maximum quality restrictions
   - Check if genre filters are too restrictive
   - Verify date range isn't excluding results

3. **Test Search Directly**:

   ```bash
   cd QobuzCLI
   dotnet run -- search \"Miles Davis Kind of Blue\" --debug
   ```

**Solutions:**

1. **Adjust Search Settings**:

   ```yaml
   Search Result Limit: 100 → 250
   Include Singles: No → Yes  
   Include Compilations: Yes
   Minimum Quality: Any (temporarily)
   ```

2. **Check Subscription Access**:
   - Log into qobuz.com
   - Search for the same content manually
   - Verify content is available in your region
   - Check subscription tier (Studio vs Sublime+)

### Search Performance Issues

**Symptoms:**

- Searches take >30 seconds to complete
- Frequent timeouts
- High CPU usage during searches

**Solutions:**

1. **Enable ML Optimization**:<!-- TODO(docval): QOBUZ_QUERY_INTELLIGENCE, QOBUZ_ADAPTIVE_RATE_LIMITING environment variables not found in codebase as of 2026-05-31. -->

   ```bash
   # Ensure query intelligence is enabled
   export QOBUZ_QUERY_INTELLIGENCE=true
   export QOBUZ_ADAPTIVE_RATE_LIMITING=true
   ```

2. **Adjust Rate Limiting**:<!-- TODO(docval): QOBUZ_INITIAL_RATE, QOBUZ_MAX_RATE, QOBUZ_RATE_INCREASE_FACTOR environment variables not found in codebase as of 2026-05-31. -->

   ```bash
   # Conservative settings for stability
   export QOBUZ_INITIAL_RATE=30
   export QOBUZ_MAX_RATE=300
   export QOBUZ_RATE_INCREASE_FACTOR=1.1
   ```

3. **Cache Configuration**:<!-- TODO(docval): QOBUZ_SEARCH_CACHE_SIZE, QOBUZ_ALBUM_CACHE_SIZE, QOBUZ_SEARCH_CACHE_TTL environment variables not found in codebase as of 2026-05-31. -->

   ```bash
   # Increase cache sizes for better performance
   export QOBUZ_SEARCH_CACHE_SIZE=100MB
   export QOBUZ_ALBUM_CACHE_SIZE=200MB
   export QOBUZ_SEARCH_CACHE_TTL=15m
   ```

### ML Optimization Issues

**Symptoms:**

- \"ML prediction failed\" errors
- Lower than expected API call reduction
- Memory errors during optimization

**Solutions:**

1. **Verify ML Models**:

   ```bash
   # Check if ML patterns file exists
   ls -la /config/plugins/ml-baseline-patterns.json
   
   # Verify file is valid JSON
   cat /config/plugins/ml-baseline-patterns.json | jq .
   ```

2. **Reset ML Performance**:<!-- TODO(docval): QOBUZ_RESET_ML_STATS environment variable not found in codebase as of 2026-05-31. -->

   ```bash
   # Clear ML statistics and restart learning
   rm -f /config/plugins/ml-performance.json
   export QOBUZ_RESET_ML_STATS=true
   ```

3. **Disable ML if Problematic**:<!-- TODO(docval): QOBUZ_QUERY_INTELLIGENCE environment variable not found in codebase as of 2026-05-31. -->

   ```bash
   # Temporary disable for troubleshooting
   export QOBUZ_QUERY_INTELLIGENCE=false

   # Test searches without ML optimization
   ```

## 📥 Download Issues

### Download Client Not Available

**Symptoms:**

- No \"Qobuzarr\" option in Download Clients list
- \"Download client unavailable\" errors
- Downloads fail immediately

**Solutions:**

1. **Verify Plugin Components**:

   ```bash
   # Check all required files are present
   ls -la /config/plugins/
   # Should include:
   # - Lidarr.Plugin.Qobuzarr.dll
   # - plugin.json
   ```

2. **Check Download Client Settings**:
   - Go to Settings → Download Clients
   - Click \"Add Download Client (+)\"  
   - Verify \"Qobuzarr\" appears in the list

### Download Failures

**Symptoms:**

- Downloads start but fail with errors
- \"Stream not available\" errors
- Incomplete downloads

**Solutions:**

1. **Check Quality Availability**:

   ```bash
   # Test quality detection
   cd QobuzCLI  
   dotnet run -- search \"album name\" --show-qualities
   ```

2. **Verify Download Permissions**:

   ```bash
   # Check download directory permissions
   ls -la /downloads/
   touch /downloads/test_write_permission
   rm /downloads/test_write_permission
   ```

3. **Network Connectivity**:

   ```bash
   # Test direct API access
   curl -I \"https://www.qobuz.com/api.json/0.2/track/get\"
   
   # Check for proxy/firewall issues
   wget --spider \"https://www.qobuz.com\"
   ```

## 🔧 Plugin Loading Errors

### Plugin Not Detected

**Symptoms:**

- Plugin doesn't appear in System → Plugins
- No Qobuzarr options in Indexers/Download Clients
- \"Plugin not found\" errors

**Solutions:**

1. **Verify Installation Path**:

   ```bash
   # Check plugin location
   ls -la /config/plugins/
   
   # Should contain:
   # Lidarr.Plugin.Qobuzarr.dll
   # plugin.json (with correct metadata)
   ```

2. **Check File Permissions**:

   ```bash
   # Ensure files are readable by Lidarr
   chmod 755 /config/plugins/
   chmod 644 /config/plugins/*
   chown -R lidarr:lidarr /config/plugins/
   ```

3. **Validate Plugin Manifest**:

   ```bash
   # Check plugin.json is valid
   cat /config/plugins/plugin.json | jq .
   
   # Should contain proper metadata:
   # - \"name\": \"Qobuzarr\"
   # - \"version\": \"0.0.12\"
   # - \"minimumLidarrVersion\": \"2.13.0.0\"
   ```

### Assembly Loading Issues

**Symptoms:**

- \"Could not load assembly\" errors
- \"Type not found\" exceptions
- Plugin loads but features don't work

**Solutions:**

1. **Check .NET Compatibility**:

   ```bash
   # Verify .NET runtime version
   dotnet --list-runtimes
   
   # Should include Microsoft.NETCore.App 6.0.x
   ```

2. **Validate Assembly Dependencies**:

   ```bash
   # Use dotnet to check assembly
   dotnet --fx-version 6.0.0 /config/plugins/Lidarr.Plugin.Qobuzarr.dll
   ```

3. **Clear Assembly Cache**:

   ```bash
   # Stop Lidarr
   systemctl stop lidarr
   
   # Clear .NET assembly cache
   rm -rf ~/.dotnet/shared/Microsoft.NETCore.App/*/
   
   # Restart
   systemctl start lidarr
   ```

## 📊 Performance Issues

### High Memory Usage

**Symptoms:**

- Lidarr memory usage >2GB
- OutOfMemory exceptions
- System slowdowns during searches

**Solutions:**

1. **Configure Memory Limits**:<!-- TODO(docval): QOBUZ_SEARCH_CACHE_SIZE, QOBUZ_ALBUM_CACHE_SIZE, QOBUZ_MAX_CONCURRENT_SEARCHES environment variables not found in codebase as of 2026-05-31. -->

   ```bash
   # Limit cache sizes
   export QOBUZ_SEARCH_CACHE_SIZE=25MB
   export QOBUZ_ALBUM_CACHE_SIZE=50MB

   # Reduce concurrent operations
   export QOBUZ_MAX_CONCURRENT_SEARCHES=2
   ```

2. **Enable Garbage Collection**:

   ```bash
   # More aggressive GC for memory-constrained systems
   export DOTNET_gcServer=0
   export DOTNET_GCHeapHardLimit=500000000  # 500MB limit
   ```

### High CPU Usage

**Symptoms:**

- CPU usage >80% during searches
- System responsiveness issues
- Slow search completion

**Solutions:**

1. **Reduce ML Processing**:<!-- TODO(docval): QOBUZ_QUERY_INTELLIGENCE, QOBUZ_SIMPLE_THRESHOLD, QOBUZ_MEDIUM_THRESHOLD environment variables not found in codebase as of 2026-05-31. -->

   ```bash
   # Disable or reduce ML optimization
   export QOBUZ_QUERY_INTELLIGENCE=false

   # Or use simpler thresholds
   export QOBUZ_SIMPLE_THRESHOLD=0
   export QOBUZ_MEDIUM_THRESHOLD=2
   ```

2. **Limit Concurrent Operations**:<!-- TODO(docval): QOBUZ_MAX_CONCURRENT_SEARCHES, QOBUZ_MAX_CONCURRENT_DOWNLOADS environment variables not found in codebase as of 2026-05-31. -->

   ```bash
   export QOBUZ_MAX_CONCURRENT_SEARCHES=1
   export QOBUZ_MAX_CONCURRENT_DOWNLOADS=2
   ```

## 🌐 Network Issues

### Connection Timeouts

**Symptoms:**

- \"Request timeout\" errors
- Long delays before failures
- Intermittent connectivity

**Solutions:**

1. **Adjust Timeout Settings**:<!-- TODO(docval): QOBUZ_API_TIMEOUT, QOBUZ_HTTP_TIMEOUT environment variables not found in codebase as of 2026-05-31. -->

   ```bash
   export QOBUZ_API_TIMEOUT=60  # Increase to 60 seconds
   export QOBUZ_HTTP_TIMEOUT=45
   ```

2. **Check Network Path**:

   ```bash
   # Test connectivity to Qobuz
   traceroute www.qobuz.com
   ping -c 4 www.qobuz.com
   
   # Test HTTPS access
   curl -I https://www.qobuz.com/api.json/0.2/album/search
   ```

### Proxy Configuration Issues

**Symptoms:**

- Works without proxy, fails with proxy
- \"Proxy authentication required\" errors
- DNS resolution failures

**Solutions:**

1. **Configure Proxy Settings**:

   ```bash
   export HTTP_PROXY=http://proxy.company.com:8080
   export HTTPS_PROXY=https://proxy.company.com:8443
   export NO_PROXY=localhost,127.0.0.1,.local
   ```

2. **Proxy Authentication**:

   ```bash
   export PROXY_USER=username
   export PROXY_PASS=password
   
   # Or in URL format
   export HTTP_PROXY=http://username:password@proxy.company.com:8080
   ```

## 📋 Log Analysis

### Enable Comprehensive Logging

```bash
# Set detailed logging levels<!-- TODO(docval): QOBUZ_LOG_LEVEL, QOBUZ_DEBUG_*, QOBUZ_DEVELOPER_MODE, QOBUZ_DEBUG_ALL, QOBUZ_ENABLE_PROFILING, QOBUZ_COLLECT_METRICS environment variables not found in codebase as of 2026-05-31. -->
export QOBUZ_LOG_LEVEL=Debug

# Enable component-specific debugging
export QOBUZ_DEBUG_API=true
export QOBUZ_DEBUG_AUTH=true
export QOBUZ_DEBUG_SEARCH=true
export QOBUZ_DEBUG_ML=true
export QOBUZ_DEBUG_CACHE=true

# Restart Lidarr
systemctl restart lidarr
```

### Key Log Patterns

**Successful Operations**:

```
[Info] QobuzarrPlugin: Plugin initialized successfully
[Info] QobuzAuthenticationService: Successfully authenticated user
[Debug] QobuzIndexer: Search completed: 25 results in 1.2s
[Debug] CompiledMLQueryOptimizer: Predicted complexity: Simple (confidence: 0.95)
```

**Common Errors**:

```
[Error] QobuzAuthenticationService: Authentication failed: Invalid credentials
[Warn] QobuzApiClient: Rate limited, waiting 60 seconds
[Error] QobuzIndexer: Search failed: Network timeout
[Error] CompiledMLQueryOptimizer: ML prediction failed: Model not loaded
```

### Log File Locations

```bash
# Linux/Docker
/config/logs/lidarr.txt         # Main log
/config/logs/lidarr.debug.txt   # Debug log (if enabled)

# Windows  
%ProgramData%\\Lidarr\\logs\\lidarr.txt
%ProgramData%\\Lidarr\\logs\\lidarr.debug.txt

# macOS
~/.config/Lidarr/logs/lidarr.txt
```

## 🔬 Advanced Debugging

### Enable Developer Tools<!-- TODO(docval): QOBUZ_DEVELOPER_MODE, QOBUZ_DEBUG_ALL, QOBUZ_LOG_HTTP_REQUESTS, QOBUZ_LOG_HTTP_RESPONSES, QOBUZ_ENABLE_PROFILING, QOBUZ_PROFILE_OUTPUT_PATH environment variables not found in codebase as of 2026-05-31. -->

```bash
# Enable all debugging features
export QOBUZ_DEVELOPER_MODE=true
export QOBUZ_DEBUG_ALL=true
export QOBUZ_LOG_HTTP_REQUESTS=true
export QOBUZ_LOG_HTTP_RESPONSES=true

# Enable performance profiling
export QOBUZ_ENABLE_PROFILING=true
export QOBUZ_PROFILE_OUTPUT_PATH=/tmp/qobuz-profile.json
```

### Memory Debugging

```bash
# Enable memory leak detection
export DOTNET_EnableWriteXorExecute=0
export DOTNET_DbgEnableMiniDump=1
export DOTNET_DbgMiniDumpType=2

# Monitor memory usage
dotnet-dump collect -p $(pidof Lidarr)
dotnet-dump analyze core_20250124_123456
```

### Performance Analysis

```bash
# Enable detailed performance metrics
export QOBUZ_COLLECT_METRICS=true
export QOBUZ_METRICS_OUTPUT=/tmp/qobuz-metrics.json

# Use dotnet-trace for profiling
dotnet-trace collect --process-id $(pidof Lidarr) --format chromium
```

## 🆘 Getting Help

### Before Seeking Support

1. **Collect Information**:

   ```bash
   # System information
   dotnet --info
   cat /etc/os-release
   
   # Plugin information  
   ls -la /config/plugins/
   cat /config/plugins/plugin.json
   
   # Recent logs
   tail -n 100 /config/logs/lidarr.txt > qobuz-debug-logs.txt
   ```

2. **Test with CLI**:

   ```bash
   # Verify basic functionality works
   cd QobuzCLI
   dotnet run -- auth login
   dotnet run -- search \"Test Artist\"
   ```

### Support Channels

1. **GitHub Issues**: [qobuzarr/issues](https://github.com/RicherTunes/qobuzarr/issues)
   - Include system information
   - Attach log files
   - Describe steps to reproduce

2. **GitHub Discussions**: [qobuzarr/discussions](https://github.com/RicherTunes/qobuzarr/discussions)
   - General questions and help
   - Feature requests
   - Community support

3. **Discord**: [Lidarr Discord](https://discord.gg/lidarr)
   - Real-time support
   - Community troubleshooting

### Creating Effective Bug Reports

**Template:**

```markdown
## System Information
- OS: Ubuntu 20.04 LTS
- .NET Runtime: 8.0.x
- Lidarr Version: 3.x.x (plugins branch)
- Qobuzarr Version: 0.5.11

## Issue Description
[Clear description of the problem]

## Steps to Reproduce
1. Step one
2. Step two  
3. Expected vs actual result

## Logs
[Include relevant log entries - use pastebin for large logs]

## Configuration
[Include relevant settings - mask sensitive information]
```

---

*Most issues can be resolved through proper configuration and log analysis. When in doubt, start with the Quick Diagnostics section and work through each troubleshooting category systematically.*
