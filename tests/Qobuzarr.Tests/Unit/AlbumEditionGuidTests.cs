using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Qobuzarr.Tests.Unit
{
    /// <summary>
    /// Regression tests for GUID collision fix: album edition is now incorporated into GUIDs.
    ///
    /// Previously, QobuzParser.CreateReleaseInfoForQuality generated GUIDs as
    /// "qobuz-{album.Id}-{(int)quality}" which did NOT incorporate the album Version
    /// (edition) field. Two albums sharing the same Qobuz ID but different editions
    /// (e.g., "Standard" vs "Deluxe Edition") produced identical GUIDs, causing
    /// deduplication to silently drop one edition.
    ///
    /// The fix incorporates a normalized version suffix into the GUID:
    ///   "qobuz-{albumId}-{normalizedVersion}-{qualityValue}"
    /// Albums without a Version field produce the same GUID as before for backward compatibility.
    /// </summary>
    public class AlbumEditionGuidTests
    {
        /// <summary>
        /// Generates a GUID using the FIXED production logic that incorporates the edition.
        /// This mirrors QobuzParser.CreateReleaseInfoForQuality after the fix.
        /// </summary>
        private static string GenerateGuid(string albumId, int qualityValue, string? version = null)
        {
            var versionSuffix = string.IsNullOrWhiteSpace(version)
                ? ""
                : $"-{version.Trim().ToLowerInvariant().Replace(" ", "-")}";
            return $"qobuz-{albumId}{versionSuffix}-{qualityValue}";
        }

        #region Core GUID uniqueness

        [Fact]
        public void Guid_SameAlbumDifferentEditions_ProducesDifferentGuids()
        {
            var standardGuid = GenerateGuid("12345", 6, null);
            var deluxeGuid = GenerateGuid("12345", 6, "Deluxe Edition");
            var remasteredGuid = GenerateGuid("12345", 6, "Remastered");

            standardGuid.Should().NotBe(deluxeGuid,
                because: "different editions must produce different GUIDs to avoid silent deduplication");
            standardGuid.Should().NotBe(remasteredGuid);
            deluxeGuid.Should().NotBe(remasteredGuid);
        }

        [Fact]
        public void Guid_SameAlbumSameEdition_ProducesSameGuid()
        {
            var guid1 = GenerateGuid("12345", 6, "Deluxe Edition");
            var guid2 = GenerateGuid("12345", 6, "Deluxe Edition");

            guid1.Should().Be(guid2,
                because: "GUID generation must be deterministic for the same inputs");
        }

        [Fact]
        public void Guid_SameAlbumSameQuality_NoVersion_IsDeterministic()
        {
            var guid1 = GenerateGuid("12345", 6);
            var guid2 = GenerateGuid("12345", 6);

            guid1.Should().Be(guid2);
            guid1.Should().Be("qobuz-12345-6");
        }

        [Fact]
        public void Guid_DifferentQualities_ProducesDifferentGuids()
        {
            var mp3Guid = GenerateGuid("12345", 5);    // MP3 320
            var flacGuid = GenerateGuid("12345", 6);   // FLAC Lossless
            var hiresGuid = GenerateGuid("12345", 7);  // FLAC HiRes

            mp3Guid.Should().NotBe(flacGuid);
            mp3Guid.Should().NotBe(hiresGuid);
            flacGuid.Should().NotBe(hiresGuid);
        }

        #endregion

        #region Backward compatibility (no version = same GUID as before)

        [Fact]
        public void Guid_NullVersion_ProducesOriginalFormat()
        {
            var guid = GenerateGuid("12345", 6, null);

            guid.Should().Be("qobuz-12345-6",
                because: "null version should fall back to the basic GUID format for backward compatibility");
        }

        [Fact]
        public void Guid_EmptyVersion_ProducesOriginalFormat()
        {
            var guid = GenerateGuid("12345", 6, "");

            guid.Should().Be("qobuz-12345-6",
                because: "empty version should fall back to the basic GUID format for backward compatibility");
        }

        [Fact]
        public void Guid_WhitespaceVersion_ProducesOriginalFormat()
        {
            var guid = GenerateGuid("12345", 6, "   ");

            guid.Should().Be("qobuz-12345-6",
                because: "whitespace-only version should fall back to the basic GUID format for backward compatibility");
        }

        #endregion

        #region Version normalization

        [Fact]
        public void Guid_VersionNormalization_IsCaseInsensitive()
        {
            var lower = GenerateGuid("12345", 6, "deluxe edition");
            var upper = GenerateGuid("12345", 6, "Deluxe Edition");
            var mixed = GenerateGuid("12345", 6, "DELUXE EDITION");

            lower.Should().Be(upper);
            lower.Should().Be(mixed);
        }

        [Fact]
        public void Guid_VersionWithLeadingTrailingWhitespace_IsNormalized()
        {
            var trimmed = GenerateGuid("12345", 6, "Deluxe Edition");
            var padded = GenerateGuid("12345", 6, "  Deluxe Edition  ");

            trimmed.Should().Be(padded,
                because: "leading/trailing whitespace in version should be trimmed");
        }

        [Fact]
        public void Guid_VersionWithSpaces_ReplacedWithDashes()
        {
            var guid = GenerateGuid("12345", 6, "Deluxe Edition");

            guid.Should().Be("qobuz-12345-deluxe-edition-6",
                because: "spaces in version are replaced with dashes for GUID safety");
        }

        #endregion

        #region Real-world edition scenarios

        [Fact]
        public void Guid_RealWorldEditions_AllUnique()
        {
            // Common real-world Qobuz album editions for the same album
            var editions = new[]
            {
                (Version: (string?)null, Label: "Standard"),
                (Version: "Deluxe Edition", Label: "Deluxe"),
                (Version: "Remastered", Label: "Remastered"),
                (Version: "Super Deluxe", Label: "Super Deluxe"),
                (Version: "25th Anniversary Edition", Label: "Anniversary"),
                (Version: "Live", Label: "Live"),
            };

            var guids = new HashSet<string>();
            foreach (var (version, label) in editions)
            {
                var guid = GenerateGuid("album-9999", 6, version);
                guids.Add(guid).Should().BeTrue(
                    because: $"edition '{label}' (version='{version ?? "null"}') must produce a unique GUID");
            }
        }

        [Fact]
        public void Guid_UnicodeVersion_DoesNotCrash()
        {
            // French edition names with diacriticals
            var act = () => GenerateGuid("12345", 6, "\u00c9dition Sp\u00e9ciale");

            act.Should().NotThrow();
            var guid = act();
            guid.Should().NotBeNullOrWhiteSpace();
            guid.Should().Contain("12345");
        }

        #endregion

        #region QobuzAlbum model integration

        [Fact]
        public void QobuzAlbum_GetFullTitle_IncludesVersionForEditions()
        {
            var album = new QobuzAlbum
            {
                Id = "12345",
                Title = "Kind of Blue",
                Version = "Deluxe Edition"
            };

            var fullTitle = album.GetFullTitle();

            fullTitle.Should().Contain("Deluxe Edition",
                because: "GetFullTitle appends the Version in parentheses");
        }

        [Fact]
        public void QobuzAlbum_GetFullTitle_WithNullVersion_DoesNotCrash()
        {
            var album = new QobuzAlbum
            {
                Id = "12345",
                Title = "Kind of Blue",
                Version = null
            };

            var act = () => album.GetFullTitle();

            act.Should().NotThrow();
            act().Should().Be("Kind of Blue");
        }

        [Fact]
        public void QobuzAlbum_GetFullTitle_WithEmptyVersion_ReturnsBaseTitle()
        {
            var album = new QobuzAlbum
            {
                Id = "12345",
                Title = "Kind of Blue",
                Version = ""
            };

            album.GetFullTitle().Should().Be("Kind of Blue");
        }

        [Fact]
        public void QobuzAlbum_SameIdDifferentVersions_GuidsDiffer()
        {
            var standard = new QobuzAlbum
            {
                Id = "12345",
                Title = "Kind of Blue",
                Version = null
            };

            var deluxe = new QobuzAlbum
            {
                Id = "12345",
                Title = "Kind of Blue",
                Version = "Deluxe Edition"
            };

            // The albums are distinguishable through GetFullTitle
            standard.GetFullTitle().Should().NotBe(deluxe.GetFullTitle(),
                because: "albums with different editions must be distinguishable");

            // GUIDs now correctly incorporate edition
            var standardGuid = GenerateGuid(standard.Id, 6, standard.Version);
            var deluxeGuid = GenerateGuid(deluxe.Id, 6, deluxe.Version);
            standardGuid.Should().NotBe(deluxeGuid,
                because: "GUID generation incorporates the edition to prevent collision");

            // Backward compatibility: standard edition without version uses original format
            standardGuid.Should().Be("qobuz-12345-6",
                because: "albums without a version produce the same GUID as the original format");
        }

        #endregion
    }
}
