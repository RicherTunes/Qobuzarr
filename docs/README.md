# Qobuzarr Documentation

Qobuzarr is a high-performance Lidarr plugin for Qobuz streaming service with ML-powered optimization. This documentation is organized by audience.

## User Documentation

| Document | Description |
|----------|-------------|
| [Getting Started](user/GETTING_STARTED.md) | Installation and initial setup |
| [Is It Working?](user/IS_IT_WORKING.md) | Verification checklist for your installation |
| [Configuration Guide](user/CONFIGURATION-GUIDE.md) | All settings explained |
| [Usage Examples](user/USAGE-EXAMPLES.md) | Common usage patterns |
| [Quick Reference](user/QUICK-REFERENCE.md) | Cheat sheet for common tasks |
| [FAQ](user/FAQ.md) | Frequently asked questions |
| [Troubleshooting](user/TROUBLESHOOTING.md) | Common issues and solutions |
| [Upgrade Guide](user/UPGRADE_GUIDE.md) | Upgrading between versions |

## Technical Documentation

| Document | Description |
|----------|-------------|
| [Architecture](ARCHITECTURE.md) | High-level system design |
| [Docker Guide](DOCKER-GUIDE.md) | Container deployment |
| [Live Testing Guide](LIVE-TESTING-GUIDE.md) | Testing with real Lidarr instances |
| [Logging Scopes](LOGGING_SCOPES.md) | Log configuration and filtering |

### Shared Library Integration

| Document | Description |
|----------|-------------|
| [Quick Reference](SHARED-LIBRARY-QUICK-REFERENCE.md) | Quick start for shared library |
| [Technical Reference](SHARED-LIBRARY-TECHNICAL-REFERENCE.md) | Detailed API documentation |
| [Collaboration Guide](SHARED-LIBRARY-COLLABORATION-GUIDE.md) | Contributing to shared library |

## Development

| Document | Description |
|----------|-------------|
| [Development Guide](development/DEVELOPMENT.md) | Local development setup |
| [Plugin Development](development/PLUGIN-DEVELOPMENT-GUIDE.md) | Creating Lidarr plugins |
| [Testing Guide](development/COMPREHENSIVE-TESTING-GUIDE.md) | Writing and running tests |
| [Test Environment](development/TEST-ENVIRONMENT-SETUP.md) | Setting up test environments |
| [Testing](TESTING.md) | Test overview |

### Infrastructure & CI/CD

| Document | Description |
|----------|-------------|
| [Build Troubleshooting](infrastructure/BUILD-FAILURE-TROUBLESHOOTING.md) | Common build issues |
| [CI/CD Optimization](infrastructure/CI-CD-OPTIMIZATION-GUIDE.md) | Pipeline optimization |
| [Plugins Branch Compatibility](infrastructure/PLUGINS_BRANCH_COMPATIBILITY.md) | Lidarr plugins branch info |

## Operations & Deployment

| Document | Description |
|----------|-------------|
| [Deployment Guide](operations/DEPLOYMENT-GUIDE.md) | Production deployment |
| [Monitoring Guide](operations/MONITORING-GUIDE.md) | Setting up monitoring |
| [Monitoring Dashboard](operations/MONITORING-DASHBOARD-SETUP.md) | Dashboard configuration |
| [Pre-Release Checklist](operations/PRE_RELEASE_CHECKLIST.md) | Release preparation |
| [Release Notes](RELEASE_NOTES.md) | Version history |

## Reference

| Document | Description |
|----------|-------------|
| [Wiki](wiki/Home.md) | Comprehensive wiki documentation |
| [Advanced Topics](advanced/) | Advanced configuration and usage |
| [Metadata Templates](samples/) | Example metadata JSON templates |
| [Security](security/) | Security best practices |

## Related Resources

### Shared Library

Qobuzarr uses the [Lidarr.Plugin.Common](https://github.com/RicherTunes/Lidarr.Plugin.Common) shared library for:

- OAuth 2.0 authentication and token management
- Streaming API request builders with resilience
- Download orchestration and metadata tagging
- Caching and adaptive rate limiting

See the [shared library documentation](https://github.com/RicherTunes/Lidarr.Plugin.Common/tree/main/docs) for:

- [Build Your First Plugin](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/docs/tutorials/BUILD_YOUR_FIRST_PLUGIN.md) - Plugin development tutorial
- [Key Services Reference](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/docs/reference/KEY_SERVICES.md) - HTTP, auth, caching APIs
- [Compatibility Matrix](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/docs/COMPATIBILITY.md) - Version compatibility

### Sister Plugins

- [Tidalarr](https://github.com/RicherTunes/tidalarr) - Tidal streaming plugin (similar architecture)
- [Brainarr](https://github.com/RicherTunes/brainarr) - AI-powered music recommendations

### Project Resources

- [README](../README.md) - Project overview
- [CLAUDE.md](../CLAUDE.md) - Claude Code development guidance
- [CHANGELOG](../CHANGELOG.md) - Release history
