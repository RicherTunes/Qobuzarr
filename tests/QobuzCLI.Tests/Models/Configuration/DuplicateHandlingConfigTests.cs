using FluentAssertions;
using QobuzCLI.Models.Configuration;
using Xunit;

namespace QobuzCLI.Tests.Models.Configuration;

/// <summary>
/// Tests for DuplicateHandlingConfig, focusing on suffix validation edge cases.
/// </summary>
public class DuplicateHandlingConfigTests
{
    [Theory]
    [InlineData(".replaced", true)]      // Valid: standard suffix
    [InlineData(".bak", true)]           // Valid: simple extension
    [InlineData("_old", true)]           // Valid: underscore prefix
    [InlineData("-backup", true)]        // Valid: hyphen prefix
    [InlineData(".replaced.bak", true)]  // Valid: multiple dots
    public void IsValidReplacedFilesSuffix_ValidSuffixes_ReturnsTrue(string suffix, bool expected)
    {
        var config = new DuplicateHandlingConfig { ReplacedFilesSuffix = suffix };
        config.IsValidReplacedFilesSuffix().Should().Be(expected);
    }

    [Theory]
    [InlineData("", false)]              // Invalid: empty
    [InlineData("test/path", false)]     // Invalid: contains path separator
    [InlineData("test\\path", false)]    // Invalid: contains backslash
    [InlineData("test:colon", false)]    // Invalid: contains colon (Windows)
    [InlineData("test<angle", false)]    // Invalid: contains angle bracket
    [InlineData("test>angle", false)]    // Invalid: contains angle bracket
    [InlineData("test|pipe", false)]     // Invalid: contains pipe
    [InlineData("test\"quote", false)]   // Invalid: contains quote
    [InlineData("test?question", false)] // Invalid: contains question mark
    [InlineData("test*star", false)]     // Invalid: contains asterisk
    public void IsValidReplacedFilesSuffix_InvalidSuffixes_ReturnsFalse(string suffix, bool expected)
    {
        var config = new DuplicateHandlingConfig { ReplacedFilesSuffix = suffix };
        config.IsValidReplacedFilesSuffix().Should().Be(expected);
    }

    [Theory]
    [InlineData(" .replaced", false)]    // Invalid: leading space (sanitization trims)
    [InlineData(".replaced ", false)]    // Invalid: trailing space (sanitization trims)
    [InlineData(".replaced.", false)]    // Invalid: trailing dot (Windows restriction)
    public void IsValidReplacedFilesSuffix_SanitizationChanges_ReturnsFalse(string suffix, bool expected)
    {
        // These test the strict validation: suffix must equal Sanitize.PathSegment(suffix)
        // Leading/trailing spaces and trailing dots are removed by sanitization
        var config = new DuplicateHandlingConfig { ReplacedFilesSuffix = suffix };
        config.IsValidReplacedFilesSuffix().Should().Be(expected);
    }

    [Fact]
    public void IsValidReplacedFilesSuffix_NullSuffix_ReturnsFalse()
    {
        var config = new DuplicateHandlingConfig { ReplacedFilesSuffix = null! };
        config.IsValidReplacedFilesSuffix().Should().BeFalse();
    }

    [Fact]
    public void IsValidReplacedFilesSuffix_DefaultValue_IsValid()
    {
        var config = new DuplicateHandlingConfig();
        // Default value ".replaced" should be valid
        config.ReplacedFilesSuffix.Should().Be(".replaced");
        config.IsValidReplacedFilesSuffix().Should().BeTrue();
    }
}
