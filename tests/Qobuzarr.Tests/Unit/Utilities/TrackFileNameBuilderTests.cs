using System.IO;
using FluentAssertions;
using Lidarr.Plugin.Common.Utilities;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Xunit;

// PARITY-EXEMPT: Path.GetInvalidFileNameChars() usage in Build_ShouldSanitizeTitleForFileSystem
// is intentional - we're verifying the output against the platform's definition of invalid chars.
// This test validates that TrackFileNameBuilder produces the same sanitization as FileNameSanitizer.

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
            var input = "A:B/C*D?E\"F<G>H|I";
            var result = TrackFileNameBuilder.Build(trackNumber: 1, trackTitle: input, formatId: 6);

            var fileName = Path.GetFileNameWithoutExtension(result);

            // Production routes through FileSystemUtilities.CreateTrackFileName -> SanitizeFileName
            // -> FileNameSanitizer.SanitizeFileName(input, replacement=' '), which REPLACES invalid
            // characters with spaces. The previous expectation used Sanitize.PathSegment which
            // STRIPS invalid chars entirely (different policy), giving a substring like "ABCDEFGHI"
            // that never appears in the space-separated "A B C D E F G H I" output. Compute the
            // expected via the same pipeline production uses.
            var sanitized = Lidarr.Plugin.Common.Utilities.FileNameSanitizer.SanitizeFileName(input).Trim();
            fileName.Should().Contain(sanitized);

            // Defense in depth: regardless of the precise policy, the filename must contain
            // no OS-invalid characters. (Path.GetInvalidFileNameChars varies by OS; the test
            // intentionally checks both the Windows superset and the POSIX subset so this
            // assertion stays meaningful on either runner.)
            foreach (var ch in new[] { '<', '>', ':', '"', '|', '?', '*', '/', '\\' })
            {
                fileName.Should().NotContain(ch.ToString(), $"sanitized filename must not contain '{ch}'");
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
