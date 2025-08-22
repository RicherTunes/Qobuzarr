# Dual-Format Title Generation - Production Validation Checklist

## Architecture Validation Status

### ✅ APPROVED: Core Architecture
- **Dual-format approach**: Architecturally sound, targets root cause
- **Parser compatibility**: Directly addresses Parser.cs:73 regex patterns
- **Backward compatibility**: Preserves existing behavior for 75% of albums
- **Minimal risk**: Changes isolated to title generation logic

### 🔍 Testing Requirements

#### Unit Tests Required:
```csharp
[Test]
public void StandardAlbum_UsesBracketFormat()
{
    // Given: Standard album without edition
    var album = new QobuzAlbum { Title = "Kind of Blue", Version = null };
    
    // When: Generate title
    var title = parser.GenerateQualitySpecificTitle(album, quality, 1959);
    
    // Then: Uses bracket format
    Assert.That(title, Does.Match(@"Miles Davis - Kind of Blue \(1959\) \[FLAC\] \[WEB\]"));
}

[Test]
public void EditionAlbum_UsesHyphenFormat()
{
    // Given: Edition album
    var album = new QobuzAlbum { 
        Title = "They Want My Soul",
        Version = "Deluxe More Soul Edition"
    };
    
    // When: Generate title
    var title = parser.GenerateQualitySpecificTitle(album, quality, 2024);
    
    // Then: Uses hyphen format
    Assert.That(title, Does.Match(@"Spoon-They Want My Soul-Deluxe More Soul Edition-WEB-2024"));
}

[Test]
public void HyphenFormat_ParsedCorrectlyByLidarr()
{
    // Given: Hyphen format title
    var title = "Spoon-They Want My Soul-Deluxe More Soul Edition-WEB-2024";
    
    // When: Parse with Lidarr parser
    var parsed = Parser.ParseAlbumTitle(title);
    
    // Then: Version extracted correctly
    Assert.That(parsed.ReleaseVersion, Is.EqualTo("Deluxe More Soul Edition"));
    Assert.That(parsed.AlbumTitle, Is.EqualTo("They Want My Soul"));
}
```

#### Integration Tests Required:
1. **Decision Engine Acceptance**: Verify edition albums pass all specifications
2. **Search Result Mapping**: Confirm correct album IDs assigned
3. **Download Flow**: Ensure downloads work with new format
4. **UI Display**: Check Lidarr UI shows titles correctly

### 🎯 Edge Cases to Validate

1. **Albums with hyphens in names**:
   - Input: "Seventy-Seven" by Nude Party
   - Expected: Proper parsing despite embedded hyphens

2. **Missing version information**:
   - Input: Album with empty Version field
   - Expected: Falls back to bracket format

3. **Extremely long version strings**:
   - Input: 200+ character version description
   - Expected: Intelligent truncation, parser doesn't break

4. **Special characters in artist/album**:
   - Input: "AC/DC", "Ke$ha", etc.
   - Expected: Proper sanitization for parser

5. **Year edge cases**:
   - Missing year (0 or null)
   - Future years
   - Very old years (pre-1900)

### 📊 Performance Validation

- **Parser Performance**: Measure regex matching time
- **Memory Usage**: Validate no memory leaks with string operations
- **Concurrency**: Test with parallel searches

### 🔒 Security Considerations

- **Input Sanitization**: Prevent injection via album/artist names
- **Format String Safety**: No user input in format strings
- **Length Validation**: Prevent buffer overflows

### 📈 Rollout Strategy

#### Phase 1: Canary Testing (1 week)
- Deploy to single test instance
- Monitor parser success rate
- Track Decision Engine acceptance rate
- Measure album ID mapping accuracy

#### Phase 2: Limited Rollout (1 week)
- Deploy to 10% of users
- Monitor for breaking changes
- Collect performance metrics
- Gather user feedback

#### Phase 3: Full Production (ongoing)
- Deploy to all users
- Continue monitoring
- Be ready for quick rollback if issues arise

### 🚨 Rollback Plan

If issues detected:
1. Revert to single bracket format for all albums
2. Clear search cache to force re-indexing
3. No database changes needed
4. Users re-search to get corrected results

### 📝 Monitoring Metrics

Track these KPIs post-deployment:
- **Parser Success Rate**: Should stay >99%
- **Decision Engine Rejection Rate**: Should decrease by 15-20%
- **Edition Album Mapping Accuracy**: Target >95%
- **Search Performance**: No degradation allowed
- **User-Reported Issues**: Track via GitHub issues

## Final Architecture Recommendation

**✅ APPROVED FOR IMPLEMENTATION**

The dual-format title generation approach is:
- Architecturally sound
- Addresses root cause effectively
- Maintains backward compatibility
- Has acceptable risk profile
- Provides clear rollback path

Proceed with implementation following the validation checklist above.