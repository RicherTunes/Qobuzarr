using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Metadata stored alongside downloaded music files to track quality, completeness, and enable smart duplicate detection
    /// </summary>
    public class QobuzDownloadMetadata
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
        public QobuzQualityInfo Quality { get; set; } = new();

        [JsonPropertyName("file")]
        public QobuzFileMetadata File { get; set; } = new();

        [JsonPropertyName("source")]
        public string Source { get; set; } = "Lidarr Qobuzarr Plugin";

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
            if (!string.IsNullOrEmpty(metadataDir))
            {
                Directory.CreateDirectory(metadataDir);
            }

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
        public static async Task<QobuzDownloadMetadata?> LoadAsync(string musicFilePath)
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
                return JsonSerializer.Deserialize<QobuzDownloadMetadata>(json, options);
            }
            catch (JsonException ex)
            {
                // Metadata file is corrupted or has invalid JSON format
                NLog.LogManager.GetCurrentClassLogger().Warn(ex, "Failed to deserialize metadata from {0}: {1}", metadataPath, ex.Message);
                return null;
            }
            catch (FileNotFoundException)
            {
                // Metadata file doesn't exist - this is expected for new downloads
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                // Permission issues reading metadata file
                NLog.LogManager.GetCurrentClassLogger().Error(ex, "Access denied reading metadata from {0}", metadataPath);
                return null;
            }
            catch (IOException ex)
            {
                // Other I/O issues (file locked, disk issues, etc.)
                NLog.LogManager.GetCurrentClassLogger().Error(ex, "I/O error reading metadata from {0}: {1}", metadataPath, ex.Message);
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

    public class QobuzQualityInfo
    {
        [JsonPropertyName("format")]
        public string Format { get; set; } = "";

        [JsonPropertyName("formatId")]
        public int FormatId { get; set; }

        [JsonPropertyName("bitDepth")]
        public int BitDepth { get; set; }

        [JsonPropertyName("sampleRate")]
        public int SampleRate { get; set; }

        [JsonPropertyName("bitrate")]
        public int Bitrate { get; set; }

        [JsonPropertyName("qualityDescription")]
        public string QualityDescription { get; set; } = "";

        /// <summary>
        /// Creates quality info from Qobuz format ID
        /// </summary>
        public static QobuzQualityInfo FromFormatId(int formatId)
        {
            return formatId switch
            {
                5 => new QobuzQualityInfo 
                { 
                    FormatId = 5, 
                    Format = "MP3", 
                    Bitrate = 320, 
                    QualityDescription = "MP3 320kbps" 
                },
                6 => new QobuzQualityInfo 
                { 
                    FormatId = 6, 
                    Format = "FLAC", 
                    BitDepth = 16, 
                    SampleRate = 44100, 
                    QualityDescription = "FLAC CD Quality" 
                },
                7 => new QobuzQualityInfo 
                { 
                    FormatId = 7, 
                    Format = "FLAC", 
                    BitDepth = 24, 
                    SampleRate = 96000, 
                    QualityDescription = "FLAC Hi-Res 24bit/96kHz" 
                },
                27 => new QobuzQualityInfo 
                { 
                    FormatId = 27, 
                    Format = "FLAC", 
                    BitDepth = 24, 
                    SampleRate = 192000, 
                    QualityDescription = "FLAC Hi-Res 24bit/192kHz" 
                },
                _ => new QobuzQualityInfo 
                { 
                    FormatId = formatId, 
                    Format = "Unknown", 
                    QualityDescription = $"Unknown Format (ID: {formatId})" 
                }
            };
        }
    }

    public class QobuzFileMetadata
    {
        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("extension")]
        public string Extension { get; set; } = "";

        /// <summary>
        /// Updates file metadata from actual file
        /// </summary>
        public void UpdateFromFile(string filePath)
        {
            if (System.IO.File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                Size = fileInfo.Length;
                Path = filePath;
                Extension = fileInfo.Extension;
            }
        }
    }
}