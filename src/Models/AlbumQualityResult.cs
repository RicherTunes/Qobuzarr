using System;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Result of album-level quality detection.
    /// </summary>
    public class AlbumQualityResult
    {
        public string AlbumId { get; set; }
        public string AlbumTitle { get; set; }
        public int PreferredQuality { get; set; }
        public int DetectedQuality { get; set; }
        public bool ConsistentQuality { get; set; }
        public double ConfidenceScore { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public int SampleSize { get; set; }
        public int TotalTracks { get; set; }
        public bool OptimizationApplied { get; set; }
        public int ApiCallsSaved { get; set; }
        public DateTime CachedAt { get; set; }

        /// <summary>
        /// Gets the confidence percentage as a string.
        /// </summary>
        public string ConfidencePercentage => $"{ConfidenceScore:P0}";

        /// <summary>
        /// Checks if this result represents a successful detection.
        /// </summary>
        public bool IsValid => Success && !string.IsNullOrEmpty(AlbumId);

        /// <summary>
        /// Gets the detected quality as a QobuzQuality object.
        /// </summary>
        public QobuzQuality GetDetectedQuality()
        {
            return QobuzQuality.FromId(DetectedQuality);
        }

        /// <summary>
        /// Gets the preferred quality as a QobuzQuality object.
        /// </summary>
        public QobuzQuality GetPreferredQuality()
        {
            return QobuzQuality.FromId(PreferredQuality);
        }

        /// <summary>
        /// Creates a failed result with an error message.
        /// </summary>
        public static AlbumQualityResult Failed(string error)
        {
            return new AlbumQualityResult
            {
                Success = false,
                Error = error,
                CachedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static AlbumQualityResult Successful(string albumId, string albumTitle, int detectedQuality)
        {
            return new AlbumQualityResult
            {
                Success = true,
                AlbumId = albumId,
                AlbumTitle = albumTitle,
                DetectedQuality = detectedQuality,
                CachedAt = DateTime.UtcNow
            };
        }

        public override string ToString()
        {
            if (Success)
            {
                var quality = GetDetectedQuality();
                return $"AlbumQualityResult[Album={AlbumTitle}, Quality={quality.Name}, Confidence={ConfidencePercentage}, Optimized={OptimizationApplied}]";
            }
            else
            {
                return $"AlbumQualityResult[Album={AlbumTitle}, Failed: {Error}]";
            }
        }
    }
}
