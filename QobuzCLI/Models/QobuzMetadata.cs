using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace QobuzCLI.Models
{
    /// <summary>
    /// Metadata stored alongside downloaded music files to track quality, completeness, and enable smart duplicate detection
    /// </summary>
    public class QobuzMetadata
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("trackId")]
        public string TrackId { get; set; } = "";

        [JsonPropertyName("albumId")]
        public string AlbumId { get; set; } = "";

        [JsonPropertyName("downloadDate")]
        public DateTime DownloadDate { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("quality")]
        public QualityInfo Quality { get; set; } = new();

        [JsonPropertyName("file")]
        public FileMetadata File { get; set; } = new();

        /// <summary>
        /// Gets the metadata file path for a given music file
        /// </summary>
        public static string GetMetadataPath(string musicFilePath)
        {
            var dir = Path.GetDirectoryName(musicFilePath) ?? "";
            var fileName = Path.GetFileNameWithoutExtension(musicFilePath);
            return Path.Combine(dir, ".qobuz", $"{fileName}.json");
        }

        /// <summary>
        /// Saves metadata to disk alongside the music file
        /// </summary>
        public async Task SaveAsync(string musicFilePath)
        {
            var metadataPath = GetMetadataPath(musicFilePath);
            var metadataDir = Path.GetDirectoryName(metadataPath);

            // Create .qobuz directory if it doesn't exist
            Directory.CreateDirectory(metadataDir ?? "");

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(this, options);
            await System.IO.File.WriteAllTextAsync(metadataPath, json);
        }

        /// <summary>
        /// Loads metadata from disk for a given music file
        /// </summary>
        public static async Task<QobuzMetadata?> LoadAsync(string musicFilePath)
        {
            var metadataPath = GetMetadataPath(musicFilePath);
            if (!System.IO.File.Exists(metadataPath))
                return null;

            try
            {
                var json = await System.IO.File.ReadAllTextAsync(metadataPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                return JsonSerializer.Deserialize<QobuzMetadata>(json, options)!;
            }
            catch
            {
                // If metadata is corrupted, return null
                return null;
            }
        }

        /// <summary>
        /// Checks if metadata exists for a given music file
        /// </summary>
        public static bool Exists(string musicFilePath)
        {
            var metadataPath = GetMetadataPath(musicFilePath);
            return System.IO.File.Exists(metadataPath);
        }
    }

    public class QualityInfo
    {
        [JsonPropertyName("format")]
        public string Format { get; set; } = ""; // FLAC, MP3, etc.

        [JsonPropertyName("bitDepth")]
        public int BitDepth { get; set; } // 16, 24

        [JsonPropertyName("sampleRate")]
        public int SampleRate { get; set; } // 44100, 96000, 192000

        [JsonPropertyName("bitrate")]
        public int Bitrate { get; set; } // For lossy formats (MP3)

        [JsonPropertyName("qualityScore")]
        public int QualityScore { get; set; } // Calculated score for comparison

        /// <summary>
        /// Calculates a quality score for comparison purposes
        /// Higher score = better quality
        /// </summary>
        public static int CalculateQualityScore(string format, int bitDepth, int sampleRate, int bitrate = 0)
        {
            // Base format score (0-1000)
            int baseScore = format?.ToUpperInvariant() switch
            {
                "FLAC" => 1000,
                "ALAC" => 950,
                "WAV" => 900,
                "AIFF" => 900,
                "MP3" => 300,
                "AAC" => 350,
                "M4A" => 350,
                "OGG" => 400,
                "OPUS" => 450,
                _ => 100
            };

            // For lossless formats, add bit depth and sample rate
            if (baseScore >= 900)
            {
                baseScore += bitDepth * 10;  // 16-bit = 160, 24-bit = 240
                baseScore += sampleRate / 100; // 44100 = 441, 192000 = 1920
            }
            // For lossy formats, use bitrate
            else
            {
                baseScore += Math.Min(bitrate, 500); // Cap at 500 to prevent unrealistic scores
            }

            return baseScore;
        }

        /// <summary>
        /// Creates a user-friendly quality label
        /// </summary>
        public string GetQualityLabel()
        {
            if (Format?.ToUpperInvariant() == "FLAC" || Format?.ToUpperInvariant() == "ALAC")
            {
                if (BitDepth >= 24 && SampleRate >= 96000)
                    return $"Hi-Res {BitDepth}-bit/{SampleRate / 1000}kHz";
                else if (BitDepth == 16 && SampleRate == 44100)
                    return "CD Quality";
                else
                    return $"{BitDepth}-bit/{SampleRate / 1000}kHz";
            }
            else if (!string.IsNullOrEmpty(Format))
            {
                return $"{Format.ToUpper()} {Bitrate}kbps";
            }

            return "Unknown";
        }
    }

    public class FileMetadata
    {
        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("expectedSize")]
        public long ExpectedSize { get; set; }

        [JsonPropertyName("checksum")]
        public string Checksum { get; set; } = ""; // Quick hash of first+last 1KB

        /// <summary>
        /// Checks if the file size indicates a complete download
        /// </summary>
        public bool IsSizeComplete(double tolerance = 0.05)
        {
            if (ExpectedSize <= 0) return true; // No expected size to compare

            var difference = Math.Abs(Size - ExpectedSize);
            var toleranceBytes = ExpectedSize * tolerance;

            return difference <= toleranceBytes;
        }
    }
}
