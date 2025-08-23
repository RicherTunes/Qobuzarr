# QobuzCLI - Command Line Interface for Qobuzarr

A comprehensive command-line application for testing, debugging, and standalone music downloads using the Qobuzarr plugin core. QobuzCLI provides full access to Qobuz's high-resolution music catalog through an intuitive command interface.

## Overview

QobuzCLI is a production-ready standalone application that:
- **Tests and manages Qobuz authentication** (email/password and user ID/token)
- **Performs advanced searches** with filtering and export capabilities
- **Downloads albums, playlists, and labels** with quality fallback and validation
- **Manages download queues** with persistent state and priority handling
- **Processes batch operations** from JSON files (Lidarr export format supported)
- **Provides real-time monitoring** with interactive progress dashboards
- **Handles conflicts intelligently** with duplicate detection and resolution options
- **Validates downloadability** before queuing to prevent failed downloads

## Architecture Note

This CLI is a **thin wrapper** around the Qobuzarr plugin core. All functionality is implemented in the plugin (`src/`) and the CLI simply provides a command-line interface for testing and standalone use. This follows the plugin-first architecture principle.

## Installation

### From Source
```bash
cd QobuzCLI
dotnet build
dotnet run -- --help
```

### As Global Tool
```bash
# Package and install globally
dotnet pack
dotnet tool install --global --add-source ./nupkg QobuzCLI
```

## Commands

### Authentication

Test and manage Qobuz authentication:

```bash
# Email/password authentication
qobuzcli auth login --email user@example.com --password yourpassword

# Check authentication status
qobuzcli auth status

# Clear stored credentials
qobuzcli auth logout
```

### Configuration

Manage QobuzCLI settings:

```bash
# Show current configuration
qobuzcli config list

# Get specific configuration value
qobuzcli config get --key app-id

# Set configuration values
qobuzcli config set --key email --value user@example.com
qobuzcli config set --key app-id --value 123456789

# Reset configuration
qobuzcli config reset --key email

# Show config file location
qobuzcli config path
```

### Search

Search for albums, artists, and tracks:

```bash
# Basic album search
qobuzcli search "Pink Floyd"

# Search with options
qobuzcli search "Dark Side" --limit 10 --type album

# Search by artist
qobuzcli search --artist "Pink Floyd" --album "The Wall"

# Advanced search options
qobuzcli search "Jazz" --genre jazz --year-min 1950 --year-max 1970

# Output formats
qobuzcli search "Beatles" --output json > results.json
qobuzcli search "Beatles" --output csv > results.csv
qobuzcli search "Beatles" --output table  # default
```

### Batch Search

Process multiple searches from a file:

```bash
# JSON input format
qobuzcli batch-search --input albums.json --output results.json

# Text file input (one search per line)
qobuzcli batch-search --input searches.txt --output results.csv

# With progress and statistics
qobuzcli batch-search --input albums.json --output results.json --progress

# Generate HTML report
qobuzcli batch-search --input albums.json --report report.html
```

#### Input File Formats

**JSON Format** (Recommended):
```json
[
  {
    "artist": "Pink Floyd",
    "album": "The Dark Side of the Moon",
    "year": 1973
  },
  {
    "artist": "Led Zeppelin",
    "album": "IV"
  }
]
```

**Text Format**:
```
Pink Floyd - The Dark Side of the Moon
Led Zeppelin - IV
Miles Davis - Kind of Blue
```

### Download

Download albums and tracks with advanced features:

```bash
# Download album by ID
qobuzcli download --album-id 0060254735439

# Download with quality selection and validation
qobuzcli download --album-id 0060254735439 --quality flac-hires --validate

# Download to specific directory with conflict resolution
qobuzcli download --album-id 0060254735439 --output-dir /music/downloads --conflict skip

# Batch download from JSON file (Lidarr export format)
qobuzcli download --input wanted-albums.json --validate --max-concurrent 3

# Download with real-time monitoring
qobuzcli download --input albums.json --monitor --dashboard

# Download specific quality with fallback
qobuzcli download --album-id 123456 --quality flac-max --allow-fallback
```

#### Advanced Download Options

- `--validate`: Pre-flight downloadability validation
- `--conflict [skip|overwrite|rename]`: Handle existing files
- `--max-concurrent N`: Limit concurrent downloads
- `--monitor`: Show real-time progress dashboard
- `--dashboard`: Interactive progress monitoring
- `--allow-fallback`: Enable automatic quality fallback
- `--priority [low|normal|high]`: Set download priority

### Queue Management

Manage the download queue:

```bash
# List queue items
qobuzcli queue list
qobuzcli queue list --status pending

# Add items to queue
qobuzcli queue add --album-id 123456
qobuzcli queue add --track-id 789012

# Remove items
qobuzcli queue remove --id 1
qobuzcli queue clear

# Change priority
qobuzcli queue priority --id 1 --priority high
```

## Options

### Global Options

```bash
--debug              Enable debug output
--config PATH        Use alternate config file
--no-cache          Disable response caching
--timeout SECONDS   API request timeout (default: 30)
--help              Show help
--version           Show version
```

### Authentication Options

