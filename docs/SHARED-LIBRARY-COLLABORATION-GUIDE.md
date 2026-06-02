# 🤝 Shared Library Collaboration Guide

## Qobuzarr ↔ Tidalarr Development Standards

> **Critical Reference for Multi-Team Development**  
> This document establishes standards for collaborative development of `Lidarr.Plugin.Common` shared between Qobuzarr and Tidalarr teams.

---

## 🎯 **Executive Summary**

**Shared Asset**: `Lidarr.Plugin.Common` library (60%+ code reduction proven)  
**Primary Users**: Qobuzarr (RicherTunes) <!-- TODO(docval): Tidalarr (TidalAuthor) not found as an active consumer repository as of 2026-05-31 - only exists in examples/ -->, Future plugins  
**Development Model**: Collaborative evolution with backwards compatibility  
**Success Metric**: 74% code reduction claimed <!-- TODO(docval): actual code reduction % not verified in code as of 2026-05-31 -->, maintain/improve this standard

---

## 🏗️ **Architecture Standards**

### **Core Design Principles**

```
1. ✅ **Backwards Compatibility First** - Never break existing implementations
2. ✅ **Additive Changes Only** - Extend, don't replace existing APIs  
3. ✅ **Optional Features** - New functionality must be opt-in
4. ✅ **Universal Patterns** - Solutions must work for all streaming services
5. ✅ **Performance Conscious** - Maintain 60%+ development time savings
```

### **Shared Namespace Structure**

```csharp
Lidarr.Plugin.Common
├── Base/                    // Core abstract classes (STABLE - minimal changes)
├── Models/                  // Universal data models (EXTEND ONLY)
├── Services/               // Shared business logic (BACKWARDS COMPATIBLE)  
├── Utilities/              // Helper functions (PURE ADDITIONS)
├── CLI/                    // Optional CLI framework (CONDITIONAL)
└── Testing/                // Mock factories and test utilities
```

### **API Stability Levels**

```
🟢 **STABLE** (Base/, Models/Core): Require RFC process for changes
🟡 **EVOLVING** (Services/, Utilities/): Additive changes welcome
🔵 **EXPERIMENTAL** (CLI/, Testing/): Rapid iteration allowed
```

---

## 📋 **Contribution Guidelines**

### **🚀 For Feature Additions**

**1. Universal Benefit Test**

```
✅ Does this help ALL streaming services? (Qobuz, Tidal, future services)
✅ Does this maintain the 60%+ development time saving?
✅ Is this additive (doesn't break existing code)?
```

**2. Implementation Pattern**

```csharp
// ✅ GOOD: Optional, backwards compatible
public class BaseStreamingDownloadClient<TSettings> 
{
    // Existing method - never change signature
    protected virtual async Task<StreamingDownloadResult> DownloadTrackAsync(...)

    // ✅ NEW: Additive enhancement
    protected virtual async Task<StreamingDownloadResult> DownloadTrackWithRetryAsync(
        string trackId, 
        RetryOptions options = null) // Optional parameter
    {
        options ??= RetryOptions.Default;
        // Implementation using existing DownloadTrackAsync
    }
}

// ❌ BAD: Breaking change
// protected virtual async Task<EnhancedDownloadResult> DownloadTrackAsync(...) // Changed return type
```

**3. Documentation Requirements**

```
✅ XML documentation on all public APIs
✅ Usage examples in README.md
✅ Migration guide if any behavior changes
✅ Performance impact assessment
```

### **🔧 For Bug Fixes**

**Priority Order:**

1. **Critical**: Breaks existing functionality → Immediate fix
2. **High**: Performance regression → Next release  
3. **Medium**: Edge case issues → Planned release
4. **Low**: Code style/cleanup → Opportunistic

**Testing Requirements:**

```
✅ Unit tests for the specific bug
✅ Integration test showing fix works in real plugin
✅ Regression test to prevent future occurrences
✅ Test with BOTH Qobuzarr AND Tidalarr integration patterns
```

---

## 🔄 **Version Management & Release Process**

### **Semantic Versioning Standards**

