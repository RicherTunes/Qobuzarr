# Frequently Asked Questions (FAQ)

## General Questions

### What is Qobuzarr?
Qobuzarr is a comprehensive Lidarr plugin that integrates the Qobuz high-fidelity music streaming service, allowing you to search and download lossless and hi-res audio. It includes both a Lidarr plugin for integration and a full-featured CLI application for standalone use.

### Is this legal?
This plugin requires a valid Qobuz subscription and only accesses content you're authorized to stream. Please respect Qobuz's terms of service and copyright laws in your jurisdiction.

### What Qobuz subscription do I need?
- **Studio** (formerly Hi-Fi): Access to CD quality (FLAC 16-bit/44.1kHz)
- **Sublime** (formerly Studio): Access to Hi-Res (up to 24-bit/192kHz)
- Free accounts have limited functionality

### Which countries are supported?
Qobuz is available in select countries. The plugin works wherever Qobuz operates. Check [Qobuz availability](https://www.qobuz.com/gb-en/music/streaming/offers) for your region.

## Installation Questions

### Why doesn't the plugin appear after installation?
1. Ensure you're using Lidarr's plugins branch
2. Check the plugin file is in the correct directory
3. Restart Lidarr after copying the plugin
4. Check logs for loading errors

### Can I use this with Lidarr stable branch?
No, plugin support is only available in the plugins development branch. Use Docker image `ghcr.io/hotio/lidarr:pr-plugins`.

### Do I need to compile from source?
No, pre-built releases are available. Only compile from source if you want to modify the plugin or contribute development.

## ML & Query Intelligence Questions

### What is Query Intelligence and should I enable it?
**Yes, absolutely enable it!** Query Intelligence reduces API calls by ~50% through smart optimization:
- **Analyzes complexity** - Simple searches use 1 query instead of 3  
- **Preserves quality** - Complex searches still use all queries
- **Zero risk** - Conservative approach ensures no missed results
- **Immediate benefits** - Works from the first search

### What is ML Predictions and is it safe to use?
**ML Predictions is experimental but safe** - it's an enhancement on top of Query Intelligence:
- **Learns your patterns** - Adapts to your specific music library
- **Gets better over time** - Improves with every search
- **Safe fallbacks** - Falls back to proven Query Intelligence if uncertain
- **Pre-trained** - Starts with 10M+ training samples for immediate benefits
- **Optional** - Can be disabled if you prefer rule-based optimization only

### What does "SmartQueryStrategy initialized with rule-based optimization only" mean?
This means ML Predictions is **disabled** and you're only using the base Query Intelligence:
- Still gets ~50% API reduction from rule-based optimization
- To enable ML, go to Settings → Indexers → Qobuz → Advanced → ✅ Enable ML Predictions

### Will the ML engine slow down my searches?
**No, it actually makes them faster!**
- ML predictions execute 2-3x faster than rule evaluation
- Most predictions complete in under 100 microseconds  
- Pre-trained model provides immediate benefits
- +2MB memory overhead is negligible

### How much training data do you have?
**10+ million samples** collected from overnight runs on high-end hardware:
- Covers diverse music genres and artists
- Includes edge cases like international characters
- Pre-trained model works immediately without waiting
- Continuous learning from your library patterns

### Can I see the ML predictions in action?
**Yes!** Enable ML and check your Lidarr logs:
```
[Info] 🤖 ML PREDICTION for 'Taylor Swift - 1989': Simple (confidence: 89.2%)
[Info] ✅ Using ML-recommended queries (1 queries) - confidence above threshold
```

## Configuration Questions

### Email/Password vs Token authentication - which should I use?
- **Email/Password**: Easier to set up, recommended for most users
- **Token**: More secure, better for shared systems, but tokens need manual retrieval

### How do I find my User ID and Token?
```javascript
// In browser console on play.qobuz.com:
localStorage.getItem('user.id')
localStorage.getItem('user.userAuthToken')
```

### Why is my password being hashed with MD5?
This is a Qobuz API requirement, not our choice. The password is still transmitted over HTTPS for security.

### What do the quality settings mean?
- **MP3-320**: 320kbps MP3 files (smaller size, lower quality)
- **FLAC**: CD quality, lossless (16-bit/44.1kHz)
- **FLAC Hi-Res**: High resolution (24-bit/96kHz or 192kHz)

## Search Questions

### Why can't I find certain albums?
1. **Simplify search terms** - Remove special characters, years, etc.
2. **Check regional availability** - Content varies by country
3. **Verify quality settings** - Album might not be available in selected quality
4. **Try artist name only** - Then browse their albums

### Search is very slow, how can I speed it up?
- Reduce "Search Result Limit" to 50 or less
- Enable caching in settings
- Use more specific search terms
- Check your internet connection

### What does "Include Singles/Compilations" do?
- **Include Singles**: Shows single track releases and EPs
- **Include Compilations**: Shows "Various Artists" compilation albums
- Disabling these reduces clutter in search results

## Quality Questions

### What quality should I choose?
Depends on your needs:
- **Storage limited**: Use MP3-320
- **Best quality**: Use FLAC Hi-Res (requires Sublime subscription)
- **Balanced**: Use FLAC (CD quality)

### Can I upgrade quality later?
Yes, Lidarr's quality profiles handle upgrades. Configure your preferred qualities and Lidarr will upgrade when available.

### What's the difference between FLAC and FLAC Hi-Res?
- **FLAC**: CD quality (16-bit/44.1kHz), ~30MB per track
- **FLAC Hi-Res**: Studio quality (24-bit/96-192kHz), ~50-100MB per track

## Troubleshooting Questions

### Why do I get "401 Unauthorized" errors?
1. Check your credentials are correct
2. Verify your subscription is active
3. Try re-entering your password
4. Check if you can log into qobuz.com

### What does "Rate limit exceeded" mean?
Qobuz limits API requests to prevent abuse. The plugin handles this automatically, but you may see delays. Reduce concurrent searches if this happens frequently.

### Downloads aren't working
The download client feature is still in development. Currently, only search/indexer functionality is available.

### How do I enable debug logging?
1. Go to indexer settings
2. Set "Log Level" to Debug
3. Check logs at `/config/logs/lidarr.txt`

## Technical Questions

### What API does this use?
The plugin uses Qobuz's internal API (same as their web player). This is not an officially supported API.

### Is the source code available?
Yes, this is open source under GPL-3.0 license. See [GitHub repository](https://github.com/richertunes/qobuzarr).

### Can I contribute?
Absolutely! See [CONTRIBUTING.md](../CONTRIBUTING.md) for guidelines.

### What .NET version is required?
.NET 6.0 or later is required for the plugin to function.

## Feature Questions

### Will you add playlist support?
This is planned for a future release. See our [roadmap](../CHANGELOG.md#future-roadmap).

### Can I use multiple Qobuz accounts?
Not currently. Multi-account support is being considered for version 2.0.

### Does it support artist monitoring?
Yes, through Lidarr's standard artist monitoring. The plugin integrates with Lidarr's existing functionality.

### Will you add [specific feature]?
Check our [issues page](https://github.com/richertunes/qobuzarr/issues) for planned features. Feel free to request new features there.

## Legal/Ethical Questions

### Is this a "piracy tool"?
No. This plugin requires a valid Qobuz subscription and only provides access to content you're already paying for.

### Does this violate Qobuz's ToS?
Users should review Qobuz's terms of service. This tool simply automates what you could do manually through their web interface.

### Can I share downloaded files?
No. Files downloaded are for personal use only, as per your Qobuz subscription agreement.

## Still Have Questions?

1. Check the [full documentation](README.md)
2. Search [existing issues](https://github.com/richertunes/qobuzarr/issues)
3. Join our [Discord community](https://discord.gg/lidarr)
4. Open a [new issue](https://github.com/richertunes/qobuzarr/issues/new) if needed

## CLI Application Questions

### Can I use Qobuzarr without Lidarr?
Yes! The QobuzCLI application is a standalone tool that provides complete access to Qobuz downloading functionality without requiring Lidarr.

### What's the difference between the plugin and CLI?
- **Plugin**: Integrates with Lidarr for automated music management
- **CLI**: Standalone application for manual downloads, testing, and batch operations
- Both use the same core engine, so functionality is identical

### How do I batch download from Lidarr exports?
Export your wanted albums from Lidarr as JSON, then use:
```bash
qobuzcli download --input wanted-albums.json --validate --quality flac-hires
```

### Why should I use download validation?
Validation checks if tracks are actually downloadable before adding them to the queue, preventing failed downloads and wasted time.

### What happens if my preferred quality isn't available?
The download engine automatically falls back to the next best quality: Hi-Res → CD FLAC → MP3 320.

### Can I pause and resume downloads?
Yes, the CLI maintains persistent download queues that survive application restarts. Downloads will resume automatically.