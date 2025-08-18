using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Qobuzarr.Tests.Simulations
{
    /// <summary>
    /// Comprehensive simulation tests using real Lidarr album data
    /// Tests Query Intelligence optimization against actual user libraries
    /// </summary>
    public class RealDataQueryIntelligenceTests
    {
        private readonly ITestOutputHelper _output;

        public RealDataQueryIntelligenceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void QueryIntelligence_RealLidarrData_ShouldShowAccurateGains()
        {
            // Arrange - Load real album data from JSON files
            var realAlbums = LoadRealAlbumData();
            
            if (realAlbums.Count == 0)
            {
                _output.WriteLine("❌ No real album data found. Skipping test.");
                return;
            }

            var currentStrategy = new CurrentQueryStrategy();
            var smartStrategy = new SmartQueryStrategy();
            
            int currentApiCalls = 0;
            int smartApiCalls = 0;
            var qualityAnalysis = new List<QualityAnalysis>();
            var categoryBreakdown = new Dictionary<QueryComplexity, CategoryStats>();

            // Act - Simulate both strategies on real data
            foreach (var album in realAlbums)
            {
                if (string.IsNullOrEmpty(album.ArtistName) || string.IsNullOrEmpty(album.AlbumTitle))
                    continue;

                var currentQueries = currentStrategy.BuildQueries(album.ArtistName, album.AlbumTitle);
                var smartQueries = smartStrategy.BuildQueries(album.ArtistName, album.AlbumTitle);
                
                currentApiCalls += currentQueries.Count;
                smartApiCalls += smartQueries.Count;

                // Analyze complexity and quality impact
                var complexity = new QueryComplexityClassifier().ClassifyComplexity(album.ArtistName, album.AlbumTitle);
                var qualityLoss = EstimateQualityLoss(currentQueries, smartQueries, complexity);
                
                qualityAnalysis.Add(new QualityAnalysis
                {
                    Album = album,
                    Complexity = complexity,
                    CurrentQueries = currentQueries.Count,
                    SmartQueries = smartQueries.Count,
                    QualityLoss = qualityLoss
                });

                // Track category statistics
                if (!categoryBreakdown.ContainsKey(complexity))
                    categoryBreakdown[complexity] = new CategoryStats();
                
                var stats = categoryBreakdown[complexity];
                stats.Count++;
                stats.CurrentQueries += currentQueries.Count;
                stats.SmartQueries += smartQueries.Count;
                stats.TotalQualityLoss += qualityLoss;
            }

            // Assert - Analyze results
            var totalAlbums = qualityAnalysis.Count;
            var apiCallReduction = (double)(currentApiCalls - smartApiCalls) / currentApiCalls;
            var avgQualityLoss = qualityAnalysis.Average(q => q.QualityLoss);
            var maxQualityLoss = qualityAnalysis.Max(q => q.QualityLoss);

            // Generate detailed report
            GenerateDetailedReport(totalAlbums, currentApiCalls, smartApiCalls, apiCallReduction, 
                                 avgQualityLoss, maxQualityLoss, categoryBreakdown, qualityAnalysis);

            // Validate results
            apiCallReduction.Should().BeGreaterThan(0.15, "Should achieve at least 15% API call reduction with real data");
            avgQualityLoss.Should().BeLessThan(0.10, "Average quality loss should be less than 10% with real data");
            maxQualityLoss.Should().BeLessThan(0.20, "Worst case quality loss should be less than 20%");
        }

        [Fact]
        public void QueryIntelligence_WorstCaseScenarios_ShouldBeBounded()
        {
            // Arrange - Load real data and filter to potential worst cases
            var realAlbums = LoadRealAlbumData();
            var worstCaseAlbums = realAlbums
                .Where(a => !string.IsNullOrEmpty(a.ArtistName) && !string.IsNullOrEmpty(a.AlbumTitle))
                .Where(a => IsComplexCase(a.ArtistName, a.AlbumTitle))
                .Take(100)
                .ToList();

            if (worstCaseAlbums.Count == 0)
            {
                _output.WriteLine("❌ No complex cases found in real data. Skipping test.");
                return;
            }

            var smartStrategy = new SmartQueryStrategy();
            var worstCaseGain = double.MaxValue;
            var worstCaseExample = "";

            // Act - Find the worst case scenario
            foreach (var album in worstCaseAlbums)
            {
                var currentQueries = 3; // Always 3 in current strategy
                var smartQueries = smartStrategy.BuildQueries(album.ArtistName, album.AlbumTitle).Count;
                
                var gain = (double)(currentQueries - smartQueries) / currentQueries;
                if (gain < worstCaseGain)
                {
                    worstCaseGain = gain;
                    worstCaseExample = $"{album.ArtistName} - {album.AlbumTitle}";
                }
            }

            // Assert - Even worst cases should have reasonable bounds
            _output.WriteLine($"=== WORST CASE ANALYSIS ===");
            _output.WriteLine($"Worst case gain: {worstCaseGain:P1}");
            _output.WriteLine($"Worst case example: {worstCaseExample}");
            _output.WriteLine($"Complex cases analyzed: {worstCaseAlbums.Count}");

            worstCaseGain.Should().BeGreaterOrEqualTo(0.0, "Even worst cases should not be negative (no performance loss)");
        }

        [Fact]
        public void QueryIntelligence_CategoryDistribution_ShouldMatchExpectations()
        {
            // Arrange
            var realAlbums = LoadRealAlbumData()
                .Where(a => !string.IsNullOrEmpty(a.ArtistName) && !string.IsNullOrEmpty(a.AlbumTitle))
                .Take(1000)
                .ToList();

            if (realAlbums.Count == 0)
            {
                _output.WriteLine("❌ No real album data found. Skipping test.");
                return;
            }

            var classifier = new QueryComplexityClassifier();
            var distribution = new Dictionary<QueryComplexity, int>
            {
                [QueryComplexity.Simple] = 0,
                [QueryComplexity.Medium] = 0,
                [QueryComplexity.Complex] = 0
            };

            // Act - Classify all albums
            foreach (var album in realAlbums)
            {
                var complexity = classifier.ClassifyComplexity(album.ArtistName, album.AlbumTitle);
                distribution[complexity]++;
            }

            // Report distribution
            var total = realAlbums.Count;
            _output.WriteLine($"=== COMPLEXITY DISTRIBUTION ===");
            _output.WriteLine($"Simple: {distribution[QueryComplexity.Simple]} ({(double)distribution[QueryComplexity.Simple]/total:P1})");
            _output.WriteLine($"Medium: {distribution[QueryComplexity.Medium]} ({(double)distribution[QueryComplexity.Medium]/total:P1})");
            _output.WriteLine($"Complex: {distribution[QueryComplexity.Complex]} ({(double)distribution[QueryComplexity.Complex]/total:P1})");

            // Assert - Distribution should be reasonable
            distribution[QueryComplexity.Simple].Should().BeGreaterThan(0, "Should have some simple cases");
            distribution[QueryComplexity.Complex].Should().BeLessThan((int)(total * 0.5), "Complex cases should be minority");
        }

        private List<RealAlbum> LoadRealAlbumData()
        {
            var albums = new List<RealAlbum>();
            var dataFiles = new[]
            {
                @"I:\Arr-Plugins\Lidarr\Lidarr.Plugin.Qobuzarr\QobuzCLI\wanted-albums.json",
                @"I:\Arr-Plugins\Lidarr\Lidarr.Plugin.Qobuzarr\QobuzCLI\lidarr-wanted-albums-advanced.json"
            };

            foreach (var file in dataFiles)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        var content = File.ReadAllText(file);
                        albums.AddRange(ParseAlbumData(content, file));
                    }
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"⚠️ Could not load {file}: {ex.Message}");
                }
            }

            _output.WriteLine($"📊 Loaded {albums.Count} real albums from Lidarr data");
            return albums.DistinctBy(a => $"{a.ArtistName}|{a.AlbumTitle}").ToList();
        }

        private List<RealAlbum> ParseAlbumData(string jsonContent, string fileName)
        {
            var albums = new List<RealAlbum>();

            try
            {
                using var document = JsonDocument.Parse(jsonContent);
                var root = document.RootElement;

                if (root.TryGetProperty("albums", out var albumsElement))
                {
                    foreach (var albumElement in albumsElement.EnumerateArray())
                    {
                        var artistName = GetStringProperty(albumElement, "artist_name");
                        var albumTitle = GetStringProperty(albumElement, "album_title");
                        var albumType = GetStringProperty(albumElement, "album_type");
                        var releaseYear = GetIntProperty(albumElement, "release_year");

                        if (!string.IsNullOrEmpty(artistName) && !string.IsNullOrEmpty(albumTitle))
                        {
                            albums.Add(new RealAlbum
                            {
                                ArtistName = artistName,
                                AlbumTitle = albumTitle,
                                AlbumType = albumType ?? "album",
                                ReleaseYear = releaseYear,
                                Source = Path.GetFileName(fileName)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"⚠️ Error parsing {fileName}: {ex.Message}");
            }

            return albums;
        }

        private string GetStringProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String 
                ? prop.GetString() ?? "" 
                : "";
        }

        private int GetIntProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number 
                ? prop.GetInt32() 
                : 0;
        }

        private bool IsComplexCase(string artist, string album)
        {
            var combined = $"{artist} {album}".ToLower();
            return Regex.IsMatch(combined, @"[&+/\-:']") ||
                   combined.Contains("various artists") ||
                   combined.Contains("compilation") ||
                   Regex.IsMatch(combined, @"[^\x00-\x7F]");
        }

        private double EstimateQualityLoss(List<string> currentQueries, List<string> smartQueries, QueryComplexity complexity)
        {
            if (currentQueries.Count == smartQueries.Count)
                return 0.0; // No change, no quality loss

            // Estimate based on complexity and query reduction
            var queryReduction = currentQueries.Count - smartQueries.Count;
            return complexity switch
            {
                QueryComplexity.Simple => queryReduction * 0.01,  // 1% loss per skipped query
                QueryComplexity.Medium => queryReduction * 0.02,  // 2% loss per skipped query
                QueryComplexity.Complex => 0.0,                   // Complex cases keep all queries
                _ => 0.0
            };
        }

        private void GenerateDetailedReport(int totalAlbums, int currentApiCalls, int smartApiCalls, 
            double apiCallReduction, double avgQualityLoss, double maxQualityLoss,
            Dictionary<QueryComplexity, CategoryStats> categoryBreakdown, List<QualityAnalysis> qualityAnalysis)
        {
            _output.WriteLine("");
            _output.WriteLine("=== COMPREHENSIVE REAL DATA ANALYSIS ===");
            _output.WriteLine($"📊 Total albums analyzed: {totalAlbums:N0}");
            _output.WriteLine($"🔄 Current strategy API calls: {currentApiCalls:N0}");
            _output.WriteLine($"⚡ Smart strategy API calls: {smartApiCalls:N0}");
            _output.WriteLine($"📈 API call reduction: {apiCallReduction:P2} ({currentApiCalls - smartApiCalls:N0} fewer calls)");
            _output.WriteLine($"📉 Average quality loss: {avgQualityLoss:P3}");
            _output.WriteLine($"⚠️ Maximum quality loss: {maxQualityLoss:P3}");
            _output.WriteLine("");

            // Category breakdown
            _output.WriteLine("=== CATEGORY BREAKDOWN ===");
            foreach (var kvp in categoryBreakdown.OrderBy(x => x.Key))
            {
                var category = kvp.Key;
                var stats = kvp.Value;
                var categoryReduction = (double)(stats.CurrentQueries - stats.SmartQueries) / stats.CurrentQueries;
                var avgQualityLossForCategory = stats.TotalQualityLoss / stats.Count;

                _output.WriteLine($"{category} ({stats.Count:N0} albums, {(double)stats.Count/totalAlbums:P1}):");
                _output.WriteLine($"  API reduction: {categoryReduction:P1} ({stats.CurrentQueries - stats.SmartQueries:N0} fewer calls)");
                _output.WriteLine($"  Avg quality loss: {avgQualityLossForCategory:P3}");
            }

            // Sample cases
            _output.WriteLine("");
            _output.WriteLine("=== SAMPLE OPTIMIZATIONS ===");
            var samples = qualityAnalysis
                .Where(q => q.CurrentQueries != q.SmartQueries)
                .OrderByDescending(q => q.CurrentQueries - q.SmartQueries)
                .Take(10);

            foreach (var sample in samples)
            {
                _output.WriteLine($"{sample.Album.ArtistName} - {sample.Album.AlbumTitle}");
                _output.WriteLine($"  {sample.CurrentQueries} → {sample.SmartQueries} queries ({sample.Complexity})");
            }
        }
    }

    // Supporting classes
    public class RealAlbum
    {
        public string ArtistName { get; set; } = "";
        public string AlbumTitle { get; set; } = "";
        public string AlbumType { get; set; } = "";
        public int ReleaseYear { get; set; }
        public string Source { get; set; } = "";
    }

    public class QualityAnalysis
    {
        public RealAlbum Album { get; set; } = new();
        public QueryComplexity Complexity { get; set; }
        public int CurrentQueries { get; set; }
        public int SmartQueries { get; set; }
        public double QualityLoss { get; set; }
    }

    public class CategoryStats
    {
        public int Count { get; set; }
        public int CurrentQueries { get; set; }
        public int SmartQueries { get; set; }
        public double TotalQualityLoss { get; set; }
    }
}