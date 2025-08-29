# 🚀 Shared Library Quick Reference Card
## Daily Development Guide

> **Keep this handy during development**  
> Essential commands, patterns, and checks for `Lidarr.Plugin.Common` collaboration.

---

## ⚡ **Essential Commands**

### **Setup & Build**
```bash
# Initial setup
git submodule update --init --recursive
dotnet restore && dotnet build

# Enable CLI framework for development
# (Already configured in project files)

# Cross-plugin compatibility check
cd ../Qobuzarr && dotnet build --configuration Release
cd ../Tidalarr && dotnet build --configuration Release
```

### **Shared Library Development**
```bash
# Work on shared library
cd ext/Lidarr.Plugin.Common
git checkout -b feature/my-enhancement
# Make changes...
git commit -m "feat: add universal quality mapper"
git push origin feature/my-enhancement

# Update both consumer projects
cd ../../ && git submodule update --remote
git commit -m "update: shared library with new features"
```

### **Testing**
```bash
# Run all tests
dotnet test

# Test specific project
dotnet test tests/Qobuzarr.Tests/
dotnet test tests/QobuzCLI.Tests/

# With coverage
dotnet test --collect:"XPlat Code Coverage"
```

---

## 🔄 **Common Workflows**

### **Adding New Feature to Shared Library**

#### **Step 1: Design Check**
- [ ] ✅ Universal benefit (helps all streaming services)?
- [ ] ✅ Backwards compatible?
- [ ] ✅ Additive only (no breaking changes)?

#### **Step 2: Implementation**
```csharp
// ✅ GOOD: Optional enhancement
public static class NewFeatureExtensions
{
    public static IBuilder WithNewFeature(this IBuilder builder, NewFeatureOptions options = null)
    {
        options ??= NewFeatureOptions.Default;
        // Implementation...
        return builder;
    }
}

// ❌ BAD: Breaking existing API
// public static IEnhancedBuilder WithNewFeature(this IBuilder builder) // Returns different type!
```

#### **Step 3: Testing & Documentation**
```csharp
// Add tests
[TestMethod]
public void NewFeature_WorksWithQobuzPattern() { ... }

[TestMethod] 
public void NewFeature_WorksWithTidalPattern() { ... }

// Update documentation
// Add examples to README.md
// Update CHANGELOG.md
```

### **Bug Fix Workflow**

#### **Step 1: Reproduce**
```csharp
// Create failing test
[TestMethod]
public void Bug_ReproducesIssue()
{
    // Setup that demonstrates the bug
    var result = SomeMethod();
    Assert.Fail("This should work but doesn't");
}
```

#### **Step 2: Fix**
```csharp
// Fix implementation (maintain backwards compatibility)
public void SomeMethod()
{
    // Fixed implementation
    // Ensure no breaking changes
}
```

#### **Step 3: Verify**
```bash
# Verify fix works
dotnet test

# Verify no regression in both projects
cd ../Qobuzarr && dotnet build && dotnet test
cd ../Tidalarr && dotnet build && dotnet test
```

---

## 📋 **Code Review Checklist**

### **For Pull Requests**
- [ ] ✅ Backwards compatibility maintained
- [ ] ✅ Both Qobuzarr and Tidalarr patterns tested  
- [ ] ✅ Documentation updated
- [ ] ✅ No performance regression
- [ ] ✅ Security considerations addressed
- [ ] ✅ Error handling implemented
- [ ] ✅ Unit tests added
- [ ] ✅ Example usage provided

### **API Design Checklist**
- [ ] ✅ Method names are clear and descriptive
- [ ] ✅ Parameters have sensible defaults
- [ ] ✅ Return types are consistent with existing patterns
- [ ] ✅ Exceptions are well-defined
- [ ] ✅ Null handling is explicit
- [ ] ✅ Async methods follow async patterns
- [ ] ✅ Disposable resources are properly managed

---

## 🎯 **Implementation Patterns**

### **Service Integration Template**
```csharp
// 1. Settings (inherit from base)
public class MyServiceSettings : BaseStreamingSettings
{
    [FieldDefinition(1, Label = "API Key", Type = FieldType.Password)]
    public string ApiKey { get; set; }
}

// 2. Indexer (inherit from base) 
public class MyServiceIndexer : HttpIndexerBase<MyServiceSettings>
{
    public override string Protocol => nameof(MyServiceDownloadProtocol);
    
    public override async Task<IList<ReleaseInfo>> PerformQuery(TorznabQuery query)
    {
        // Use shared utilities
        var request = new StreamingApiRequestBuilder(Settings.BaseUrl)
            .Endpoint("search")
            .Query("q", query.GetQueryString())
            .ApiKey(Settings.ApiKey)
            .Build();
            
        var response = await _httpClient.ExecuteWithRetryAsync(request);
        return ParseResults(response.Content);
    }
}

// 3. Download Client (inherit from base)
public class MyServiceDownloadClient : BaseStreamingDownloadClient<MyServiceSettings>
{
    protected override string ServiceName => "MyService";
    
    protected override async Task<StreamingDownloadResult> DownloadTrackAsync(
        StreamingTrack track, string outputPath, CancellationToken cancellationToken = default)
    {
        var streamUrl = await GetStreamUrlAsync(track.Id);
        return await base.DownloadFromUrlAsync(streamUrl, outputPath, track, cancellationToken);
    }
}
```

