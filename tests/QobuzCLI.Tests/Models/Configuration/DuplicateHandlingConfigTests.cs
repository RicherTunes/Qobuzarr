using System;
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
    [InlineData("", false)]              // Invalid: empty (all platforms)
    [InlineData("test/path", false)]     // Invalid: contains path separator (all platforms)
    public void IsValidReplacedFilesSuffix_UniversallyInvalidSuffixes_ReturnsFalse(string suffix, bool expected)
    {
        var config = new DuplicateHandlingConfig { ReplacedFilesSuffix = suffix };
        config.IsValidReplacedFilesSuffix().Should().Be(expected);
    }

    [Theory]
    [InlineData("test\\path")]    // Backslash - invalid on Windows (path sep), valid on Linux
    [InlineData("test:colon")]    // Colon - invalid on Windows, valid on Linux
    [InlineData("test<angle")]    // Less-than - invalid on Windows, valid on Linux
    [InlineData("test>angle")]    // Greater-than - invalid on Windows, valid on Linux
    [InlineData("test|pipe")]     // Pipe - invalid on Windows, valid on Linux
    [InlineData("test\"quote")]   // Quote - invalid on Windows, valid on Linux
    [InlineData("test?question")] // Question mark - invalid on Windows, valid on Linux
    [InlineData("test*star")]     // Asterisk - invalid on Windows, valid on Linux
    public void IsValidReplacedFilesSuffix_WindowsInvalidChars_OsAware(string suffix)
    {
        var config = new DuplicateHandlingConfig { ReplacedFilesSuffix = suffix };
        var result = config.IsValidReplacedFilesSuffix();

        if (OperatingSystem.IsWindows())
        {
            result.Should().BeFalse($"'{suffix}' should be invalid on Windows");
        }
        else
        {
            // On Linux/macOS, these chars are valid in filenames
            result.Should().BeTrue($"'{suffix}' should be valid on non-Windows platforms");
        }
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
