# Contributing to Qobuzarr

Thank you for your interest in contributing to Qobuzarr! This document provides guidelines and instructions for contributing to the project.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Architecture Principles](#architecture-principles)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Submitting Changes](#submitting-changes)
- [Coding Standards](#coding-standards)
- [Testing Guidelines](#testing-guidelines)
- [Documentation](#documentation)
- [Community](#community)

## Code of Conduct

We are committed to providing a welcoming and inspiring community for all. Please read and follow our Code of Conduct:

- Be respectful and inclusive
- Welcome newcomers and help them get started
- Focus on what is best for the community
- Show empathy towards other community members

## Architecture Principles

### Plugin-First Architecture
**The plugin (`src/`) is the core foundation. The CLI (`QobuzCLI/`) is strictly a test program.**

- All core functionality MUST live in the plugin
- CLI never reimplements plugin functionality
- CLI uses plugin classes directly via project reference
- When adding features, always implement in plugin first

### No Stub/Placeholder Data Policy
**Never use stub/placeholder/hardcoded data in production code paths.**

- Real API Integration Required: All services must connect to real APIs
- Fail-Fast Principle: If real APIs are unavailable, fail immediately with clear errors
- Test-Only Exceptions: Stub data is ONLY acceptable in unit tests

## Getting Started

1. **Fork the Repository**
   - Visit [Qobuzarr on GitHub](https://github.com/RicherTunes/qobuzarr)
   - Click the "Fork" button in the top right
   - Clone your fork locally

2. **Set Up Development Environment**
   ```bash
   # Clone your fork
   git clone https://github.com/YOUR_USERNAME/qobuzarr.git
   cd qobuzarr
   
   # Add upstream remote
   git remote add upstream https://github.com/richertunes/qobuzarr.git
   
   # Install dependencies
   dotnet restore
   ```

3. **Create a Branch**
   ```bash
   # Get latest changes
   git fetch upstream
   git checkout main
   git merge upstream/main
   
   # Create feature branch
   git checkout -b feature/your-feature-name
   ```

## How to Contribute

### Reporting Bugs

Before creating a bug report, please check existing issues to avoid duplicates.

**To report a bug:**

1. Use the [Bug Report Template](.github/ISSUE_TEMPLATE/bug_report.md)
2. Include:
   - Clear, descriptive title
   - Steps to reproduce
   - Expected vs actual behavior
   - System information
   - Relevant logs
   - Screenshots (if applicable)

**Example:**
```markdown
Title: Search fails for albums with special characters

**Description:**
When searching for albums containing "&" character, the search returns no results.

**Steps to Reproduce:**
1. Go to Lidarr search
2. Search for "Simon & Garfunkel"
3. No results returned

**Expected:** Results should include Simon & Garfunkel albums
**Actual:** Empty results

**Environment:**
- Plugin Version: 1.0.0
- Lidarr Version: 2.0.0.1234
- OS: Ubuntu 22.04
```

### Suggesting Features

We love feature suggestions! Please:

1. Check if the feature already exists or is planned
2. Use the [Feature Request Template](.github/ISSUE_TEMPLATE/feature_request.md)
3. Explain the use case and benefits
4. Consider implementation complexity

### Contributing Code

#### Types of Contributions

- **Bug Fixes**: Fix reported issues
- **Features**: Add new functionality
- **Performance**: Improve speed or efficiency
- **Refactoring**: Improve code quality
- **Tests**: Add missing tests
- **Documentation**: Improve or add docs

#### Before You Start

1. **Check Issues**: Look for issues tagged `good first issue` or `help wanted`
2. **Discuss**: For major changes, open an issue first to discuss
3. **Assign Yourself**: Comment on the issue to avoid duplicate work

## Development Setup

### Prerequisites

- .NET 6.0 SDK or later
- Visual Studio 2022, VS Code, or Rider
- Git
- Docker (optional, for testing)

### Building

```bash
# Build debug version
dotnet build

# Build release version
dotnet build --configuration Release

# Run tests
dotnet test
```

### Testing with Lidarr

```bash
# Run Lidarr with plugin support
docker run -d \
  --name=lidarr-dev \
  -p 8686:8686 \
  -v ./config:/config \
  -v ./bin/Debug/net6.0:/config/plugins \
  ghcr.io/hotio/lidarr:pr-plugins

# Copy plugin after building
cp bin/Debug/net6.0/Lidarr.Plugin.Qobuz.dll ./config/plugins/

# Restart Lidarr
docker restart lidarr-dev
```

## Submitting Changes

### Pull Request Process

1. **Ensure Quality**
   - Code compiles without warnings
   - All tests pass
   - New code has tests
   - Documentation is updated

2. **Commit Guidelines**
   ```bash
   # Good commit messages
   git commit -m "feat: Add playlist search support"
   git commit -m "fix: Handle special characters in search queries"
   git commit -m "docs: Update API documentation"
   git commit -m "test: Add authentication service tests"
   git commit -m "perf: Optimize search result parsing"
   ```

3. **Create Pull Request**
   - Push to your fork
   - Open PR against `main` branch
   - Fill out the PR template
   - Link related issues

### PR Template

```markdown
## Description
Brief description of changes

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation update

## Testing
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] Manually tested with Lidarr

## Checklist
- [ ] Code follows style guidelines
- [ ] Self-review completed
- [ ] Comments added for complex code
- [ ] Documentation updated
- [ ] No new warnings
```

## Coding Standards

### C# Style Guide

```csharp
// File header
using System;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuz.Services
{
    /// <summary>
    /// Service for managing user playlists.
    /// </summary>
    public class PlaylistService : IPlaylistService
    {
        private readonly IQobuzApiClient _apiClient;
        private readonly ILogger _logger;

        public PlaylistService(IQobuzApiClient apiClient, Logger logger)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets a playlist by ID.
        /// </summary>
        /// <param name="playlistId">The playlist identifier.</param>
        /// <returns>The playlist or null if not found.</returns>
        public async Task<Playlist> GetPlaylistAsync(string playlistId)
        {
            try
            {
                _logger.Debug("Fetching playlist: {0}", playlistId);
                return await _apiClient.GetAsync<Playlist>($"/playlist/get?id={playlistId}");
            }
            catch (QobuzApiException ex) when (ex.StatusCode == 404)
            {
                _logger.Debug("Playlist not found: {0}", playlistId);
                return null;
            }
        }
    }
}
```

### Best Practices

1. **SOLID Principles**
   - Single Responsibility
   - Open/Closed
   - Liskov Substitution
   - Interface Segregation
   - Dependency Inversion

2. **Error Handling**
   - Use specific exception types
   - Log errors appropriately
   - Fail gracefully

3. **Async/Await**
   - Always use async for I/O
   - Configure await properly
   - Avoid blocking calls

4. **Testing**
   - Write testable code
   - Mock external dependencies
   - Test edge cases

## Testing Guidelines

### Unit Tests

```csharp
[TestFixture]
public class PlaylistServiceTests
{
    private Mock<IQobuzApiClient> _apiClientMock;
    private Mock<Logger> _loggerMock;
    private PlaylistService _service;

    [SetUp]
    public void Setup()
    {
        _apiClientMock = new Mock<IQobuzApiClient>();
        _loggerMock = new Mock<Logger>();
        _service = new PlaylistService(_apiClientMock.Object, _loggerMock.Object);
    }

    [Test]
    public async Task GetPlaylistAsync_ValidId_ReturnsPlaylist()
    {
        // Arrange
        var playlistId = "123";
        var expectedPlaylist = new Playlist { Id = playlistId, Name = "Test" };
        _apiClientMock.Setup(x => x.GetAsync<Playlist>(It.IsAny<string>()))
                      .ReturnsAsync(expectedPlaylist);

        // Act
        var result = await _service.GetPlaylistAsync(playlistId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo(playlistId));
    }
}
```

### Integration Tests

- Test real API interactions
- Use test accounts
- Clean up test data
- Don't hardcode credentials

### Test Coverage

- Aim for >80% code coverage
- Focus on critical paths
- Test error scenarios
- Verify edge cases

## Documentation

### Code Documentation

```csharp
/// <summary>
/// Searches for albums matching the specified criteria.
/// </summary>
/// <param name="query">The search query.</param>
/// <param name="limit">Maximum number of results.</param>
/// <returns>A list of matching albums.</returns>
/// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
/// <exception cref="QobuzApiException">Thrown when the API request fails.</exception>
/// <example>
/// <code>
/// var albums = await SearchAlbumsAsync("Pink Floyd", 10);
/// foreach (var album in albums)
/// {
///     Console.WriteLine(album.Title);
/// }
/// </code>
/// </example>
public async Task<List<Album>> SearchAlbumsAsync(string query, int limit = 50)
```

### Documentation Updates

When adding features, update:

1. **API Reference** (`docs/API-REFERENCE.md`)
2. **Configuration Guide** (`docs/CONFIGURATION-GUIDE.md`)
3. **README** if needed
4. **Inline XML documentation**

## Community

### Getting Help

- **Discord**: [Lidarr Discord](https://discord.gg/lidarr)
- **GitHub Discussions**: For questions and ideas
- **Issues**: For bugs and feature requests

### Code Review

All submissions require review:

1. **Automated Checks**: Must pass CI/CD
2. **Peer Review**: At least one approval
3. **Maintainer Review**: For significant changes

### Recognition

Contributors are recognized in:
- GitHub contributors page
- Release notes
- README acknowledgments

## License

By contributing, you agree that your contributions will be licensed under the GPL-3.0 License.

## Questions?

Feel free to:
- Open an issue for clarification
- Ask in Discord
- Email the maintainers

Thank you for contributing to Qobuzarr! 🎵