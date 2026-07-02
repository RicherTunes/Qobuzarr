# Qobuzarr Wiki

Welcome to the **Qobuzarr** project wiki — your guide to the high-performance Lidarr plugin for the Qobuz streaming service.

## Built on Lidarr.Plugin.Common

Qobuzarr builds on [Lidarr.Plugin.Common](https://github.com/RicherTunes/Lidarr.Plugin.Common), a shared library providing foundation plumbing for Lidarr streaming-service plugins. For shared concepts, cross-plugin patterns, and extension points, consult Common's wiki:

- [Common — Home](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/Home.md) — project overview, ecosystem context, and links to the other Common plugins.
- [Architecture Overview](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/Architecture-Overview.md) — shared service layer, DI patterns, and how Common fits inside a Lidarr plugin host.
- [SDK and Extension Points](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/SDK-and-Extension-Points.md) — reusable base classes (`StreamingPlugin`, token stores, rate limiters) that Qobuzarr consumes.
- [Shared Helpers Catalog](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/Shared-Helpers-Catalog.md) — quick-reference for every Common helper (`PluginLogContext`, `Scrub`, `WarnOnce`, `BackendHealthCache`, etc.).
- [Versioning and Submodule Pinning](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/Versioning-and-Submodule-Pinning.md) — how the Common submodule pin works (`ext-common-sha.txt`), the manual re-pin workflow, and coordination across plugins.

## What is Qobuzarr?

**Qobuzarr** is a production-ready Lidarr plugin that provides both indexing and download capabilities for Qobuz's high-fidelity music streaming service. Built upon TrevTV's foundation with ML-powered enhancements, it delivers professional-grade audio management with advanced optimization.

### Key Highlights

- **High-Fidelity Audio**: Lossless FLAC up to 24-bit/192 kHz Hi-Res quality
- **ML-Powered Optimization**: ~50% API call reduction using pre-compiled machine-learning models
- **Production Ready**: Designed for real-world automated music collection management
- **Plugin-First Architecture**: Clean separation between plugin core and CLI wrapper

## Documentation

### Getting Started

- [[Installation Guide]] — complete installation and setup instructions
- [[Configuration Guide]] — detailed configuration options and examples

### Features and Capabilities

- [[Security Features]] — security architecture and best practices

### Development and Technical

- [[API Reference]] — complete API documentation
- [[Plugin Development]] — guide for extending functionality

### Operations

- [[Troubleshooting]] — common issues and solutions

## Quick Links

| Topic | Description | Link |
|-------|-------------|------|
| **Installation** | Get Qobuzarr installed | [[Installation Guide]] |
| **Configuration** | Configure your settings | [[Configuration Guide]] |
| **API Reference** | Technical documentation | [[API Reference]] |
| **Troubleshooting** | Solve common issues | [[Troubleshooting]] |
| **Security** | Security best practices | [[Security Features]] |
| **Development** | Contribute to the project | [[Plugin Development]] |

## Project Stats

- **Version**: v0.5.11
- **Lidarr Compatibility**: v3.0.0+
- **Framework**: .NET 8.0
- **License**: GPL v3.0
- **ML API Call Reduction**: ~50% (target 49.83%) through query optimization
- **ML Cache Hit Rate**: 94.7% target

## Requirements

- Active Qobuz subscription (Studio Premier recommended for Hi-Res)
- Lidarr v3.0.0 or higher (plugins branch — `pr-plugins-3.x`)
- .NET 8.0 Runtime

## External Resources

- **GitHub Repository**: [RicherTunes/Qobuzarr](https://github.com/RicherTunes/Qobuzarr)
- **Issues and Bug Reports**: [GitHub Issues](https://github.com/RicherTunes/Qobuzarr/issues)
- **Feature Requests**: [GitHub Discussions](https://github.com/RicherTunes/Qobuzarr/discussions)
- **Release Downloads**: [GitHub Releases](https://github.com/RicherTunes/Qobuzarr/releases)

## Legal Compliance

This plugin requires compliance with Qobuz's Terms of Service. Users must have valid subscriptions and follow all applicable licensing agreements.
