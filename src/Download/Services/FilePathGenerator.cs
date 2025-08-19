using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Lidarr.Plugin.Qobuzarr.Configuration;
using NzbDrone.Common.Extensions;

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

            // Generate safe filename: "01 - Track Title.flac"
            var trackNumber = track.TrackNumber.ToString(QobuzConstants.FileNaming.TrackNumberFormat);
            var title = track.GetFullTitle().ToSafeFileName();
            var extension = GetFileExtension(formatId);

            return $"{trackNumber} - {title}{extension}";
        }

        public string GenerateOptimizedFileName(TrackDownload trackDownload, int quality)
        {
            Guard.NotNull(trackDownload, nameof(trackDownload));

            var extension = GetFileExtension(quality);
            var sanitizedArtist = FileNameUtility.SanitizeFileName(trackDownload.Artist ?? "Unknown Artist");
            var sanitizedTitle = FileNameUtility.SanitizeFileName(trackDownload.Title ?? "Unknown Track");
            var trackNumber = trackDownload.TrackNumber?.ToString(QobuzConstants.FileNaming.TrackNumberFormat) ?? "00";

            return $"{trackNumber}. {sanitizedArtist} - {sanitizedTitle}{extension}";
        }

        public string GetFileExtension(int formatId)
        {
            return formatId switch
            {
                QobuzConstants.Quality.Mp3320 => ".mp3",        // MP3 320
                QobuzConstants.Quality.FlacCd => ".flac",       // FLAC CD
                QobuzConstants.Quality.Flac24_96 => ".flac",    // FLAC 24/96
                QobuzConstants.Quality.Flac24_192 => ".flac",   // FLAC 24/192
                _ => ".flac"        // Default to FLAC
            };
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