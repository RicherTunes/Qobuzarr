using System;
using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Lidarr.Plugin.Qobuzarr.Security;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.Builders;
using Xunit;

namespace Qobuzarr.Tests.PropertyBased
{
    /// <summary>
    /// Property-based tests for album edition handling using FsCheck.
    /// Tests invariants and properties that should hold for any album edition scenario.
    /// </summary>
    public class AlbumEditionPropertyTests
    {
        #region Property Generators

        /// <summary>
        /// Generates arbitrary album versions for property testing
        /// </summary>
        public static Arbitrary<string> AlbumVersions()
        {
            var validVersions = Gen.Elements(new[]
            {
                "Live", "Deluxe Edition", "Remastered", "Special Edition",
                "Live at Wembley", "2023 Remaster", "Anniversary Edition",
                "Acoustic", "Unplugged", "Director's Cut", "Extended Version",
                "Collector's Edition", "Limited Edition", "Box Set",
                "Complete Sessions", "Expanded Edition"
            });

            var nullOrEmpty = Gen.Elements<string>(null, "", "   ");

            var unicodeVersions = Gen.Elements(new[]
            {
                "Édition Spéciale", "特別版", "Версия делюкс", "Ausgabe"
            });

            var complexVersions = Gen.Elements(new[]
            {
                "25th Anniversary Deluxe Remastered Edition",
                "Live at Madison Square Garden, December 2019",
                "Director's Cut Special Edition with Bonus Content"
            });

            return Gen.OneOf(validVersions, nullOrEmpty, unicodeVersions, complexVersions).ToArbitrary();
        }

        /// <summary>
        /// Generates arbitrary QobuzAlbum objects for property testing
        /// </summary>
        public static Arbitrary<QobuzAlbum> QobuzAlbums()
        {
            return Arb.Generate<string>()
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(title => QobuzAlbumBuilder.New()
                    .WithTitle(title)
                    .WithArtist("Test Artist")
                    .WithReleaseYear(Gen.Choose(1950, 2030).Sample(0, 1).First())
                    .Build())
                .ToArbitrary();
        }

        #endregion

        #region Title Generation Properties

        [Property(Arbitrary = new[] { typeof(AlbumEditionPropertyTests) })]
        public Property GetFullTitle_WithAnyVersion_ShouldNeverReturnNull(string version)
        {
            return Prop.ForAll<string>(v =>
            {
                // Arrange
                var album = QobuzAlbumBuilder.New()
                    .WithTitle("Test Album")
                    .Build();
                album.Version = v;

                // Act
                var fullTitle = album.GetFullTitle();

                // Assert
                return !string.IsNullOrEmpty(fullTitle);
            }).When(!string.IsNullOrWhiteSpace(version) || version == null || version == "");
        }

        [Property(Arbitrary = new[] { typeof(AlbumEditionPropertyTests) })]
        public Property GetFullTitle_WithValidVersion_ShouldContainOriginalTitle(string version)
        {
            return Prop.ForAll<string>(v =>
            {
                // Arrange
                var originalTitle = "Original Album Title";
                var album = QobuzAlbumBuilder.New()
                    .WithTitle(originalTitle)
                    .Build();
                album.Version = v;

                // Act
                var fullTitle = album.GetFullTitle();

                // Assert
                return fullTitle.Contains(originalTitle);
            }).When(!string.IsNullOrWhiteSpace(version));
        }

        [Property(Arbitrary = new[] { typeof(AlbumEditionPropertyTests) })]
        public Property GetFullTitle_WithVersion_ShouldNotHaveDoubleParentheses(string version)
        {
            return Prop.ForAll<string>(v =>
            {
                // Arrange
                var album = QobuzAlbumBuilder.New()
                    .WithTitle("Test Album")
                    .Build();
                album.Version = v;

                // Act
                var fullTitle = album.GetFullTitle();

                // Assert
                return !fullTitle.Contains("((") && !fullTitle.Contains("))");
            }).When(!string.IsNullOrWhiteSpace(version));
        }

