using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Models;
using Xunit;

namespace Qobuzarr.Tests.Unit.Download.Services
{
    /// <summary>
    /// Wave B parity: <see cref="QobuzAudioMetadataApplier"/> must write EXACTLY the same tag set as the
    /// prior bespoke <c>TrackDownloadService.ApplyMetadataTagsAsync</c> (Title, Track, Disc, Album,
    /// AlbumArtists, Year, Genre, Comment=Label:&lt;name&gt;, Performers, Composers) so routing downloads
    /// through Common's orchestrator does not silently drop or change any tag.
    /// </summary>
    public sealed class QobuzAudioMetadataApplierTests : IDisposable
    {
        private readonly string _tempDir;

        public QobuzAudioMetadataApplierTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"QobuzApplierTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best effort */ }
        }

        [Fact]
        public async Task ApplyAsync_WritesEveryQobuzTagField()
        {
            var filePath = CreateMinimalFlac();
            var album = new QobuzAlbum
            {
                Id = "alb-1",
                Title = "Random Access Memories",
                Artist = new QobuzArtist { Id = "26887", Name = "Daft Punk" },
                ReleaseDateOriginal = "2013-05-17",
                Genre = new QobuzGenre { Name = "Electronic" },
                Label = new QobuzLabel { Name = "Columbia" },
            };
            var track = new QobuzTrack
            {
                Id = "t-1",
                Title = "Giorgio by Moroder",
                TrackNumber = 3,
                DiscNumber = 2,
                Performer = new QobuzArtist { Id = "p1", Name = "Daft Punk" },
                Composer = new QobuzComposer { Id = "c1", Name = "Giovanni Giorgio Moroder" },
            };

            var applier = new QobuzAudioMetadataApplier();
            await applier.ApplyAsync(filePath, QobuzStreamingTrack.From(track, album));

            using var file = TagLib.File.Create(filePath);
            file.Tag.Title.Should().Be("Giorgio by Moroder");
            file.Tag.Track.Should().Be(3u);
            file.Tag.Disc.Should().Be(2u);
            file.Tag.Album.Should().Be("Random Access Memories");
            file.Tag.AlbumArtists.Should().ContainSingle().Which.Should().Be("Daft Punk");
            file.Tag.Year.Should().Be(2013u);
            file.Tag.Genres.Should().ContainSingle().Which.Should().Be("Electronic");
            file.Tag.Comment.Should().Be("Label: Columbia");
            file.Tag.Performers.Should().ContainSingle().Which.Should().Be("Daft Punk");
            file.Tag.Composers.Should().ContainSingle().Which.Should().Be("Giovanni Giorgio Moroder");
        }

        [Fact]
        public async Task ApplyAsync_PreservesCurlyApostropheInTitle()
        {
            var filePath = CreateMinimalFlac();
            // U+2019 RIGHT SINGLE QUOTATION MARK — the curly apostrophe that breaks naive sanitizers.
            const string trickyTitle = "Don’t Stop";
            var album = new QobuzAlbum { Id = "a", Title = "Album", Artist = new QobuzArtist { Name = "Artist" } };
            var track = new QobuzTrack { Id = "t", Title = trickyTitle, TrackNumber = 1, DiscNumber = 1 };

            var applier = new QobuzAudioMetadataApplier();
            await applier.ApplyAsync(filePath, QobuzStreamingTrack.From(track, album));

            using var file = TagLib.File.Create(filePath);
            file.Tag.Title.Should().Be(trickyTitle);
        }

        [Fact]
        public async Task ApplyAsync_NonQobuzMetadata_IsNoOp()
        {
            var filePath = CreateMinimalFlac();
            var applier = new QobuzAudioMetadataApplier();

            // A plain StreamingTrack (not the Qobuz carrier) must be ignored, never crash.
            var act = async () => await applier.ApplyAsync(filePath, new Lidarr.Plugin.Abstractions.Models.StreamingTrack { Title = "x" });
            await act.Should().NotThrowAsync();

            using var file = TagLib.File.Create(filePath);
            file.Tag.Title.Should().BeNullOrEmpty("a non-Qobuz carrier must not be tagged");
        }

        private string CreateMinimalFlac()
        {
            var path = Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}.flac");
            var flacSignature = System.Text.Encoding.ASCII.GetBytes("fLaC");
            var streamInfoHeader = new byte[] { 0x80, 0x00, 0x00, 0x22 };
            var streamInfo = new byte[]
            {
                0x00, 0x10, 0x00, 0x10,
                0x00, 0x00, 0x01,
                0x00, 0x00, 0x01,
                0x0A, 0xC4, 0x40,
                0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            };
            var frame = new byte[] { 0xFF, 0xF8, 0x09, 0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

            var flacBytes = new byte[flacSignature.Length + streamInfoHeader.Length + streamInfo.Length + frame.Length];
            int offset = 0;
            flacSignature.CopyTo(flacBytes, offset); offset += flacSignature.Length;
            streamInfoHeader.CopyTo(flacBytes, offset); offset += streamInfoHeader.Length;
            streamInfo.CopyTo(flacBytes, offset); offset += streamInfo.Length;
            frame.CopyTo(flacBytes, offset);

            File.WriteAllBytes(path, flacBytes);
            return path;
        }
    }
}
