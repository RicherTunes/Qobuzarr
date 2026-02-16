using System.IO;
using FluentAssertions;
using Lidarr.Plugin.Common.Utilities;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Xunit;

namespace Qobuzarr.Tests.Unit.Utilities
{
    public class TrackFileNameBuilderTests
    {
        [Theory]
        [InlineData(5, ".mp3")]
        [InlineData(6, ".flac")]
        [InlineData(7, ".flac")]
        [InlineData(27, ".flac")]
        [InlineData(999, ".flac")]
        public void Build_ShouldUseExpectedExtension(int formatId, string expectedExtension)
        {
            var result = TrackFileNameBuilder.Build(trackNumber: 1, trackTitle: "Test", formatId: formatId);
            result.Should().EndWith(expectedExtension);
        }

        [Fact]
        public void Build_WithSingleDisc_ShouldUseTrackNumberPrefix()
        {
            var result = TrackFileNameBuilder.Build(trackNumber: 1, trackTitle: "Song", formatId: 6, discNumber: 1, totalDiscs: 1);
            result.Should().StartWith("01 - ");
        }

        [Theory]
        [InlineData(1, 2, "D01T01 - ")]
        [InlineData(2, 2, "D02T01 - ")]
        [InlineData(10, 12, "D10T01 - ")]
        public void Build_WithMultiDisc_ShouldIncludeDiscAndTrackPrefix(int discNumber, int totalDiscs, string expectedPrefix)
        {
            var result = TrackFileNameBuilder.Build(trackNumber: 1, trackTitle: "Song", formatId: 6, discNumber: discNumber, totalDiscs: totalDiscs);
            result.Should().StartWith(expectedPrefix);
        }

        [Theory]
        [InlineData(1, 1, 1, 6, "Song")]
        [InlineData(1, 1, 2, 6, "Song")]
        [InlineData(3, 2, 2, 5, "A:B/C*D?E\"F<G>H|I")]
        [InlineData(0, 0, 0, 27, "Cafe\u0301")]
        public void Build_ShouldMatchCommonTrackFileNameContract(int trackNumber, int discNumber, int totalDiscs, int formatId, string title)
        {
            var extension = TrackFileNameBuilder.GetExtensionForFormat(formatId).TrimStart('.');
            var expected = FileSystemUtilities.CreateTrackFileName(title, trackNumber, extension, discNumber, totalDiscs);
            var actual = TrackFileNameBuilder.Build(trackNumber, title, formatId, discNumber, totalDiscs);

            actual.Should().Be(expected);
        }

        [Fact]
        public void Build_ShouldSanitizeTitleForFileSystem()
        {
            var result = TrackFileNameBuilder.Build(trackNumber: 1, trackTitle: "A:B/C*D?E\"F<G>H|I", formatId: 6);

            var fileName = Path.GetFileNameWithoutExtension(result);
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName.Should().NotContain(invalidChar.ToString());
            }
        }

        [Fact]
        public void Build_WithInvalidDiscValues_ShouldNotThrow()
        {
            var result = TrackFileNameBuilder.Build(trackNumber: 1, trackTitle: "Song", formatId: 6, discNumber: 0, totalDiscs: 0);
            result.Should().NotBeNullOrEmpty();
        }
    }
}
