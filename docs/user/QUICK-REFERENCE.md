# Qobuzarr Quick Reference

A quick reference guide for developers working with the Qobuzarr plugin.

## API Endpoints

### Authentication

```csharp
// Email/Password Login
POST /user/login
{
  "email": "user@example.com",
  "password": "md5_hashed_password",
  "app_id": "798273057"
}

// Token Validation
GET /user/login?app_id={appId}&user_auth_token={token}
```

### Search

```csharp
// Album Search
GET /album/search?query={query}&limit={limit}

// Artist Search  
GET /artist/search?query={query}&limit={limit}

// Track Search
GET /track/search?query={query}&limit={limit}
```

### Metadata

```csharp
// Get Album Details
GET /album/get?album_id={albumId}

// Get Track Stream URL
GET /track/getFileUrl?track_id={trackId}&format_id={formatId}
// Requires request signing
```

## Quality Format IDs

| Format | ID | Quality | Details |
|--------|-----|---------|---------|
| MP3 V0 | 1 | ~245 kbps | Variable bitrate |
| MP3 320 | 5 | 320 kbps | Constant bitrate |
| FLAC CD | 6 | 16/44.1 | CD Quality |
| FLAC Hi-Res | 7 | 24/96 | Hi-Res |
| FLAC Hi-Res | 27 | 24/192 | Ultra Hi-Res |

## Common Patterns

### Authentication Flow

```csharp
// 1. Create credentials
var credentials = new QobuzCredentials
{
    Email = email,
    MD5Password = QobuzAuthenticationService.HashPassword(password)
};

// 2. Authenticate
var session = await _authService.AuthenticateAsync(credentials);

// 3. Set session
_apiClient.SetSession(session);
```

### Search Implementation

```csharp
// Basic search
var results = await _apiClient.GetAsync<QobuzSearchResponse>(
    "/album/search",
    new Dictionary<string, string>
    {
        ["query"] = searchTerm,
        ["limit"] = "100"
    });

// Process results
foreach (var album in results.Albums.Items)
{
    Console.WriteLine($"{album.Artist.Name} - {album.Title}");
}
```

### Error Handling

```csharp
try
{
    var result = await _apiClient.GetAsync<T>(endpoint);
    return result;
}
catch (QobuzApiException ex) when (ex.StatusCode == 401)
{
    // Re-authenticate
    await EnsureAuthenticatedAsync();
    throw;
}
catch (QobuzApiException ex) when (ex.StatusCode == 429)
{
    // Rate limited - wait and retry
    await Task.Delay(TimeSpan.FromSeconds(60));
    throw;
}
```

## Configuration Keys

### Indexer Settings

```yaml
AuthenticationMethod: Email|Token
Email: user@example.com
Password: password
UserId: 12345678
AuthToken: auth_token_string
AppId: ""   # Optional; leave empty for auto-detection
AppSecret: app_secret
SearchLimit: 100
IncludeSingles: false
IncludeCompilations: true
QueryOptimizationMode: QueryIntelligence  # Disabled|QueryIntelligence|MLPrediction
CountryCode: US
```

### Environment Variables

```bash
QOBUZ_APP_ID=your_app_id
QOBUZ_APP_SECRET=your_secret
```

## Useful Commands

### QobuzCLI Testing

```bash
# Authenticate
qobuzcli auth --email user@example.com --password pass

# Search
qobuzcli search "Pink Floyd" --limit 10
qobuzcli search --album-id 0060254735439

# Batch search
qobuzcli batch-search --input albums.json --output results.json

# Queue management
qobuzcli queue list
qobuzcli queue clear
```

### Development

```bash
# Build
dotnet build
dotnet build --configuration Release

# Test
dotnet test
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration

# Package
dotnet pack --configuration Release
```

## Logging

### Log Levels

```csharp
_logger.Trace("Detailed trace info");
_logger.Debug("Debug information");
_logger.Info("General information");
_logger.Warn("Warning messages");
_logger.Error(ex, "Error with exception");
```

### Important Log Patterns

```log
# Authentication
[Info] Successfully authenticated with Qobuz API using email/password

# API Calls
[Debug] GET /album/search?query=test&limit=100
[Debug] Response received in 235ms

# Caching
[Debug] Cache hit for key: qobuz_api_/album/search_12345
[Debug] Cache miss, fetching from API

# Errors
[Error] QobuzApiException: 401 Unauthorized
[Warn] Rate limited, waiting 60 seconds
```

## Database Schema

### Indexer Table

```sql
CREATE TABLE Indexers (
    Id INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    Implementation TEXT NOT NULL,
    Settings TEXT,
    ConfigContract TEXT,
    EnableRss INTEGER,
    EnableAutomaticSearch INTEGER,
    EnableInteractiveSearch INTEGER
);
```

### Download Queue

```sql
CREATE TABLE QobuzDownloadQueue (
    Id INTEGER PRIMARY KEY,
    AlbumId TEXT NOT NULL,
    TrackId TEXT NOT NULL,
    Status TEXT NOT NULL,
    Progress REAL,
    Priority INTEGER,
    CreatedAt DATETIME,
    UpdatedAt DATETIME
);
```

## Performance Tips

1. **Caching**
   - Search results: 5 minutes
   - Album details: 1 hour
   - Artist info: 24 hours

2. **Rate Limiting**
   - Default: 60 requests/minute
   - Respect Retry-After headers
   - Use exponential backoff

3. **Batch Operations**
   - Group API calls when possible
   - Use parallel processing carefully
   - Monitor memory usage

## Troubleshooting Checklist

- [ ] Plugin loaded successfully
- [ ] Authentication working
- [ ] API connection established
- [ ] Search returns results
- [ ] Logs show no errors
- [ ] Cache functioning
- [ ] Rate limits not exceeded

## Release Checklist

- [ ] Version bumped in .csproj
- [ ] CHANGELOG updated
- [ ] Tests passing
- [ ] Documentation updated
- [ ] Release notes written
- [ ] Binary built in Release mode
- [ ] ILRepack successful
- [ ] Manual testing completed

## Useful Links

- [Qobuz API (Unofficial)](https://github.com/Qobuz/api-documentation)
- [Lidarr Plugin Docs](https://wiki.servarr.com/lidarr/plugins)
- [.NET 8 Docs](https://docs.microsoft.com/dotnet/core/)
- [NLog Documentation](https://nlog-project.org/)
- [DryIoc Documentation](https://github.com/dadhi/DryIoc)
