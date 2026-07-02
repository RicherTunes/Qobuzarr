<!-- docval:ignore-workflow-refs -->
# 📦 Repository Separation Plan: Professional Ecosystem Architecture

## 🎯 **Strategic Separation: Shared vs Specialized**

### **Repository Structure (Target State)**

```
📁 Lidarr.Plugin.Common (NEW REPOSITORY)
├── 🛠️ src/
│   ├── Base/                     # BaseStreamingSettings, helpers
│   ├── Interfaces/               # IStreamingAuthenticationService, etc.
│   ├── Services/                 # All reusable service implementations
│   ├── Models/                   # Universal StreamingArtist, Album, Track, Quality
│   ├── Utilities/                # FileNameSanitizer, RetryUtilities, HttpExtensions
│   └── Testing/                  # MockFactories, TestDataSets
├── 📚 docs/                      # API documentation, usage guides
├── 🧪 tests/                     # Comprehensive test suite
├── 📋 examples/                  # Usage examples and templates
├── 🚀 .github/workflows/         # CI/CD for NuGet publishing
├── 📦 Lidarr.Plugin.Common.csproj
├── 📋 README.md
└── 🔄 CHANGELOG.md

📁 Qobuzarr (CURRENT REPOSITORY - CLEANED)
├── 🎵 src/                       # Qobuz-specific implementation only
│   ├── API/QobuzApiClient        # Qobuz API specifics
│   ├── Indexers/QobuzIndexer     # Qobuz search logic  
│   ├── Download/                 # Qobuz download implementation
│   ├── Authentication/           # Qobuz auth specifics
│   └── Models/Qobuz*             # Qobuz API models
├── 📦 PackageReference Include="Lidarr.Plugin.Common" Version="1.0.0"
└── 🧪 tests/                     # Qobuz-specific tests

📁 Tidalarr (FUTURE REPOSITORY)  
├── 🎵 src/                       # Tidal-specific implementation only
│   ├── API/TidalApiClient        # Tidal API specifics
│   ├── Indexers/TidalIndexer     # Tidal search logic
│   ├── Download/                 # Tidal download implementation  
│   └── Models/Tidal*             # Tidal API models
├── 📦 PackageReference Include="Lidarr.Plugin.Common" Version="1.0.0"
└── 🧪 tests/                     # Tidal-specific tests
```

---

## 🔄 **Migration Strategy**

### **Phase 1: Create Shared Library Repository (Day 1)**

#### **Repository Setup**
```bash
# Create new repository
gh repo create RicherTunes/Lidarr.Plugin.Common --public \
  --description "Shared library for Lidarr streaming service plugins" \
  --clone

cd Lidarr.Plugin.Common

# Initialize with professional structure
mkdir -p src/{Base,Interfaces,Services,Models,Utilities,Testing}
mkdir -p tests docs examples .github/workflows

# Copy shared components from Qobuzarr
cp -r ../Qobuzarr/Lidarr.Plugin.Common/* src/
cp -r ../Qobuzarr/examples/Tidalarr* examples/
```

#### **Project Configuration**
```xml
<!-- Lidarr.Plugin.Common.csproj - INDEPENDENT PROJECT -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <PackageId>Lidarr.Plugin.Common</PackageId>
    <Version>1.0.0</Version>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
    
    <!-- NuGet package metadata -->
    <Authors>RicherTunes Community</Authors>
    <Description>Shared utilities and patterns for Lidarr streaming service plugins</Description>
    <PackageProjectUrl>https://github.com/RicherTunes/Lidarr.Plugin.Common</PackageProjectUrl>
    <RepositoryUrl>https://github.com/RicherTunes/Lidarr.Plugin.Common.git</RepositoryUrl>
    <PackageTags>lidarr;plugin;streaming;music;utilities</PackageTags>
    
    <!-- No Lidarr dependencies in shared library -->
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.Text.Json" Version="8.0.0" />
  </ItemGroup>
</Project>
```

### **Phase 2: Setup Independent CI/CD (Day 2)**

