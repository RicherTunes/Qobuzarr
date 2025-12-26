using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Common.Utilities;
using Lidarr.Plugin.Qobuzarr.Configuration;
using NzbDrone.Common.Extensions;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Generates file paths and names for downloaded tracks with proper sanitization
    /// </summary>
    public class FilePathGenerator : IFilePathGenerator
    {
        public string GenerateFileName(QobuzTrack track, QobuzAlbum album, int formatId)
        {
            Guard.NotNull(track, nameof(track));
            Guard.NotNull(album, nameof(album));

            // Delegate to TrackFileNameBuilder for consistent multi-disc handling and sanitization
            return TrackFileNameBuilder.Build(
                trackNumber: track.TrackNumber,
                trackTitle: track.GetFullTitle(),
                formatId: formatId,
                discNumber: track.DiscNumber,
                totalDiscs: album.MediaCount);
        }

        public string GenerateOptimizedFileName(TrackDownload trackDownload, int quality)
        {
            Guard.NotNull(trackDownload, nameof(trackDownload));

            var extension = GetFileExtension(quality);
            var sanitizedArtist = Lidarr.Plugin.Common.Security.Sanitize.PathSegment(trackDownload.Artist ?? "Unknown Artist").Normalize(System.Text.NormalizationForm.FormC);
            var sanitizedTitle = Lidarr.Plugin.Common.Security.Sanitize.PathSegment(trackDownload.Title ?? "Unknown Track").Normalize(System.Text.NormalizationForm.FormC);
            var trackNumber = trackDownload.TrackNumber?.ToString(QobuzPluginConstants.FileNaming.TrackNumberFormat) ?? "00";

            return $"{trackNumber}. {sanitizedArtist} - {sanitizedTitle}{extension}";
        }

        public string GetFileExtension(int formatId)
        {
            return TrackFileNameBuilder.GetExtensionForFormat(formatId);
        }

        public string GetQualityDescription(QobuzTrack track)
        {
            Guard.NotNull(track, nameof(track));

            // Get quality information from track maximum specs
            var maxBitDepth = track.MaximumBitDepth;
            var maxSampleRate = track.MaximumSampleRate;
            
            if (maxBitDepth >= 24 && maxSampleRate >= 96000)
            {
                return $"Hi-Res FLAC {maxBitDepth}bit/{maxSampleRate / 1000}kHz";
            }
            else if (maxBitDepth >= 16 && maxSampleRate >= 44100)
            {
                return $"FLAC {maxBitDepth}bit/{maxSampleRate / 1000}kHz";
            }
            else
            {
                return "MP3 320kbps";
            }
        }
    }
}
