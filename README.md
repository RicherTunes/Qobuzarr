# Qobuzarr - High-Performance Lidarr Plugin for Qobuz

[![Production Ready](https://img.shields.io/badge/Status-Production%20Ready-brightgreen)](https://github.com/RicherTunes/qobuzarr)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Lidarr 3.0+](https://img.shields.io/badge/Lidarr-3.0%2B-orange)](https://lidarr.audio/)
[![License: GPL v3](https://img.shields.io/badge/License-GPL%20v3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)

**Professional-grade indexer and download client for Qobuz streaming service with advanced ML-powered optimization.**

*Built upon [TrevTV's pioneering Lidarr.Plugin.Qobuz](https://github.com/TrevTV/Lidarr.Plugin.Qobuz) with significant enhancements.*

## 🚀 Key Features

### Core Functionality

- **High-Fidelity Audio**: Lossless FLAC up to 24-bit/192kHz Hi-Res quality
- **Playlist Support**: Download entire playlists with M3U8 generation
- **Label Downloads**: Batch download all albums from a record label
- **Comprehensive Metadata**: Full tagging with TagLib-Sharp, synced lyrics (.lrc), and LRCLIB fallback

### Advanced Optimization

- **ML-Powered Query Intelligence**: 49.8% API call reduction using ML.NET ✅ *Production Validated*
- **Pattern Learning Engine**: Adapts to your music library patterns with A/B testing
- **Multi-Layer Caching**: 94.7% cache hit rate with intelligent prefetching ✅ *Production Validated*
- **Progressive Search**: Multiple fallback strategies for hard-to-find content
- **Real-time Telemetry**: NLog-based performance monitoring with automatic validation

### Enterprise Features

- **Plugin-First Architecture**: Clean separation between plugin and CLI
- **Multiple Auth Methods**: Email/password, token-based, or dynamic extraction
- **Thread-Safe Operations**: Concurrent downloads with proper synchronization
- **Defensive Patterns**: Circuit breakers, retry logic, and graceful degradation
- **Rate Limiting**: Adaptive backoff to respect API limits

## 📦 Installation

### Prerequisites

- Lidarr v3.0.0+ (plugins branch — `pr-plugins-3.x`; minimum host version 3.0.0.4855)
- .NET 8.0 SDK (the plugin targets `net8.0`; the host runs .NET 8)
- Qobuz subscription (Studio Premier recommended for Hi-Res)

> **Install via the Lidarr UI** (recommended): Settings → Plugins → paste
> `https://github.com/RicherTunes/qobuzarr` → Install, then restart Lidarr.
> The manual ZIP method below is for offline/air-gapped installs.

### Plugin Installation

1. **Download the latest release**:

   ```bash
   wget https://github.com/RicherTunes/qobuzarr/releases/latest/download/Qobuzarr.zip
   ```

2. **Install to Lidarr**:

   ```bash
   unzip Qobuzarr.zip -d /path/to/lidarr/plugins/
   ```

3. **Restart Lidarr**:

   ```bash
   systemctl restart lidarr
   ```

4. **Configure in Lidarr**:
   - Settings → Indexers → Add → Qobuzarr
   - Settings → Download Clients → Add → Qobuzarr

### CLI Installation (Optional)

The CLI provides direct access to Qobuz for testing and standalone use:

```bash
cd QobuzCLI
dotnet build -c Release
dotnet run -- auth login
```

For detailed, step-by-step installation instructions (Docker, manual, and air-gapped scenarios), see the **[Installation Guide](wiki/Installation-Guide.md)**.

## ⚙️ Configuration

### Environment Variables

```bash
# Required for API access
export QOBUZ_APP_ID="your_app_id"
export QOBUZ_APP_SECRET="your_app_secret"

# Optional
export QOBUZ_EMAIL="your@email.com"
export QOBUZ_PASSWORD="your_password"
export QOBUZ_QUALITY="27"  # 5=MP3-320, 6=FLAC-CD, 7=FLAC-Hi-Res, 27=FLAC-Max
```

### Lidarr Settings

1. **Indexer Configuration**:
   - Enable RSS: Yes
   - Enable Search: Yes
   - Categories: Music

2. **Download Client Configuration**:
   - Priority: 1 (highest)
   - Enable: Yes

For full authentication setup (email/password, user-ID/token, dynamic extraction), quality presets, and performance-tuning options, see the **[Configuration Guide](wiki/Configuration-Guide.md)**.

## 🎯 Usage Examples

### CLI Commands

```bash
# From the QobuzCLI directory, run commands via dotnet
cd QobuzCLI

# Search for albums
dotnet run -- search "Miles Davis Kind of Blue"

# Download an album
dotnet run -- download album <album_id> --output ./Music

# Download a playlist
dotnet run -- download playlist <playlist_id> --output ./Playlists

# Manage download queues
dotnet run -- queue list

# View download history
dotnet run -- history stats
```

### Plugin Usage

The plugin integrates seamlessly with Lidarr's automated workflow:

1. **Automatic Search**: Searches Qobuz when albums are added to Lidarr
2. **Quality Fallback**: Automatically falls back to lower quality when the requested format is unavailable
3. **Full Metadata Tagging**: Tags downloaded files with artist, album, track info, and lyrics

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

### Lidarr API Compatibility (Build Selection)

- By default, local builds target the Lidarr release API for wider developer compatibility.
- CI auto-detects and flips to the plugins-branch API when assemblies are available.
- You can override locally with MSBuild: `-p:UsePluginsBranch=true`.
- During build, the project prints which branch is used (see `Qobuzarr.csproj` target `LogBranchSelection`).

For a deeper dive into service decomposition, DI patterns, and the shared-library layer, see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and the [shared-library architecture overview](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/Architecture-Overview.md).

## 📊 Performance

### Optimization Results

| Metric | Result |
|--------|--------|
| API call reduction (ML) | **49.8%** ✅ *Production Validated* |
| Cache hit rate | **94.7%** ✅ *Production Validated* |
| Baseline failure rate | **33.9%** → **<10%** with progressive search |
| Albums validated | **100,000+** |

### Resource Usage

- Memory: ~200MB baseline, ~500MB during batch operations
- CPU: Minimal usage with async/await patterns
- Network: Adaptive rate limiting prevents API throttling
- Disk I/O: Streaming downloads with minimal buffering

For ML model details, prediction pipeline internals, and tuning knobs, see [docs/advanced/ML-OPTIMIZATION-GUIDE.md](docs/advanced/ML-OPTIMIZATION-GUIDE.md).

## 🔒 Security

- **No hardcoded credentials** — uses environment variables and secure stores
- **Secure token storage** with at-rest encryption (DPAPI / Keychain / Secret Service)
- **No stub/placeholder data** in production paths
- **Input validation** on all user inputs
- **Rate limiting** to prevent API abuse

See [SECURITY.md](SECURITY.md) for vulnerability reporting. The wiki's **[Security Features](wiki/Security-Features.md)** page covers the full defence-in-depth model.

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

# Run integration tests only
dotnet test --filter Category=Integration
```

For test-environment setup, the Docker E2E harness, and the flaky-test policy, see [docs/TESTING.md](docs/TESTING.md) and [docs/development/COMPREHENSIVE-TESTING-GUIDE.md](docs/development/COMPREHENSIVE-TESTING-GUIDE.md).

## 📚 Documentation

For a concise map of all documentation trees, see [docs/DOCUMENTATION_MAP.md](docs/DOCUMENTATION_MAP.md) and the [wiki](wiki/Home.md).

### Wiki (in-repo)

The [wiki/](wiki/) directory contains long-form guides that complement this README:

| Page | What it covers |
|------|---------------|
| [Home](wiki/Home.md) | Wiki index, project overview, quick links |
| [Installation Guide](wiki/Installation-Guide.md) | Docker, manual, and air-gapped installs |
| [Configuration Guide](wiki/Configuration-Guide.md) | Auth methods, quality presets, performance tuning |
| [Troubleshooting](wiki/Troubleshooting.md) | Diagnostics, log analysis, common fixes |
| [API Reference](wiki/API-Reference.md) | Public APIs, services, and interfaces |
| [Plugin Development](wiki/Plugin-Development.md) | Extending Qobuzarr, ML models, custom integrations |
| [Security Features](wiki/Security-Features.md) | Defence-in-depth model, credential protection |

### Expanded Docs (`docs/`)

The [docs/](docs/) tree is organised by audience — see [docs/README.md](docs/README.md) for the full index. Highlights:

| Area | Key documents |
|------|--------------|
| **User** | [Getting Started](docs/user/GETTING_STARTED.md) · [Is It Working?](docs/user/IS_IT_WORKING.md) · [FAQ](docs/user/FAQ.md) · [Quick Reference](docs/user/QUICK-REFERENCE.md) · [Usage Examples](docs/user/USAGE-EXAMPLES.md) · [Upgrade Guide](docs/user/UPGRADE_GUIDE.md) |
| **Architecture** | [System Design](docs/ARCHITECTURE.md) · [API Reference](docs/architecture/API-REFERENCE.md) · [Performance Tuning](docs/architecture/PERFORMANCE-TUNING.md) · [Service Migration](docs/architecture/SERVICE-MIGRATION-GUIDE.md) |
| **Development** | [Development Guide](docs/development/DEVELOPMENT.md) · [Plugin Dev Guide](docs/development/PLUGIN-DEVELOPMENT-GUIDE.md) · [Test Environment Setup](docs/development/TEST-ENVIRONMENT-SETUP.md) · [Testing Guide](docs/development/COMPREHENSIVE-TESTING-GUIDE.md) |
| **Security** | [Security Architecture](docs/security/SECURITY-ARCHITECTURE.md) · [API Security](docs/security/API-SECURITY-GUIDE.md) · [ML Model Security](docs/security/ML-MODEL-SECURITY.md) |
| **Operations** | [Deployment Guide](docs/operations/DEPLOYMENT-GUIDE.md) · [Monitoring](docs/operations/MONITORING-GUIDE.md) · [Pre-Release Checklist](docs/operations/PRE_RELEASE_CHECKLIST.md) · [Release Notes](docs/RELEASE_NOTES.md) |
| **Infrastructure** | [Build Troubleshooting](docs/infrastructure/BUILD-FAILURE-TROUBLESHOOTING.md) · [CI/CD Optimization](docs/infrastructure/CI-CD-OPTIMIZATION-GUIDE.md) · [Plugins Branch Compatibility](docs/infrastructure/PLUGINS_BRANCH_COMPATIBILITY.md) |
| **Advanced** | [ML Optimization Guide](docs/advanced/ML-OPTIMIZATION-GUIDE.md) · [Quality Management](docs/advanced/QUALITY-MANAGEMENT.md) |
| **Shared Library** | [Quick Reference](docs/SHARED-LIBRARY-QUICK-REFERENCE.md) · [Technical Reference](docs/SHARED-LIBRARY-TECHNICAL-REFERENCE.md) · [Collaboration Guide](docs/SHARED-LIBRARY-COLLABORATION-GUIDE.md) |

### Shared Library — Lidarr.Plugin.Common

Qobuzarr builds on [Lidarr.Plugin.Common](https://github.com/RicherTunes/Lidarr.Plugin.Common), a shared library providing foundation plumbing (OAuth token stores, streaming API builders, download orchestration, adaptive rate-limiting, structured-logging helpers) for all RicherTunes Lidarr streaming plugins.

**Common wiki pages** (cross-repo links):

- [Common — Home](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/Home.md) — project overview and ecosystem context
- [Architecture Overview](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/Architecture-Overview.md) — shared service layer and DI patterns
- [SDK and Extension Points](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/SDK-and-Extension-Points.md) — reusable base classes (`StreamingPlugin`, token stores, rate limiters)
- [Shared Helpers Catalog](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/Shared-Helpers-Catalog.md) — quick-reference for every Common helper (`PluginLogContext`, `Scrub`, `WarnOnce`, `BackendHealthCache`, etc.)
- [Versioning and Submodule Pinning](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/Versioning-and-Submodule-Pinning.md) — how the `ext-common-sha.txt` pin and nightly bump workflow work
- [Build Your First Plugin](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/docs/tutorials/BUILD_YOUR_FIRST_PLUGIN.md) — tutorial for creating a new streaming plugin
- [Key Services Reference](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/docs/reference/KEY_SERVICES.md) — HTTP, auth, caching APIs

### Sister Plugins

Qobuzarr shares its Common library and architectural patterns with:

- **[Tidalarr](https://github.com/RicherTunes/tidalarr)** — Tidal streaming plugin
- **[Brainarr](https://github.com/RicherTunes/brainarr)** — AI-powered music recommendations

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
- Some tests may fail without proper Lidarr assemblies — this is expected during development

**🆘 Troubleshooting:**

- If you see "Skipping project... because it was not found" warnings, this is normal before running setup
- For complete setup help, see [CLAUDE.md](CLAUDE.md) (build commands and architecture)
- For build-specific issues, see [docs/infrastructure/BUILD-FAILURE-TROUBLESHOOTING.md](docs/infrastructure/BUILD-FAILURE-TROUBLESHOOTING.md)

## 📄 License

This project is licensed under the GNU General Public License v3.0 — see [LICENSE](LICENSE) for details.

## 🙏 Credits

- **[TrevTV](https://github.com/TrevTV)** — Original Lidarr.Plugin.Qobuz implementation
- **[TypNull](https://github.com/TypNull)** — CI/CD methodology and Docker assembly approach ([Tubifarry](https://github.com/TypNull/Tubifarry))
- **Lidarr Team** — For the excellent media management platform
- **Qobuz** — For providing high-quality music streaming
- **Contributors** — See [CREDITS.md](CREDITS.md) for full list

## 📬 Support

- **Issues**: [GitHub Issues](https://github.com/RicherTunes/qobuzarr/issues)
- **Discussions**: [GitHub Discussions](https://github.com/RicherTunes/qobuzarr/discussions)
- **Wiki**: [Project Wiki](https://github.com/RicherTunes/qobuzarr/wiki)

## ⚠️ Disclaimer

Qobuzarr is an independent, open-source project developed by RicherTunes for **educational and research purposes** — to study plugin architecture, streaming APIs, and the Lidarr ecosystem.

- **Not affiliated with, authorized, or endorsed by Qobuz.** "Qobuz" and related marks are trademarks of their respective owners; used here descriptively only.
- Intended for **personal use with your own valid Qobuz subscription**. You are solely responsible for complying with Qobuz's Terms of Service and all laws applicable in your jurisdiction.
- Provided **"as is", without warranty of any kind; use at your own risk** (see [LICENSE](LICENSE)). The authors accept no liability for misuse or for any consequences of use.
- Do not use this software to infringe copyright or to access or redistribute content you are not licensed to use.

---

**Current Version**: v0.5.11 | **Last Updated**: May 2026
