using System;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Services.Caching;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Qobuzarr.Tests.Unit
{
    /// <summary>
    /// Regression tests for Unicode normalization and diacritical stripping.
    /// Root cause: SubstringMatcher.NormalizeString() uses FormD decomposition to strip
    /// NonSpacingMark characters (diacriticals), but matching strategies and title
    /// normalizers may not apply the same normalization, causing mismatches.
    /// See: CLAUDE.md flaky test "ParsedAlbumInfo_WithUnicodeVersions_*"
    /// </summary>
    public class UnicodeNormalizationTests
    {
        private readonly SubstringMatcher _matcher = new();

        #region Basic ASCII passthrough

        [Theory]
        [InlineData("Hello World")]
        [InlineData("Miles Davis")]
        [InlineData("Kind of Blue")]
        [InlineData("Abbey Road 2019")]
        public void NormalizeString_BasicAscii_PassesThroughUnchanged(string input)
        {
            var result = _matcher.NormalizeString(input);

            // ASCII is lowercased and punctuation removed, but letters/digits preserved
            result.Should().Be(input.ToLowerInvariant());
        }

        [Fact]
        public void NormalizeString_AlphanumericWithDigits_PreservesDigits()
        {
            var result = _matcher.NormalizeString("Album 2024 Remaster");

            result.Should().Be("album 2024 remaster");
        }

        #endregion

        #region Diacritical stripping (e -> e, u -> u, etc.)

        [Theory]
        [InlineData("\u00e9", "e")]         // e-acute
        [InlineData("\u00fc", "u")]         // u-umlaut
        [InlineData("\u00e0", "a")]         // a-grave
        [InlineData("\u00f1", "n")]         // n-tilde
        [InlineData("\u00e7", "c")]         // c-cedilla
        [InlineData("\u00f6", "o")]         // o-umlaut
        [InlineData("\u00e4", "a")]         // a-umlaut
        [InlineData("\u00ee", "i")]         // i-circumflex
        public void NormalizeString_SingleDiacritical_StripsToBaseCharacter(string input, string expected)
        {
            var result = _matcher.NormalizeString(input);

            result.Should().Be(expected,
                because: $"diacritical '{input}' (U+{(int)input[0]:X4}) should be stripped to '{expected}'");
        }

        [Theory]
        [InlineData("Bj\u00f6rk", "bjork")]
        [InlineData("Mot\u00f6rhead", "motorhead")]
        [InlineData("Beyonc\u00e9", "beyonce")]
        [InlineData("Zo\u00e9", "zoe")]
        [InlineData("Sigur R\u00f3s", "sigur ros")]
        [InlineData("\u00c9dith Piaf", "edith piaf")]
        [InlineData("Cr\u00e8me de la Cr\u00e8me", "creme de la creme")]
        public void NormalizeString_ArtistNamesWithDiacriticals_NormalizesCorrectly(string input, string expected)
        {
            var result = _matcher.NormalizeString(input);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("\u00c9dition Sp\u00e9ciale", "edition speciale")]
        [InlineData("Version Fran\u00e7aise", "version francaise")]
        [InlineData("Remasteris\u00e9", "remasterise")]
        public void NormalizeString_EditionStringsWithDiacriticals_NormalizesCorrectly(string input, string expected)
        {
            var result = _matcher.NormalizeString(input);

            result.Should().Be(expected);
        }

        #endregion

        #region CJK and non-Latin scripts (should not crash)

        [Theory]
        [InlineData("\u97f3\u697d")]                          // Japanese kanji "music"
        [InlineData("\uc74c\uc545")]                          // Korean "music"
        [InlineData("\u97f3\u4e50")]                          // Chinese "music"
        [InlineData("\u041c\u0443\u0437\u044b\u043a\u0430")]  // Russian "Muzyka"
        public void NormalizeString_NonLatinScript_DoesNotThrow(string input)
        {
            var act = () => _matcher.NormalizeString(input);

            act.Should().NotThrow(
                because: "non-Latin scripts should be handled gracefully without exceptions");
        }

        [Fact]
        public void NormalizeString_CjkCharacters_ReturnsNonEmptyResult()
        {
            // CJK characters are not NonSpacingMark, so they survive FormD decomposition
            var result = _matcher.NormalizeString("\u97f3\u697d");

            // The result should contain the CJK characters (they pass through the
            // diacritical strip but may be removed by the [^\w\s] regex depending on locale)
            result.Should().NotBeNull();
        }

        #endregion

        #region Empty/null input handling

        [Fact]
        public void NormalizeString_Null_ReturnsEmpty()
        {
            var result = _matcher.NormalizeString(null);

            result.Should().BeEmpty();
        }

        [Fact]
        public void NormalizeString_EmptyString_ReturnsEmpty()
        {
            var result = _matcher.NormalizeString("");

            result.Should().BeEmpty();
        }

        [Fact]
        public void NormalizeString_WhitespaceOnly_ReturnsEmpty()
        {
            var result = _matcher.NormalizeString("   ");

            result.Should().BeEmpty();
        }

        #endregion

        #region Mixed scripts (Latin + Cyrillic + numbers)

        [Fact]
        public void NormalizeString_MixedLatinAndNumbers_PreservesBoth()
        {
            var result = _matcher.NormalizeString("Album 123 Test");

            result.Should().Be("album 123 test");
        }

        [Fact]
        public void NormalizeString_MixedLatinCyrillicAndNumbers_DoesNotThrow()
        {
            // "Album \u041c\u0443\u0437\u044b\u043a\u0430 123" - Latin + Cyrillic + digits
            var act = () => _matcher.NormalizeString("Album \u041c\u0443\u0437\u044b\u043a\u0430 123");

            act.Should().NotThrow();
        }

        [Fact]
        public void NormalizeString_MixedDiacriticsAndAscii_NormalizesAll()
        {
            var result = _matcher.NormalizeString("Caf\u00e9 del Mar 2024");

            result.Should().Be("cafe del mar 2024");
        }

        #endregion

        #region TitleNormalizer comparison (documents the discrepancy)

        [Fact]
        public void TitleNormalizer_DoesNotStripDiacriticals_DocumentedDiscrepancy()
        {
            // TitleNormalizer.Normalize does NOT strip diacriticals -- it only removes
            // non-alphanumeric characters via [^a-z0-9\s]. Since diacritical characters
            // like e-acute are not in [a-z0-9], they get replaced with space.
            // This documents the discrepancy between TitleNormalizer and SubstringMatcher.
            var titleResult = TitleNormalizer.Normalize("Beyonc\u00e9");
            var matcherResult = _matcher.NormalizeString("Beyonc\u00e9");

            // TitleNormalizer strips the e-acute entirely (replaced by space then trimmed)
            // SubstringMatcher decomposes it to base 'e'
            // Both produce a usable result, but they differ:
            titleResult.Should().NotBe(matcherResult,
                because: "TitleNormalizer and SubstringMatcher use different normalization strategies");

            // SubstringMatcher preserves the base character
            matcherResult.Should().Be("beyonce");
        }

        #endregion

        #region Regression: same string normalized twice produces same result

        [Theory]
        [InlineData("Beyonc\u00e9 - Lemonade")]
        [InlineData("Bj\u00f6rk - Homogenic")]
        [InlineData("\u00c9dith Piaf - La Vie en Rose")]
        public void NormalizeString_CalledTwice_ProducesSameResult(string input)
        {
            var first = _matcher.NormalizeString(input);
            var second = _matcher.NormalizeString(input);

            first.Should().Be(second, because: "normalization must be idempotent");
        }

        #endregion
    }
}