#### **GitHub Actions Workflow**
```yaml
# .github/workflows/build-and-publish.yml
name: Build and Publish Shared Library

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]
  release:
    types: [ published ]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v4
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: 8.0.x
        
    - name: Restore dependencies
      run: dotnet restore
      
    - name: Build
      run: dotnet build --configuration Release --no-restore
      
    - name: Test  
      run: dotnet test --configuration Release --no-build --verbosity normal
      
    - name: Pack NuGet
      run: dotnet pack --configuration Release --no-build --output nupkg
      
    - name: Upload NuGet Packages
      uses: actions/upload-artifact@v4
      with:
        name: nuget-packages
        path: nupkg/*.nupkg

  publish:
    needs: build
    runs-on: ubuntu-latest
    if: github.event_name == 'release'
    steps:
    - name: Download packages
      uses: actions/download-artifact@v4
      
    - name: Publish to NuGet
      run: dotnet nuget push nupkg/*.nupkg --source https://api.nuget.org/v3/index.json --api-key ${{ secrets.NUGET_API_KEY }}
```

### **Phase 3: Update Plugin Repositories (Day 3)**

#### **Qobuzarr Changes**
```xml
<!-- Remove local shared library -->
<ItemGroup>
  <!-- Remove: Local project reference -->
  <!-- <ProjectReference Include="Lidarr.Plugin.Common\Lidarr.Plugin.Common.csproj" /> -->
  
  <!-- Add: NuGet package reference -->
  <PackageReference Include="Lidarr.Plugin.Common" Version="1.0.0" />
</ItemGroup>
```

#### **Clean Up Qobuzarr Repository**
```bash
# Remove shared library from Qobuzarr repo
rm -rf Lidarr.Plugin.Common/

# Update imports to use NuGet package
# All existing imports still work: using Lidarr.Plugin.Common.Utilities;

# Remove shared library examples (moved to shared repo)
# Keep only Qobuz-specific examples
```

---

## 📋 **What Goes Where**

### **🔗 Shared Library Repository (Lidarr.Plugin.Common)**

#### **✅ Core Utilities (SHARED)**
- `FileNameSanitizer` - Cross-platform file naming
- `HttpClientExtensions` - HTTP utilities with retry  
- `RetryUtilities` - Circuit breaker, rate limiting
- `QualityMapper` - Universal quality comparison
- `PerformanceMonitor` - Metrics and monitoring

#### **✅ Universal Models (SHARED)**
- `StreamingArtist` - Universal artist representation
- `StreamingAlbum` - Universal album with quality support
- `StreamingTrack` - Universal track model  
- `StreamingQuality` - Quality abstraction across services
- `StreamingQualityTier` - Universal quality classification

#### **✅ Service Abstractions (SHARED)**
- `BaseStreamingSettings` - Common configuration patterns
- `IStreamingAuthenticationService` - Auth service contracts
- `StreamingIndexerMixin` - Composition helpers for Lidarr integration
- `StreamingApiRequestBuilder` - Fluent HTTP request building
- `LidarrIntegrationHelpers` - Generic Lidarr integration patterns

#### **✅ Testing Infrastructure (SHARED)**
- `MockFactories` - Realistic test data generators
- `TestDataSets` - Edge case scenarios
- All testing utilities and helpers

### **🎵 Plugin-Specific Repositories (Qobuzarr, Tidalarr, etc.)**

#### **✅ Service-Specific Code (SPECIALIZED)**
```
Qobuzarr/
├── QobuzApiClient              # Qobuz API implementation
├── QobuzIndexer               # Qobuz search specifics
├── QobuzDownloadClient        # Qobuz download implementation
├── QobuzAuthenticationService # Qobuz auth implementation
├── Qobuz* models              # Qobuz API response models
└── Qobuz-specific tests       # Service-specific test scenarios

Tidalarr/  
├── TidalApiClient             # Tidal API implementation
├── TidalIndexer               # Tidal search specifics  
├── TidalDownloadClient        # Tidal download implementation
├── TidalAuthenticationService # Tidal OAuth implementation
├── Tidal* models              # Tidal API response models
└── Tidal-specific tests       # Service-specific test scenarios
```