        [Property(Arbitrary = new[] { typeof(AlbumEditionPropertyTests) })]
        public Property GetFullTitle_WhenVersionInTitle_ShouldNotDuplicate(NonEmptyString title, NonEmptyString version)
        {
            // Arrange
            var sanitizedVersion = MetadataSanitizer.SanitizeVersion(version.Get);
            if (string.IsNullOrWhiteSpace(sanitizedVersion))
            {
                return true.ToProperty();
            }

            var albumTitle = $"{title.Get} {sanitizedVersion}";
            var album = QobuzAlbumBuilder.New()
                .WithTitle(albumTitle)
                .Build();
            album.Version = sanitizedVersion;

            // Act
            var fullTitle = album.GetFullTitle();

            // Assert - Should not have version duplicated in parentheses       
            return (fullTitle == albumTitle).ToProperty();
        }

        #endregion

        #region Version Field Properties

        [Property(Arbitrary = new[] { typeof(AlbumEditionPropertyTests) })]
        public Property Version_ShouldBePreservedInFullTitle(string version)
        {
            return Prop.ForAll<string>(v =>
            {
                // Arrange
                var album = QobuzAlbumBuilder.New()
                    .WithTitle("Test Album")
                    .Build();
                album.Version = v;

                // Act
                var fullTitle = album.GetFullTitle();

                // Assert
                var sanitizedVersion = MetadataSanitizer.SanitizeVersion(v);
                if (string.IsNullOrWhiteSpace(sanitizedVersion))
                {
                    return !fullTitle.Contains("()"); // No empty parentheses   
                }

                // If the version is already present in the title, we should not duplicate it.
                if (fullTitle.Contains(sanitizedVersion, StringComparison.OrdinalIgnoreCase) &&
                    !fullTitle.Contains($"({sanitizedVersion})", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return fullTitle.Contains($"({sanitizedVersion})");
            }).When(version != null);
        }

        [Property]
        public Property Version_WithUnicodeCharacters_ShouldBePreserved(NonEmptyString version)
        {
            // Arrange
            var unicodeVersion = version.Get + "éñ中文🎵";
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .Build();
            album.Version = unicodeVersion;

            // Act
            var fullTitle = album.GetFullTitle();

            // Assert
            var sanitizedVersion = MetadataSanitizer.SanitizeVersion(unicodeVersion);
            if (string.IsNullOrWhiteSpace(sanitizedVersion))
            {
                return true.ToProperty();
            }

            return fullTitle.Contains(sanitizedVersion, StringComparison.OrdinalIgnoreCase).ToProperty();
        }

        #endregion

        #region Album Comparison Properties

        [Property(Arbitrary = new[] { typeof(AlbumEditionPropertyTests) })]
        public Property DifferentVersions_ShouldProduceDifferentFullTitles(
            NonEmptyString version1,
            NonEmptyString version2)
        {
            var v1 = MetadataSanitizer.SanitizeVersion(version1.Get);
            var v2 = MetadataSanitizer.SanitizeVersion(version2.Get);

            if (string.IsNullOrWhiteSpace(v1) || string.IsNullOrWhiteSpace(v2))
            {
                return true.ToProperty();
            }

            if (v1.Equals(v2, StringComparison.OrdinalIgnoreCase))
            {
                return true.ToProperty();
            }

            // Arrange
            var album1 = QobuzAlbumBuilder.New()
                .WithTitle("Same Album")
                .WithArtist("Same Artist")
                .Build();
            album1.Version = v1;

            var album2 = QobuzAlbumBuilder.New()
                .WithTitle("Same Album")
                .WithArtist("Same Artist")
                .Build();
            album2.Version = v2;

            // Act
            var title1 = album1.GetFullTitle();
            var title2 = album2.GetFullTitle();

            // Assert
            return (title1 != title2).ToProperty();
        }

        [Property(Arbitrary = new[] { typeof(AlbumEditionPropertyTests) })]
        public Property SameVersions_ShouldProduceSameFullTitles(NonEmptyString version)
        {
            // Arrange
            var album1 = QobuzAlbumBuilder.New()
                .WithTitle("Same Album")
                .WithArtist("Same Artist")
                .Build();
            album1.Version = version.Get;

            var album2 = QobuzAlbumBuilder.New()
                .WithTitle("Same Album")
                .WithArtist("Same Artist")
                .Build();
            album2.Version = version.Get;

            // Act
            var title1 = album1.GetFullTitle();
            var title2 = album2.GetFullTitle();

            // Assert
            return (title1 == title2).ToProperty();
        }

        #endregion

        #region Title Generation Format Properties

        [Property]
        public Property RedactedStyleTitle_ShouldAlwaysHaveCorrectStructure(
            NonEmptyString artist,
            NonEmptyString album,
            PositiveInt year)
        {
            // Arrange
            var qobuzAlbum = QobuzAlbumBuilder.New()
                .WithTitle(album.Get)
                .WithArtist(artist.Get)
                .WithReleaseYear(1950 + (year.Get % 80)) // Keep year reasonable
                .AsCdQualityFlac()
                .Build();

            // Act
            var title = GenerateRedactedStyleTitle(qobuzAlbum);

            // Assert
            var hasCorrectStructure = title.Contains(" - ") &&
                                    title.Contains("(") &&
                                    title.Contains(")") &&
                                    title.EndsWith("]");

            return hasCorrectStructure.ToProperty();
        }

        [Property(Arbitrary = new[] { typeof(AlbumEditionPropertyTests) })]
        public Property RedactedStyleTitle_WithVersion_ShouldHaveVersionBracket(
            NonEmptyString version)
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .WithReleaseYear(2020)
                .AsCdQualityFlac()
                .Build();
            var sanitizedVersion = MetadataSanitizer.SanitizeVersion(version.Get);
            if (string.IsNullOrWhiteSpace(sanitizedVersion))
            {
                return true.ToProperty();
            }

            album.Version = sanitizedVersion;

            // Act
            var title = GenerateRedactedStyleTitle(album);

            // Assert
            // Should have version in brackets and quality in brackets
            var versionBracketCount = title.Split('[').Length - 1;
            return (versionBracketCount >= 2).ToProperty(); // At least version bracket and quality bracket
        }

        [Property]
        public Property RedactedStyleTitle_ShouldAlwaysEndWithQualityBracket(
            NonEmptyString artist,
            NonEmptyString album)
        {
            // Arrange
            var qobuzAlbum = QobuzAlbumBuilder.New()
                .WithTitle(album.Get)
                .WithArtist(artist.Get)
                .WithReleaseYear(2020)
                .AsCdQualityFlac()
                .Build();

            // Act
            var title = GenerateRedactedStyleTitle(qobuzAlbum);

            // Assert
            return (title.EndsWith("[FLAC WEB]") || title.EndsWith("[MP3 WEB]")).ToProperty();
        }

        #endregion

        #region Performance Properties

        [Property]
        public Property GetFullTitle_ShouldBeIdempotent(NonEmptyString version)
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .Build();
            album.Version = version.Get;

            // Act
            var title1 = album.GetFullTitle();
            var title2 = album.GetFullTitle();

            // Assert - Multiple calls should return same result
            return (title1 == title2).ToProperty();
        }

        [Fact]
        public void GetFullTitle_ShouldBeFast()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .Build();
            album.Version = "Very Long Version String That Could Potentially Cause Performance Issues";

            // Act
            var stopwatch = Stopwatch.StartNew();
            string title = string.Empty;
            for (int i = 0; i < 10_000; i++)
            {
                title = album.GetFullTitle();
            }
            stopwatch.Stop();

            // Assert
            title.Should().Be("Test Album (Very Long Version String That Could Potentially Cause Performance Issues)");
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Simplified version of Redacted style title generation for property testing
        /// </summary>
        private string GenerateRedactedStyleTitle(QobuzAlbum album)
        {
            var artistName = album.GetArtistName();
            var albumTitle = album.Title;
            var year = album.ReleaseDate.Year;
            var quality = album.MaximumBitDepth >= 16 ? "FLAC" : "MP3";

            var titleBuilder = $"{artistName} - {albumTitle} ({year})";

            if (!string.IsNullOrWhiteSpace(album.Version))
            {
                titleBuilder += $" [{album.Version}]";
            }

            titleBuilder += $" [{quality} WEB]";
            return titleBuilder;
        }

        #endregion
    }
}