```
MAJOR.MINOR.PATCH[-PRERELEASE]

Examples:
1.1.0    - Current stable (Qobuzarr production)
1.2.0    - Next minor (Tidalarr collaborative features)
1.2.1    - Bug fix (backwards compatible)
2.0.0    - ONLY if breaking changes absolutely required
```

### **Release Workflow**

#### **1. Development Phase**

```bash
# Tidalarr working branch
git checkout -b feature/tidal-oauth-patterns
git push origin feature/tidal-oauth-patterns

# Development in isolation, Qobuzarr unaffected
git submodule update --remote ext/Lidarr.Plugin.Common  # Gets latest stable
```

#### **2. Integration Testing**

```bash
# Test with BOTH plugins before merge
cd ../Qobuzarr && git submodule update --init && dotnet build  # Verify no breakage
cd ../Tidalarr && git submodule update --init && dotnet build   # Verify works
```

#### **3. Release Preparation**

```bash
# Create release PR in Lidarr.Plugin.Common
git checkout main
git merge feature/tidal-oauth-patterns
git tag v1.2.0
git push origin main --tags

# Update consuming projects
cd ../Qobuzarr && git submodule update --remote && git commit -m "update: shared library v1.2.0"
cd ../Tidalarr && git submodule update --remote && git commit -m "update: shared library v1.2.0"
```

### **Hotfix Process**

```bash
# Critical bug affecting production
git checkout v1.1.0              # Last stable
git checkout -b hotfix/critical-auth-bug
# Fix + test
git tag v1.1.1
git push origin hotfix/critical-auth-bug --tags

# Both teams update immediately
git submodule update --remote --merge
```

---

## 🧪 **Testing & Quality Standards**

### **Test Coverage Requirements**

```
✅ Unit Tests: 80%+ coverage on public APIs
✅ Integration Tests: Each major workflow path
✅ Cross-Plugin Tests: Verify both Qobuz + Tidal patterns work
✅ Performance Tests: Maintain 60%+ development time savings
```

### **Testing Matrix**

| Component | Qobuzarr Test | Tidalarr Test | Universal Test |
|-----------|---------------|---------------|----------------|
| BaseStreamingDownloadClient | ✅ | ✅ | ✅ |
| OAuth patterns | N/A | ✅ | ✅ |
| Basic auth patterns | ✅ | N/A | ✅ |
| File utilities | ✅ | ✅ | ✅ |
| Quality mapping | ✅ | ✅ | ✅ |

### **Pre-Commit Requirements**

```bash
# Automated checks (both repos)
✅ dotnet build (both projects)
✅ dotnet test (both projects) 
✅ No breaking changes in public APIs
✅ Performance benchmark (if applicable)
```

---

## 🎯 **Development Workflow Examples**

### **Example 1: Tidalarr Adds OAuth Support**

**Tidalarr Implementation:**

```csharp
// TODO(docval): OAuthStreamingAuthenticationService not found in code as of 2026-05-31
// // 1. Tidalarr adds OAuth to shared library
// public abstract class OAuthStreamingAuthenticationService<TCredentials, TSession>
//     : BaseStreamingAuthenticationService<TCredentials, TSession>
//     where TCredentials : OAuthCredentials
// {
//     // OAuth-specific implementation
// }
//
// // 2. Tidalarr uses new base class
// public class TidalAuthenticationService : OAuthStreamingAuthenticationService<TidalCredentials, TidalSession>
// {
//     // Tidal-specific OAuth implementation
// }
```

**Qobuzarr Integration (Optional):**

```csharp
// 3. Qobuzarr can optionally adopt (when ready)
public class QobuzAuthenticationService : BaseStreamingAuthenticationService<QobuzCredentials, QobuzSession>
{
    // Continues using basic auth - NO BREAKING CHANGES
    // Can migrate to OAuth later: : OAuthStreamingAuthenticationService<...>
}
```

### **Example 2: Qobuzarr Adds ML Query Optimization**

**Qobuzarr Implementation:**

```csharp
// 1. Add optional ML enhancement
public static class QueryOptimizationExtensions
{
    public static IStreamingApiRequestBuilder WithMLOptimization(
        this IStreamingApiRequestBuilder builder, 
        MLOptimizationOptions options = null)
    {
        // ML optimization logic
        return builder;
    }
}
```

