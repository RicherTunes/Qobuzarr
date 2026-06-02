# Lidarr Plugins Branch Compatibility Solution

## Problem Summary

The plugin compiles successfully against regular Lidarr release assemblies (2.13.2.4685) but fails to load in Lidarr plugins branch runtime (2.13.3.4692) with this error:

```
Method 'get_Protocol' in type 'Lidarr.Plugin.Qobuzarr.Indexers.QobuzIndexer' from assembly 'Lidarr.Plugin.Qobuzarr, Version=0.0.13.0, Culture=neutral, PublicKeyToken=(removed) does not have an implementation.
Method 'get_Protocol' in type 'Lidarr.Plugin.Qobuzarr.Download.Clients.QobuzDownloadClient' from assembly 'Lidarr.Plugin.Qobuzarr, Version=0.0.13.0, Culture=neutral, PublicKeyToken=(removed) does not have an implementation.
```

## Root Cause Analysis

**Interface Signature Mismatch Between Branches:**

1. **Regular Release Branch (2.13.2.4685):**
   - `IndexerBase<T>.Protocol` returns `DownloadProtocol` enum
   - `DownloadClientBase<T>.Protocol` returns `DownloadProtocol` enum

2. **Plugins Branch (2.13.3.4692):**
   - `IndexerBase<T>.Protocol` returns `string`
   - `DownloadClientBase<T>.Protocol` returns `string`

**The Compilation Mismatch:**

- Plugin compiles against release assemblies (expects enum)
- Runtime uses plugins branch assemblies (expects string)
- Result: Method signature mismatch causes runtime failure

## Solution Implementation

### Code Changes Made

Updated all Protocol property implementations to return `string` instead of `DownloadProtocol` enum:

1. **QobuzIndexer.cs** (line 44):

   ```csharp
   // OLD: public override DownloadProtocol Protocol => DownloadProtocol.Unknown;
   public override string Protocol => nameof(QobuzarrDownloadProtocol);
   ```

2. **QobuzDownloadClient.cs** (line 75):

   ```csharp
   // OLD: public override DownloadProtocol Protocol => DownloadProtocol.Unknown;
   public override string Protocol => nameof(QobuzarrDownloadProtocol);
   ```

3. **QobuzParser.cs**:

   ```csharp
   // OLD: DownloadProtocol = DownloadProtocol.Unknown,
   DownloadProtocol = nameof(QobuzarrDownloadProtocol),
   ```

4. **QobuzDownloadItem.cs**:

   ```csharp
   // OLD: Protocol = DownloadProtocol.Unknown,
   Protocol = nameof(QobuzarrDownloadProtocol),
   ```

### Assembly Requirements

**CRITICAL:** To compile with these changes, you need plugins branch assemblies, not release assemblies.

## Getting Plugins Branch Assemblies

### Method 1: Docker Container Extraction (Recommended)

Use the provided scripts to extract assemblies from the hotio plugins branch container:

```bash
# PowerShell
.\download-plugins-branch-assemblies.ps1 -Force

# Bash
./download-plugins-branch-assemblies.sh --force
```

**Container source:** `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913`

### Method 2: Manual Docker Extraction

```bash
# Pull the plugins branch container
docker pull ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913

# Extract assemblies manually
docker create --name temp-lidarr ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913
docker cp temp-lidarr:/app ./lidarr-plugins-temp
docker rm temp-lidarr

# Copy required assemblies to ext/Lidarr/_output/net8.0/
cp lidarr-plugins-temp/*.dll ext/Lidarr/_output/net8.0/
```

### Method 3: Build from Plugins Branch Source

```bash
# Clone the plugins branch
git clone -b plugins https://github.com/Lidarr/Lidarr.git plugins-lidarr
cd plugins-lidarr

# Build specific assemblies
dotnet build src/NzbDrone.Core/NzbDrone.Core.csproj -c Release
dotnet build src/Lidarr.Http/Lidarr.Http.csproj -c Release
dotnet build src/Lidarr.Api.V1/Lidarr.Api.V1.csproj -c Release

# Copy to plugin directory
cp src/*/_output/net6.0/*.dll /path/to/qobuzarr/ext/Lidarr/_output/net6.0/
```

## Build Verification

After obtaining plugins branch assemblies, verify the build works:

```bash
# Should compile successfully with string Protocol properties
dotnet build Qobuzarr.csproj --configuration Release -p:RunAnalyzersDuringBuild=false -p:EnableNETAnalyzers=false -p:TreatWarningsAsErrors=false
```

**Expected result:** Clean build with no Protocol-related errors.

## Testing Compatibility

Deploy the compiled plugin to a Lidarr plugins branch instance:

1. **Target Runtime:** Lidarr plugins branch 2.13.3.4692
2. **Container:** `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913`
3. **Validation:** Plugin should load without Protocol method implementation errors

## Important Notes

### Backward Compatibility

**WARNING:** Plugins compiled for plugins branch (string Protocol) will NOT work with regular release branch (enum Protocol).

- **Plugins Branch Plugin:** `Protocol => nameof(QobuzarrDownloadProtocol)` ✅ Works with pr-plugins-3.1.2.4913
- **Release Branch Plugin:** `Protocol => DownloadProtocol.Unknown` ✅ Works with regular 2.13.2.4685

### Branch-Specific Deployment

Maintain separate builds for different Lidarr branches:

```
builds/
├── release-branch/      # For regular Lidarr releases
│   └── Qobuzarr.dll     # Protocol returns DownloadProtocol enum
└── plugins-branch/      # For Lidarr plugins branch
    └── Qobuzarr.dll     # Protocol returns string
```

### CI/CD Considerations

Update GitHub Actions workflow to build both variants:

```yaml
strategy:
  matrix:
    lidarr-branch: [release, plugins]
    
steps:
- name: Download Assemblies
  run: |
    if [ "${{ matrix.lidarr-branch }}" == "plugins" ]; then
      ./download-plugins-branch-assemblies.sh
    else
      ./download-lidarr-assemblies.sh
    fi
```

## Resolution Status

✅ **RESOLVED:** All Protocol properties updated to return `string` for plugins branch compatibility.

⚠️ **PENDING:** Acquisition of plugins branch assemblies required for successful compilation.

🔄 **NEXT STEPS:**

1. Run assembly extraction script
2. Verify clean build
3. Test runtime compatibility with plugins branch Lidarr
