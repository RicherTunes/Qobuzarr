using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Qobuzarr.Tests.Unit.Download
{
    /// <summary>
    /// The download path routes through Common's SimpleDownloadOrchestrator, whose artwork embedder
    /// reads <c>track.Album.GetBestCoverArtUrl()</c>. That album is built by
    /// <see cref="QobuzStreamingTrack.From"/>, so it MUST carry the Qobuz cover-art URLs — otherwise
    /// downloaded albums arrive with no embedded cover art (the indexer path maps them, but the
    /// download path was dropping them).
    /// </summary>
    public class QobuzStreamingTrackTests
    {
        [Fact]
        public void From_PopulatesAlbumCoverArtUrls_FromQobuzImage()
        {
            var album = new QobuzAlbum
            {
                Id = "a1",
                Title = "Album",
                Image = new QobuzImage
                {
                    Small = "https://ex/s.jpg",
                    Medium = "https://ex/m.jpg",
                    Large = "https://ex/l.jpg",
                    ExtraLarge = "https://ex/xl.jpg",
                },
            };
            var track = new QobuzTrack { Id = "t1", Title = "T", TrackNumber = 1 };

            var carrier = QobuzStreamingTrack.From(track, album);

            carrier.Album.CoverArtUrls.Should().ContainKey("large").WhoseValue.Should().Be("https://ex/l.jpg");
            carrier.Album.CoverArtUrls.Should().ContainKey("extralarge").WhoseValue.Should().Be("https://ex/xl.jpg");
            carrier.Album.GetBestCoverArtUrl().Should().Be("https://ex/l.jpg");
        }

        [Fact]
        public void From_NoImage_LeavesCoverArtUrlsEmpty_AndDoesNotThrow()
        {
            var album = new QobuzAlbum { Id = "a1", Title = "Album", Image = null };
            var track = new QobuzTrack { Id = "t1", Title = "T", TrackNumber = 1 };

            var carrier = QobuzStreamingTrack.From(track, album);

            carrier.Album.CoverArtUrls.Should().BeEmpty();
            carrier.Album.GetBestCoverArtUrl().Should().BeNullOrEmpty();
        }

        [Fact]
        public void From_PartialImage_MapsOnlyPresentSizes()
        {
            var album = new QobuzAlbum
            {
                Id = "a1",
                Title = "Album",
                Image = new QobuzImage { Large = "https://ex/l.jpg" },
            };
            var track = new QobuzTrack { Id = "t1", Title = "T", TrackNumber = 1 };

            var carrier = QobuzStreamingTrack.From(track, album);

            carrier.Album.CoverArtUrls.Should().ContainKey("large");
            carrier.Album.CoverArtUrls.Should().NotContainKey("small");
            carrier.Album.CoverArtUrls.Should().NotContainKey("medium");
        }
    }
}