**Tidalarr Usage (Optional):**

```csharp
// 2. Tidalarr can opt-in when beneficial
var request = new StreamingApiRequestBuilder(baseUrl)
    .Endpoint("search/albums")
    .Query("q", searchTerm)
    .WithMLOptimization() // ✅ Optional enhancement
    .Build();
```

### **Example 3: Both Teams Add Quality Mapping**

**Collaborative Development:**

```csharp
// Universal quality mapping (both teams contribute)
public static class UniversalQualityMapper
{
    // Qobuzarr contributes
    public static LidarrQuality MapQobuzQuality(int qobuzQualityId) { ... }
    
    // Tidalarr contributes  
    public static LidarrQuality MapTidalQuality(string tidalQualityLevel) { ... }
    
    // Shared logic both use
    public static LidarrQuality FindBestMatch(IEnumerable<object> availableQualities, QualityTier targetTier) { ... }
}
```

---

## 🚨 **Conflict Resolution Process**

### **Technical Conflicts**

```
1. 🤝 **Discussion First**: GitHub issue with both teams tagged
2. 🧪 **Proof of Concept**: Both approaches implemented in branches
3. 📊 **Metrics Comparison**: Performance, maintainability, universality
4. 🎯 **Decision**: Based on objective criteria, not team preference
5. 📝 **Documentation**: Decision rationale recorded
```

### **API Design Conflicts**

```
🥇 **Priority 1**: Backwards compatibility (never break existing)
🥈 **Priority 2**: Universal applicability (works for all services)  
🥉 **Priority 3**: Performance impact (maintain development time savings)
🏅 **Priority 4**: Code elegance (prefer clean, but not at cost of above)
```

### **Release Timing Conflicts**

```
🔥 **Critical Bugs**: Immediate hotfix (both teams coordinate)
⚡ **Feature Releases**: Monthly cadence, alternating team lead
📅 **Major Changes**: Quarterly planning, 6 week advance notice
🎯 **Breaking Changes**: Avoid entirely, or 6 month deprecation cycle
```

---

## 📈 **Success Metrics & Monitoring**

### **Key Performance Indicators**

```
🎯 **Development Time Savings**: Maintain 60%+ (currently 74% with Tidalarr)
🎯 **API Stability**: < 1 breaking change per year
🎯 **Cross-Plugin Compatibility**: 100% (both projects always build)
🎯 **Test Coverage**: > 80% on shared components
🎯 **Documentation Currency**: < 7 days lag on API changes
```

### **Regular Health Checks**

```bash
# Monthly automated report
# 1. Dependency analysis
dotnet list package --vulnerable --include-transitive

# 2. Performance regression detection  
dotnet run --project benchmarks

# 3. API compatibility verification
dotnet build ../Qobuzarr && dotnet build ../Tidalarr

# 4. Test coverage analysis
dotnet test --collect:"XPlat Code Coverage"
```

---

## 🎖️ **Recognition & Responsibility**

### **Shared Ownership Model**

```
🏛️ **Governance**: Both teams have equal voice in major decisions
🔧 **Implementation**: Contributing team owns the feature lifecycle  
🧪 **Testing**: Both teams verify compatibility with their use cases
📚 **Documentation**: Contributing team documents, reviewing team validates
🚀 **Releases**: Alternating release management between teams
```

### **Code Review Standards**

```
✅ **Required Reviewers**: At least 1 from each team (Qobuzarr + Tidalarr) <!-- TODO(docval): only Qobuzarr team verified; Tidalarr not found as active consumer as of 2026-05-31 -->
✅ **Review Criteria**: Functionality, compatibility, performance, documentation
✅ **Approval Process**: 2 approvals minimum (1 per team)
✅ **Timeline**: 48 hour review SLA, 7 day maximum
```

---

## 🛠️ **Development Environment Setup**

### **Standard Development Setup**

