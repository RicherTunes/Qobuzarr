> ⚠️ Historical (flagged 2026-05-31): describes a past state; some details below no longer match the current code.

# DryIoc Dependency Resolution - SOLVED

## Problem
The Qobuzarr plugin was trying to use DryIoc directly for service registration, causing compilation error:
```
error CS0246: The type or namespace name 'DryIoc' could not be found
```

## Root Cause
- Plugin was attempting to manually register services with DryIoc's `IContainer`
- DryIoc is used internally by Lidarr but NOT exposed to plugins
- Plugins should NOT reference or use DryIoc directly

## Solution: Use Lidarr's Auto-Registration

### Key Insights
1. **DryIoc is NOT available to plugins** - It's an internal Lidarr implementation detail
2. **Lidarr automatically discovers and registers**:
   - Classes implementing `IIndexer` (e.g., `QobuzIndexer`)
   - Classes implementing `IDownloadClient` (e.g., `QobuzDownloadClient`)
   - Any service class implementing an interface (registered as singleton)
3. **Manual registration is NOT required or supported**

### What Changed
- **Removed**: Direct DryIoc references and `IContainer` usage
- **Removed**: Manual service registration methods
- **Added**: Documentation helpers that explain auto-registration
- **Result**: Plugin builds successfully and services are auto-discovered by Lidarr

### Correct Pattern for Lidarr Plugins

```csharp
// ✅ CORRECT: Let Lidarr auto-register services
public interface IMyService { }
public class MyService : IMyService { }  // Auto-registered as singleton

// ✅ CORRECT: Indexer auto-discovered
public class QobuzIndexer : HttpIndexerBase<QobuzIndexerSettings> { }

// ✅ CORRECT: Download client auto-discovered  
public class QobuzDownloadClient : DownloadClientBase<QobuzDownloadSettings> { }
```

```csharp
// ❌ WRONG: Manual DI registration
using DryIoc;
container.Register<IMyService, MyService>(Reuse.Singleton);  // NOT SUPPORTED
```

### CLI Compatibility
The CLI project needs to handle its own DI since it runs standalone:
- Use simple manual instantiation or
- Use a lightweight DI container like Microsoft.Extensions.DependencyInjection
- Never depend on Lidarr's DryIoc container

## Verification
```bash
# Build successful without DryIoc
dotnet build Qobuzarr.csproj --configuration Debug
# Output: Build succeeded. 0 Error(s)
```

## Lessons Learned
1. **Lidarr plugins must follow conventions** - Auto-discovery over configuration
2. **Don't fight the framework** - Use Lidarr's patterns, not custom DI
3. **Keep it simple** - Let Lidarr handle the complexity of service registration

## References
- Lidarr auto-registers services implementing interfaces as singletons
- DryIoc is an internal implementation detail of Lidarr
- Plugins should focus on implementing interfaces, not registration