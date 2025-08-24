# Credits and Acknowledgments

## Primary Attribution

### TrevTV - Original Creator
**[TrevTV's Lidarr.Plugin.Qobuz](https://github.com/TrevTV/Lidarr.Plugin.Qobuz)**

Qobuzarr is built upon the excellent foundational work of TrevTV's original Lidarr Qobuz plugin. TrevTV pioneered the integration of Qobuz streaming service with Lidarr, creating the essential framework that made this enhanced version possible.

**TrevTV's original contributions include:**
- Initial Qobuz API integration and authentication system
- Core indexer and download client architecture for Lidarr
- Basic metadata handling and file organization
- Plugin lifecycle management and Lidarr integration patterns
- Community establishment and early user adoption

**Repository**: https://github.com/TrevTV/Lidarr.Plugin.Qobuz  
**License**: GPL-3.0 (maintained in this project)

---

## Qobuzarr Enhancements by RicherTunes

Building upon TrevTV's foundation, this project adds:
- **ML.NET-powered Query Intelligence** (65.8% API call reduction)
- **Enterprise-grade reliability patterns** (circuit breakers, defensive programming)
- **Thread-safe concurrent operations** with proper synchronization
- **Comprehensive testing framework** (69 test files with real-world validation)
- **Advanced metadata handling** with quality preservation
- **Multiple authentication strategies** with secure credential management
- **Performance optimization** and memory-efficient operations

---

## Additional Acknowledgments

### CI/CD and Build System Innovation

#### TypNull - GitHub Actions Solution Pioneer
**[TypNull's Tubifarry Plugin](https://github.com/TypNull/Tubifarry)**

TypNull deserves special recognition for solving one of the most challenging problems in the Lidarr plugin ecosystem: **achieving working GitHub Actions builds**. This breakthrough enabled automated CI/CD for complex Lidarr plugins - a challenge that had stumped developers for months.

**TypNull's revolutionary contributions:**
- **Minimal NuGet.config approach** - Elegant solution to private Azure DevOps feed authentication issues
- **Docker assembly extraction methodology** - Brilliant workaround enabling plugins branch compatibility in CI environments
- **Multi-workflow orchestration patterns** - Sophisticated yet reliable CI architecture that actually works in production
- **Git submodule strategy** - Clean approach to Lidarr source dependency management
- **Proven build system** - First documented success of complex Lidarr plugin CI/CD automation

**Community Impact**: 
TypNull's Tubifarry plugin proved that complex Lidarr plugins with multiple dependencies CAN build successfully in GitHub Actions, breaking through months of failed attempts by other developers. Their approach became the foundation for our own CI/CD breakthrough and should be the standard for all future Lidarr plugin development.

**Technical Achievement**: 
Solved the "impossible" problem of building Lidarr plugins in CI without access to private Servarr/Lidarr NuGet feeds, enabling the entire community to adopt modern DevOps practices.

**Repository**: https://github.com/TypNull/Tubifarry  
**Innovation**: First known successful automated CI/CD for complex Lidarr plugins with private dependencies
**Documentation**: See `docs/development/AI-PROMPT-LIDARR-PLUGIN-CICD.md` for teaching this approach to other developers

### Core Dependencies and Inspiration
- **[Lidarr Team](https://lidarr.audio/)** - Outstanding media management platform that provides the plugin architecture
- **[Qobuz](https://www.qobuz.com/)** - High-quality music streaming service with excellent API capabilities
- **[QobuzDownloaderX-MOD](https://github.com/DJDoubleD/QobuzDownloaderX-MOD)** - Additional inspiration for download optimization patterns

### Development Tools and Libraries
- **[Microsoft ML.NET](https://dotnet.microsoft.com/apps/machinelearning-ai/ml-dotnet)** - Machine learning framework powering Query Intelligence
- **[TagLib-Sharp](https://github.com/mono/taglib-sharp)** - Comprehensive metadata handling
- **[Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)** - JSON processing and API communication
- **[NLog](https://nlog-project.org/)** - Robust logging framework
- **[FluentValidation](https://fluentvalidation.net/)** - Input validation and data integrity

### Community Contributors
- **Beta testers** - Early adopters who provided real-world validation data
- **Issue reporters** - Community members who identified edge cases and improvement opportunities
- **Documentation reviewers** - Contributors who helped improve user guides and technical documentation

---

## License and Legal

This project is licensed under **GPL-3.0**, maintaining compatibility with TrevTV's original work and the broader Lidarr ecosystem.

**Copyright Notice:**
- Original concept and foundation: Copyright © TrevTV
- Enhancements and optimizations: Copyright © 2025 RicherTunes
- Combined work: GPL-3.0 license applies to the entire codebase

---

## Recognition Statement

**Qobuzarr would not exist without TrevTV's pioneering work.** Their original Lidarr.Plugin.Qobuz established the essential patterns, proved the concept, and created the foundation that enabled all subsequent enhancements. This project represents an evolution rather than a replacement, building upon their excellent groundwork to push the boundaries of what's possible in automated music management.

The Lidarr and self-hosted community owes TrevTV a debt of gratitude for bringing high-quality Qobuz integration to the ecosystem.

---

## How to Contribute

If you'd like to contribute to Qobuzarr's continued development:
1. **Report Issues**: [GitHub Issues](https://github.com/richertunes/qobuzarr/issues)
2. **Suggest Features**: [GitHub Discussions](https://github.com/richertunes/qobuzarr/discussions)  
3. **Contribute Code**: See [CONTRIBUTING.md](CONTRIBUTING.md)
4. **Support TrevTV**: Check out their original work at https://github.com/TrevTV/Lidarr.Plugin.Qobuz

---

*"Standing on the shoulders of giants" - This project exemplifies how open source software evolves through collaboration and mutual respect.*