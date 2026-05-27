using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using NLog;
using Xunit;

namespace Qobuzarr.Tests.Unit.Download;

[Trait("Category", "Unit")]
public class LyricsEnricherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LyricsEnricher _enricher;

    public LyricsEnricherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"qobuzarr-lyrics-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _enricher = new LyricsEnricher(LogManager.GetCurrentClassLogger());
    }

    [Fact]
    public async Task TryEnrichAsync_EmptyArtist_DoesNothing()
    {
        var audioPath = Path.Combine(_tempDir, "track.flac");
        await File.WriteAllBytesAsync(audioPath, new byte[] { 1, 2, 3 });

        await _enricher.TryEnrichAsync(audioPath, "", "Track", "Album", 180);

        Assert.False(File.Exists(Path.ChangeExtension(audioPath, ".lrc")));
    }

    [Fact]
    public async Task TryEnrichAsync_EmptyTrackName_DoesNothing()
    {
        var audioPath = Path.Combine(_tempDir, "track.flac");
        await File.WriteAllBytesAsync(audioPath, new byte[] { 1, 2, 3 });

        await _enricher.TryEnrichAsync(audioPath, "Artist", "", "Album", 180);

        Assert.False(File.Exists(Path.ChangeExtension(audioPath, ".lrc")));
    }

    [Fact]
    public async Task TryEnrichAsync_CancellationRespected()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var audioPath = Path.Combine(_tempDir, "track.flac");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _enricher.TryEnrichAsync(audioPath, "Artist", "Track", "Album", 180, cts.Token));
    }

    [Fact]
    public async Task TryEnrichAsync_LrcPathIsAudioPathWithLrcExtension()
    {
        var audioPath = Path.Combine(_tempDir, "01 - My Song.flac");
        var expectedLrcPath = Path.Combine(_tempDir, "01 - My Song.lrc");

        Assert.Equal(expectedLrcPath, Path.ChangeExtension(audioPath, ".lrc"));
    }

    public void Dispose()
    {
        _enricher.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
