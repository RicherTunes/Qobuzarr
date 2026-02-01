using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Minimal.Tests
{
    /// <summary>
    /// Simplified integration tests that validate semantic query workflows without full Lidarr dependencies.
    /// These tests ensure our semantic components integrate correctly at the architectural level.
    /// </summary>
    public class QobuzIndexerIntegrationTests
    {
        private readonly SemanticQueryStrategy _semanticStrategy;
        private readonly AlbumComponentClassifier _classifier;

        public QobuzIndexerIntegrationTests()
        {
            _semanticStrategy = new SemanticQueryStrategy();
            _classifier = new AlbumComponentClassifier();
        }

        [Fact]
        public void Integration_SemanticWorkflow_EndToEndValidation()
        {
            // Arrange - The original bug case
            var artist = "070 Shake";
            var album = "Modus Vivendi Instrumental";

            // Act - Follow the complete semantic workflow

            // Step 1: Component analysis
            var components = _classifier.ClassifyComponents(album);

            // Step 2: Strategy determination  
            var strategy = _semanticStrategy.DetermineStrategy(artist, album);

            // Step 3: Query generation
            var queries = _semanticStrategy.BuildQueriesForStrategy(artist, album, strategy);

            // Assert - Verify end-to-end workflow integrity

            // Step 1 validation
            components.Should().ContainKey("Instrumental");
            components["Instrumental"].Should().Be(AlbumComponentType.VersionDescriptor);

            // Step 2 validation  
            strategy.CleaningLevel.Should().Be(CleaningLevel.Minimal);
            strategy.PreserveTerms.Should().Contain("Instrumental");

            // Step 3 validation
            queries.Should().NotBeEmpty();
            queries.Should().OnlyContain(q => q.Contains("Instrumental"),
                "End-to-end workflow must preserve version descriptors");
        }

        [Theory]
        [InlineData("Simple Album", CleaningLevel.Aggressive, 1)]
        [InlineData("Album Instrumental", CleaningLevel.Minimal, 2)]
        [InlineData("Album (Deluxe Edition)", CleaningLevel.Moderate, 2)]
        public void Integration_StrategyToQueryCount_ArchitecturalConsistency(string album, CleaningLevel expectedLevel, int expectedMinQueries)
        {
            // Act
            var strategy = _semanticStrategy.DetermineStrategy("Artist", album);
            var queries = _semanticStrategy.BuildQueriesForStrategy("Artist", album, strategy);

            // Assert - Architecture should be consistent
            strategy.CleaningLevel.Should().Be(expectedLevel, $"Album '{album}' should use {expectedLevel} cleaning");
            queries.Should().HaveCountGreaterOrEqualTo(expectedMinQueries, $"Should generate at least {expectedMinQueries} queries");
            queries.Should().HaveCountLessOrEqualTo(strategy.QueryVariants, "Should not exceed strategy variant limit");
        }

        [Fact]
        public void Integration_PerformanceCharacteristics_ReasonableExecutionTime()
        {
            // Arrange - Mix of album complexities
            var testAlbums = new[]
            {
                "Simple",
                "Album Live Instrumental Acoustic Sessions",
                "The Complete MTV Unplugged En Vivo Hi-Fi 24-Bit Collection (Deluxe Edition)",
                "Ultra Complex Album With Many Version Descriptors And Edition Markers"
            };

            // Act & Assert - All should complete quickly
            foreach (var album in testAlbums)
            {
                var startTime = DateTime.UtcNow;

                var components = _classifier.ClassifyComponents(album);
                var strategy = _semanticStrategy.DetermineStrategy("Artist", album);
                var queries = _semanticStrategy.BuildQueriesForStrategy("Artist", album, strategy);

                var duration = DateTime.UtcNow - startTime;

                // Performance validation
                duration.Should().BeLessThan(TimeSpan.FromMilliseconds(100),
                    $"Semantic processing for '{album}' should complete under 100ms");

                // Correctness validation
                components.Should().NotBeNull($"Classification should succeed for '{album}'");
                strategy.Should().NotBeNull($"Strategy should be generated for '{album}'");
                queries.Should().NotBeEmpty($"Queries should be generated for '{album}'");
            }
        }

        [Fact]
        public void Integration_MemoryEfficiency_NoMemoryLeaks()
        {
            // Arrange
            var albumTitles = new[]
            {
                "Album Live",
                "Songs Instrumental",
                "Music Acoustic",
                "Complex Album Live Instrumental Acoustic Sessions Hi-Fi 24-Bit"
            };

            var initialMemory = GC.GetTotalMemory(true);

            // Act - Process many requests to check for memory leaks
            for (int i = 0; i < 1000; i++)
            {
                foreach (var album in albumTitles)
                {
                    _classifier.ClassifyComponents(album);
                    _semanticStrategy.DetermineStrategy("Artist", album);
                    _semanticStrategy.BuildQueriesForStrategy("Artist", album,
                        _semanticStrategy.DetermineStrategy("Artist", album));
                }
            }

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var finalMemory = GC.GetTotalMemory(false);
            var memoryIncrease = finalMemory - initialMemory;

            // Assert - Memory usage should be reasonable
            memoryIncrease.Should().BeLessThan(10 * 1024 * 1024, // 10MB limit
                $"Memory increase should be reasonable after 4000 operations: {memoryIncrease / 1024 / 1024}MB");
        }

        [Fact]
        public void Integration_ComponentStrategyAlignment_SemanticConsistency()
        {
            // Arrange - Albums with different semantic characteristics
            var testCases = new[]
            {
                new { Album = "Live Concert", ExpectedDescriptors = new[] { "Live", "Concert" } },
                new { Album = "Acoustic Sessions Demo", ExpectedDescriptors = new[] { "Acoustic", "Sessions", "Demo" } },
                new { Album = "MTV Unplugged En Vivo", ExpectedDescriptors = new[] { "Unplugged", "En Vivo" } },
                new { Album = "Hi-Fi Audiophile 24-Bit", ExpectedDescriptors = new[] { "Hi-Fi", "Audiophile", "24-Bit" } }
            };

            foreach (var testCase in testCases)
            {
                // Act
                var components = _classifier.ClassifyComponents(testCase.Album);
                var strategy = _semanticStrategy.DetermineStrategy("Artist", testCase.Album);

                // Assert - Semantic consistency across layers
                var detectedDescriptors = components
                    .Where(c => c.Value == AlbumComponentType.VersionDescriptor)
                    .Select(c => c.Key)
                    .ToList();

                foreach (var expectedDescriptor in testCase.ExpectedDescriptors)
                {
                    // Either the exact descriptor or a containing token should be detected
                    var found = detectedDescriptors.Any(d =>
                        d.Equals(expectedDescriptor, StringComparison.OrdinalIgnoreCase) ||
                        d.Contains(expectedDescriptor, StringComparison.OrdinalIgnoreCase) ||
                        expectedDescriptor.Contains(d, StringComparison.OrdinalIgnoreCase));

                    found.Should().BeTrue($"Expected descriptor '{expectedDescriptor}' should be detected in '{testCase.Album}'");
                }

                // Strategy should reflect the presence of version descriptors
                if (detectedDescriptors.Any())
                {
                    strategy.CleaningLevel.Should().Be(CleaningLevel.Minimal,
                        $"Albums with version descriptors should use minimal cleaning: {testCase.Album}");
                }
            }
        }
    }
}
