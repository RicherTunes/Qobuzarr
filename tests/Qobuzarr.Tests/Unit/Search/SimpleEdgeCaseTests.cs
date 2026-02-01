using System.Linq;
using FluentAssertions;
using Qobuzarr.Tests.TestData;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Lidarr.Plugin.Common.Utilities;
using Xunit;
using Xunit.Abstractions;

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

            // Assert
            sanitizedPath.Should().NotContain("..");
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