```bash
--email EMAIL       Qobuz account email
--password PASS     Qobuz account password  
--user-id ID        Qobuz user ID
--token TOKEN       Authentication token
--app-id ID         Application ID (optional)
--app-secret SECRET Application secret (optional)
```

### Search Options

```bash
--limit N           Maximum results (default: 50)
--type TYPE         Search type: all, album, artist, track
--genre GENRE       Filter by genre
--year-min YEAR     Minimum release year
--year-max YEAR     Maximum release year
--quality QUALITY   Minimum quality: mp3, flac, flac-hires
--include-singles   Include single releases
--fuzzy             Use fuzzy matching
```

### Output Options

```bash
--output FORMAT     Output format: table, json, csv, xml
--output-file PATH  Save output to file
--quiet             Suppress non-error output
--verbose           Verbose output
```

## Examples

### Testing Authentication Flow

```bash
# Test email/password auth
qobuzcli auth login --email test@example.com --password testpass --debug

# Verify authentication status
qobuzcli auth status --debug
```

### Debugging Search Issues

```bash
# Debug search with full API details
qobuzcli search "problematic artist name" --debug --limit 5

# Test different search strategies
qobuzcli search "Pink Floyd" --fuzzy
qobuzcli search --artist "Pink Floyd" --album "Animals"
```

### Batch Processing

```bash
# Create input file
cat > wanted-albums.json << EOF
[
  {"artist": "Pink Floyd", "album": "The Wall"},
  {"artist": "Beatles", "album": "Abbey Road"}
]
EOF

# Run batch search
qobuzcli batch-search --input wanted-albums.json \
  --output found-albums.json \
  --report search-report.html \
  --progress
```

### Integration Testing

```bash
#!/bin/bash
# test-qobuz-integration.sh

# Test authentication
if ! qobuzcli auth status; then
  echo "Authentication failed"
  exit 1
fi

# Test search
if ! qobuzcli search "test" --limit 1 --quiet; then
  echo "Search failed"
  exit 1
fi

echo "All tests passed"
```

## Configuration File

QobuzCLI stores configuration in `~/.qobuz/qobuz-configuration.json`:

```json
{
  "authentication": {
    "method": "email",
    "email": "user@example.com",
    "userId": null,
    "authToken": null,
    "appId": "285473059",
    "appSecret": null
  },
  "search": {
    "defaultLimit": 50,
    "includeTypes": ["albums"],
    "excludeTypes": ["singles"],
    "preferredGenres": [],
    "minimumQuality": "flac"
  },
  "download": {
    "outputDirectory": "~/Music/Qobuz",
    "fileNamePattern": "{artist} - {album} ({year})/{track} - {title}",
    "embedMetadata": true,
    "downloadArtwork": true
  },
  "api": {
    "timeout": 30,
    "retryCount": 3,
    "rateLimit": 60,
    "enableCache": true,
    "cacheDirectory": "~/.qobuzcli/cache"
  }
}
```

## Troubleshooting

### Common Issues

**Authentication Fails**
```bash
# Check credentials
qobuzcli config list

# Test with debug output
qobuzcli auth status --debug

# Clear cached session
rm ~/.qobuzcli/session.cache
```

**Search Returns No Results**
```bash
# Try simpler search terms
qobuzcli search "artist name" --fuzzy

# Check API response
qobuzcli search "test" --debug --limit 1
```

**Rate Limiting**
```bash
# Reduce rate limit
qobuzcli config set --rate-limit 30

# Add delays in scripts
sleep 2 between commands
```

### Debug Mode

Enable comprehensive debugging:

```bash
# Set environment variable
export QOBUZCLI_DEBUG=1

# Or use --debug flag
qobuzcli search "test" --debug
```

Debug output includes:
- HTTP requests/responses
- API timing information
- Cache hit/miss statistics
- Detailed error messages

## Scripting

QobuzCLI is designed for scripting:

```bash
#!/bin/bash
# find-missing-albums.sh

# Read album list
while IFS= read -r line; do
  # Search for each album
  if qobuzcli search "$line" --quiet --output json | jq -e '.albums.total > 0' > /dev/null; then
    echo "✓ Found: $line"
  else
    echo "✗ Missing: $line"
  fi
done < albums.txt
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | General error |
| 2 | Authentication error |
| 3 | Network error |
| 4 | Not found |
| 5 | Rate limited |
| 6 | Invalid arguments |

## Development

### Building from Source

```bash
# Clone repository
git clone https://github.com/richertunes/qobuzarr.git
cd qobuzarr/QobuzCLI

# Build
dotnet build

# Run tests
dotnet test ../tests/QobuzCLI.Tests

# Create release build
dotnet publish -c Release -r linux-x64 --self-contained
```

### Adding New Commands

1. Create command class in `Commands/`
2. Implement `ICommand` interface
3. Register in `Program.cs`
4. Add tests

### Contributing

See main [CONTRIBUTING.md](../CONTRIBUTING.md) for guidelines.

## Support

- **Issues**: [GitHub Issues](https://github.com/richertunes/qobuzarr/issues)
- **Discord**: [Lidarr Discord](https://discord.gg/lidarr)
- **Docs**: [Main Documentation](../README.md)