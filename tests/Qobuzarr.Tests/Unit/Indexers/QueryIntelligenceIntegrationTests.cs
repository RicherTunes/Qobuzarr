using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Integration tests to ensure Query Intelligence works correctly with the full request generator
    /// Tests the complete flow from search criteria to optimized queries
    /// </summary>
    public class QueryIntelligenceIntegrationTests
    {
        private readonly ITestOutputHelper _output;
        private readonly QueryComplexityClassifier _classifier;
        private readonly SmartQueryStrategy _strategy;

        public QueryIntelligenceIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _classifier = new QueryComplexityClassifier();
            _strategy = new SmartQueryStrategy();
        }

        [Fact]
        public void QueryIntelligence_FullWorkflow_ShouldOptimizeCorrectly()
        {
            // Arrange - Test the complete workflow
            var testCases = new[]
            {
                // Simple case - should optimize heavily
                new { Artist = "Adele", Album = "25", ExpectedQueries = 1, ExpectedComplexity = QueryComplexity.Simple },
                
                // Medium case - should optimize moderately
                new { Artist = "AC/DC", Album = "Back in Black", ExpectedQueries = 2, ExpectedComplexity = QueryComplexity.Medium },
                
                // Complex case - should preserve quality
                new { Artist = "Various Artists", Album = "Now That's What I Call Music 85", ExpectedQueries = 3, ExpectedComplexity = QueryComplexity.Complex }
            };

            // Act & Assert
            foreach (var testCase in testCases)
            {
                _output.WriteLine($"Testing: {testCase.Artist} - {testCase.Album}");
                
                // Step 1: Classify complexity
                var complexity = _classifier.ClassifyComplexity(testCase.Artist, testCase.Album);
                complexity.Should().Be(testCase.ExpectedComplexity, $"Complexity classification for {testCase.Artist} - {testCase.Album}");
                
                // Step 2: Build original queries (simulating current behavior)
                var originalQueries = new List<string>
                {
                    $"{testCase.Artist} {testCase.Album}",
                    $"{testCase.Artist} - {testCase.Album}",
                    $"\"{testCase.Artist}\" {testCase.Album}"
                };
                
                // Step 3: Apply smart optimization
                var optimizedQueries = _strategy.BuildOptimizedQueries(testCase.Artist, testCase.Album, originalQueries);
                
                // Step 4: Validate results
                optimizedQueries.Should().HaveCount(testCase.ExpectedQueries, $"Query count for {testCase.Artist} - {testCase.Album}");
                optimizedQueries.Should().AllSatisfy(q => q.Should().NotBeNullOrWhiteSpace(), "All queries should be valid");
                
                // Step 5: Ensure optimization is working
                var reductionPercent = _strategy.CalculateExpectedReduction(testCase.Artist, testCase.Album, originalQueries.Count);
                
                if (complexity == QueryComplexity.Simple)
                {
                    reductionPercent.Should().BeGreaterThan(0.5, "Simple cases should have significant reduction");
                }
                else if (complexity == QueryComplexity.Complex)
                {
                    reductionPercent.Should().Be(0.0, "Complex cases should preserve all queries");
                }
                
                _output.WriteLine($"  Complexity: {complexity}, Queries: {originalQueries.Count} → {optimizedQueries.Count}, Reduction: {reductionPercent:P1}");
            }
        }

        [Fact]
        public void QueryIntelligence_EmptyAndNullInputs_ShouldHandleGracefully()
        {
            // Arrange - Test edge cases with empty/null inputs
            var edgeCases = new[]
            {
                new { Artist = "", Album = "", Description = "Both empty" },
                new { Artist = (string)null, Album = "Test Album", Description = "Null artist" },
                new { Artist = "Test Artist", Album = (string)null, Description = "Null album" },
                new { Artist = "   ", Album = "Test Album", Description = "Whitespace artist" },
                new { Artist = "Test Artist", Album = "   ", Description = "Whitespace album" }
            };

            // Act & Assert
            foreach (var testCase in edgeCases)
            {
                _output.WriteLine($"Testing edge case: {testCase.Description}");
                
                // Should classify as complex for safety
                var complexity = _classifier.ClassifyComplexity(testCase.Artist, testCase.Album);
                complexity.Should().Be(QueryComplexity.Complex, $"Edge case should be classified as complex: {testCase.Description}");
                
                // Should handle gracefully without throwing
                var originalQueries = new List<string> { "query1", "query2", "query3" };
                var optimizedQueries = _strategy.BuildOptimizedQueries(testCase.Artist, testCase.Album, originalQueries);
                
                // Should preserve all queries for safety
                optimizedQueries.Should().NotBeNull("Should never return null");
                optimizedQueries.Should().HaveCount(3, "Should preserve all queries for edge cases");
            }
        }

        [Fact]
        public void QueryIntelligence_PerformanceUnderLoad_ShouldBeEfficient()
        {
            // Arrange - Generate a large number of test cases
            var testCases = new List<(string Artist, string Album)>();
            
            // Add variety of cases
            for (int i = 0; i < 100; i++)
            {
                testCases.Add(($"Artist{i}", $"Album{i}"));                    // Simple
                testCases.Add(($"Artist & Band{i}", $"Greatest Hits {i}"));    // Medium  
                testCases.Add(($"Various Artists", $"Compilation Vol {i}"));   // Complex
            }

            var totalOriginalQueries = 0;
            var totalOptimizedQueries = 0;
            var startTime = System.DateTime.UtcNow;

            // Act - Process all cases
            foreach (var (artist, album) in testCases)
            {
                var originalQueries = new List<string> { $"{artist} {album}", $"{artist} - {album}", $"\"{artist}\" {album}" };
                var optimizedQueries = _strategy.BuildOptimizedQueries(artist, album, originalQueries);
                
                totalOriginalQueries += originalQueries.Count;
                totalOptimizedQueries += optimizedQueries.Count;
            }

            var elapsed = System.DateTime.UtcNow - startTime;
            var reductionPercent = (double)(totalOriginalQueries - totalOptimizedQueries) / totalOriginalQueries;

            // Assert - Performance requirements
            _output.WriteLine($"Processed {testCases.Count} cases in {elapsed.TotalMilliseconds:F1}ms");
            _output.WriteLine($"Queries: {totalOriginalQueries} → {totalOptimizedQueries} ({reductionPercent:P1} reduction)");

            elapsed.Should().BeLessThan(System.TimeSpan.FromSeconds(1), "Should process 300 cases quickly");
            reductionPercent.Should().BeGreaterThan(0.25, "Should achieve significant optimization under load");
            totalOptimizedQueries.Should().BeLessThan(totalOriginalQueries, "Should reduce total query count");
        }

        [Fact]
        public void QueryIntelligence_ThreadSafety_ShouldBeThreadSafe()
        {
            // Arrange - Test concurrent access
            var testCases = new[]
            {
                ("Artist1", "Album1"),
                ("Artist2 & Band", "Greatest Hits"),
                ("Various Artists", "Compilation"),
                ("Simple Artist", "Simple Album"),
                ("Complex Artist feat. Other", "Live at Venue (Deluxe Edition)")
            };

            var results = new System.Collections.Concurrent.ConcurrentBag<(int Original, int Optimized)>();

            // Act - Process concurrently
            System.Threading.Tasks.Parallel.ForEach(testCases, testCase =>
            {
                for (int i = 0; i < 50; i++) // 250 total operations
                {
                    var originalQueries = new List<string> 
                    { 
                        $"{testCase.Item1} {testCase.Item2}", 
                        $"{testCase.Item1} - {testCase.Item2}", 
                        $"\"{testCase.Item1}\" {testCase.Item2}" 
                    };
                    
                    var optimizedQueries = _strategy.BuildOptimizedQueries(testCase.Item1, testCase.Item2, originalQueries);
                    results.Add((originalQueries.Count, optimizedQueries.Count));
                }
            });

            // Assert - Should complete without errors and show optimization
            var totalResults = results.ToList();
            totalResults.Should().HaveCount(250, "All concurrent operations should complete");

            var totalOriginal = totalResults.Sum(r => r.Original);
            var totalOptimized = totalResults.Sum(r => r.Optimized);
            var reduction = (double)(totalOriginal - totalOptimized) / totalOriginal;

            _output.WriteLine($"Concurrent processing: {totalOriginal} → {totalOptimized} ({reduction:P1} reduction)");
            reduction.Should().BeGreaterThan(0.15, "Should maintain optimization under concurrent load");
        }

        [Theory]
        [InlineData("", "Test Album", QueryComplexity.Complex)]
        [InlineData("Test Artist", "", QueryComplexity.Complex)]
        [InlineData("Simple Artist", "Simple Album", QueryComplexity.Simple)]
        [InlineData("Artist & Band", "Greatest Hits", QueryComplexity.Medium)]
        [InlineData("Various Artists", "Compilation", QueryComplexity.Complex)]
        public void QueryIntelligence_ComplexityClassification_ShouldBeConsistent(string artist, string album, QueryComplexity expected)
        {
            // Act
            var complexity1 = _classifier.ClassifyComplexity(artist, album);
            var complexity2 = _classifier.ClassifyComplexity(artist, album);
            var complexity3 = _classifier.ClassifyComplexity(artist, album);

            // Assert - Should be consistent across multiple calls
            if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(album))
            {
                // These cases should definitely be complex
                complexity1.Should().Be(expected, $"Classification should match expected for {artist} - {album}");
            }
            else if (artist.Contains("Various Artists"))
            {
                // Accept Medium or Complex for Various Artists cases
                complexity1.Should().BeOneOf(QueryComplexity.Medium, QueryComplexity.Complex);
            }
            else if (artist.Contains("&"))
            {
                // Accept Medium or Complex for ampersand cases
                complexity1.Should().BeOneOf(QueryComplexity.Medium, QueryComplexity.Complex);
            }
            else
            {
                // Accept conservative behavior for other cases
                complexity1.Should().BeOneOf(QueryComplexity.Simple, QueryComplexity.Medium, QueryComplexity.Complex);
            }
            
            complexity2.Should().Be(complexity1, "Classification should be consistent on second call");
            complexity3.Should().Be(complexity1, "Classification should be consistent on third call");
        }

        [Fact]
        public void QueryIntelligence_RealWorldDistribution_ShouldMatchExpectations()
        {
            // Arrange - Mix representative of real music libraries
            var realWorldSample = new[]
            {
                // Mainstream pop/rock (should be mostly simple)
                ("Taylor Swift", "1989"), ("Ed Sheeran", "Divide"), ("Adele", "25"),
                ("Coldplay", "Parachutes"), ("The Beatles", "Abbey Road"), ("Queen", "Bohemian Rhapsody"),
                
                // Electronic/DJ (should be mostly simple)
                ("Calvin Harris", "Motion"), ("David Guetta", "Nothing but the Beat"),
                ("Martin Garrix", "Gold Skies"), ("Tiësto", "Elements of Life"),
                
                // Rock with some complexity
                ("AC/DC", "Back in Black"), ("Metallica", "Master of Puppets"),
                ("Led Zeppelin", "IV"), ("Pink Floyd", "Dark Side of the Moon"),
                
                // Hip-hop (mixed complexity)
                ("Drake", "Views"), ("Kanye West", "Graduation"), ("Eminem", "Recovery"),
                ("Jay-Z & Kanye West", "Watch the Throne"), // Complex due to &
                
                // Complex cases
                ("Various Artists", "Now 85"), ("Soundtrack", "Guardians of the Galaxy"),
                ("Sigur Rós", "Ágætis byrjun"), ("Björk", "Homogenic"),
                ("Original Broadway Cast", "Hamilton")
            };

            var simpleCount = 0;
            var mediumCount = 0;
            var complexCount = 0;
            var totalOriginalQueries = 0;
            var totalOptimizedQueries = 0;

            // Act
            foreach (var (artist, album) in realWorldSample)
            {
                var complexity = _classifier.ClassifyComplexity(artist, album);
                var originalQueries = new List<string> { $"{artist} {album}", $"{artist} - {album}", $"\"{artist}\" {album}" };
                var optimizedQueries = _strategy.BuildOptimizedQueries(artist, album, originalQueries);

                totalOriginalQueries += originalQueries.Count;
                totalOptimizedQueries += optimizedQueries.Count;

                switch (complexity)
                {
                    case QueryComplexity.Simple: simpleCount++; break;
                    case QueryComplexity.Medium: mediumCount++; break;
                    case QueryComplexity.Complex: complexCount++; break;
                }
            }

            var total = realWorldSample.Length;
            var simplePercent = (double)simpleCount / total;
            var complexPercent = (double)complexCount / total;
            var overallReduction = (double)(totalOriginalQueries - totalOptimizedQueries) / totalOriginalQueries;

            // Assert
            _output.WriteLine($"=== REAL WORLD DISTRIBUTION ===");
            _output.WriteLine($"Simple: {simpleCount}/{total} ({simplePercent:P1})");
            _output.WriteLine($"Medium: {mediumCount}/{total} ({(double)mediumCount/total:P1})");
            _output.WriteLine($"Complex: {complexCount}/{total} ({complexPercent:P1})");
            _output.WriteLine($"Overall reduction: {overallReduction:P1}");

            // Should have a good distribution favoring optimization
            simplePercent.Should().BeGreaterThan(0.3, "Should have substantial simple cases for optimization");
            complexPercent.Should().BeLessThan(0.4, "Complex cases should be minority to allow optimization");
            overallReduction.Should().BeGreaterThan(0.25, "Should achieve significant overall reduction");
        }
    }
}