# Search Edge Cases Test Data

This directory contains comprehensive test data for validating search functionality with edge cases that are commonly problematic in music search and file system operations.

## Overview

The `SearchEdgeCases.cs` file provides a centralized test data set covering:
- Special characters and punctuation
- International characters and scripts
- Similar/duplicate album names
- File system sanitization challenges
- Query parsing edge cases

## Categories

### 1. Special Characters (`SpecialCharacterAlbums`)
Tests handling of:
- Punctuation: `/`, `&`, `'`, `$`, `*`, `!`, `?`, etc.
- Mathematical symbols: `∆`, `≠`, `∞`
- Brackets and quotes: `()`, `[]`, `{}`, `""`
- HTML/XML entities that need escaping

**Examples:**
- AC/DC - Back in Black
- Guns N' Roses - Appetite for Destruction
- "Weird Al" Yankovic - Mandatory Fun

### 2. International Characters (`InternationalAlbums`)
Tests Unicode handling for:
- Japanese (Kanji, Hiragana, Katakana)
- Korean (Hangul)
- Chinese (Traditional and Simplified)
- Arabic (RTL scripts)
- Cyrillic
- Special diacritics (Nordic, French, Spanish, German)

**Examples:**
- 宇多田ヒカル - First Love
- 방탄소년단 - MAP OF THE SOUL: 7
- Björk - Homogenic

### 3. Similar Names (`SimilarNameAlbums`)
Tests disambiguation for:
- Same title, different artists
- Multiple versions (Deluxe, Remastered, Live)
- Self-titled albums
- Greatest Hits variations

**Examples:**
- Pink Floyd - The Wall vs Roger Waters - The Wall
- Multiple "Weezer" self-titled albums

### 4. Sanitization Cases (`SanitizationCases`)
Tests file system safety for:
- Invalid file system characters
- Path traversal attempts
- Excessive whitespace
- Zero-width Unicode characters
- Very long names
- Empty/null-like values

**Examples:**
- "../../../etc" - Security test
- Names with tabs, newlines, multiple spaces
- Zero-width space characters (U+200B)

### 5. Query Parsing (`QueryParsingCases`)
Tests search query handling for:
- Boolean operators in names (AND, OR, NOT)
- Search wildcards (`*`, `?`)
- Pre-quoted content
- Numeric edge cases
- Case sensitivity

**Examples:**
- Band name "AND" or "OR"
- Albums with wildcards like "Artist*"

## Usage

### In Unit Tests

```csharp
[Theory]
[MemberData(nameof(GetSpecialCharacterTestCases))]
public void YourTest(SearchEdgeCases.SearchTestCase testCase)
{
    // Test your search/parsing logic
    var result = YourSearchMethod(testCase.ArtistName, testCase.AlbumTitle);
    
    // Validate handling
    result.Should().NotBeNull();
}

public static IEnumerable<object[]> GetSpecialCharacterTestCases()
{
    return SearchEdgeCases.SpecialCharacterAlbums
        .Select(tc => new object[] { tc });
}
```

### Getting All Test Cases

```csharp
// Get all test cases
var allCases = SearchEdgeCases.AllTestCases;

// Get by category
var internationalCases = SearchEdgeCases.GetByCategory(TestCategory.International);
```

### Performance Testing

```csharp
// Use for stress testing
foreach (var testCase in SearchEdgeCases.AllTestCases)
{
    var sanitized = FileNameSanitizer.SanitizeFileName(
        $"{testCase.ArtistName} - {testCase.AlbumTitle}");
}
```

## Adding New Test Cases

To add new edge cases:

1. Identify the category that best fits
2. Add to the appropriate list in `SearchEdgeCases.cs`
3. Include a descriptive comment explaining the edge case

```csharp
new SearchTestCase(
    artistName: "New Edge Case",
    albumTitle: "Problematic Title",
    description: "Tests handling of specific issue",
    expectedIssue: "Optional: what might go wrong"
)
```

## Test Coverage Goals

These test cases aim to ensure:
1. **Search Accuracy**: Special characters don't break search
2. **Unicode Support**: International content is preserved
3. **File System Safety**: Names are sanitized for storage
4. **Security**: Path traversal and injection attempts are blocked
5. **Performance**: Edge cases don't cause performance degradation
6. **User Experience**: Similar results are properly disambiguated

## Related Tests

- `EdgeCaseSearchTests.cs`: Validates search with edge cases
- `SearchPerformanceTests.cs`: Performance testing with edge cases
- `QobuzParserEdgeCaseTests.cs`: Parser validation with edge cases