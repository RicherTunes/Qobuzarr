# Getting Started with Qobuzarr Development

## Quick Start

The fastest way to get up and running with Qobuzarr development:

```bash
# 1. Clone the repository
git clone https://github.com/RicherTunes/qobuzarr.git
cd qobuzarr

# 2. Run the setup script
# Linux/macOS:
chmod +x setup.sh && ./setup.sh

# Windows PowerShell:
.\setup.ps1

# 3. You're ready to develop!
```

## What the Setup Script Does

The setup script automatically:
1. ✅ Downloads required Lidarr dependencies to `ext/Lidarr-source/`
2. ✅ Restores all NuGet packages 
3. ✅ Attempts to build the project
4. ✅ Runs basic tests to verify everything works

## Development Environment

### Required Tools
- **.NET 6.0 SDK** or later
- **Git** for version control
- **IDE**: Visual Studio 2022, VS Code, or JetBrains Rider

### Recommended VS Code Extensions
```bash
# Install recommended extensions
code --install-extension ms-dotnettools.csharp
code --install-extension ms-dotnettools.vscode-dotnet-runtime
code --install-extension formulahendry.dotnet-test-explorer
```

## Project Structure

```
qobuzarr/
├── src/                    # 🎯 Main plugin code (Lidarr integration)
├── QobuzCLI/              # 🛠️ CLI testing tool
├── tests/                 # 🧪 Test suites
├── docs/                  # 📚 Documentation
├── ext/                   # 📦 External dependencies (Lidarr)
├── .github/               # 🤖 CI/CD workflows
└── setup.{sh,ps1}         # 🚀 Quick setup scripts
```

## Build & Test

```bash
# Build everything
dotnet build

# Run tests
dotnet test

# Build CLI tool
dotnet build QobuzCLI/

# Run CLI (requires Qobuz credentials)
cd QobuzCLI
dotnet run -- auth login
```

## Setting Up Qobuz Credentials

1. **Copy environment template:**
   ```bash
   cp .env.example .env
   ```

2. **Get Qobuz API credentials:**
   - You need `QOBUZ_APP_ID` and `QOBUZ_APP_SECRET`
   - Plus your Qobuz account credentials

3. **Edit `.env` file:**
   ```bash
   QOBUZ_APP_ID=your_app_id
   QOBUZ_APP_SECRET=your_app_secret
   QOBUZ_EMAIL=your_email@example.com
   QOBUZ_PASSWORD=your_password
   ```

## Testing Your Setup

### 1. CLI Authentication Test
```bash
cd QobuzCLI
dotnet run -- auth login
# Should successfully authenticate with Qobuz
```

### 2. Search Test
```bash
dotnet run -- search "Miles Davis"
# Should return album search results
```

### 3. Lidarr Integration Test
```bash
dotnet run -- lidarr test --url http://localhost:8686 --api-key YOUR_KEY
# Tests connection to your Lidarr instance
```

## Common Issues & Solutions

### ❌ Build Fails: "NzbDrone could not be found"
**Problem:** Missing Lidarr dependencies
**Solution:** 
```bash
# Re-run setup script or manually clone Lidarr:
git clone https://github.com/Lidarr/Lidarr.git ext/Lidarr-source
```

### ❌ Tests Fail: "Authentication failed"
**Problem:** Missing or invalid Qobuz credentials
**Solution:** Set up `.env` file with valid credentials (see above)

### ❌ CLI Crashes: "App secret required"
**Problem:** Hardcoded credentials were removed for security
**Solution:** Provide credentials via `.env` file or environment variables

### ❌ Plugin Not Loading in Lidarr
**Problem:** Plugin compilation or compatibility issues
**Solution:**
```bash
# Check plugin.json version matches your assembly
cat plugin.json
cat bin/Release/net6.0/Lidarr.Plugin.Qobuzarr.dll
```

## Development Workflow

### Making Changes

1. **Create a feature branch:**
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. **Make your changes** in `src/` (plugin code) or `QobuzCLI/` (CLI code)

3. **Test your changes:**
   ```bash
   dotnet test                    # Run unit tests
   cd QobuzCLI && dotnet run      # Test CLI functionality
   ```

4. **Build release version:**
   ```bash
   dotnet build --configuration Release
   ```

### Plugin Testing in Lidarr

1. **Build the plugin:**
   ```bash
   dotnet build --configuration Release
   ```

2. **Copy to Lidarr plugins directory:**
   ```bash
   cp bin/Release/net6.0/* /path/to/lidarr/plugins/
   ```

3. **Restart Lidarr and configure:**
   - Settings → Indexers → Add → Qobuzarr
   - Settings → Download Clients → Add → Qobuzarr

## Contributing

1. **Fork** the repository on GitHub
2. **Create** a feature branch
3. **Make** your changes with tests
4. **Ensure** all tests pass: `dotnet test`
5. **Submit** a pull request

See [CONTRIBUTING.md](CONTRIBUTING.md) for detailed guidelines.

## Need Help?

- 📖 **Documentation**: Check the `docs/` directory
- 🐛 **Bug Reports**: [GitHub Issues](https://github.com/RicherTunes/qobuzarr/issues)
- 💬 **Discussions**: [GitHub Discussions](https://github.com/RicherTunes/qobuzarr/discussions)
- 🔒 **Security Issues**: See [SECURITY.md](SECURITY.md)

## Next Steps

Once you have everything working:

1. 📖 Read the [Architecture Guide](docs/architecture/ARCHITECTURE.md)
2. 🧪 Explore the [Testing Guide](docs/development/TESTING-GUIDE.md) 
3. 🔧 Check out [Development Guide](docs/development/DEVELOPMENT.md)
4. 🎯 Pick an issue labeled [`good first issue`](https://github.com/RicherTunes/qobuzarr/labels/good%20first%20issue)

Happy coding! 🎵