using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Lyrics;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Download.Services;

public interface ILyricsEnricher
{
    Task TryEnrichAsync(string audioFilePath, string artistName, string trackName, string albumName, int durationSeconds, CancellationToken ct = default);
}

public sealed class LyricsEnricher : ILyricsEnricher, IDisposable
{
    private readonly LrclibClient _client;
    private readonly Logger _logger;

    public LyricsEnricher(Logger logger)
    {
        _client = new LrclibClient();
        _logger = logger;
    }

    public async Task TryEnrichAsync(string audioFilePath, string artistName, string trackName, string albumName, int durationSeconds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(trackName))
            return;

        try
        {
            var lyrics = await _client.TryFetchSyncedLyricsAsync(artistName, trackName, albumName, durationSeconds, ct).ConfigureAwait(false);
            if (lyrics is null) return;

            var lrcPath = Path.ChangeExtension(audioFilePath, ".lrc");
            await File.WriteAllTextAsync(lrcPath, lyrics, ct).ConfigureAwait(false);
            _logger.Debug("Saved synced lyrics: {0}", Path.GetFileName(lrcPath));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Lyrics fetch failed for {0} — {1} (non-fatal)", artistName, trackName);
        }
    }

    public void Dispose() => _client.Dispose();
}
