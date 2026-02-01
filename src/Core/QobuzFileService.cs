using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Core
{
    /// <summary>
    /// Implementation of file checking service
    /// </summary>
    public class QobuzFileService : IQobuzFileService
    {
        private readonly IQobuzLogger _logger;

        public QobuzFileService(IQobuzLogger logger)
        {
            _logger = logger;
        }

        public Task<FileExistenceResult> CheckExistingAlbumAsync(string albumId, string albumDir, string requestedQuality)
        {
            try
            {
                // Check if album directory exists
                if (!Directory.Exists(albumDir))
                {
                    return Task.FromResult(new FileExistenceResult { AlreadyExists = false });
                }

                // Get all music files in the directory
                var musicFiles = Directory.GetFiles(albumDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f =>
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        return ext == ".flac" || ext == ".mp3" || ext == ".m4a" || ext == ".aac" ||
                               ext == ".wav" || ext == ".alac" || ext == ".opus" || ext == ".ogg";
                    })
                    .ToList();

                if (!musicFiles.Any())
                {
                    return Task.FromResult(new FileExistenceResult { AlreadyExists = false });
                }

                _logger.Debug("Found {0} music files in {1}", musicFiles.Count, albumDir);

                // Calculate requested quality score
                var requestedScore = CalculateQualityScore(requestedQuality);

                // Check quality of existing files
                int highestExistingScore = 0;
                string highestQualityFormat = "";

                foreach (var file in musicFiles)
                {
                    // Estimate quality from file extension and size
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    var fileInfo = new FileInfo(file);
                    var estimatedScore = EstimateQualityFromFile(ext, fileInfo.Length);

                    if (estimatedScore > highestExistingScore)
                    {
                        highestExistingScore = estimatedScore;
                        highestQualityFormat = GetQualityLabelFromExtension(ext);
                    }
                }

                _logger.Debug("Highest existing quality score: {0}, Requested score: {1}",
                    highestExistingScore, requestedScore);

                // Determine if existing quality is adequate
                if (highestExistingScore >= requestedScore)
                {
                    var reason = $"Album already exists with {highestQualityFormat} quality";

                    return Task.FromResult(new FileExistenceResult
                    {
                        AlreadyExists = true,
                        ExistingTrackCount = musicFiles.Count,
                        Reason = reason
                    });
                }

                return Task.FromResult(new FileExistenceResult { AlreadyExists = false });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking for existing files in {0}", albumDir);
                // On error, proceed with download
                return Task.FromResult(new FileExistenceResult { AlreadyExists = false });
            }
        }

        private int CalculateQualityScore(string quality)
        {
            // Simple quality scoring system
            // Higher scores = better quality
            return quality?.ToLowerInvariant() switch
            {
                "flac-max" => 2400,    // FLAC 24-bit/192kHz
                "flac-hires" => 2200,  // FLAC 24-bit/96kHz
                "flac-cd" => 1600,     // FLAC 16-bit/44.1kHz
                "mp3-320" => 620,      // MP3 320kbps
                _ => 1000              // Default to a medium score
            };
        }

        private int EstimateQualityFromFile(string extension, long fileSize)
        {
            // Very rough estimation based on typical file sizes
            var sizeMB = fileSize / (1024.0 * 1024.0);

            return extension switch
            {
                ".flac" => sizeMB > 40 ? 2000 : 1600, // Assume Hi-Res if > 40MB per track
                ".wav" => sizeMB > 40 ? 1900 : 1500,
                ".alac" => sizeMB > 40 ? 1950 : 1550,
                ".mp3" => 620, // Assume 320kbps
                ".m4a" => 650,
                ".aac" => 650,
                ".ogg" => 700,
                ".opus" => 750,
                _ => 100
            };
        }

        private string GetQualityLabelFromExtension(string extension)
        {
            return extension switch
            {
                ".flac" => "FLAC",
                ".wav" => "WAV",
                ".alac" => "ALAC",
                ".mp3" => "MP3 320kbps",
                ".m4a" => "M4A",
                ".aac" => "AAC",
                ".ogg" => "OGG Vorbis",
                ".opus" => "Opus",
                _ => "Unknown"
            };
        }
    }
}
