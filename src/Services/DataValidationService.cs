using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Data validation service that handles corrupted metadata and file system limitations
    /// Prevents crashes from null/empty data and invalid file paths
    /// </summary>
    public class DataValidationService
    {
        private readonly Logger _logger;
        
        // Invalid characters for file names on different platforms
        private static readonly char[] InvalidFileChars = Path.GetInvalidFileNameChars();
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();
        
        // Maximum path lengths for different platforms
        private static readonly int MAX_PATH_LENGTH = Environment.OSVersion.Platform == PlatformID.Win32NT ? 260 : 4096;
        private const int MAX_FILENAME_LENGTH = 255;

        public DataValidationService(Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Validates and sanitizes track metadata
        /// </summary>
        public ValidationResult<T> ValidateTrackData<T>(T track, Func<T, string> getTitle, Func<T, string> getArtist)
        {
            if (track == null)
            {
                return ValidationResult<T>.Failed("Track is null");
            }

            var title = getTitle(track);
            var artist = getArtist(track);

            // Check for null/empty critical fields
            if (string.IsNullOrWhiteSpace(title))
            {
                return ValidationResult<T>.Failed("Track title is null or empty");
            }

            if (string.IsNullOrWhiteSpace(artist))
            {
                return ValidationResult<T>.Failed("Track artist is null or empty");
            }

            // Check for suspicious data patterns
            if (title.Length > 500 || artist.Length > 200)
            {
                _logger.Warn("⚠️ SUSPICIOUS DATA: Very long title/artist - Title: {0} chars, Artist: {1} chars", 
                           title.Length, artist.Length);
            }

            return ValidationResult<T>.Success(track);
        }

        /// <summary>
        /// Sanitizes filename for file system compatibility
        /// </summary>
        public string SanitizeFileName(string fileName)
        {
            try
            {
                var sanitized = FileNameSanitizer.SanitizeFileName(fileName);
                
                // Ensure not too long
                if (sanitized.Length > MAX_FILENAME_LENGTH)
                {
                    sanitized = sanitized.Substring(0, MAX_FILENAME_LENGTH - 10) + "...";
                    _logger.Debug("📏 FILENAME TRUNCATED: Original too long, truncated to {0} chars", sanitized.Length);
                }

                return sanitized;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "🛡️ DEFENSIVE: Filename sanitization failed for '{0}', using fallback", fileName);
                return $"Fallback_{DateTime.UtcNow.Ticks}";
            }
        }

        /// <summary>
        /// Validates and sanitizes file path
        /// </summary>
        public PathValidationResult ValidateFilePath(string basePath, string fileName)
        {
            try
            {
                var sanitizedFileName = SanitizeFileName(fileName);
                var fullPath = Path.Combine(basePath, sanitizedFileName);

                // Check path length (platform-specific)
                if (fullPath.Length > MAX_PATH_LENGTH)
                {
                    var shortenedFileName = SanitizeFileName(fileName.Substring(0, 
                        Math.Max(10, fileName.Length - (fullPath.Length - MAX_PATH_LENGTH + 20))));
                    
                    fullPath = Path.Combine(basePath, shortenedFileName);
                    
                    _logger.Warn("📏 PATH TRUNCATED: Original path too long, shortened filename to fit");
                }

                return new PathValidationResult
                {
                    IsValid = true,
                    SanitizedPath = fullPath,
                    SanitizedFileName = Path.GetFileName(fullPath)
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to validate file path: {0}", fileName);
                return new PathValidationResult
                {
                    IsValid = false,
                    ErrorMessage = ex.Message,
                    SanitizedFileName = "fallback_file.mp3"
                };
            }
        }

        /// <summary>
        /// Detects duplicate tracks in a collection
        /// </summary>
        public DuplicateDetectionResult<T> DetectDuplicates<T>(T[] tracks, 
            Func<T, string> getTitle, 
            Func<T, string> getArtist,
            Func<T, TimeSpan?> getDuration)
        {
            var duplicates = tracks
                .GroupBy(t => new { 
                    Title = NormalizeForComparison(getTitle(t)), 
                    Artist = NormalizeForComparison(getArtist(t))
                })
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicates.Any())
            {
                _logger.Warn("🔍 DUPLICATES DETECTED: {0} duplicate groups found", duplicates.Count);
                
                // Get all duplicate track IDs to exclude
                var duplicateTrackIds = new HashSet<T>(duplicates.SelectMany(g => g.Skip(1)));
                
                // Get non-duplicate tracks plus the first track from each duplicate group
                var uniqueTracks = tracks.Where(t => !duplicateTrackIds.Contains(t)).ToList();

                return new DuplicateDetectionResult<T>
                {
                    HasDuplicates = true,
                    DuplicateCount = duplicates.Sum(g => g.Count() - 1),
                    RecommendedTracks = uniqueTracks
                };
            }

            return new DuplicateDetectionResult<T>
            {
                HasDuplicates = false,
                RecommendedTracks = tracks.ToList()
            };
        }

        /// <summary>
        /// Validates track numbering sequence
        /// </summary>
        public TrackSequenceResult ValidateTrackSequence<T>(T[] tracks, Func<T, int> getTrackNumber)
        {
            var trackNumbers = tracks.Select(getTrackNumber).Where(n => n > 0).OrderBy(n => n).ToArray();
            
            if (!trackNumbers.Any())
            {
                return new TrackSequenceResult 
                { 
                    IsValid = false, 
                    Issue = "No valid track numbers found" 
                };
            }

            // Check for gaps
            var gaps = new System.Collections.Generic.List<int>();
            for (int i = 1; i < trackNumbers.Max(); i++)
            {
                if (!trackNumbers.Contains(i))
                {
                    gaps.Add(i);
                }
            }

            if (gaps.Any())
            {
                _logger.Warn("📊 TRACK GAPS: Missing track numbers: {0}", string.Join(", ", gaps));
                
                return new TrackSequenceResult
                {
                    IsValid = false,
                    Issue = $"Missing track numbers: {string.Join(", ", gaps)}",
                    MissingTrackNumbers = gaps
                };
            }

            return new TrackSequenceResult { IsValid = true };
        }

        private string NormalizeForComparison(string text)
        {
            return text?.ToLowerInvariant()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "") ?? "";
        }
    }

    #region Result Classes

    public class ValidationResult<T>
    {
        public bool IsValid { get; set; }
        public T Data { get; set; }
        public string ErrorMessage { get; set; }

        public static ValidationResult<T> Success(T data) => new() { IsValid = true, Data = data };
        public static ValidationResult<T> Failed(string error) => new() { IsValid = false, ErrorMessage = error };
    }

    public class PathValidationResult
    {
        public bool IsValid { get; set; }
        public string SanitizedPath { get; set; }
        public string SanitizedFileName { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class DuplicateDetectionResult<T>
    {
        public bool HasDuplicates { get; set; }
        public int DuplicateCount { get; set; }
        public List<T> RecommendedTracks { get; set; } = new();
    }

    public class TrackSequenceResult
    {
        public bool IsValid { get; set; }
        public string Issue { get; set; }
        public System.Collections.Generic.List<int> MissingTrackNumbers { get; set; } = new();
    }

    #endregion
}