using System;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.Builders;
using NLog;
using Lidarr.Plugin.Qobuzarr.Indexers.Parsing;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Integration tests that verify our releases will be correctly parsed by Lidarr's quality detection
    /// These tests simulate Lidarr's actual QualityParser.ParseQuality() logic using the same regex patterns
    /// </summary>
    [Trait("Category", "Simulations")]
    public class LidarrQualityDetectionTests
    {
        private readonly ITitleGenerator _titleGenerator;

        // These are the EXACT regex patterns from Lidarr's QualityParser
        private static readonly Regex BitRateRegex = new(@"\b(?:(?<B096>96[ ]?kbps|96|[\[\(].*96.*[\]\)])|
                                                                (?<B128>128[ ]?kbps|128|[\[\(].*128.*[\]\)])|
                                                                (?<B160>160[ ]?kbps|160|[\[\(].*160.*[\]\)]|q5)|
                                                                (?<B192>192[ ]?kbps|192|[\[\(].*192.*[\]\)]|q6)|
                                                                (?<B224>224[ ]?kbps|224|[\[\(].*224.*[\]\)]|q7)|
                                                                (?<B256>256[ ]?kbps|256|itunes\splus|[\[\(].*256.*[\]\)]|q8)|
                                                                (?<B320>320[ ]?kbps|320|[\[\(].*320.*[\]\)]|q9)|
                                                                (?<B500>500[ ]?kbps|500|[\[\(].*500.*[\]\)]|q10)|
                                                                (?<VBRV0>V0[ ]?kbps|V0|[\[\(].*V0.*[\]\)])|
                                                                (?<VBRV2>V2[ ]?kbps|V2|[\[\(].*V2.*[\]\)]))\b",
                                                            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

        private static readonly Regex SampleSizeRegex = new(@"\b(?:(?<S24>24[-._ ]?bit|flac24(?:[-._ ]?bit)?|tr24|24-(?:44|48|96|192)|[\[\(].*24bit.*[\]\)]))\b",
                                                           RegexOptions.Compiled);

        private static readonly Regex CodecRegex = new(@"\b(?:(?<MP1>MPEG Version \d(.5)? Audio, Layer 1|MP1)|(?<MP2>MPEG Version \d(.5)? Audio, Layer 2|MP2)|(?<MP3VBR>MP3.*VBR|MPEG Version \d(.5)? Audio, Layer 3 vbr)|(?<MP3CBR>MP3|MPEG Version \d(.5)? Audio, Layer 3)|(?<FLAC>(web)?flac(?:24(?:[-._ ]?bit)?)?|TR24)|(?<WAVPACK>wavpack|wv)|(?<ALAC>alac)|(?<WMA>WMA\d?)|(?<WAV>WAV|PCM)|(?<AAC>M4A|M4P|M4B|AAC|mp4a|MPEG-4 Audio(?!.*alac))|(?<OGG>OGG|OGA|Vorbis))\b|(?<APE>monkey's audio|[\[|\(].*\bape\b.*[\]|\)])|(?<OPUS>Opus Version \d(.5)? Audio|[\[|\(].*\bopus\b.*[\]|\)])",
                                                      RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public LidarrQualityDetectionTests()
        {
            var logger = LogManager.GetCurrentClassLogger();
            _titleGenerator = new TitleGenerator(logger);
        }

        #region Critical Quality Detection Tests

        /// <summary>
        /// CRITICAL TEST: Verify that MP3 320kbps titles will be detected by Lidarr's BitRateRegex
        /// If this fails, Lidarr will show "Unknown" quality for MP3 releases
        /// </summary>
        [Fact]
        public void MP3_320kbps_Titles_ShouldMatchLidarrBitRateRegex()
        {
            // Arrange
            var album = CreateTestAlbum("MP3 Quality Test");

            // Act
            var title = GenerateTitleForQuality(album, QobuzAudioQuality.MP3320);

            // Assert - Simulate Lidarr's parsing
            var bitRateMatch = BitRateRegex.Match(title);
            bitRateMatch.Success.Should().BeTrue("MP3 320kbps title should match Lidarr's BitRateRegex");
            bitRateMatch.Groups["B320"].Success.Should().BeTrue("Should specifically match B320 group for 320kbps");

            // Verify the actual title format
            title.Should().Contain("320kbps", "Title should contain explicit bitrate");
            title.Should().MatchRegex(@".*\[MP3 320kbps\].*", "Title should have correct MP3 320kbps format");
        }

        /// <summary>
        /// CRITICAL TEST: Verify that FLAC titles will be detected by Lidarr's CodecRegex
        /// If this fails, Lidarr will show "Unknown" quality for FLAC releases
        /// </summary>
        [Fact]
        public void FLAC_Lossless_Titles_ShouldMatchLidarrCodecRegex()
        {
            // Arrange
            var album = CreateTestAlbum("FLAC Quality Test");

            // Act
            var title = GenerateTitleForQuality(album, QobuzAudioQuality.FLACLossless);

            // Assert - Simulate Lidarr's parsing
            var codecMatch = CodecRegex.Match(title);
            codecMatch.Success.Should().BeTrue("FLAC title should match Lidarr's CodecRegex");
            codecMatch.Groups["FLAC"].Success.Should().BeTrue("Should specifically match FLAC group");

            // Verify the actual title format
            title.Should().Contain("FLAC", "Title should contain FLAC codec identifier");
            title.Should().MatchRegex(@".*\[FLAC\].*", "Title should have correct FLAC format");
        }

        /// <summary>
        /// CRITICAL TEST: Verify that Hi-Res FLAC titles will be detected by both CodecRegex and SampleSizeRegex
        /// If this fails, Lidarr will show "FLAC" instead of "FLAC 24bit" quality
        /// </summary>
        [Theory]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit96kHz, "96kHz")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit192Khz, "192kHz")]
        public void FLAC_HiRes_Titles_ShouldMatchBothCodecAndSampleSizeRegex(
            QobuzAudioQuality quality, string expectedSampleRate)
        {
            // Arrange
            var album = CreateTestAlbum("Hi-Res FLAC Test");

            // Act
            var title = GenerateTitleForQuality(album, quality);

            // Assert - Simulate Lidarr's parsing for FLAC codec
            var codecMatch = CodecRegex.Match(title);
            codecMatch.Success.Should().BeTrue($"{quality} title should match Lidarr's CodecRegex");
            codecMatch.Groups["FLAC"].Success.Should().BeTrue($"Should match FLAC group for {quality}");

            // Assert - Simulate Lidarr's parsing for 24bit sample size
            var sampleSizeMatch = SampleSizeRegex.Match(title);
            sampleSizeMatch.Success.Should().BeTrue($"{quality} title should match Lidarr's SampleSizeRegex");
            sampleSizeMatch.Groups["S24"].Success.Should().BeTrue($"Should match S24 group for {quality}");

            // Verify the actual title contains required markers
            title.Should().Contain("FLAC", "Hi-Res title should contain FLAC codec");
            title.Should().Contain("24bit", "Hi-Res title should contain 24bit marker");
            title.Should().Contain(expectedSampleRate, $"Hi-Res title should contain {expectedSampleRate}");
        }

        #endregion

        #region Quality Detection Simulation Tests

        /// <summary>
        /// Simulate Lidarr's complete quality detection process for all our quality formats
        /// This is the most comprehensive test - it covers the entire quality detection chain
        /// </summary>
        [Theory]
        [InlineData(QobuzAudioQuality.MP3320, "MP3_320", "Should be detected as MP3-320")]
        [InlineData(QobuzAudioQuality.FLACLossless, "FLAC", "Should be detected as FLAC")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit96kHz, "FLAC_24", "Should be detected as FLAC 24bit")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit192Khz, "FLAC_24", "Should be detected as FLAC 24bit")]
        public void SimulateLidarrQualityDetection_AllQualities_ShouldBeDetectedCorrectly(
            QobuzAudioQuality qobuzQuality, string expectedLidarrQuality, string reason)
        {
            // Arrange
            var album = CreateTestAlbum($"Quality Detection Test - {qobuzQuality}");

            // Act
            var title = GenerateTitleForQuality(album, qobuzQuality);
            var detectedQuality = SimulateLidarrQualityDetection(title);

            // Assert
            detectedQuality.Should().Be(expectedLidarrQuality, reason);
        }

        /// <summary>
        /// Test that all generated titles avoid the dreaded "Unknown" quality detection
        /// </summary>
        [Fact]
        public void AllGeneratedTitles_ShouldNeverResultInUnknownQuality()
        {
            // Arrange
            var album = CreateTestAlbum("Unknown Quality Prevention Test");
            var allQualities = new[]
            {
                QobuzAudioQuality.MP3320,
                QobuzAudioQuality.FLACLossless,
                QobuzAudioQuality.FLACHiRes24Bit96kHz,
                QobuzAudioQuality.FLACHiRes24Bit192Khz
            };

            // Act & Assert
            foreach (var quality in allQualities)
            {
                var title = GenerateTitleForQuality(album, quality);
                var detectedQuality = SimulateLidarrQualityDetection(title);

                detectedQuality.Should().NotBe("Unknown",
                    $"Title '{title}' for {quality} should not result in Unknown quality detection");
            }
        }

        #endregion

        #region Real-World Scenario Tests

        /// <summary>
        /// Test with realistic album titles that might contain special characters or edge cases
        /// </summary>
        [Theory]
        [InlineData("Album: With Colon", "Artist (Special)", QobuzAudioQuality.MP3320)]
        [InlineData("Album [With Brackets]", "Artist & Friends", QobuzAudioQuality.FLACLossless)]
        [InlineData("Album - With Dash", "Artist, Various", QobuzAudioQuality.FLACHiRes24Bit96kHz)]
        public void RealWorldAlbumTitles_ShouldStillHaveCorrectQualityDetection(
            string albumTitle, string artistName, QobuzAudioQuality quality)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("realworld123")
                .WithTitle(albumTitle)
                .WithArtist(artistName, "test-slug")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .Build();

            // Act
            var title = GenerateTitleForQuality(album, quality);
            var detectedQuality = SimulateLidarrQualityDetection(title);

            // Assert
            detectedQuality.Should().NotBe("Unknown",
                $"Real-world album '{albumTitle}' by '{artistName}' should have correct quality detection");

            // Verify title structure remains intact
            title.Should().Contain(artistName, "Artist name should be preserved");
            title.Should().Contain(albumTitle, "Album title should be preserved");
            title.Should().Contain("[WEB]", "WEB tag should be present");
        }

        #endregion

        #region Helper Methods

        private QobuzAlbum CreateTestAlbum(string title)
        {
            return new QobuzAlbumBuilder()
                .WithId("test123")
                .WithTitle(title)
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .WithTracks(10, 270) // 10 tracks, ~4.5 minutes each = 45 minutes total
                .Build();
        }

        private string GenerateTitleForQuality(QobuzAlbum album, QobuzAudioQuality quality)
        {
            return _titleGenerator.GenerateQualitySpecificTitle(album, quality, 2023);
        }

        /// <summary>
        /// Simulates Lidarr's QualityParser.ParseQuality() logic
        /// This is a simplified version that focuses on the patterns we care about
        /// </summary>
        private string SimulateLidarrQualityDetection(string title)
        {
            var normalizedTitle = title.Replace('_', ' ').Trim().ToLower();

            // Check for 24bit sample size first (for Hi-Res detection)
            var sampleSizeMatch = SampleSizeRegex.Match(normalizedTitle);
            var has24Bit = sampleSizeMatch.Groups["S24"].Success;

            // Check codec
            var codecMatch = CodecRegex.Match(normalizedTitle);
            if (codecMatch.Groups["FLAC"].Success)
            {
                return has24Bit ? "FLAC_24" : "FLAC";
            }

            if (codecMatch.Groups["MP3CBR"].Success)
            {
                // Check bitrate for MP3
                var bitrateMatch = BitRateRegex.Match(normalizedTitle);
                if (bitrateMatch.Groups["B320"].Success)
                {
                    return "MP3_320";
                }
                return "MP3_Unknown";
            }

            return "Unknown";
        }

        #endregion

        #region Regression Tests

        /// <summary>
        /// Regression test to ensure our title format changes don't break in the future
        /// This test locks in the exact expected format
        /// </summary>
        [Fact]
        public void TitleFormat_ShouldMatchExpectedStructureExactly()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("format123")
                .WithTitle("Format Test Album")
                .WithArtist("Format Artist", "format-artist")
                .WithReleaseDate(new DateTime(2023, 5, 15))
                .Build();

            // Act & Assert - Lock in the exact expected formats
            var mp3Title = GenerateTitleForQuality(album, QobuzAudioQuality.MP3320);
            mp3Title.Should().Be("Format Artist - Format Test Album (2023) [MP3 320kbps] [WEB]");

            var flacTitle = GenerateTitleForQuality(album, QobuzAudioQuality.FLACLossless);
            flacTitle.Should().Be("Format Artist - Format Test Album (2023) [FLAC] [WEB]");

            var hiRes96Title = GenerateTitleForQuality(album, QobuzAudioQuality.FLACHiRes24Bit96kHz);
            hiRes96Title.Should().Be("Format Artist - Format Test Album (2023) [FLAC 24bit 96kHz] [WEB]");

            var hiRes192Title = GenerateTitleForQuality(album, QobuzAudioQuality.FLACHiRes24Bit192Khz);
            hiRes192Title.Should().Be("Format Artist - Format Test Album (2023) [FLAC 24bit 192kHz] [WEB]");
        }

        /// <summary>
        /// Test to ensure that future changes don't break the critical regex matching
        /// </summary>
        [Fact]
        public void LidarrRegexPatterns_ShouldContinueToWork()
        {
            // This test ensures our understanding of Lidarr's patterns is correct
            var testCases = new[]
            {
                ("Test [MP3 320kbps] Test", BitRateRegex, "B320"),
                ("Test [FLAC] Test", CodecRegex, "FLAC"),
                ("Test [FLAC 24bit] Test", SampleSizeRegex, "S24"),
                ("Test FLAC 24bit 96kHz Test", SampleSizeRegex, "S24")
            };

            foreach (var (testString, regex, expectedGroup) in testCases)
            {
                var match = regex.Match(testString);
                match.Success.Should().BeTrue($"'{testString}' should match the regex");
                match.Groups[expectedGroup].Success.Should().BeTrue($"Should match group '{expectedGroup}'");
            }
        }

        #endregion
    }
}