```bash
# 1. Clone both projects
git clone https://github.com/RicherTunes/Qobuzarr.git
git clone https://github.com/TidalAuthor/Tidalarr.git

# 2. Shared library development
cd shared-workspace/
git clone https://github.com/RicherTunes/Lidarr.Plugin.Common.git

# 3. Link for development
cd Qobuzarr && git submodule add ../shared-workspace/Lidarr.Plugin.Common ext/Lidarr.Plugin.Common
cd ../Tidalarr && git submodule add ../shared-workspace/Lidarr.Plugin.Common ext/Lidarr.Plugin.Common

# 4. Enable CLI framework for development
# (Both projects have IncludeCLIFramework=true configured)
```

### **Build Verification Script**

```bash
#!/bin/bash
# verify-cross-compatibility.sh

echo "🔍 Verifying cross-plugin compatibility..."

# Test Qobuzarr with current shared library
cd Qobuzarr
git submodule update --init --recursive
dotnet build --configuration Release
if [ $? -ne 0 ]; then
    echo "❌ Qobuzarr build failed"
    exit 1
fi

# Test Tidalarr with current shared library  
cd ../Tidalarr
git submodule update --init --recursive
dotnet build --configuration Release
if [ $? -ne 0 ]; then
    echo "❌ Tidalarr build failed"
    exit 1
fi

echo "✅ Cross-plugin compatibility verified"
```

---

## 📞 **Communication Protocols**

### **Regular Sync Points**

```
📅 **Weekly**: Async status update (GitHub project board)
📅 **Bi-weekly**: Video sync if active collaboration
📅 **Monthly**: Architecture review and planning
📅 **Quarterly**: Roadmap alignment and metrics review
```

### **Issue Management**

```
🏷️ **Labels**: 
   - `needs-qobuzarr-review` - Requires Qobuzarr team input
   - `needs-tidalarr-review` - Requires Tidalarr team input  
   - `breaking-change` - Requires special approval process
   - `performance-critical` - Needs benchmarking
   - `cross-plugin-impact` - Affects both plugins

🎯 **Assignment**:
   - Bug reports: Assigned to team that last modified component
   - Feature requests: Assigned to team with domain expertise
   - API changes: Both teams assigned for review
```

### **Emergency Communication**

```
🚨 **Critical Issues**: Direct messaging (Discord/Slack)
⚡ **Urgent Reviews**: GitHub review request + direct ping
📝 **Major Decisions**: GitHub Discussion + both teams tagged
🎯 **Release Coordination**: 48 hour advance notice minimum
```

---

## 🎯 **Quick Reference Checklist**

### **Before Adding New Features**

- [ ] ✅ Universal benefit (helps all streaming services)
- [ ] ✅ Backwards compatible (doesn't break existing code)
- [ ] ✅ Optional integration (existing projects unaffected)
- [ ] ✅ Documented with examples
- [ ] ✅ Tests for both integration patterns

### **Before Making Changes**

- [ ] ✅ Issue created with impact assessment
- [ ] ✅ Both teams notified and consulted
- [ ] ✅ Cross-plugin compatibility verified
- [ ] ✅ Performance impact measured
- [ ] ✅ Documentation updated

### **Before Releasing**

- [ ] ✅ Both Qobuzarr and Tidalarr build successfully
- [ ] ✅ Test suites pass for both projects
- [ ] ✅ Migration guide provided (if applicable)
- [ ] ✅ Version incremented appropriately
- [ ] ✅ Release notes include cross-plugin impact

---

## 🚀 **Conclusion**

This shared library represents a **strategic partnership** between Qobuzarr and Tidalarr teams. The 74% code reduction already achieved with Tidalarr proves the model works.

**Success depends on**:

- **Mutual respect** for each team's use cases <!-- TODO(docval): "each team" implies multiple teams but only Qobuzarr team is verified as of 2026-05-31 -->
- **Backwards compatibility** as the highest priority
- **Universal solutions** over service-specific hacks
- **Open communication** and collaborative decision making

**The goal**: Make streaming plugin development **effortless** for both teams while maintaining the **highest quality standards**.

---

**Document Version**: 1.0  
**Last Updated**: 2025-08-29  
**Next Review**: 2025-09-29  
**Maintained By**: Qobuzarr + Tidalarr Teams

**💡 Questions?** Open a GitHub Discussion in `Lidarr.Plugin.Common` with both teams tagged.
