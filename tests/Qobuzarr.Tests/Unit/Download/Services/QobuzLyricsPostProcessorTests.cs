using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Services.Lyrics;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Models;
using Xunit;

namespace Qobuzarr.Tests.Unit.Download.Services
{
    /// <summary>
    /// Wave B parity: synced-lyrics enrichment is preserved via the orchestrator's post-processor seam with
    /// the same canonical gating as before — <c>SaveSyncedLyrics</c> is the master toggle, <c>UseLRCLIB</c>
    /// only gates the LRCLIB fallback — and remains strictly non-fatal.
    /// </summary>
    public sealed class QobuzLyricsPostProcessorTests : IDisposable
    {
        private readonly string _tempDir;

        public QobuzLyricsPostProcessorTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"QobuzLyricsTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best effort */ }
        }

        private sealed class RecordingLyricsEnricher : ILyricsEnricher
        {
            public int Calls;
            public string? Artist;
            public string? Track;
            public string? Album;
            public int Duration;
            public bool AllowLrclibFallback;
            public Func<Task>? OnEnrich;

            public async Task TryEnrichAsync(string audioFilePath, string artistName, string trackName, string albumName, int durationSeconds, bool allowLrclibFallback, CancellationToken cancellationToken = default)
            {
                Calls++;
                Artist = artistName;
                Track = trackName;
                Album = albumName;
                Duration = durationSeconds;
                AllowLrclibFallback = allowLrclibFallback;
                if (OnEnrich != null) await OnEnrich();
            }

            public void Dispose() { }
        }

        private (string path, QobuzStreamingTrack carrier) MakeCarrier()
        {
            var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.flac");
            File.WriteAllBytes(path, new byte[] { 0x66, 0x4C, 0x61, 0x43 }); // "fLaC"
            var album = new QobuzAlbum { Id = "a", Title = "Discovery", Artist = new QobuzArtist { Name = "Daft Punk" } };
            var track = new QobuzTrack { Id = "t", Title = "One More Time", TrackNumber = 1, DiscNumber = 1, DurationSeconds = 320 };
            return (path, QobuzStreamingTrack.From(track, album));
        }

        [Fact]
        public async Task PostProcess_WhenSaveSyncedLyricsEnabled_InvokesEnricherWithTrackContext()
        {
            var (path, carrier) = MakeCarrier();
            var enricher = new RecordingLyricsEnricher();
            var settings = new QobuzDownloadSettings { SaveSyncedLyrics = true, UseLRCLIB = true };
            var sut = new QobuzLyricsPostProcessor(settings, enricher);

            var result = await sut.PostProcessAsync(path, carrier, null, CancellationToken.None);

            result.Should().Be(path);
            enricher.Calls.Should().Be(1);
            enricher.Artist.Should().Be("Daft Punk");
            enricher.Track.Should().Be("One More Time");
            enricher.Album.Should().Be("Discovery");
            enricher.Duration.Should().Be(320);
            enricher.AllowLrclibFallback.Should().BeTrue("UseLRCLIB gates the LRCLIB fallback");
        }

        [Fact]
        public async Task PostProcess_WhenUseLrclibDisabled_PassesAllowFallbackFalse()
        {
            var (path, carrier) = MakeCarrier();
            var enricher = new RecordingLyricsEnricher();
            var settings = new QobuzDownloadSettings { SaveSyncedLyrics = true, UseLRCLIB = false };
            var sut = new QobuzLyricsPostProcessor(settings, enricher);

            await sut.PostProcessAsync(path, carrier, null, CancellationToken.None);

            enricher.Calls.Should().Be(1);
            enricher.AllowLrclibFallback.Should().BeFalse();
        }

        [Fact]
        public async Task PostProcess_WhenSaveSyncedLyricsDisabled_DoesNotInvokeEnricher()
        {
            var (path, carrier) = MakeCarrier();
            var enricher = new RecordingLyricsEnricher();
            var settings = new QobuzDownloadSettings { SaveSyncedLyrics = false };
            var sut = new QobuzLyricsPostProcessor(settings, enricher);

            await sut.PostProcessAsync(path, carrier, null, CancellationToken.None);

            enricher.Calls.Should().Be(0);
        }

        [Fact]
        public async Task PostProcess_WhenEnricherThrows_IsNonFatalAndReturnsPath()
        {
            var (path, carrier) = MakeCarrier();
            var enricher = new RecordingLyricsEnricher { OnEnrich = () => throw new InvalidOperationException("lrclib boom") };
            var settings = new QobuzDownloadSettings { SaveSyncedLyrics = true, UseLRCLIB = true };
            var sut = new QobuzLyricsPostProcessor(settings, enricher);

            var result = await sut.PostProcessAsync(path, carrier, null, CancellationToken.None);

            result.Should().Be(path, "a lyrics failure must never fail the download");
        }

        [Fact]
        public async Task PostProcess_WhenPayloadIsNotAudio_DoesNotCreateLyricsSidecar()
        {
            var (path, carrier) = MakeCarrier();
            await File.WriteAllTextAsync(path, "<html>provider error</html>");
            var enricher = new RecordingLyricsEnricher();
            var settings = new QobuzDownloadSettings { SaveSyncedLyrics = true, UseLRCLIB = true };
            var sut = new QobuzLyricsPostProcessor(settings, enricher);

            var result = await sut.PostProcessAsync(path, carrier, null, CancellationToken.None);

            result.Should().Be(path, "qobuz payload validation later fails the track, but lyrics must not run for HTML/JSON error bodies");
            enricher.Calls.Should().Be(0);
        }
    }
}
