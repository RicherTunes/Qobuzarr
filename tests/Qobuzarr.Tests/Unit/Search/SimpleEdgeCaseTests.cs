using System.Linq;
using FluentAssertions;
using Qobuzarr.Tests.TestData;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Lidarr.Plugin.Common.Utilities;
using Xunit;
using Xunit.Abstractions;

// These tests pin the contract of the deprecated FileNameSanitizer for in-flight
// regression protection — the test SUBJECT is the obsolete API itself. Suppressing
// the obsolete-usage warning file-wide is the standard pattern for "this test
// exercises an obsolete type on purpose."
#pragma warning disable CS0618

namespace Qobuzarr.Tests.Unit.Search
{
    /// <summary>
    /// Simplified edge case testing for search functionality.
    /// </summary>
    public class SimpleEdgeCaseTests
    {
        private readonly ITestOutputHelper _output;

        public SimpleEdgeCaseTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData("AC/DC", "AC DC")] // Slash to space
        [InlineData("*NSYNC", "NSYNC")] // Remove leading special char
        [InlineData("P!nk", "P!nk")] // Preserve exclamation in middle
        [InlineData("Artist?", "Artist")] // Remove trailing special char
        [InlineData("Album:", "Album")] // Remove colon
        [InlineData("Folder\\File", "Folder File")] // Backslash to space
        [InlineData("Line1\nLine2", "Line1 Line2")] // Newline to space
        [InlineData("Multiple   Spaces", "Multiple Spaces")] // Collapse spaces
        public void FileNameSanitizer_CommonCases_ShouldSanitizeAsExpected(string input, string expected)
        {
            // Act
            var result = FileNameSanitizer.SanitizeFileName(input);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("宇多田ヒカル", "Japanese artist name")]
        [InlineData("방탄소년단", "Korean artist name")]
        [InlineData("Björk", "Nordic characters")]
        [InlineData("Françoise Hardy", "French accents")]
        public void FileNameSanitizer_WithUnicodeCharacters_ShouldPreserve(string input, string description)
        {
            // Arrange
            _output.WriteLine($"Testing: {input} ({description})");

            // Act
            var result = FileNameSanitizer.SanitizeFileName(input);

            // Assert
            result.Should().NotBeNullOrWhiteSpace();
            result.Should().Be(input); // Unicode should be preserved
        }

        [Theory]
        [InlineData("../../../etc", "Path traversal attempt")]
        [InlineData(".\\Windows\\System32", "Windows path attempt")]
        [InlineData("~/.ssh", "Unix home directory attempt")]
        public void FileNameSanitizer_WithSecurityThreats_ShouldBlock(string input, string description)
        {
            // Arrange
            _output.WriteLine($"Testing: {input} ({description})");

            // Act
            var sanitizedPath = FileNameSanitizer.SanitizePath(input);

            // Assert — verify the SECURITY OUTCOME, not a specific string-form implementation.
            // FileNameSanitizer's path-traversal guard evolved (Common Phase-1 fix): pure-dot
            // segments are now neutralized to "_.." rather than stripped, so a substring
            // `Contains("..")` no longer captures the intent. The semantic guarantee is:
            // (a) no segment is a pure-dot sequence (".", "..", "...") that Path.GetFullPath
            // would resolve as a parent-tree reference, (b) no home-dir shortcut (~), and
            // (c) no leading separator that would make the path absolute against an unintended
            // root. Form-agnostic checks below capture all three.
            var segments = sanitizedPath.Split(new[] { '/', '\\' }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                segment.Should().NotMatchRegex(@"^\.+$",
                    because: "pure-dot segments resolve as parent-tree references via Path.GetFullPath");
            }
            sanitizedPath.Should().NotContain("~");
            sanitizedPath.Should().NotStartWith("/");
            sanitizedPath.Should().NotStartWith("\\");
        }

        [Fact]
        public void SearchEdgeCases_ShouldHaveComprehensiveTestData()
        {
            // Act
            var allCases = SearchEdgeCases.AllTestCases.ToList();

            // Assert
            allCases.Should().NotBeEmpty();
            allCases.Count.Should().BeGreaterThan(50, "Should have comprehensive edge cases");

            // Check that we have various categories
            var specialCharCases = allCases.Where(c => c.Description.Contains("special") ||
                                                      c.ArtistName.Any(ch => !char.IsLetterOrDigit(ch) && ch != ' ')).ToList();
            specialCharCases.Should().NotBeEmpty("Should have special character cases");

            var unicodeCases = allCases.Where(c => c.ArtistName.Any(ch => ch > 127) ||
                                                  c.AlbumTitle.Any(ch => ch > 127)).ToList();
            unicodeCases.Should().NotBeEmpty("Should have Unicode cases");

            _output.WriteLine($"Total test cases: {allCases.Count}");
            _output.WriteLine($"Special character cases: {specialCharCases.Count}");
            _output.WriteLine($"Unicode cases: {unicodeCases.Count}");
        }

        [Theory]
        [InlineData("", "Empty string")]
        [InlineData("   ", "Whitespace only")]
        [InlineData("null", "Literal null")]
        [InlineData("undefined", "JavaScript-like undefined")]
        public void FileNameSanitizer_WithEmptyOrSpecialValues_ShouldHandleGracefully(string input, string description)
        {
            // Arrange
            _output.WriteLine($"Testing: '{input}' ({description})");

            // Act
            var result = FileNameSanitizer.SanitizeFileName(input);

            // Assert
            result.Should().NotBeNullOrWhiteSpace();
            if (string.IsNullOrWhiteSpace(input))
            {
                result.Should().Be("Unknown");
            }
        }

        [Fact]
        public void FileNameSanitizer_WithZeroWidthCharacters_ShouldRemove()
        {
            // Arrange
            var inputWithZeroWidth = "Arti\u200Bst"; // Zero-width space

            // Act
            var result = FileNameSanitizer.SanitizeFileName(inputWithZeroWidth);

            // Assert
            result.Should().Be("Artist");
            result.Should().NotContain("\u200B");
        }
    }
}
