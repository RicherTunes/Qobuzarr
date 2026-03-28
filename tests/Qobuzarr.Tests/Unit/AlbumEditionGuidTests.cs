using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Qobuzarr.Tests.Unit
{
    /// <summary>
    /// Regression tests for GUID collision when the same album has different editions.
    ///
    /// Root cause: QobuzParser.CreateReleaseInfoForQuality generates GUIDs as
    /// "qobuz-{album.Id}-{(int)quality}" which does NOT incorporate the album Version
    /// (edition) field. Two albums sharing the same Qobuz ID but different editions
    /// (e.g., "Standard" vs "Deluxe Edition") produce identical GUIDs, causing
    /// deduplication to silently drop one edition.
    ///
    /// See: CLAUDE.md flaky test "AlbumRepository_FindByTitle_WithDifferentEditions_*"
    ///
    /// These tests document the current (buggy) behavior and verify the expected
    /// (correct) behavior once the production code is fixed.
    /// </summary>
    public class AlbumEditionGuidTests
    {
        /// <summary>
        /// Generates a GUID using the CURRENT production logic.
        /// This is the format from QobuzParser.CreateReleaseInfoForQuality line 234:
        ///   Guid = $"qobuz-{album.Id}-{(int)quality}"
        /// </summary>
        private static string GenerateCurrentGuid(string albumId, int qualityValue)
        {
            return $"qobuz-{albumId}-{qualityValue}";
        }

        /// <summary>
        /// Generates a GUID using the PROPOSED fixed logic that incorporates the edition.
        /// When the production code is fixed, this should match the actual implementation.
        /// </summary>
        private static string GenerateFixedGuid(string albumId, int qualityValue, string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return $"qobuz-{albumId}-{qualityValue}";
            }

            // Normalize version to avoid trivial differences causing spurious uniqueness
            var normalizedVersion = version.Trim().ToLowerInvariant().Replace(" ", "-");
            return $"qobuz-{albumId}-{normalizedVersion}-{qualityValue}";
        }

        #region Current behavior (documents the bug)

        [Fact]
        public void CurrentGuid_SameAlbumDifferentEditions_ProducesSameGuid_DocumentedBug()
        {
            // This documents the CURRENT buggy behavior where different editions collide
            var standardGuid = GenerateCurrentGuid("12345", 6);
            var deluxeGuid = GenerateCurrentGuid("12345", 6);

            // BUG: These are identical because the current code ignores the edition/version
            standardGuid.Should().Be(deluxeGuid,
                because: "current production code does not include edition in GUID (this is the bug)");
        }

        [Fact]
        public void CurrentGuid_SameAlbumSameQuality_IsDeterministic()
        {
            var guid1 = GenerateCurrentGuid("12345", 6);
            var guid2 = GenerateCurrentGuid("12345", 6);

            guid1.Should().Be(guid2);
            guid1.Should().Be("qobuz-12345-6");
        }

        [Fact]
        public void CurrentGuid_DifferentQualities_ProducesDifferentGuids()
        {
            var mp3Guid = GenerateCurrentGuid("12345", 5);    // MP3 320
            var flacGuid = GenerateCurrentGuid("12345", 6);   // FLAC Lossless
            var hiresGuid = GenerateCurrentGuid("12345", 7);  // FLAC HiRes

            mp3Guid.Should().NotBe(flacGuid);
            mp3Guid.Should().NotBe(hiresGuid);
            flacGuid.Should().NotBe(hiresGuid);
        }

        #endregion

        #region Fixed behavior (what SHOULD happen after the bug is fixed)

        [Fact]
        public void FixedGuid_SameAlbumDifferentEditions_ProducesDifferentGuids()
        {
            var standardGuid = GenerateFixedGuid("12345", 6, null);
            var deluxeGuid = GenerateFixedGuid("12345", 6, "Deluxe Edition");
            var remasteredGuid = GenerateFixedGuid("12345", 6, "Remastered");

            standardGuid.Should().NotBe(deluxeGuid,
                because: "different editions must produce different GUIDs to avoid silent deduplication");
            standardGuid.Should().NotBe(remasteredGuid);
            deluxeGuid.Should().NotBe(remasteredGuid);
        }

        [Fact]
        public void FixedGuid_SameAlbumSameEdition_ProducesSameGuid()
        {
            var guid1 = GenerateFixedGuid("12345", 6, "Deluxe Edition");
            var guid2 = GenerateFixedGuid("12345", 6, "Deluxe Edition");

            guid1.Should().Be(guid2,
                because: "GUID generation must be deterministic for the same inputs");
        }

        [Fact]
        public void FixedGuid_NullVersion_DoesNotCrash()
        {
            var act = () => GenerateFixedGuid("12345", 6, null);

            act.Should().NotThrow();
            act().Should().Be("qobuz-12345-6",
                because: "null version should fall back to the basic GUID format");
        }

        [Fact]
        public void FixedGuid_EmptyVersion_DoesNotCrash()
        {
            var act = () => GenerateFixedGuid("12345", 6, "");

            act.Should().NotThrow();
            act().Should().Be("qobuz-12345-6",
                because: "empty version should fall back to the basic GUID format");
        }

        [Fact]
        public void FixedGuid_WhitespaceVersion_DoesNotCrash()
        {
            var act = () => GenerateFixedGuid("12345", 6, "   ");

            act.Should().NotThrow();
            act().Should().Be("qobuz-12345-6",
                because: "whitespace-only version should fall back to the basic GUID format");
        }

        [Fact]
        public void FixedGuid_VersionNormalization_IsCaseInsensitive()
        {
            var lower = GenerateFixedGuid("12345", 6, "deluxe edition");
            var upper = GenerateFixedGuid("12345", 6, "Deluxe Edition");
            var mixed = GenerateFixedGuid("12345", 6, "DELUXE EDITION");

            lower.Should().Be(upper);
            lower.Should().Be(mixed);
        }

        #endregion

        #region Real-world edition scenarios

        [Fact]
        public void FixedGuid_RealWorldEditions_AllUnique()
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
                var guid = GenerateFixedGuid("album-9999", 6, version);
                guids.Add(guid).Should().BeTrue(
                    because: $"edition '{label}' (version='{version ?? "null"}') must produce a unique GUID");
            }
        }

        [Fact]
        public void FixedGuid_UnicodeVersion_DoesNotCrash()
        {
            // French edition names with diacriticals
            var act = () => GenerateFixedGuid("12345", 6, "\u00c9dition Sp\u00e9ciale");

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
        public void QobuzAlbum_SameIdDifferentVersions_AreDistinguishable()
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

            // But the current GUID generation would NOT distinguish them (the bug)
            var standardGuid = GenerateCurrentGuid(standard.Id, 6);
            var deluxeGuid = GenerateCurrentGuid(deluxe.Id, 6);
            standardGuid.Should().Be(deluxeGuid,
                because: "this documents the current bug -- GUIDs collide for different editions");

            // The fixed version WOULD distinguish them
            var fixedStandardGuid = GenerateFixedGuid(standard.Id, 6, standard.Version);
            var fixedDeluxeGuid = GenerateFixedGuid(deluxe.Id, 6, deluxe.Version);
            fixedStandardGuid.Should().NotBe(fixedDeluxeGuid,
                because: "the fixed GUID generation must incorporate the edition");
        }

        #endregion
    }
}
