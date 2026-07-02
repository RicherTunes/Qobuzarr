using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Models;
using Lidarr.Plugin.Common.Interfaces;
using Lidarr.Plugin.Common.Services.Lyrics;
using Lidarr.Plugin.Common.Utilities;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Best-effort synced-lyrics (.lrc) enrichment run as Common's <c>SimpleDownloadOrchestrator</c>
    /// post-processor seam (Wave B). Preserves the prior bespoke behaviour:
    /// <list type="bullet">
    ///   <item><see cref="QobuzDownloadSettings.SaveSyncedLyrics"/> is the master toggle — when off, nothing runs.</item>
    ///   <item><see cref="QobuzDownloadSettings.UseLRCLIB"/> only gates the LRCLIB fallback (passed as <c>allowLrclibFallback</c>).</item>
    ///   <item>When no shared enricher is injected (production — Common's <see cref="LyricsEnricher"/> is internalized so
    ///   DryIoc doesn't auto-register it), a short-lived instance is constructed and disposed per track, mirroring the original.</item>
    /// </list>
    /// Never fails the download: a lyrics miss/throw is logged at debug and the audio path is returned unchanged.
    /// </summary>
    public sealed class QobuzLyricsPostProcessor : IAudioPostProcessor
    {
        private readonly QobuzDownloadSettings _settings;
        private readonly ILyricsEnricher? _lyricsEnricher;
        private readonly Func<ILyricsEnricher> _enricherFactory;
        private readonly Logger _logger;

        public QobuzLyricsPostProcessor(
            QobuzDownloadSettings settings,
            ILyricsEnricher? lyricsEnricher = null,
            Logger? logger = null,
            Func<ILyricsEnricher>? enricherFactory = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _lyricsEnricher = lyricsEnricher;
            // Fallback used only when no shared enricher is injected (the production case — Common's
            // LyricsEnricher is internalized so DryIoc doesn't auto-register it). Injectable so the
            // production construct-invoke-dispose path is unit-testable rather than only observable live.
            _enricherFactory = enricherFactory ?? (() => new LyricsEnricher());
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        public async Task<string> PostProcessAsync(string filePath, StreamingTrack track, StreamingQuality? quality, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_settings.SaveSyncedLyrics ||
                track is not QobuzStreamingTrack carrier ||
                string.IsNullOrWhiteSpace(filePath) ||
                !File.Exists(filePath))
            {
                return filePath;
            }

            // The post-processor seam runs before Qobuz's audio-payload validation. In the prior bespoke
            // loop, a non-audio body (HTML/JSON error page served as audio) failed the download before
            // lyrics ran, so lyrics never enriched garbage. Preserve that: skip enrichment when the file
            // doesn't look like audio (the orchestrator then fails the track on validation anyway).
            try
            {
                DownloadPayloadValidator.ValidateFileOrThrow(filePath);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Skipping lyrics for non-audio payload '{0}'", Path.GetFileName(filePath));
                return filePath;
            }

            var qobuzTrack = carrier.QobuzTrack;
            var album = carrier.QobuzAlbum;

            var enricher = _lyricsEnricher;
            var ownsEnricher = enricher is null;
            enricher ??= _enricherFactory();
            try
            {
                await enricher.TryEnrichAsync(
                    filePath,
                    album.GetArtistName() ?? "Unknown",
                    qobuzTrack.Title ?? "Unknown",
                    album.GetFullTitle() ?? "",
                    qobuzTrack.DurationSeconds,
                    allowLrclibFallback: _settings.UseLRCLIB,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Synced-lyrics enrichment failed for '{0}' (non-fatal)", Path.GetFileName(filePath));
            }
            finally
            {
                if (ownsEnricher)
                {
                    enricher.Dispose();
                }
            }

            return filePath;
        }
    }
}
