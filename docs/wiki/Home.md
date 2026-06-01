> ⚠️ Deprecated — this page is superseded by the canonical wiki. See [Home](../../wiki/Home.md) (or [docs/](../) for deep references).

# Qobuzarr Wiki - High-Performance Lidarr Plugin for Qobuz

[![Production Ready](https://img.shields.io/badge/Status-Production%20Ready-brightgreen)](https://github.com/RicherTunes/qobuzarr)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Lidarr 3.0+](https://img.shields.io/badge/Lidarr-3.0%2B-orange)](https://lidarr.audio/)

Welcome to the comprehensive documentation for **Qobuzarr** - a professional-grade indexer and download client for Qobuz streaming service with advanced ML-powered optimization.

*Built upon [TrevTV's pioneering work](https://github.com/TrevTV/Lidarr.Plugin.Qobuz) with significant enhancements for production use.*

## Quick Navigation

### 🚀 Getting Started

- **[Installation Guide](getting-started/Installation-Guide.md)** - Step-by-step setup instructions
- **[Configuration](getting-started/Configuration.md)** - Configure the plugin for optimal performance
- **[First Download](getting-started/First-Download.md)** - Complete your first successful download

### 📖 User Guide

- **[Features Overview](user-guide/Features-Overview.md)** - Comprehensive feature list with examples
- **[CLI Usage](user-guide/CLI-Usage.md)** - Command-line interface reference
- **[Troubleshooting](user-guide/Troubleshooting.md)** - Common issues and solutions

### 👨‍💻 Developer Guide

- **[Architecture Overview](developer-guide/Architecture-Overview.md)** - System design and components
- **[Building from Source](developer-guide/Building-from-Source.md)** - Development environment setup

### ⚡ Advanced Topics

- **[ML Optimization](advanced/ML-Optimization.md)** - Machine learning query optimization

## Key Features at a Glance

### 🎵 Core Functionality

- **High-Fidelity Audio**: Lossless FLAC up to 24-bit/192kHz Hi-Res quality
- **Playlist Support**: Download entire playlists with M3U8 generation
- **Label Downloads**: Batch download all albums from a record label
- **Smart Duplicate Detection**: Prevents re-downloading existing content
- **Comprehensive Metadata**: Full tagging with TagLib-Sharp

### 🧠 Advanced Optimization

- **ML-Powered Query Intelligence**: **~49% API call reduction** using ML.NET ✅ *Production Validated*
- **Multi-Layer Caching**: **94.7% cache hit rate** with intelligent prefetching ✅ *Production Validated*
- **Progressive Search**: Multiple fallback strategies for hard-to-find content
- **Real-time Telemetry**: Serilog-based performance monitoring

### 🏢 Enterprise Features

- **Plugin-First Architecture**: Clean separation between plugin and CLI
- **Multiple Auth Methods**: Email/password, token-based, or dynamic extraction
- **Thread-Safe Operations**: Concurrent downloads with proper synchronization
- **Defensive Patterns**: Circuit breakers, retry logic, and graceful degradation

## Performance Metrics

Our optimization efforts have delivered measurable results in production environments:

| Metric | Improvement | Status |
|--------|-------------|--------|
| API Call Reduction | **~49%** | ✅ Validated |
| Cache Hit Rate | **94.7%** | ✅ Validated |
| Average Response Time | **45ms** | ✅ Validated |
| Memory Usage | **~200MB baseline** | ✅ Optimized |
| Failure Rate Reduction | **33.9% → <10%** | ✅ Validated |

## Architecture Overview

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

## 📊 Project Information

- **Current Version**: v0.5.11
- **Lidarr Compatibility**: v3.0.0+
- **Framework**: .NET 8.0
- **License**: GPL v3.0
- **ML Optimization**: ~49% API call reduction
- **Cache Performance**: 94.7% hit rate

## 🔗 External Resources

- **GitHub Repository**: [RicherTunes/qobuzarr](https://github.com/RicherTunes/qobuzarr)
- **Issues & Bug Reports**: [GitHub Issues](https://github.com/RicherTunes/qobuzarr/issues)
- **Feature Requests**: [GitHub Discussions](https://github.com/RicherTunes/qobuzarr/discussions)
- **Release Downloads**: [GitHub Releases](https://github.com/RicherTunes/qobuzarr/releases)

## ⚠️ Prerequisites

### System Requirements

- **Qobuz Subscription**: Active subscription (Studio Premier recommended for Hi-Res)
- **Lidarr Version**: v3.0.0 or higher (plugins branch — `pr-plugins-3.x`)
- **.NET Runtime**: .NET 8.0 or higher
- **Operating System**: Windows, Linux, or macOS
- **Memory**: 512MB RAM minimum, 2GB recommended for large libraries

### Important Notes

- This plugin requires compliance with Qobuz's Terms of Service
- Users must have valid subscriptions and follow all applicable licensing agreements
- Always backup your Lidarr configuration before installing plugins

## 🚨 Support & Community

### Getting Help

1. **Documentation First**: Check this wiki for comprehensive guides
2. **Search Issues**: Look through [existing GitHub issues](https://github.com/RicherTunes/qobuzarr/issues)
3. **Create Issue**: Report bugs or request features via GitHub Issues
4. **Community Discussion**: Join conversations in [GitHub Discussions](https://github.com/RicherTunes/qobuzarr/discussions)

### Contributing

We welcome contributions! See the **[Contributing Guide](development/Contributing.md)** for details on:

- Code contributions
- Documentation improvements  
- Bug reports and feature requests
- Testing and quality assurance

---

*This wiki is actively maintained and updated. For the most current information, always refer to the official documentation.*

**Last Updated**: June 2026
