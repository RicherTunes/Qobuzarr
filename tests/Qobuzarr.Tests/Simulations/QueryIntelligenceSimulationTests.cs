using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Qobuzarr.Tests.Simulations
{
    /// <summary>
    /// Simulation tests to validate Query Intelligence optimization potential
    /// These tests analyze query patterns to predict performance gains before implementation
    /// </summary>
    [Trait("Category", "Simulations")]
    public class QueryIntelligenceSimulationTests
    {
        private readonly ITestOutputHelper _output;

        public QueryIntelligenceSimulationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void QueryIntelligence_RealWorldSearchPatterns_ShouldShowSignificantGains()
        {
            // Arrange - Real world search patterns from typical user behavior
            var searchScenarios = GetRealWorldSearchScenarios();
            var maxSimEnv = Environment.GetEnvironmentVariable("QOBUZ_TEST_MAX_SIM_SCENARIOS");
            if (int.TryParse(maxSimEnv, out var maxSim) && maxSim > 0)
            {
                searchScenarios = searchScenarios.Take(maxSim).ToList();
                _output.WriteLine($"Limiting simulation scenarios to: {maxSim}");
            }
            
            var currentStrategy = new CurrentQueryStrategy();
            var smartStrategy = new SmartQueryStrategy();
            
            int currentApiCalls = 0;
            int smartApiCalls = 0;
            var qualityComparison = new List<QualityComparisonResult>();

            // Act - Simulate both strategies
            foreach (var scenario in searchScenarios)
            {
                var currentQueries = currentStrategy.BuildQueries(scenario.Artist, scenario.Album);
                var smartQueries = smartStrategy.BuildQueries(scenario.Artist, scenario.Album);
                
                currentApiCalls += currentQueries.Count;
                smartApiCalls += smartQueries.Count;
                
                // Simulate quality impact
                var qualityResult = SimulateQualityImpact(currentQueries, smartQueries, scenario);
                qualityComparison.Add(qualityResult);
                
                _output.WriteLine($"Scenario: {scenario.Artist} - {scenario.Album}");
                _output.WriteLine($"  Current: {currentQueries.Count} queries, Smart: {smartQueries.Count} queries");
                _output.WriteLine($"  Quality Impact: {qualityResult.QualityLoss:P1}");
                _output.WriteLine("");
            }

            // Assert - Validate performance gains
            var apiCallReduction = (double)(currentApiCalls - smartApiCalls) / currentApiCalls;
            var avgQualityLoss = qualityComparison.Average(q => q.QualityLoss);
            var worstQualityLoss = qualityComparison.Max(q => q.QualityLoss);

            _output.WriteLine("=== SIMULATION RESULTS ===");
            _output.WriteLine($"Total scenarios tested: {searchScenarios.Count}");
            _output.WriteLine($"Current strategy API calls: {currentApiCalls}");
            _output.WriteLine($"Smart strategy API calls: {smartApiCalls}");
            _output.WriteLine($"API call reduction: {apiCallReduction:P1}");
            _output.WriteLine($"Average quality loss: {avgQualityLoss:P2}");
            _output.WriteLine($"Worst case quality loss: {worstQualityLoss:P2}");

            // Validate our success criteria
            apiCallReduction.Should().BeGreaterThan(0.3, "Should reduce API calls by at least 30%");
            avgQualityLoss.Should().BeLessThan(0.05, "Average quality loss should be less than 5%");
            worstQualityLoss.Should().BeLessThan(0.15, "Worst case quality loss should be less than 15%");
        }

        [Theory]
        [InlineData("Pink Floyd", "The Dark Side of the Moon", QueryComplexity.Simple)] // No special chars, well-known
        [InlineData("AC/DC", "Back in Black", QueryComplexity.Complex)] // Special chars
        [InlineData("Various Artists", "Now That's What I Call Music", QueryComplexity.Complex)] // Compilation
        [InlineData("The Beatles", "Abbey Road", QueryComplexity.Medium)] // "The" prefix
        [InlineData("Metallica", "Master of Puppets (Remastered)", QueryComplexity.Medium)] // Year/edition info
        [InlineData("Sigur Rós", "Ágætis byrjun", QueryComplexity.Complex)] // Unicode characters
        public void QueryComplexityClassification_ShouldMatchExpectedLevels(string artist, string album, QueryComplexity expected)
        {
            // Arrange
            var classifier = new QueryComplexityClassifier();

            // Act
            var actual = classifier.ClassifyComplexity(artist, album);

            // Assert
            actual.Should().Be(expected, $"Artist '{artist}' and Album '{album}' should be classified as {expected}");
        }

        [Fact]
        public void QueryStrategy_EdgeCases_ShouldHandleGracefully()
        {
            // Arrange
            var edgeCases = new[]
            {
                ("", ""), // Empty strings
                ("Artist", ""), // Missing album
                ("", "Album"), // Missing artist
                ("A", "B"), // Very short names
                ("Artist with very long name that might cause issues", "Album with equally long name that could be problematic"),
                ("Art!st w1th numb3rs & symb0ls", "Alb@m w1th $pecial ch@rs"),
                ("Artist", null), // Null values
                (null, "Album")
            };

            var strategy = new SmartQueryStrategy();

            // Act & Assert
            foreach (var (artist, album) in edgeCases)
            {
                var queries = strategy.BuildQueries(artist, album);
                
                // Should not crash and should return at least one query for valid inputs
                queries.Should().NotBeNull();
                if (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(album))
                {
                    queries.Should().NotBeEmpty($"Should generate queries for artist='{artist}', album='{album}'");
                }
            }
        }

        [Fact]
        public void QueryIntelligence_PerformanceMetrics_ShouldMeetTargets()
        {
            // Arrange
            var scenarios = GetPerformanceTestScenarios();
            var strategy = new SmartQueryStrategy();
            var startTime = DateTime.UtcNow;

            // Act
            var totalQueries = 0;
            foreach (var scenario in scenarios)
            {
                var queries = strategy.BuildQueries(scenario.Artist, scenario.Album);
                totalQueries += queries.Count;
            }

            var elapsed = DateTime.UtcNow - startTime;

            // Assert - Performance requirements
            elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100), "Query generation should be fast");
            
            // Calculate actual performance - should be around 50% reduction (1.5 queries per scenario on average)
            var averageQueriesPerScenario = (double)totalQueries / scenarios.Count;
            var reductionFromBaseline = (3.0 - averageQueriesPerScenario) / 3.0; // 3 is baseline queries per scenario
            
            averageQueriesPerScenario.Should().BeLessThan(2.5, "Should average significantly less than 2.5 queries per scenario");
            reductionFromBaseline.Should().BeGreaterThan(0.20, "Should achieve at least 20% reduction from baseline");
        }

        private List<SearchScenario> GetRealWorldSearchScenarios()
        {
            return new List<SearchScenario>
            {
                // Simple cases - should use single query
                new("Pink Floyd", "The Wall"),
                new("Led Zeppelin", "IV"),
                new("Queen", "Bohemian Rhapsody"),
                new("Metallica", "Master of Puppets"),
                
                // "The" prefix cases - should use optimized dual
                new("The Beatles", "Abbey Road"),
                new("The Rolling Stones", "Sticky Fingers"),
                new("The Who", "Who's Next"),
                
                // Special characters - should use all formats
                new("AC/DC", "Back in Black"),
                new("Guns N' Roses", "Appetite for Destruction"),
                new("Black Sabbath", "Paranoid"),
                
                // Compilations - should use all formats
                new("Various Artists", "Now That's What I Call Music 80"),
                new("Various Artists", "Greatest Hits of the 80s"),
                new("Various Artists", "The Best Rock Ballads Ever Collection"),
                
                // Years in titles - should use optimized dual
                new("Taylor Swift", "1989 (Taylor's Version)"),
                new("Chicago", "Chicago II (Remastered)"),
                new("Green Day", "American Idiot (Deluxe Edition)"),
                
                // Unicode/International - should use all formats
                new("Sigur Rós", "Ágætis byrjun"),
                new("Björk", "Homogenic"),
                new("Mötley Crüe", "Dr. Feelgood"),
                
                // Complex cases
                new("Twenty One Pilots", "Blurryface"),
                new("System of a Down", "Toxicity"),
                new("Panic! At The Disco", "Death of a Bachelor")
            };
        }

        private List<SearchScenario> GetPerformanceTestScenarios()
        {
            // Generate 1000 realistic scenarios for performance testing
            var scenarios = new List<SearchScenario>();
            var random = new Random(42); // Fixed seed for reproducible tests
            
            var artists = new[] { "Artist", "Band", "Singer", "Group", "The Band", "AC/DC", "Various Artists" };
            var albums = new[] { "Album", "Greatest Hits", "Collection", "Live Album", "Remastered", "Deluxe Edition" };
            
            for (int i = 0; i < 1000; i++)
            {
                var artist = artists[random.Next(artists.Length)] + i;
                var album = albums[random.Next(albums.Length)] + i;
                scenarios.Add(new SearchScenario(artist, album));
            }
            
            return scenarios;
        }

        private QualityComparisonResult SimulateQualityImpact(List<string> currentQueries, List<string> smartQueries, SearchScenario scenario)
        {
            // Simulate the quality impact of using fewer queries
            // This is a heuristic based on query diversity and complexity
            
            var qualityLoss = 0.0;
            
            // If we reduced from 3 to 1 query for a complex case, there's potential quality loss
            if (currentQueries.Count == 3 && smartQueries.Count == 1)
            {
                var complexity = new QueryComplexityClassifier().ClassifyComplexity(scenario.Artist, scenario.Album);
                qualityLoss = complexity switch
                {
                    QueryComplexity.Complex => 0.10, // 10% potential quality loss
                    QueryComplexity.Medium => 0.03,  // 3% potential quality loss
                    QueryComplexity.Simple => 0.01,  // 1% potential quality loss
                    _ => 0.0
                };
            }
            
            // If we kept the same number of queries, no quality loss
            if (currentQueries.Count == smartQueries.Count)
            {
                qualityLoss = 0.0;
            }
            
            return new QualityComparisonResult
            {
                Scenario = scenario,
                QualityLoss = qualityLoss,
                QueryReduction = currentQueries.Count - smartQueries.Count
            };
        }
    }

    // Supporting classes for simulation
    public class SearchScenario
    {
        public string Artist { get; }
        public string Album { get; }

        public SearchScenario(string artist, string album)
        {
            Artist = artist;
            Album = album;
        }
    }

    public enum QueryComplexity
    {
        Simple,
        Medium,
        Complex
    }

    public class QualityComparisonResult
    {
        public SearchScenario Scenario { get; set; }
        public double QualityLoss { get; set; }
        public int QueryReduction { get; set; }
    }

    public class CurrentQueryStrategy
    {
        public List<string> BuildQueries(string artist, string album)
        {
            // Simulate current behavior - always 3 queries
            if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(album))
                return new List<string>();
                
            return new List<string>
            {
                $"{artist} {album}",           // Primary query
                $"{artist} - {album}",         // Dash format
                $"\"{artist}\" {album}"        // Quoted artist
            };
        }
    }

    public class SmartQueryStrategy
    {
        private readonly QueryComplexityClassifier _classifier = new();

        public List<string> BuildQueries(string artist, string album)
        {
            if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(album))
                return new List<string>();

            var complexity = _classifier.ClassifyComplexity(artist, album);
            var queries = new List<string>();

            // Always include primary query
            queries.Add($"{artist} {album}");

            switch (complexity)
            {
                case QueryComplexity.Simple:
                    // Single query is sufficient
                    break;
                    
                case QueryComplexity.Medium:
                    // Add dash format for "The" prefix or year cases
                    queries.Add($"{artist} - {album}");
                    break;
                    
                case QueryComplexity.Complex:
                    // Use all query formats for complex cases
                    queries.Add($"{artist} - {album}");
                    queries.Add($"\"{artist}\" {album}");
                    break;
            }

            return queries;
        }
    }

    public class QueryComplexityClassifier
    {
        public QueryComplexity ClassifyComplexity(string artist, string album)
        {
            if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(album))
                return QueryComplexity.Complex; // Be conservative with edge cases

            var complexity = QueryComplexity.Simple;

            // Check for factors that increase complexity
            var combined = $"{artist} {album}".ToLower();

            // Special characters require multiple query formats
            if (Regex.IsMatch(combined, @"[&+/\-:']"))
                complexity = QueryComplexity.Complex;

            // Unicode characters are complex
            if (Regex.IsMatch(combined, @"[^\x00-\x7F]"))
                complexity = QueryComplexity.Complex;

            // Compilation indicators are complex
            if (combined.Contains("various artists") || 
                combined.Contains("greatest hits") || 
                combined.Contains("best of") ||
                combined.Contains("collection"))
                complexity = QueryComplexity.Complex;

            // "The" prefix requires special handling (medium complexity)
            if (artist.StartsWith("The ", StringComparison.OrdinalIgnoreCase) && complexity == QueryComplexity.Simple)
                complexity = QueryComplexity.Medium;

            // Year or edition info in title (case insensitive)
            if (Regex.IsMatch(album, @"\(\d{4}\)|remaster|deluxe|edition", RegexOptions.IgnoreCase) && complexity == QueryComplexity.Simple)
                complexity = QueryComplexity.Medium;

            return complexity;
        }
    }
}
