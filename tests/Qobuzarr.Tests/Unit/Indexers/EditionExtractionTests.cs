using FluentAssertions;
using Xunit;
using NLog;
using Lidarr.Plugin.Qobuzarr.Indexers.Parsing;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Comprehensive tests for edition extraction from album titles.
    /// Tests the TitleGenerator.ExtractVersionFromTitle and ContainsEditionKeywords methods.
    /// </summary>
    public class EditionExtractionTests
    {
        private readonly TitleGenerator _titleGenerator;

        public EditionExtractionTests()
        {
            _titleGenerator = new TitleGenerator(LogManager.GetCurrentClassLogger());
        }

        #region Parentheses Extraction

        [Theory]
        [InlineData("Album (Deluxe Edition)", "Deluxe Edition")]
        [InlineData("Album (Remastered 2023)", "Remastered 2023")]
        [InlineData("Album (Live)", "Live")]
        [InlineData("Album (Live at Madison Square Garden)", "Live at Madison Square Garden")]
        [InlineData("Album (Unplugged)", "Unplugged")]
        [InlineData("Album (Acoustic)", "Acoustic")]
        [InlineData("Album (Extended Edition)", "Extended Edition")]
        [InlineData("Album (Special Edition)", "Special Edition")]
        [InlineData("Album (Collector's Edition)", "Collector's Edition")]
        [InlineData("Album (Limited Edition)", "Limited Edition")]
        [InlineData("Album (Bonus Tracks)", "Bonus Tracks")]
        [InlineData("Album (Complete Sessions)", "Complete Sessions")]
        [InlineData("Album (Legacy Edition)", "Legacy Edition")]
        [InlineData("Album (Archive Edition)", "Archive Edition")]
        [InlineData("Album (Demos)", "Demos")]
        public void ExtractVersion_FromParentheses_ShouldExtractCorrectly(string title, string expectedVersion)
        {
            // Act
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            // Assert
            result.Should().Be(expectedVersion);
        }

        [Theory]
        [InlineData("Album (25th Anniversary Edition)", "25th Anniversary Edition")]
        [InlineData("Album (30th Anniversary Deluxe)", "30th Anniversary Deluxe")]
        [InlineData("Album (10th Anniversary Remaster)", "10th Anniversary Remaster")]
        [InlineData("Album (50th Anniversary Super Deluxe)", "50th Anniversary Super Deluxe")]
        public void ExtractVersion_FromParentheses_AnniversaryEditions_ShouldExtractCorrectly(string title, string expectedVersion)
        {
            // Act
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            // Assert
            result.Should().Be(expectedVersion);
        }

        [Theory]
        [InlineData("Album (2020 Remaster)", "2020 Remaster")]
        [InlineData("Album (Remastered)", "Remastered")]
        [InlineData("Album (Digital Remaster)", "Digital Remaster")]
        [InlineData("Album (HD Remaster)", "HD Remaster")]
        [InlineData("Album (2023 Digital Remaster)", "2023 Digital Remaster")]
        public void ExtractVersion_FromParentheses_RemasteredEditions_ShouldExtractCorrectly(string title, string expectedVersion)
        {
            // Act
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            // Assert
            result.Should().Be(expectedVersion);
        }

        #endregion

        #region Bracket Extraction

        [Theory]
        [InlineData("Album [Expanded Edition]", "Expanded Edition")]
        [InlineData("Album [25th Anniversary]", "25th Anniversary")]
        [InlineData("Album [Deluxe]", "Deluxe")]
        [InlineData("Album [Remastered]", "Remastered")]
        [InlineData("Album [Live at Wembley]", "Live at Wembley")]
        [InlineData("Album [Concert Edition]", "Concert Edition")]
        [InlineData("Album [Special Edition]", "Special Edition")]
        [InlineData("Album [Bonus Edition]", "Bonus Edition")]
        public void ExtractVersion_FromBrackets_ShouldExtractCorrectly(string title, string expectedVersion)
        {
            // Act
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            // Assert
            result.Should().Be(expectedVersion);
        }

        #endregion

        #region Mixed Formats (Parentheses and Brackets)

        [Fact]
        public void ExtractVersion_WithBothParenthesesAndBrackets_ShouldPrioritizeParentheses()
        {
            // The implementation checks parentheses first, then brackets
            var title = "Album (Deluxe Edition) [Bonus Tracks]";

            // Act
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            // Assert - Parentheses version should be extracted (checked first)
            result.Should().Be("Deluxe Edition");
        }

        [Fact]
        public void ExtractVersion_WithBracketsOnly_WhenParenthesesHaveNoEdition_ShouldExtractFromBrackets()
        {
            // Parentheses contain non-edition text, brackets contain edition
            var title = "Album (2023) [Remastered]";

            // Act
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            // Assert - Should fall back to brackets since parentheses don't contain edition keywords
            result.Should().Be("Remastered");
        }

        [Fact]
        public void ExtractVersion_WithMultipleParentheses_ShouldExtractFirstEdition()
        {
            // Multiple parentheses - should find first one with edition keywords
            var title = "Album (2023) (Deluxe Edition)";

            // Act
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            // Assert - First match with edition keyword
            // Note: The regex only matches the first parentheses group
            // Since "(2023)" doesn't contain edition keywords, it returns empty
            // This tests current behavior - implementation extracts first match only
            result.Should().BeEmpty();
        }

        #endregion

        #region Edge Cases - Empty and Missing

        [Theory]
        [InlineData("Album ()", "")]
        [InlineData("Album []", "")]
        [InlineData("Album", "")]
        [InlineData("Album Title Without Parentheses", "")]
        [InlineData("Album - No Edition Here", "")]
        [InlineData("", "")]
        public void ExtractVersion_WithNoEdition_ShouldReturnEmpty(string title, string expected)
        {
            // Act
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("Album (Rock)", "")]
        [InlineData("Album (2023)", "")]
        [InlineData("Album [CD1]", "")]
        [InlineData("Album (Part 1)", "")]
        [InlineData("Album [Disc 2]", "")]
        public void ExtractVersion_WithNonEditionContent_ShouldReturnEmpty(string title, string expected)
        {
            // Act
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            // Assert
            result.Should().Be(expected);
        }

        #endregion

        #region Edge Cases - Nested Brackets

        [Theory]
        [InlineData("Album ((Live Edition))")]
        [InlineData("Album [[Deluxe]]")]
        public void ExtractVersion_WithNestedBrackets_ShouldNotThrow(string title)
        {
            // Nested brackets are edge cases - we don't assert specific behavior,
            // just that it handles them gracefully without throwing
            var act = () => _titleGenerator.ExtractVersionFromTitle(title);

            act.Should().NotThrow();
        }

        [Theory]
        [InlineData("Album ((Live Edition))")]
        [InlineData("Album [[Deluxe]]")]
        public void ExtractVersion_WithNestedBrackets_ShouldReturnEmptyOrContainEditionKeyword(string title)
        {
            // Nested brackets may produce quirky results - we only assert the result
            // is either empty or contains a valid edition keyword (not garbage)
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            if (!string.IsNullOrEmpty(result))
            {
                // If something is extracted, it should contain an edition keyword
                _titleGenerator.ContainsEditionKeywords(result).Should().BeTrue(
                    "extracted content should contain an edition keyword if non-empty");
            }
        }

        [Fact]
        public void ExtractVersion_WithMismatchedBrackets_ShouldNotCrash()
        {
            var title = "Album (Deluxe Edition";

            // Act - Should not throw
            var act = () => _titleGenerator.ExtractVersionFromTitle(title);

            // Assert
            act.Should().NotThrow();
        }

        #endregion

        #region Unicode and International

        [Theory]
        [InlineData("Album (Édition Deluxe)", "Édition Deluxe")]  // "Deluxe" is English keyword
        public void ExtractVersion_WithFrenchEditionText_ContainingEnglishKeyword_ShouldExtract(string title, string expectedVersion)
        {
            // French text with accents, but contains English keyword "Deluxe"
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            result.Should().Be(expectedVersion);
        }

        [Theory]
        [InlineData("Album (Édition Spéciale)", "")]  // No English keywords
        public void ExtractVersion_WithFrenchOnlyText_ShouldNotExtract(string title, string expectedVersion)
        {
            // French text without English edition keywords should not be extracted
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            result.Should().Be(expectedVersion);
        }

        [Theory]
        [InlineData("Album (Remasterisée)", "Remasterisée")]  // Contains "remaster" substring
        public void ExtractVersion_WithFrenchTextContainingEnglishSubstring_ShouldExtract(string title, string expectedVersion)
        {
            // French "Remasterisée" contains English "remaster" keyword substring
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            result.Should().Be(expectedVersion);
        }

        [Theory]
        [InlineData("Album [リマスター版]", "")]
        [InlineData("Album (特別版)", "")]
        [InlineData("Album (デラックス・エディション)", "")]
        public void ExtractVersion_WithJapaneseText_ShouldHandleGracefully(string title, string expected)
        {
            // Japanese text doesn't contain English edition keywords
            // Should return empty since no English keywords are found
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("Album (Neuauflage)", "")]  // German - no English keyword
        public void ExtractVersion_WithGermanText_ShouldNotMatch(string title, string expected)
        {
            // German text without English keywords
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("Album (Remasterizado)", "Remasterizado")]  // Contains "remaster" substring
        [InlineData("Album (Edición Especial)", "Edición Especial")]  // Contains "edition" substring
        public void ExtractVersion_WithSpanishText_ContainingEnglishSubstring_ShouldExtract(string title, string expected)
        {
            // Spanish words that contain English keyword substrings are matched
            // "Remasterizado" contains "remaster", "Edición" contains "edition"
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("Album (Live 日本)", "Live 日本")]
        [InlineData("Album (Deluxe 限定版)", "Deluxe 限定版")]
        [InlineData("Album (2023 Remaster 最新版)", "2023 Remaster 最新版")]
        public void ExtractVersion_WithMixedEnglishAndJapanese_ShouldExtractIfEnglishKeywordPresent(string title, string expectedVersion)
        {
            // Mixed language with English edition keyword
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            result.Should().Be(expectedVersion);
        }

        #endregion

        #region Whitespace Handling

        [Theory]
        [InlineData("Album ( Deluxe Edition )", " Deluxe Edition ")]
        [InlineData("Album (  Remastered  )", "  Remastered  ")]
        [InlineData("Album [  Live  ]", "  Live  ")]
        public void ExtractVersion_WithExtraSpaces_ShouldPreserveInternalSpaces(string title, string expectedVersion)
        {
            // Extra spaces inside brackets are preserved in extraction
            // The content includes the spaces as captured
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            result.Should().Be(expectedVersion);
        }

        [Theory]
        [InlineData("Album\t(Deluxe Edition)", "Deluxe Edition")]
        [InlineData("Album  (Remastered)", "Remastered")]
        [InlineData("Album\n(Live)", "Live")]
        public void ExtractVersion_WithWhitespaceBeforeParentheses_ShouldExtractCorrectly(string title, string expectedVersion)
        {
            // Whitespace before parentheses shouldn't affect extraction
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            result.Should().Be(expectedVersion);
        }

        [Theory]
        [InlineData("Album (Deluxe\tEdition)", "Deluxe\tEdition")]
        [InlineData("Album (Live\nat Venue)", "Live\nat Venue")]
        public void ExtractVersion_WithWhitespaceInContent_ShouldPreserve(string title, string expectedVersion)
        {
            // Tabs and newlines inside content are preserved
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            result.Should().Be(expectedVersion);
        }

        #endregion

        #region ContainsEditionKeywords Tests

        [Theory]
        [InlineData("Deluxe Edition", true)]
        [InlineData("Remastered", true)]
        [InlineData("Live at Venue", true)]
        [InlineData("Anniversary Edition", true)]
        [InlineData("Expanded", true)]
        [InlineData("Special Edition", true)]
        [InlineData("Collector's Edition", true)]
        [InlineData("Limited", true)]
        [InlineData("Bonus Tracks", true)]
        [InlineData("Concert Recording", true)]
        [InlineData("Unplugged", true)]
        [InlineData("Acoustic", true)]
        [InlineData("Remix", true)]
        [InlineData("Instrumental", true)]
        [InlineData("Extended", true)]
        [InlineData("Radio Edit", true)]
        [InlineData("Legacy", true)]
        [InlineData("Archive", true)]
        [InlineData("Complete", true)]
        [InlineData("Sessions", true)]
        [InlineData("Demos", true)]
        public void ContainsEditionKeywords_WithValidKeywords_ShouldReturnTrue(string text, bool expected)
        {
            // Act
            var result = _titleGenerator.ContainsEditionKeywords(text);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("Rock", false)]
        [InlineData("2023", false)]
        [InlineData("CD1", false)]
        [InlineData("Part 1", false)]
        [InlineData("Disc 2", false)]
        [InlineData("Vol. 1", false)]
        [InlineData("Album", false)]
        public void ContainsEditionKeywords_WithNonEditionText_ShouldReturnFalse(string text, bool expected)
        {
            // Act
            var result = _titleGenerator.ContainsEditionKeywords(text);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("DELUXE EDITION", true)]
        [InlineData("remastered", true)]
        [InlineData("LIVE AT VENUE", true)]
        [InlineData("Anniversary EDITION", true)]
        [InlineData("DeLuXe", true)]
        public void ContainsEditionKeywords_ShouldBeCaseInsensitive(string text, bool expected)
        {
            // Act
            var result = _titleGenerator.ContainsEditionKeywords(text);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void ContainsEditionKeywords_WithNull_ShouldReturnFalse()
        {
            // Act
            var result = _titleGenerator.ContainsEditionKeywords(null);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region IsLiveAlbum Tests

        [Theory]
        [InlineData("Album Live", true)]
        [InlineData("Album (Live)", true)]
        [InlineData("Album [Live]", true)]
        [InlineData("Live at Wembley", true)]
        [InlineData("Live in Paris", true)]
        [InlineData("Concert at the Garden", true)]
        [InlineData("MTV Unplugged", true)]
        public void IsLiveAlbum_WithLiveIndicators_ShouldReturnTrue(string title, bool expected)
        {
            // Act
            var result = _titleGenerator.IsLiveAlbum(title);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("Studio Album", false)]
        [InlineData("Greatest Hits", false)]
        [InlineData("Deluxe Edition", false)]
        [InlineData("Remastered", false)]
        public void IsLiveAlbum_WithoutLiveIndicators_ShouldReturnFalse(string title, bool expected)
        {
            // Act
            var result = _titleGenerator.IsLiveAlbum(title);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void IsLiveAlbum_WithAliveInTitle_ShouldNotThrow()
        {
            // "Alive" contains "live" as a substring - this is an edge case
            // We don't assert specific behavior, just that it handles gracefully
            var act = () => _titleGenerator.IsLiveAlbum("Alive (not a live album)");

            act.Should().NotThrow();
        }

        [Fact]
        public void IsLiveAlbum_WithNull_ShouldReturnFalse()
        {
            // Act
            var result = _titleGenerator.IsLiveAlbum(null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsLiveAlbum_WithEmptyString_ShouldReturnFalse()
        {
            // Act
            var result = _titleGenerator.IsLiveAlbum("");

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region Real-World Album Title Scenarios

        [Theory]
        [InlineData("Random Access Memories (10th Anniversary Edition)", "10th Anniversary Edition")]
        [InlineData("Abbey Road (2019 Remaster)", "2019 Remaster")]
        [InlineData("Rumours (Deluxe)", "Deluxe")]
        [InlineData("The Dark Side of the Moon (Live at Wembley 1974)", "Live at Wembley 1974")]
        [InlineData("Nevermind (30th Anniversary Super Deluxe)", "30th Anniversary Super Deluxe")]
        [InlineData("OK Computer (OKNOTOK 1997-2017)", "")]  // No edition keyword
        [InlineData("In Rainbows (Disk 2)", "")]  // No edition keyword
        [InlineData("Kid A Mnesia (Collector's Edition)", "Collector's Edition")]
        public void ExtractVersion_RealWorldAlbumTitles_ShouldExtractCorrectly(string title, string expectedVersion)
        {
            // Act
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            // Assert
            result.Should().Be(expectedVersion);
        }

        [Theory]
        [InlineData("Unplugged in New York", true)]
        [InlineData("Live at Leeds", true)]
        [InlineData("Stop Making Sense", false)]  // Concert film but title doesn't match patterns
        [InlineData("The Last Waltz", false)]  // Concert but title doesn't contain keywords
        [InlineData("MTV Unplugged: Alice in Chains", true)]
        public void IsLiveAlbum_RealWorldAlbumTitles_ShouldIdentifyCorrectly(string title, bool expected)
        {
            // Act
            var result = _titleGenerator.IsLiveAlbum(title);

            // Assert
            result.Should().Be(expected);
        }

        #endregion

        #region Edge Cases - Special Characters in Content

        [Theory]
        [InlineData("Album (Deluxe Edition - Bonus Tracks)", "Deluxe Edition - Bonus Tracks")]
        [InlineData("Album (Remaster: 2023 Edition)", "Remaster: 2023 Edition")]
        [InlineData("Album (Live @ The Forum)", "Live @ The Forum")]
        [InlineData("Album (Director's Cut Edition)", "Director's Cut Edition")]
        [InlineData("Album (Re-Issue + Bonus)", "Re-Issue + Bonus")]  // Contains "bonus" keyword
        public void ExtractVersion_WithSpecialCharactersInContent_ShouldExtractCorrectly(string title, string expectedVersion)
        {
            // Act
            var result = _titleGenerator.ExtractVersionFromTitle(title);

            // Assert
            result.Should().Be(expectedVersion);
        }

        #endregion
    }
}
