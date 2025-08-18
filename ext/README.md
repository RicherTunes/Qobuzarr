# External Dependencies

This directory contains external dependencies required for building the Qobuzarr plugin.

## Lidarr Source Code

The `Lidarr-source/` directory contains the Lidarr source code necessary for compiling the plugin. 

### Important Notes:

1. **Local Development Only**: The `Lidarr-source/` directory is **gitignored** and not included in the repository to keep it clean and focused on the plugin code.

2. **Required for Compilation**: You must have the Lidarr source code in this directory to build the plugin successfully.

3. **How to Obtain**: 
   - Clone the official Lidarr repository: `git clone https://github.com/Lidarr/Lidarr.git Lidarr-source`
   - Or extract from a Lidarr release package
   - Ensure you're using a compatible version with the plugin

4. **Version Compatibility**: This plugin is built against Lidarr v2.0+ APIs. Ensure your Lidarr source matches this version requirement.

## Directory Structure

```
ext/
├── README.md           (this file)
└── Lidarr-source/      (gitignored - obtain separately)
    ├── src/
    │   ├── Lidarr.Api.V1/
    │   ├── Lidarr.Common/
    │   ├── Lidarr.Core/
    │   └── ...
    └── ...
```

## Setup Instructions

### Option 1: Clone from GitHub (Recommended)
1. Navigate to the `ext/` directory
2. Clone the Lidarr source:
   ```bash
   git clone https://github.com/Lidarr/Lidarr.git Lidarr-source
   ```
3. The plugin build process will automatically reference the necessary assemblies

### Option 2: Extract from Docker Container
If you have Lidarr running in Docker, you can extract the assemblies:
```bash
# Copy assemblies from a running Lidarr container
docker cp lidarr-container:/app/bin ./ext/Lidarr-source
```

### Option 3: Download Release Package
1. Download a Lidarr release from: https://github.com/Lidarr/Lidarr/releases
2. Extract the contents to `ext/Lidarr-source/`

## Why This Approach?

- **Clean Repository**: Keeps the plugin repository focused on plugin-specific code
- **Version Flexibility**: Developers can test against different Lidarr versions
- **Reduced Size**: Prevents the repository from becoming bloated with external code
- **Legal Clarity**: Maintains clear separation between plugin code and Lidarr code