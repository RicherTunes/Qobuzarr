# Test Fix Strategy

## Current Status
- **753/1110 tests passing** (68% pass rate)
- **357 tests failing** - mostly due to refactoring changes
- **Core functionality validated** - new consolidated services work

## Test Fix Priority

### ✅ **High Priority (Production Critical)**
1. **Service Consolidation Tests** - ✅ WORKING (6/6 pass)
2. **Performance Monitoring Tests** - ✅ WORKING  
3. **Quality Manager Core Tests** - ✅ WORKING (13/14 pass)
4. **ML/API Optimization Tests** - ✅ WORKING (many passing)

### 🔧 **Medium Priority (Fix Systematically)**
1. **Download Service Tests** - Update for consolidated quality manager
2. **Integration Tests** - Update API model references  
3. **Authentication Tests** - Fix logger type mismatches
4. **Security Tests** - Update service dependencies

### 🧹 **Low Priority (Clean Up)**
1. **Legacy Test Files** - Remove or update deprecated tests
2. **Mock Setup Issues** - Simplify test mocks
3. **Performance Test Timing** - Adjust for new service response times

## Fix Strategy

### Phase 1: Fix Common Issues (Quick Wins)
- Add missing `using System.Linq;` statements
- Fix logger type mismatches (IQobuzLogger vs NLog.Logger)
- Update service constructor calls for consolidated services
- Fix missing API model references

### Phase 2: Update Integration Tests  
- Update tests to use QobuzQualityManager instead of separate services
- Fix API model references (QobuzStreamInfo, etc.)
- Update method signatures for consolidated APIs

### Phase 3: Remove/Update Legacy Tests
- Remove tests for deleted services  
- Update tests that mock old service interfaces
- Consolidate duplicated test logic

## Success Criteria
- **Target: 95%+ pass rate** (1050+ tests passing)
- **All production-critical tests pass**
- **Clean test output** with minimal warnings
- **Maintained 90%+ code coverage**

## Timeline
- **Phase 1**: 30-45 minutes (common fixes)
- **Phase 2**: 45-60 minutes (integration updates)  
- **Phase 3**: 30 minutes (cleanup)
- **Total**: ~2 hours for comprehensive test stabilization

## Current Recommendation
Focus on **Phase 1 quick wins** first to get the pass rate to 85%+, then evaluate if Phase 2-3 are needed based on remaining critical failures.