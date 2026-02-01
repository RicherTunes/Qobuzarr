using Newtonsoft.Json;

namespace QobuzCLI.Models.Configuration
{
    /// <summary>
    /// Configuration settings for download behavior, file organization, and concurrency.
    /// </summary>
    public class DownloadConfig
    {
        [JsonProperty("outputDirectory")]
        public string OutputDirectory { get; set; } = "./Downloads";

        [JsonProperty("maxConcurrentDownloads")]
        public int MaxConcurrentDownloads { get; set; } = 8; // Increased from 4 for better throughput

        [JsonProperty("maxConcurrentApiRequests")]
        public int MaxConcurrentApiRequests { get; set; } = 16; // Increased from 8 for higher API throughput

        [JsonProperty("maxConcurrentSearches")]
        public int MaxConcurrentSearches { get; set; } = 6; // Increased from 4 for batch operations

        [JsonProperty("maxConcurrentArtistAlbums")]
        public int MaxConcurrentArtistAlbums { get; set; } = 2;

        [JsonProperty("createArtistFolders")]
        public bool CreateArtistFolders { get; set; } = true;

        [JsonProperty("createAlbumFolders")]
        public bool CreateAlbumFolders { get; set; } = true;

        [JsonProperty("fileNamingPattern")]
        public string FileNamingPattern { get; set; } = "{track:00} - {title}";

        [JsonProperty("albumFolderPattern")]
        public string AlbumFolderPattern { get; set; } = "{artist} - {album} ({year})";

        [JsonProperty("enableMetadataTagging")]
        public bool EnableMetadataTagging { get; set; } = true;

        [JsonProperty("validateDownloads")]
        public bool ValidateDownloads { get; set; } = true;

        [JsonProperty("partialSizeTolerancePercent")]
        public double PartialSizeTolerancePercent { get; set; } = 5.0;

        // Existing file handling strategy: suffix, skip, overwrite
        [JsonProperty("existingFileBehavior")]
        public string ExistingFileBehavior { get; set; } = "overwrite";

        /// <summary>
        /// Validates that the output directory is accessible
        /// </summary>
        public bool IsValidOutputDirectory()
        {
            try
            {
                return !string.IsNullOrEmpty(OutputDirectory) &&
                       (Directory.Exists(OutputDirectory) || Directory.CreateDirectory(OutputDirectory) != null);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ensures concurrency settings are within reasonable bounds
        /// </summary>
        public void ValidateConcurrencySettings()
        {
            MaxConcurrentDownloads = Math.Max(1, Math.Min(20, MaxConcurrentDownloads));
            MaxConcurrentApiRequests = Math.Max(1, Math.Min(50, MaxConcurrentApiRequests));
            MaxConcurrentSearches = Math.Max(1, Math.Min(20, MaxConcurrentSearches));
            MaxConcurrentArtistAlbums = Math.Max(1, Math.Min(10, MaxConcurrentArtistAlbums));
        }
    }
}
