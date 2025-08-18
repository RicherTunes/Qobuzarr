using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Centralized validation utilities to eliminate code duplication.
    /// This class provides the single source of truth for all validation logic.
    /// </summary>
    public static class ValidationUtilities
    {
        private static readonly Regex UrlRegex = new Regex(
            @"^(https?:\/\/)?([\da-z\.-]+)\.([a-z\.]{2,6})([\/\w \.-]*)*\/?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex EmailRegex = new Regex(
            @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
            RegexOptions.Compiled);

        /// <summary>
        /// Validates that a file was downloaded successfully.
        /// </summary>
        public static bool ValidateDownloadedFile(string filePath, long? expectedSize = null, string expectedHash = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            if (!File.Exists(filePath))
                return false;

            var fileInfo = new FileInfo(filePath);
            
            // Check file is not empty
            if (fileInfo.Length == 0)
                return false;

            // Validate expected size if provided
            if (expectedSize.HasValue && fileInfo.Length != expectedSize.Value)
                return false;

            // Validate hash if provided
            if (!string.IsNullOrEmpty(expectedHash))
            {
                var actualHash = CalculateFileHash(filePath, expectedHash.Length > 40 ? "SHA256" : "SHA1");
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Calculates the hash of a file using the specified algorithm.
        /// </summary>
        private static string CalculateFileHash(string filePath, string algorithm = "SHA256")
        {
            using (var stream = File.OpenRead(filePath))
            {
                using (var hasher = algorithm.ToUpperInvariant() switch
                {
                    "SHA1" => SHA1.Create() as HashAlgorithm,
                    "SHA256" => SHA256.Create() as HashAlgorithm,
                    "SHA512" => SHA512.Create() as HashAlgorithm,
                    "MD5" => MD5.Create() as HashAlgorithm,
                    _ => SHA256.Create()
                })
                {
                    var hash = hasher.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        /// <summary>
        /// Validates a directory path is valid and optionally creates it.
        /// </summary>
        public static bool ValidateDirectoryPath(string directoryPath, bool createIfMissing = false)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return false;

            try
            {
                // Check if path is valid
                var fullPath = Path.GetFullPath(directoryPath);
                
                if (Directory.Exists(fullPath))
                    return true;

                if (createIfMissing)
                {
                    Directory.CreateDirectory(fullPath);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates a file path is valid (doesn't check existence).
        /// </summary>
        public static bool ValidateFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            try
            {
                // This will throw if path is invalid
                Path.GetFullPath(filePath);
                
                // Check for invalid characters
                var fileName = Path.GetFileName(filePath);
                var invalidChars = Path.GetInvalidFileNameChars();
                
                foreach (var c in invalidChars)
                {
                    if (fileName.Contains(c))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates that a string is a valid URL.
        /// </summary>
        public static bool ValidateUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return Uri.TryCreate(url, UriKind.Absolute, out var result) &&
                   (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>
        /// Validates that a string is a valid email address.
        /// </summary>
        public static bool ValidateEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            return EmailRegex.IsMatch(email);
        }

        /// <summary>
        /// Validates that a track number is within reasonable bounds.
        /// </summary>
        public static bool ValidateTrackNumber(int trackNumber, int? maxTracks = null)
        {
            if (trackNumber < 1)
                return false;

            if (trackNumber > 999) // Reasonable upper limit
                return false;

            if (maxTracks.HasValue && trackNumber > maxTracks.Value)
                return false;

            return true;
        }

        /// <summary>
        /// Validates that a disc number is within reasonable bounds.
        /// </summary>
        public static bool ValidateDiscNumber(int discNumber, int? maxDiscs = null)
        {
            if (discNumber < 1)
                return false;

            if (discNumber > 99) // Reasonable upper limit
                return false;

            if (maxDiscs.HasValue && discNumber > maxDiscs.Value)
                return false;

            return true;
        }

        /// <summary>
        /// Validates audio quality parameters.
        /// </summary>
        public static bool ValidateAudioQuality(int? bitDepth, double? sampleRate)
        {
            // Validate bit depth
            if (bitDepth.HasValue)
            {
                var validBitDepths = new[] { 16, 24, 32 };
                if (!validBitDepths.Contains(bitDepth.Value))
                    return false;
            }

            // Validate sample rate
            if (sampleRate.HasValue)
            {
                var validSampleRates = new[] { 44.1, 48.0, 88.2, 96.0, 176.4, 192.0, 352.8, 384.0 };
                if (!validSampleRates.Contains(sampleRate.Value))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Validates that a duration is within reasonable bounds for a music track.
        /// </summary>
        public static bool ValidateTrackDuration(TimeSpan duration)
        {
            // Track shouldn't be less than 1 second
            if (duration.TotalSeconds < 1)
                return false;

            // Track shouldn't be more than 2 hours (classical music edge case)
            if (duration.TotalHours > 2)
                return false;

            return true;
        }

        /// <summary>
        /// Validates that a year is reasonable for music releases.
        /// </summary>
        public static bool ValidateReleaseYear(int year)
        {
            var currentYear = DateTime.Now.Year;
            
            // Music recording didn't exist before 1860
            if (year < 1860)
                return false;

            // Can't be in the future (allow 1 year for pre-releases)
            if (year > currentYear + 1)
                return false;

            return true;
        }

        /// <summary>
        /// Validates API response data for basic integrity.
        /// </summary>
        public static bool ValidateApiResponse(object response, out string error)
        {
            error = null;

            if (response == null)
            {
                error = "Response is null";
                return false;
            }

            // Check for common API error patterns
            var responseStr = response.ToString();
            if (responseStr.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                responseStr.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                responseStr.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
            {
                error = "Response contains error indicators";
                return false;
            }

            return true;
        }
    }
}