---

## 🚀 **Benefits of Separation**

### **For Shared Library Development**
✅ **Independent versioning** - Shared library evolves at its own pace  
✅ **Community contributions** - Developers can contribute to shared components  
✅ **Professional CI/CD** - Automated testing and NuGet publishing  
✅ **Ecosystem governance** - Clear ownership and contribution guidelines  
✅ **Quality assurance** - Focused testing for shared components  

### **For Plugin Development**  
✅ **Clean dependencies** - Reference shared library as standard NuGet package  
✅ **Focus on service logic** - No shared library maintenance burden  
✅ **Automatic updates** - Get shared library improvements via package updates  
✅ **Faster development** - Clone template, add NuGet reference, start coding  
✅ **Professional quality** - Inherit battle-tested patterns automatically  

### **For Ecosystem Growth**
✅ **Lower barrier to entry** - New developers just add NuGet package  
✅ **Consistent quality** - All plugins use same shared components  
✅ **Rapid expansion** - New streaming services in weeks, not months  
✅ **Community collaboration** - Shared improvements benefit everyone  

---

## 📋 **Implementation Plan**

### **Step 1: Create Shared Library Repository**
```bash
# Create new repository
gh repo create RicherTunes/Lidarr.Plugin.Common --public \
  --description "Shared library for Lidarr streaming service plugins - reduces development time by 60%+" \
  --gitignore VisualStudio --license MIT

# Setup repository with proper structure
cd Lidarr.Plugin.Common
git clone https://github.com/RicherTunes/Lidarr.Plugin.Common.git .
```

### **Step 2: Migrate Shared Components**  
```bash
# Copy all shared code (maintaining git history if possible)
cp -r ../Qobuzarr/Lidarr.Plugin.Common/* src/
cp -r ../Qobuzarr/examples/STREAMING-PLUGIN-TEMPLATE.md examples/
cp -r ../Qobuzarr/examples/Tidalarr-Optimized/ examples/
```

### **Step 3: Setup Professional CI/CD**
```yaml
# Independent build pipeline for shared library
# Automated testing, NuGet packaging, GitHub Packages publishing
# Quality gates and security scanning
# Documentation generation and deployment
```

### **Step 4: Update Plugin Repositories**
```xml
<!-- Qobuzarr.csproj - Clean plugin-specific project -->
<PackageReference Include="Lidarr.Plugin.Common" Version="1.0.0" />
<!-- Remove local project reference, remove shared library subdirectory -->
```

### **Step 5: Establish Governance**
```markdown
# CONTRIBUTING.md in shared library repository  
# Code review process for shared components
# Quality standards and testing requirements
# Release management and versioning strategy
```

---

## 🎯 **Repository Responsibilities**

### **Lidarr.Plugin.Common Repository**
- **Maintainers**: Core streaming plugin ecosystem team
- **Purpose**: Shared utilities, models, and patterns for ALL streaming services
- **Quality bar**: Enterprise-grade, comprehensive testing, security-first
- **Release cycle**: Stable releases with semantic versioning
- **Community**: Open to contributions from all streaming plugin developers

### **Plugin-Specific Repositories (Qobuzarr, Tidalarr, etc.)**
- **Maintainers**: Service-specific plugin teams  
- **Purpose**: Service-specific implementation and integration
- **Quality bar**: Production-ready using shared library foundation
- **Release cycle**: Independent releases based on service needs
- **Community**: Service-specific community with shared library collaboration

---

## 🚀 **Immediate Action Plan**

Let me create the separation plan and then implement it:

1. **Create repository structure** for Lidarr.Plugin.Common
2. **Migrate shared components** while preserving git history where possible  
3. **Setup CI/CD pipeline** for independent shared library development
4. **Configure NuGet publishing** for professional package distribution
5. **Update Qobuzarr** to use shared library as external NuGet dependency
6. **Create governance framework** for community contributions

**This separation will transform our prototype into a professional, scalable ecosystem ready for unlimited growth!**

Should we proceed with creating the new repository structure? This is the foundation for proper ecosystem development! 🚀