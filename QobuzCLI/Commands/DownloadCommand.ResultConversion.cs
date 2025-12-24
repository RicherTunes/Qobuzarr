using Microsoft.Extensions.Logging;
using QobuzCLI.Models;
using CliDownloadResult = QobuzCLI.Models.CliDownloadResult;
using CliPlaylistDownloadResult = QobuzCLI.Models.CliPlaylistDownloadResult;

namespace QobuzCLI.Commands;

/// <summary>
/// Partial class containing result conversion helpers for DownloadCommand.
/// Separated to keep the main command file under the LOC gate.
/// </summary>
public partial class DownloadCommand
{
    /// <summary>
    /// Safely converts PlaylistDownloadResult to DownloadResult with proper error handling
    /// </summary>
    private CliDownloadResult ConvertPlaylistResultToCliResult(
        CliPlaylistDownloadResult playlistResult)
    {
        try
        {
            var trackDownloads = new List<Lidarr.Plugin.Qobuzarr.Models.TrackDownload>();

            foreach (var track in playlistResult.DownloadedTracks ?? new List<TrackDownloadInfo>())
            {
                try
                {
                    int? trackId = null;
                    if (!string.IsNullOrEmpty(track.TrackId))
                    {
                        if (int.TryParse(track.TrackId, out var parsedId))
                        {
                            trackId = parsedId;
                        }
                        else
                        {
                            _logger.LogWarning("Failed to parse track ID '{TrackId}' as integer", track.TrackId);
                        }
                    }

                    trackDownloads.Add(new Lidarr.Plugin.Qobuzarr.Models.TrackDownload
                    {
                        StreamingUrl = track.Skipped ? null! : "downloaded",
                        QobuzTrackId = trackId,
                        Title = $"Track {track.Position}",
                        MetadataSource = track.Skipped ? "Skipped" : "Playlist Download"
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error converting playlist track");
                    // Add a placeholder for the failed track
                    trackDownloads.Add(new Lidarr.Plugin.Qobuzarr.Models.TrackDownload
                    {
                        Title = $"Track {track.Position} (Conversion Error)",
                        MetadataSource = "Error"
                    });
                }
            }

            return new CliDownloadResult
            {
                Success = playlistResult.Success,
                Message = playlistResult.Message,
                StartedAt = playlistResult.StartedAt,
                CompletedAt = playlistResult.CompletedAt,
                TrackDownloads = playlistResult.DownloadedTracks ?? new List<TrackDownloadInfo>(),
                MetadataStrategy = "Playlist Download",
                ApiCallsSaved = 0,
                AdditionalApiCalls = playlistResult.TotalTracks
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert playlist download result");
            return CliDownloadResult.Failure("Playlist download conversion failed");
        }
    }

    /// <summary>
    /// Safely converts LabelDownloadResult to DownloadResult with proper error handling
    /// </summary>
    private CliDownloadResult ConvertLabelResultToCliResult(
        Lidarr.Plugin.Qobuzarr.Download.Services.LabelDownloadResult labelResult)
    {
        try
        {
            return new CliDownloadResult
            {
                Success = labelResult.Success,
                Message = labelResult.Message ?? $"Downloaded {labelResult.SuccessfulAlbums}/{labelResult.TotalAlbums} albums from {labelResult.LabelName}",
                StartedAt = labelResult.StartedAt,
                CompletedAt = labelResult.CompletedAt,
                TrackDownloads = new List<TrackDownloadInfo>(), // Label downloads don't track individual tracks
                MetadataStrategy = "Label",
                ApiCallsSaved = 0,
                AdditionalApiCalls = labelResult.TotalAlbums
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert label download result");
            return CliDownloadResult.Failure("Label download conversion failed");
        }
    }
}
