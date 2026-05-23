using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Generates ML training data from real album datasets to create pre-trained baseline models
    /// Uses rule-based classification to create labeled training data from 100k+ real albums
    /// </summary>
    public class MLTrainingDataGenerator
    {
        private readonly Logger _logger;
        private readonly QueryComplexityClassifier _classifier;

        public MLTrainingDataGenerator(Logger logger = null)
        {
            _logger = logger;
            _classifier = new QueryComplexityClassifier();
        }

        /// <summary>
        /// Loads album data from all.json and generates training patterns
        /// Creates baseline training dataset from real Lidarr album data
        /// </summary>
        /// <param name="allJsonPath">Path to all.json file containing 100k+ albums</param>
        /// <param name="sampleSize">Number of albums to sample for training (default: 10000)</param>
        /// <returns>List of QueryPattern training data</returns>
        public async Task<List<QueryPattern>> GenerateTrainingDataFromDatasetAsync(string allJsonPath, int sampleSize = 10000)
        {
            _logger?.Info("Generating ML training data from dataset: {0} (sample size: {1})", allJsonPath, sampleSize);

            if (!File.Exists(allJsonPath))
            {
                _logger?.Error("Dataset file not found: {0}", allJsonPath);
                return GetFallbackTrainingData();
            }

            try
            {
                var jsonContent = await File.ReadAllTextAsync(allJsonPath);
                var dataset = JsonSerializer.Deserialize<AlbumDataset>(jsonContent);

                if (dataset?.Albums == null || !dataset.Albums.Any())
                {
                    _logger?.Warn("No albums found in dataset, using fallback training data");
                    return GetFallbackTrainingData();
                }

                return GenerateTrainingPatternsFromAlbums(dataset.Albums, sampleSize);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to load dataset from {0}, using fallback training data", allJsonPath);
                return GetFallbackTrainingData();
            }
        }

        /// <summary>
        /// Generates QueryPattern training data from album list
        /// Uses rule-based classifier to label the data for ML training
        /// </summary>
        private List<QueryPattern> GenerateTrainingPatternsFromAlbums(List<AlbumData> albums, int sampleSize)
        {
            var patterns = new List<QueryPattern>();
            var random = new Random(42); // Fixed seed for reproducible results

            // Sample albums to avoid overwhelming the ML training
            var sampledAlbums = albums
                .OrderBy(x => random.Next())
                .Take(Math.Min(sampleSize, albums.Count))
                .ToList();

            _logger?.Info("Processing {0} albums for training pattern generation", sampledAlbums.Count);

            var complexityDistribution = new Dictionary<QueryComplexity, int>
            {
                { QueryComplexity.Simple, 0 },
                { QueryComplexity.Medium, 0 },
                { QueryComplexity.Complex, 0 }
            };

            foreach (var album in sampledAlbums)
            {
                try
                {
                    var artist = CleanString(album.ArtistName);
                    var albumTitle = CleanString(album.AlbumTitle);

                    // Skip invalid entries
                    if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(albumTitle))
                        continue;

                    // Use rule-based classifier to create labels
                    var complexity = _classifier.ClassifyComplexity(artist, albumTitle);
                    complexityDistribution[complexity]++;

                    patterns.Add(new QueryPattern
                    {
                        Artist = artist,
                        Album = albumTitle,
                        ActualComplexity = complexity.ToString(),
                        Features = ExtractFeatures(artist, albumTitle)
                    });
                }
                catch (Exception ex)
                {
                    _logger?.Warn(ex, "Failed to process album: {0} - {1}", album.ArtistName, album.AlbumTitle);
                }
            }

            _logger?.Info("Generated {0} training patterns. Distribution: Simple={1}, Medium={2}, Complex={3}",
                patterns.Count,
                complexityDistribution[QueryComplexity.Simple],
                complexityDistribution[QueryComplexity.Medium],
                complexityDistribution[QueryComplexity.Complex]);

            return patterns;
        }

        /// <summary>
        /// Extracts feature vector for ML training
        /// Same feature extraction as used in PatternLearningEngine
        /// </summary>
        private float[] ExtractFeatures(string artist, string album)
        {
            var features = new float[25];

            // Length features
            features[0] = artist?.Length ?? 0;
            features[1] = album?.Length ?? 0;
            features[2] = artist?.Split(' ').Length ?? 0;
            features[3] = album?.Split(' ').Length ?? 0;

            // Character analysis
            features[4] = CountSpecialChars(artist);
            features[5] = CountSpecialChars(album);
            features[6] = CountNumbers(artist);
            features[7] = CountNumbers(album);
            features[8] = HasPattern(album, "remaster") ? 1 : 0;
            features[9] = HasPattern(album, "deluxe") ? 1 : 0;
            features[10] = HasPattern(album, "edition") ? 1 : 0;
            features[11] = HasPattern(album, "live") ? 1 : 0;
            features[12] = HasPattern(album, "greatest hits") ? 1 : 0;
            features[13] = HasPattern(artist, "various") ? 1 : 0;
            features[14] = HasPattern(album, "soundtrack") ? 1 : 0;
            features[15] = HasPattern(album, "vol") ? 1 : 0;
            features[16] = HasPattern(album, "part") ? 1 : 0;
            features[17] = HasPattern(artist, "feat") ? 1 : 0;
            features[18] = HasPattern(album, "anniversary") ? 1 : 0;
            features[19] = HasNonAscii(artist) ? 1 : 0;
            features[20] = HasNonAscii(album) ? 1 : 0;
            features[21] = CountPunctuation(artist);
            features[22] = CountPunctuation(album);
            features[23] = (artist?.Length ?? 0) > 50 ? 1 : 0;
            features[24] = (album?.Length ?? 0) > 50 ? 1 : 0;

            return features;
        }

        /// <summary>
        /// Fallback training data when dataset loading fails
        /// Provides minimal baseline patterns for ML training
        /// </summary>
        private List<QueryPattern> GetFallbackTrainingData()
        {
            _logger?.Info("Using fallback training data (20 baseline patterns)");

            return new List<QueryPattern>
            {
                // Simple cases
                new() { Artist = "Taylor Swift", Album = "1989", ActualComplexity = "Simple" },
                new() { Artist = "The Beatles", Album = "Abbey Road", ActualComplexity = "Simple" },
                new() { Artist = "Pink Floyd", Album = "The Wall", ActualComplexity = "Simple" },
                new() { Artist = "Queen", Album = "Bohemian Rhapsody", ActualComplexity = "Simple" },
                new() { Artist = "Metallica", Album = "Master of Puppets", ActualComplexity = "Simple" },
                new() { Artist = "Nirvana", Album = "Nevermind", ActualComplexity = "Simple" },
                new() { Artist = "Led Zeppelin", Album = "IV", ActualComplexity = "Simple" },
                new() { Artist = "Radiohead", Album = "OK Computer", ActualComplexity = "Simple" },
                new() { Artist = "The Rolling Stones", Album = "Sticky Fingers", ActualComplexity = "Simple" },
                new() { Artist = "Bob Dylan", Album = "Highway 61 Revisited", ActualComplexity = "Simple" },

                // Medium cases
                new() { Artist = "AC/DC", Album = "Back in Black", ActualComplexity = "Medium" },
                new() { Artist = "Guns N' Roses", Album = "Appetite for Destruction", ActualComplexity = "Medium" },
                new() { Artist = "The Who", Album = "Who's Next", ActualComplexity = "Medium" },
                new() { Artist = "Black Sabbath", Album = "Paranoid", ActualComplexity = "Medium" },

                // Complex cases
                new() { Artist = "Various Artists", Album = "Now That's What I Call Music! 85", ActualComplexity = "Complex" },
                new() { Artist = "Björk", Album = "Homogenic", ActualComplexity = "Complex" },
                new() { Artist = "Sigur Rós", Album = "Ágætis byrjun", ActualComplexity = "Complex" },
                new() { Artist = "Johnny Cash", Album = "At Folsom Prison (Live)", ActualComplexity = "Complex" },
                new() { Artist = "Miles Davis", Album = "Kind of Blue (Remastered)", ActualComplexity = "Complex" },
                new() { Artist = "Soundtrack", Album = "Guardians of the Galaxy: Awesome Mix Vol. 1", ActualComplexity = "Complex" }
            };
        }

        private string CleanString(string input)
        {
            return input?.Trim() ?? "";
        }

        private int CountSpecialChars(string input)
        {
            if (string.IsNullOrEmpty(input)) return 0;
            return input.Count(c => "[&+/\\-:'\"()]".Contains(c));
        }

        private int CountNumbers(string input)
        {
            if (string.IsNullOrEmpty(input)) return 0;
            return input.Count(char.IsDigit);
        }

        private int CountPunctuation(string input)
        {
            if (string.IsNullOrEmpty(input)) return 0;
            return input.Count(char.IsPunctuation);
        }

        private bool HasPattern(string input, string pattern)
        {
            if (string.IsNullOrEmpty(input)) return false;
            return input.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        private bool HasNonAscii(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            return input.Any(c => c > 127);
        }
    }

    /// <summary>
    /// Data structure for deserializing all.json album dataset
    /// </summary>
    public class AlbumDataset
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("source")]
        public string Source { get; set; }

        [JsonPropertyName("total_albums")]
        public int TotalAlbums { get; set; }

        [JsonPropertyName("albums")]
        public List<AlbumData> Albums { get; set; }
    }

    /// <summary>
    /// Individual album data from dataset
    /// </summary>
    public class AlbumData
    {
        [JsonPropertyName("lidarr_id")]
        public int LidarrId { get; set; }

        [JsonPropertyName("artist_name")]
        public string ArtistName { get; set; }

        [JsonPropertyName("artist_id")]
        public int ArtistId { get; set; }

        [JsonPropertyName("album_title")]
        public string AlbumTitle { get; set; }

        [JsonPropertyName("album_title_clean")]
        public string AlbumTitleClean { get; set; }

        [JsonPropertyName("album_type")]
        public string AlbumType { get; set; }

        [JsonPropertyName("release_date")]
        public string ReleaseDate { get; set; }

        [JsonPropertyName("release_year")]
        public string ReleaseYear { get; set; }

        [JsonPropertyName("track_count")]
        public int TrackCount { get; set; }

        [JsonPropertyName("monitored")]
        public bool Monitored { get; set; }

        [JsonPropertyName("search_query")]
        public string SearchQuery { get; set; }

        [JsonPropertyName("disambiguation")]
        public string Disambiguation { get; set; }

        [JsonPropertyName("foreign_album_id")]
        public string ForeignAlbumId { get; set; }

        [JsonPropertyName("genres")]
        public List<string> Genres { get; set; }

        [JsonPropertyName("ratings")]
        public AlbumRatings Ratings { get; set; }

        [JsonPropertyName("overview")]
        public string Overview { get; set; }

        [JsonPropertyName("album_id")]
        public int AlbumId { get; set; }

        [JsonPropertyName("artist_metadata_id")]
        public int ArtistMetadataId { get; set; }
    }

    public class AlbumRatings
    {
        [JsonPropertyName("votes")]
        public int Votes { get; set; }

        [JsonPropertyName("value")]
        public double Value { get; set; }
    }
}