### **Testing Template**
```csharp
[TestClass]
public class MyServiceTests
{
    [TestInitialize]
    public void Setup()
    {
        // Use shared mock factories
        _settings = MockFactories.CreateSettings<MyServiceSettings>();
        _httpClient = MockFactories.CreateHttpClient();
        _logger = MockFactories.CreateLogger<MyService>();
    }
    
    [TestMethod]
    public async Task MyMethod_ValidInput_ReturnsExpected()
    {
        // Arrange - Use shared test data
        var input = TestDataSets.CreateValidInput();
        var expectedResponse = TestDataSets.MyServiceResponse;
        
        _httpClient.Setup(x => x.ExecuteWithRetryAsync(It.IsAny<HttpRequestMessage>()))
            .ReturnsAsync(new HttpResponseMessage { Content = new StringContent(expectedResponse) });
        
        // Act
        var result = await _service.MyMethod(input);
        
        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Success);
    }
}
```

---

## 🚨 **Common Issues & Solutions**

### **Build Issues**

#### **"CLI Framework not found"**
```bash
# Solution: CLI framework is optional and conditional
# If you need CLI features:
dotnet build -p:IncludeCLIFramework=true

# Or add to project file:
<IncludeCLIFramework>true</IncludeCLIFramework>
```

#### **"TagLib namespace not found"**  
```bash
# Solution: Ensure TagLibSharp-Lidarr package is restored
dotnet restore
dotnet build
```

#### **"Assembly version conflicts"**
```bash
# Solution: Use consistent Lidarr assembly source
./download-lidarr-assemblies.sh --version 2.13.2.4685
# OR
./setup.sh  # Uses source build with version override
```

### **Runtime Issues**

#### **"Protocol property type mismatch"**
```csharp
// ✅ CORRECT (plugins branch compatible)
public override string Protocol => nameof(MyServiceDownloadProtocol);

// ❌ WRONG (release branch pattern)
// public override DownloadProtocol Protocol => DownloadProtocol.Unknown;
```

#### **"Dependency injection failures"**
```csharp
// ✅ CORRECT: Register services properly
services.AddSingleton<IMyService, MyService>();

// ❌ WRONG: Manual instantiation breaks DI
// var myService = new MyService(dependency); // Don't do this
```

---

## 📈 **Quality Gates**

### **Before Committing**
```bash
# 1. Build check
dotnet build --configuration Release

# 2. Test check
dotnet test

# 3. Cross-plugin compatibility
cd ../Qobuzarr && dotnet build
cd ../Tidalarr && dotnet build

# 4. Performance check (if applicable)
dotnet run --project benchmarks
```

### **Before Pull Request**
- [ ] ✅ Feature branch up to date with main
- [ ] ✅ All tests pass locally
- [ ] ✅ Documentation updated
- [ ] ✅ No breaking changes
- [ ] ✅ Both teams can review (add reviewers)

### **Before Release**
- [ ] ✅ Version number incremented
- [ ] ✅ CHANGELOG.md updated
- [ ] ✅ Both consumer projects tested
- [ ] ✅ Migration guide provided (if needed)
- [ ] ✅ Release notes prepared

---

## 🔗 **Quick Links**

### **Repository Links**
- [Lidarr.Plugin.Common](https://github.com/RicherTunes/Lidarr.Plugin.Common) - Shared library
- [Qobuzarr](https://github.com/RicherTunes/Qobuzarr) - Qobuz plugin  
- [Tidalarr](https://github.com/TidalAuthor/Tidalarr) - Tidal plugin

### **Documentation**
- [`SHARED-LIBRARY-COLLABORATION-GUIDE.md`](./SHARED-LIBRARY-COLLABORATION-GUIDE.md) - Full collaboration standards
- [`SHARED-LIBRARY-TECHNICAL-REFERENCE.md`](./SHARED-LIBRARY-TECHNICAL-REFERENCE.md) - Technical implementation guide
- [`README.md`](../ext/Lidarr.Plugin.Common/README.md) - Library usage guide

### **Communication**
- **Issues**: Use GitHub Issues with appropriate labels
- **Discussions**: GitHub Discussions for architecture decisions
- **Reviews**: Tag both teams for cross-plugin impact
- **Urgent**: Direct messaging with immediate GitHub notification

---

## 💡 **Pro Tips**

### **Development Efficiency**
```bash
# Alias for quick cross-plugin testing
alias test-both="cd ../Qobuzarr && dotnet build && cd ../Tidalarr && dotnet build && cd -"

# Alias for shared library work
alias work-shared="cd ext/Lidarr.Plugin.Common"

# Quick status check
alias status-all="git status && cd ext/Lidarr.Plugin.Common && git status && cd -"
```

### **Code Quality**
```csharp
// Use shared utilities instead of reimplementing
var sanitized = FileNameSanitizer.SanitizeFileName(userInput); // ✅
// var sanitized = userInput.Replace("/", "_"); // ❌ Don't reimplement

// Follow established patterns
var request = new StreamingApiRequestBuilder(baseUrl) // ✅ Use builder pattern
    .Endpoint("search")
    .Query("q", searchTerm)
    .WithStreamingDefaults()
    .Build();
```

### **Performance**  
```csharp
// Use shared caching
var result = await _cache.GetOrSetAsync(key, factory, ttl); // ✅

// Use shared retry logic
var response = await _httpClient.ExecuteWithRetryAsync(request); // ✅

// Use shared batching for bulk operations
var results = await _batchProcessor.ProcessAsync(items, options); // ✅
```

---

**🎯 Remember**: The goal is 60%+ development time savings while maintaining 100% backwards compatibility.

**📞 Need Help?** Open a GitHub issue or discussion with both teams tagged.

---

**Version**: 1.0  
**Last Updated**: 2025-08-29  
**Print this out**: Keep by your desk for quick reference!