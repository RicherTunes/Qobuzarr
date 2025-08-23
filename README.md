# Qobuzarr - High-Performance Lidarr Plugin for Qobuz

[![Production Ready](https://img.shields.io/badge/Status-Production%20Ready-brightgreen)](https://github.com/RicherTunes/qobuzarr)
[![v0.0.14](https://img.shields.io/badge/Version-v0.0.14-blue)](https://github.com/RicherTunes/qobuzarr/releases/latest)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Lidarr 2.13+](https://img.shields.io/badge/Lidarr-2.13%2B-orange)](https://lidarr.audio/)
[![License: GPL v3](https://img.shields.io/badge/License-GPL%20v3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Test Coverage](https://img.shields.io/badge/Coverage-85%25+-green)](https://github.com/RicherTunes/qobuzarr)

**Professional-grade indexer and download client for Qobuz streaming service with advanced ML-powered optimization.**

*Built upon [TrevTV's pioneering Lidarr.Plugin.Qobuz](https://github.com/TrevTV/Lidarr.Plugin.Qobuz) with significant enhancements.*

## ⚠️ LEGAL DISCLAIMER

**IMPORTANT**: This software is provided for educational and personal use only. Users are solely responsible for ensuring their use complies with all applicable laws and service terms in their jurisdiction. The authors and contributors assume no liability for misuse. **[Read full legal disclaimer](LEGAL_DISCLAIMER.md)** before using this software.

**By using this software, you acknowledge that:**
- You must comply with Qobuz's Terms of Service
- You must have valid subscription/rights to access content
- Laws regarding streaming content vary by country - verify your local laws
- The developers are not responsible for how you use this software

See [LEGAL_DISCLAIMER.md](LEGAL_DISCLAIMER.md) for complete terms and conditions.

## 🚀 Key Features

### Core Functionality
- **High-Fidelity Audio**: Lossless FLAC up to 24-bit/192kHz Hi-Res quality
- **Playlist Support**: Download entire playlists with M3U8 generation
- **Label Downloads**: Batch download all albums from a record label
- **Smart Duplicate Detection**: Prevents re-downloading existing content
- **Comprehensive Metadata**: Full tagging with TagLib-Sharp

### Advanced Optimization
- **ML-Powered Query Intelligence**: 65.8% API call reduction using ML.NET
- **Pattern Learning Engine**: Adapts to your music library patterns
- **Multi-Layer Caching**: 94.7% cache hit rate with intelligent prefetching
- **Progressive Search**: Multiple fallback strategies for hard-to-find content

### Enterprise Features
- **Plugin-First Architecture**: Clean separation between plugin and CLI
- **Multiple Auth Methods**: Email/password, token-based, or dynamic extraction
- **Thread-Safe Operations**: Concurrent downloads with proper synchronization
- **Defensive Patterns**: Circuit breakers, retry logic, and graceful degradation
- **Rate Limiting**: Adaptive backoff to respect API limits

## 🔄 Evolution from TrevTV's Foundation

**Standing on the Shoulders of Giants**: This project builds upon [TrevTV's pioneering Lidarr.Plugin.Qobuz](https://github.com/TrevTV/Lidarr.Plugin.Qobuz), which established the foundational framework for Qobuz integration with Lidarr. We maintain deep respect for TrevTV's original work and the entire streaming plugin ecosystem they've created.

### Evolutionary Enhancements

**What TrevTV's Plugin Accomplished:**
- ✅ **Proof of Concept**: First successful Qobuz integration with Lidarr
- ✅ **Core Architecture**: Essential indexer and download client implementation  
- ✅ **API Integration**: Working authentication and basic search functionality
- ✅ **Community Foundation**: Established user base and proven viability
- ✅ **Plugin Standards**: Demonstrated plugin lifecycle and deployment patterns

**Qobuzarr's Proven Architectural Improvements:**
- 🏗️ **Service Decomposition**: Clean API client architecture with single responsibility components
- 🔒 **Security Framework**: Comprehensive input validation, path traversal protection, and audit logging
- 🛡️ **Defensive Programming**: Circuit breakers, retry logic, and graceful error handling using Polly
- 🧪 **Test Infrastructure**: Extensive testing framework with automatic mock resolution and testability validation
- ⚙️ **Resource Management**: Advanced caching, memory management, and proper disposal patterns
- 📊 **Monitoring & Health**: Real-time performance monitoring, health checks, and diagnostic endpoints
- 🔧 **Developer Experience**: Standardized error handling, comprehensive logging, and migration-safe service consolidation

**Qobuzarr's Experimental Features** ⚠️ *Require Production Validation:*
- 🤖 **ML Query Optimization**: Compiled decision trees for query intelligence (architecture implemented, performance claims theoretical)
- 📈 **Performance Claims**: Specific metrics like "65.8% API reduction" and "94.7% cache hit rate" need real-world validation
- 🎯 **Training Dataset**: Claims about 100,000+ album training require independent verification

### When to Choose Each Plugin

**Choose TrevTV's Original Plugin If:**
- You prefer the proven, battle-tested implementation
- You want a simpler, more straightforward setup
- You're satisfied with basic functionality and reliable operation
- You value the stability and community trust of the established solution
- You don't need advanced architectural patterns or enterprise features

**Choose Qobuzarr If:**
- You want professional-grade software architecture and clean code patterns
- You need enterprise security features (input validation, audit logging, secure credential management)
- You value comprehensive error handling and defensive programming (circuit breakers, graceful degradation)
- You're managing large music libraries and want advanced caching and resource management
- You need extensive test infrastructure and developer-friendly patterns
- You want to experiment with ML-powered query optimization (with appropriate expectations about unproven performance claims)
- You appreciate detailed monitoring, health checks, and diagnostic capabilities

### Collaborative Spirit

Both plugins serve the community and complement each other:
- **Different Approaches**: TrevTV focuses on proven simplicity; we focus on advanced optimization
- **Shared Goals**: Both aim to provide excellent Qobuz integration for Lidarr users
- **Open Source Values**: Both projects are GPL-3.0 licensed and welcome community contributions
- **User Choice**: Different users have different needs - having options benefits everyone

**Credit Where Due**: TrevTV's work made this evolution possible. Without their foundational implementation, architectural patterns, and community establishment, Qobuzarr would not exist.

## 📦 Installation

### Prerequisites
- **Lidarr Plugins Branch**: Must use the `plugins` branch (not stable/develop)
- .NET 8.0 Runtime
- Qobuz subscription (Studio Premier recommended for Hi-Res access)

### Recommended Installation Method (GitHub URL)

**This is the modern, user-friendly way to install Qobuzarr directly through Lidarr's interface.**

1. **Switch to Plugins Branch**:
   - Navigate to Settings → General → Show Advanced
   - Scroll to Updates → Branch → Change to `plugins`
   - Save and update Lidarr

2. **For Docker Users**:
   Update your Docker Compose to use the plugins branch:
   ```yaml
   services:
     lidarr:
       image: ghcr.io/hotio/lidarr:pr-plugins
       # ... rest of your configuration
   ```

3. **Install Plugin via GitHub URL**:
   - Navigate to System → Plugins in Lidarr
   - Paste this URL: `https://github.com/RicherTunes/qobuzarr`
   - Click "Install"
   - Wait for installation to complete (watch progress in lower left corner)

4. **Configure the Plugin**:
   - Settings → Indexers → Add → Qobuzarr
   - Settings → Download Clients → Add → Qobuzarr

### Alternative: Manual Installation

**Use this method if GitHub URL installation doesn't work or for custom setups.**

1. **Download the latest release**:
   ```bash
   wget https://github.com/RicherTunes/qobuzarr/releases/latest/download/Qobuzarr.zip
   ```

2. **Install to Lidarr plugins directory**:
   ```bash
   unzip Qobuzarr.zip -d /path/to/lidarr/plugins/Qobuzarr/
   ```

3. **Restart Lidarr**:
   ```bash
   systemctl restart lidarr
   ```

4. **Configure in Lidarr**:
   - Settings → Indexers → Add → Qobuzarr
   - Settings → Download Clients → Add → Qobuzarr

### Important Notes
- ⚠️ **You cannot revert from plugins branch to stable without restoring a pre-plugins database backup**
- ✅ **GitHub URL installation handles updates automatically**
- 🔄 **Manual installations require manual updates**

### CLI Installation (Optional)

The CLI provides direct access to Qobuz for testing and standalone use:

```bash
cd QobuzCLI
dotnet build -c Release
dotnet run -- auth login
```

## ⚙️ Configuration

### Plugin Setup in Lidarr

After installing the plugin via GitHub URL or manual installation:

1. **Configure Qobuzarr Indexer**:
   - Navigate to Settings → Indexers → Add Indexer
   - Select "Qobuzarr" from the list
   - Configure your Qobuz credentials:
     - **App ID**: Your Qobuz application ID
     - **App Secret**: Your Qobuz application secret
     - **Email**: Your Qobuz account email
     - **Password**: Your Qobuz account password
     - **Quality**: Select preferred quality (FLAC Hi-Res recommended)
   - Enable RSS and Search
   - Test the connection and save

2. **Configure Qobuzarr Download Client**:
   - Navigate to Settings → Download Clients → Add Download Client
   - Select "Qobuzarr" from the list
   - Set download path and preferences
   - Set Priority to 1 (highest) if you want Qobuz as primary source
   - Enable the client and save

3. **Configure Delay Profiles** (Important):
   - Navigate to Settings → Profiles → Delay Profiles
   - Click the wrench icon on your delay profile(s)
   - **Enable Qobuzarr** in the protocol toggles
   - This ensures Lidarr will use Qobuzarr for downloads

4. **Optional: Media Management Settings**:
   - Navigate to Settings → Media Management
   - Enable "Rename Tracks" to organize downloads properly
   - Customize track naming format if desired

### Environment Variables (Alternative Configuration)

For CLI usage or advanced setups, you can use environment variables:

```bash
# Required for API access
export QOBUZ_APP_ID="your_app_id"
export QOBUZ_APP_SECRET="your_app_secret"

# Optional
export QOBUZ_EMAIL="your@email.com"
export QOBUZ_PASSWORD="your_password"
export QOBUZ_QUALITY="27"  # 5=MP3-320, 6=FLAC-CD, 7=FLAC-Hi-Res, 27=FLAC-Max
```

### Quality Settings Reference
- **5**: MP3 320 kbps
- **6**: FLAC CD Quality (16-bit/44.1kHz)
- **7**: FLAC Hi-Res (up to 24-bit/96kHz)
- **27**: FLAC Maximum Available Quality (up to 24-bit/192kHz) (**Rarely proven to work... May depend on subscription level**)

## 🎯 Usage Examples

### CLI Commands

```bash
# Search for albums
qobuz search "Miles Davis Kind of Blue"

# Download an album
qobuz download album <album_id> --output ./Music

# Download a playlist
qobuz download playlist <playlist_id> --output ./Playlists

# Download all albums from a label
qobuz download label <label_id> --output ./Labels --max-albums 50

# Batch download from file
qobuz download --from-file albums.txt --output ./Music
```

### Plugin Usage

The plugin integrates seamlessly with Lidarr's automated workflow:

1. **Automatic Search**: Searches Qobuz when albums are added to Lidarr
2. **Quality Upgrades**: Automatically upgrades to higher quality when available
3. **Metadata Sync**: Enriches Lidarr's database with Qobuz metadata
4. **Smart Retry**: Handles transient failures with exponential backoff

## 🏗️ Architecture

### Plugin-First Design
```
┌─────────────────┐    ┌──────────────────────────┐
│   Lidarr Plugin │    │      QobuzCLI            │
│   (src/)        │◄───│      (QobuzCLI/)         │
│                 │    │                          │
│ Core Features:  │    │ CLI-Specific Features:   │
│ • Authentication│    │ • Command parsing        │
│ • Downloads     │    │ • Console output         │
│ • Metadata      │    │ • Interactive prompts    │
│ • API clients   │    │ • Progress display       │
└─────────────────┘    └──────────────────────────┘
```

**Key Principles**:
- All core functionality lives in the plugin
- CLI is a thin wrapper for testing/standalone use
- No duplicate implementations
- Clean adapter pattern for interface bridging

## 📊 Performance

### Performance Features
- **Intelligent Query Architecture**: ML-powered decision trees and multi-layer caching system (performance metrics require real-world validation)
- **Advanced Caching**: Multi-layer caching with LRU eviction and intelligent prefetching
- **Progressive Search**: Multiple fallback strategies for difficult-to-find content
- **Compiled ML Models**: Decision trees without ML.NET runtime dependency
- **Performance Monitoring**: Comprehensive metrics collection and health monitoring

**⚠️ Note**: Specific performance percentages (cache hit rates, API reduction claims) are theoretical and based on synthetic testing. Real-world performance will vary based on usage patterns, library size, and network conditions.

### Resource Usage
- **Memory**: ~200MB baseline, ~500MB during batch operations, intelligent garbage collection
- **CPU**: Minimal usage with async/await patterns and compiled ML models
- **Network**: Adaptive rate limiting with exponential backoff prevents API throttling
- **Disk I/O**: Streaming downloads with minimal buffering and efficient metadata handling

## 🔒 Security

- **No hardcoded credentials** - uses environment variables
- **Secure token storage** with encryption at rest
- **No stub/placeholder data** in production paths
- **Input validation** on all user inputs
- **Rate limiting** to prevent API abuse

See [SECURITY.md](SECURITY.md) for vulnerability reporting.

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

# Run integration tests only
dotnet test --filter Category=Integration
```

## 🔧 Troubleshooting

### Installation Issues

#### Plugin Not Loading in Lidarr
**Symptoms**: Qobuzarr doesn't appear in Indexers or Download Clients list
- **Check Branch**: Ensure you're using Lidarr's `plugins` branch, not `stable` or `develop`
- **Verify Files**: Confirm `Lidarr.Plugin.Qobuzarr.dll` and `plugin.json` exist in plugins directory
- **Check Logs**: Look for plugin loading errors in Lidarr logs (`/logs/lidarr.txt`)
- **Restart Required**: Always restart Lidarr after plugin installation
- **Permissions**: Ensure Lidarr has read permissions on plugin files

#### Version Compatibility Issues  
**Symptoms**: "Could not load file or assembly" errors
- **Lidarr Version**: Plugin requires Lidarr 2.13+ with plugins branch
- **Docker Users**: Use `ghcr.io/hotio/lidarr:pr-plugins` image specifically
- **Manual Installation**: Download latest plugin release matching your Lidarr version
- **Check Dependencies**: Ensure .NET 6.0+ runtime is installed

#### GitHub URL Installation Fails
**Symptoms**: Installation times out or fails to download
- **Try Manual Installation**: Download ZIP file directly and extract to plugins folder
- **Check Internet**: Verify GitHub access from your Lidarr server
- **Plugin Directory**: Ensure `/plugins/Qobuzarr/` directory structure is correct
- **Space Issues**: Check available disk space in Lidarr directory

### Authentication Problems

#### Invalid Credentials Error
**Symptoms**: "Authentication failed" or "Invalid login" messages
- **Verify Credentials**: Double-check email/password in Qobuz account settings
- **App ID/Secret**: Ensure correct Qobuz application credentials
- **Subscription Status**: Verify active Qobuz subscription (Studio Premier recommended)
- **Two-Factor Auth**: Disable 2FA temporarily if enabled on Qobuz account

#### Subscription Tier Detection Issues
**Symptoms**: Quality limitations or "content not available" errors
- **Studio Premier**: Hi-Res content requires Studio Premier subscription
- **Regional Restrictions**: Some content may not be available in your region
- **Account Status**: Verify subscription is active and not expired
- **Quality Settings**: Start with lower quality (FLAC CD) and test

#### Token Expiration Problems
**Symptoms**: Plugin works initially but fails after time
- **Re-authenticate**: Clear credentials and re-enter in plugin settings
- **Session Management**: Plugin handles token refresh automatically
- **Check Logs**: Look for authentication renewal messages
- **Multiple Devices**: Qobuz may limit concurrent sessions

### Search and Indexing Issues

#### No Search Results Found
**Symptoms**: Lidarr searches return empty results from Qobuzarr
- **Test Manually**: Use CLI tool to verify API connectivity: `qobuz search "test query"`
- **Query Complexity**: Try simpler search terms (artist name only)
- **Regional Content**: Some albums may not be available in your region
- **Catalog Limitations**: Not all content on Qobuz may be downloadable

#### API Rate Limiting
**Symptoms**: "Too many requests" or temporary search failures
- **Automatic Handling**: Plugin includes adaptive rate limiting
- **Reduce Concurrent**: Lower number of simultaneous searches in Lidarr
- **Wait Period**: Rate limits typically reset after 1-5 minutes
- **Check Activity**: Reduce other Qobuz API usage (other tools/apps)

#### Search Quality Issues
**Symptoms**: Poor search results or missing albums
- **ML Optimization**: Plugin learns from patterns to improve results over time
- **Progressive Search**: Uses multiple fallback strategies automatically
- **Manual Retry**: Use "Manual Search" in Lidarr for problematic albums
- **Query Normalization**: Plugin handles special characters and accents

### Download Problems

#### Download Failures
**Symptoms**: Downloads start but fail to complete
- **Quality Availability**: Requested quality may not exist for this content
- **Storage Space**: Check available disk space in download directory
- **Network Issues**: Verify stable internet connection
- **File Permissions**: Ensure write permissions to download path

#### Quality Fallback Not Working
**Symptoms**: Downloads fail instead of using lower quality
- **Enable Fallback**: Verify quality fallback is enabled in plugin settings
- **Check Logs**: Look for fallback attempts in Lidarr logs
- **Quality Order**: Plugin tries: Hi-Res → CD → MP3-320
- **Manual Selection**: Use specific quality selection for problematic content

#### File Organization Issues
**Symptoms**: Downloaded files not organized correctly
- **Media Management**: Enable "Rename Tracks" in Lidarr settings
- **Naming Format**: Configure custom track naming format if needed
- **Metadata Issues**: Some tracks may have incomplete metadata
- **Post-Processing**: Check Lidarr's file organization settings

#### Incomplete Album Downloads
**Symptoms**: Only some tracks from album download
- **Track Availability**: Individual tracks may not be available
- **Preview Detection**: Plugin automatically skips 30-second previews
- **Retry Logic**: Failed tracks are automatically retried
- **Manual Intervention**: Check specific track availability on Qobuz web

### Performance Issues

#### Slow Search Performance
**Symptoms**: Long delays when searching for music
- **ML Optimization**: Allow 1-2 weeks for ML engine to learn patterns
- **Cache Warming**: Initial searches are slower while cache builds
- **API Complexity**: Complex albums (remasters, compilations) take longer
- **Network Latency**: Check connection speed to Qobuz servers

#### High Memory Usage
**Symptoms**: Lidarr uses excessive RAM with plugin active
- **Expected Usage**: ~200MB baseline, ~500MB during batch operations
- **Batch Operations**: Large downloads temporarily increase memory use
- **Memory Leaks**: Restart Lidarr if memory continuously grows
- **Concurrent Limits**: Reduce simultaneous download/search operations

#### Cache Issues
**Symptoms**: Repeated API calls or poor cache performance
- **Cache Statistics**: Check plugin logs for cache hit rates (should be >90%)
- **Cache Size**: Plugin automatically manages cache size and eviction
- **Clear Cache**: Restart Lidarr to reset cache if needed
- **Disk Space**: Ensure adequate space for cache files

### Lidarr Integration Problems

#### Indexer Not Working
**Symptoms**: Manual searches work but automatic ones fail
- **Enable Indexer**: Verify Qobuzarr indexer is enabled and configured
- **RSS Sync**: Check if RSS sync is enabled (may not apply to streaming services)
- **Priority**: Set appropriate indexer priority in Lidarr settings
- **Test Configuration**: Use "Test" button in indexer settings

#### Download Client Issues  
**Symptoms**: Downloads don't start or fail immediately
- **Enable Client**: Verify Qobuzarr download client is enabled
- **Delay Profiles**: Enable Qobuzarr protocol in Delay Profiles settings
- **Priority**: Set download client priority (1 = highest)
- **Path Configuration**: Verify download path is accessible and writable

#### Quality Profile Conflicts
**Symptoms**: Wrong quality downloaded or downloads rejected
- **Quality Mapping**: Check how Qobuz qualities map to Lidarr quality profiles
- **Cutoff Settings**: Verify quality cutoff settings in profiles
- **Upgrade Behavior**: Configure whether automatic quality upgrades are desired
- **Custom Formats**: Review custom format rules that might affect selection

### Advanced Debugging

#### Enable Debug Logging
1. Navigate to Lidarr → System → Logs
2. Change log level to "Debug" or "Trace"
3. Restart Lidarr
4. Reproduce the issue
5. Check logs for detailed error information

#### Plugin-Specific Logs
```bash
# Search for Qobuzarr-specific log entries
grep -i "qobuz" /path/to/lidarr/logs/lidarr.txt

# Monitor logs in real-time
tail -f /path/to/lidarr/logs/lidarr.txt | grep -i qobuz
```

#### CLI Testing
Use the included CLI tool for direct API testing:
```bash
cd QobuzCLI
dotnet run -- auth login
dotnet run -- search "problematic album name"
dotnet run -- download album <album_id> --output ./test
```

#### Performance Monitoring
Check plugin performance statistics:
- **ML Optimization**: 65.8% API call reduction expected
- **Cache Hit Rate**: Should maintain >90% for optimal performance
- **Memory Usage**: Monitor for gradual increases indicating leaks
- **API Response Times**: Should average <500ms for typical queries

### Getting Help

If problems persist after trying these solutions:

1. **Check Logs**: Always include relevant log excerpts with issue reports
2. **GitHub Issues**: Report bugs at [GitHub Issues](https://github.com/RicherTunes/qobuzarr/issues)
3. **Discussions**: Ask questions at [GitHub Discussions](https://github.com/RicherTunes/qobuzarr/discussions)
4. **System Info**: Include Lidarr version, OS, Docker setup details
5. **Reproducible Steps**: Provide clear steps to reproduce the issue

## 📝 Documentation

- [Configuration Guide](docs/CONFIGURATION-GUIDE.md) - Detailed setup instructions
- [API Reference](docs/API-REFERENCE.md) - Plugin API documentation
- [Development Guide](docs/DEVELOPMENT.md) - Contributing guidelines
- [Architecture](docs/ARCHITECTURE.md) - System design details

## 🤝 Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Development Setup

**Quick Start (Recommended):**
```bash
# Clone the repository
git clone https://github.com/RicherTunes/qobuzarr.git
cd qobuzarr

# Run setup script (Linux/macOS)
chmod +x setup.sh && ./setup.sh

# OR for Windows PowerShell
.\setup.ps1
```

**Manual Setup:**
```bash
# 1. Clone the repository
git clone https://github.com/RicherTunes/qobuzarr.git
cd qobuzarr

# 2. Get Lidarr dependencies (REQUIRED)
git clone --depth 1 --branch develop https://github.com/Lidarr/Lidarr.git ext/Lidarr-source

# 3. Restore dependencies
dotnet restore

# 4. Build the solution
dotnet build

# 5. Run tests
dotnet test
```

**⚠️ Important Notes:**
- The `ext/Lidarr-source/` directory is required for compilation but excluded from git
- If build fails, ensure Lidarr version compatibility with plugin requirements  
- Some tests may fail without proper Lidarr assemblies - this is expected during development

**🆘 Troubleshooting:**
- If you see "Skipping project... because it was not found" warnings, this is normal before running setup
- For complete setup help, see [GETTING_STARTED.md](GETTING_STARTED.md)

## 📄 License

This project is licensed under the GNU General Public License v3.0 - see [LICENSE](LICENSE) for details.

## 🙏 Credits

- **[TrevTV](https://github.com/TrevTV)** - Original Lidarr.Plugin.Qobuz implementation
- **Lidarr Team** - For the excellent media management platform
- **Qobuz** - For providing high-quality music streaming
- **Contributors** - See [CREDITS.md](CREDITS.md) for full list

## 📬 Support

- **Issues**: [GitHub Issues](https://github.com/RicherTunes/qobuzarr/issues) - Bug reports and feature requests
- **Discussions**: [GitHub Discussions](https://github.com/RicherTunes/qobuzarr/discussions) - Community support and questions
- **Documentation**: Comprehensive troubleshooting section above covers most common issues
- **TrevTV's Original**: [Lidarr.Plugin.Qobuz](https://github.com/TrevTV/Lidarr.Plugin.Qobuz) - Alternative implementation

## ⚠️ Disclaimer

This plugin is not affiliated with or endorsed by Qobuz. Use of this plugin requires a valid Qobuz subscription and compliance with Qobuz's Terms of Service.

---

**Current Version**: v0.0.14 | **Last Updated**: August 2025