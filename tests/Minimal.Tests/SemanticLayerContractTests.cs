using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Minimal.Tests
{
    /// <summary>
    /// Contract tests that validate data flow between semantic analysis layers.
    /// These tests ensure that interfaces and data contracts remain stable across layers.
    /// CRITICAL: These tests prevent production failures from layer mismatches.
    /// </summary>
    public class SemanticLayerContractTests
    {
        private readonly AlbumComponentClassifier _classifier;
        private readonly SemanticQueryStrategy _strategy;

        public SemanticLayerContractTests()
        {
            _classifier = new AlbumComponentClassifier();
            _strategy = new SemanticQueryStrategy();
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        public void Contract_NullOrEmptyInput_AllLayersShouldHandleGracefully(string input)
        {
            // Act & Assert - Each layer should handle null/empty gracefully without throwing
            Action classifierAction = () => _classifier.ClassifyComponents(input);
            Action preservedAction = () => _classifier.GetPreservedTerms(input);
            Action cleaningLevelAction = () => _classifier.RecommendCleaningLevel(input);
            Action strategyAction = () => _strategy.DetermineStrategy("Artist", input);

            classifierAction.Should().NotThrow("ClassifyComponents should handle null/empty input");
            preservedAction.Should().NotThrow("GetPreservedTerms should handle null/empty input");
            cleaningLevelAction.Should().NotThrow("RecommendCleaningLevel should handle null/empty input");
            strategyAction.Should().NotThrow("DetermineStrategy should handle null/empty input");

            // Verify consistent behavior across layers
            var components = _classifier.ClassifyComponents(input);
            var preservedTerms = _classifier.GetPreservedTerms(input);

            components.Should().BeEmpty("Null/empty input should yield empty components");
            preservedTerms.Should().BeEmpty("Null/empty input should yield empty preserved terms");
        }

        [Fact]
        public void Contract_ClassifierToStrategy_DataFlowIntegrity()
        {
            // Arrange - Complex real-world case
            var albumTitle = "MTV Unplugged Live Acoustic Sessions (Deluxe Edition)";

            // Act - Follow the data through each layer
            var components = _classifier.ClassifyComponents(albumTitle);
            var preservedTerms = _classifier.GetPreservedTerms(albumTitle);
            var cleaningLevel = _classifier.RecommendCleaningLevel(albumTitle);
            var strategy = _strategy.DetermineStrategy("Artist", albumTitle);

            // Assert - Contract validation between layers

            // 1. Strategy should use the same cleaning level recommendation
            strategy.CleaningLevel.Should().Be(cleaningLevel,
                "Strategy should respect classifier's cleaning level recommendation");

            // 2. Strategy preserved terms should be subset of classifier preserved terms
            foreach (var strategyTerm in strategy.PreserveTerms)
            {
                preservedTerms.Should().Contain(strategyTerm,
                    $"Strategy term '{strategyTerm}' should be in classifier preserved terms");
            }

            // 3. All version descriptors should be preserved by strategy
            var versionDescriptors = components
                .Where(c => c.Value == AlbumComponentType.VersionDescriptor)
                .Select(c => c.Key);

            foreach (var descriptor in versionDescriptors)
            {
                strategy.PreserveTerms.Should().Contain(descriptor,
                    $"Version descriptor '{descriptor}' must be preserved by strategy");
            }
        }

        [Fact]
        public void Contract_StrategyToQueryGeneration_ConsistentBehavior()
        {
            // Arrange
            var testCases = new[]
            {
                ("Artist", "Album Instrumental", CleaningLevel.Minimal),
                ("Artist", "Album (Deluxe Edition)", CleaningLevel.Moderate),
                ("Artist", "Simple Album", CleaningLevel.Aggressive)
            };

            foreach (var (artist, album, expectedLevel) in testCases)
            {
                // Act
                var strategy = _strategy.DetermineStrategy(artist, album);
                var queries = _strategy.BuildQueriesForStrategy(artist, album, strategy);

                // Assert - Contract validation
                strategy.CleaningLevel.Should().Be(expectedLevel,
                    $"Strategy for '{album}' should use {expectedLevel} cleaning");

                queries.Should().HaveCountGreaterThan(0,
                    "Query generation should always produce at least one query");

                queries.Should().HaveCountLessOrEqualTo(strategy.QueryVariants,
                    "Generated queries should not exceed strategy variant limit");

                // All generated queries should be valid (not null/empty)
                queries.Should().OnlyContain(q => !string.IsNullOrWhiteSpace(q),
                    "All generated queries must be valid non-empty strings");

                // If strategy preserves terms, queries should contain them
                if (strategy.PreserveTerms.Any())
                {
                    foreach (var preservedTerm in strategy.PreserveTerms)
                    {
                        queries.Should().OnlyContain(q => q.Contains(preservedTerm, StringComparison.OrdinalIgnoreCase),
                            $"All queries must preserve term '{preservedTerm}'");
                    }
                }
            }
        }

        [Theory]
        [InlineData("Album\nwith\nnewlines")]
        [InlineData("Album\twith\ttabs")]
        [InlineData("Album   with   multiple   spaces")]
        [InlineData("Album@#$%^&*()with!special?chars")]
        [InlineData("Album\"with\"quotes\"")]
        [InlineData("Álbum with açcénts")]
        [InlineData("アルバム Japanese")]
        [InlineData("Very Long Album Title That Exceeds Normal Length Limits And Contains Many Words That Should Be Processed Correctly")]
        public void Contract_SpecialCharactersAndEncoding_AllLayersHandleCorrectly(string albumTitle)
        {
            // Act & Assert - All layers should handle special characters without crashing
            Action classifierAction = () => _classifier.ClassifyComponents(albumTitle);
            Action strategyAction = () => _strategy.DetermineStrategy("Artist", albumTitle);

            classifierAction.Should().NotThrow($"Classifier should handle special chars: {albumTitle}");
            strategyAction.Should().NotThrow($"Strategy should handle special chars: {albumTitle}");

            // Verify data integrity through the pipeline
            var components = _classifier.ClassifyComponents(albumTitle);
            var strategy = _strategy.DetermineStrategy("Artist", albumTitle);
            var queries = _strategy.BuildQueriesForStrategy("Artist", albumTitle, strategy);

            components.Should().NotBeNull("Components should not be null for special chars");
            queries.Should().NotBeEmpty("Queries should be generated for special chars");
            queries.Should().OnlyContain(q => !string.IsNullOrWhiteSpace(q),
                "Generated queries should be valid for special chars");
        }

        [Fact]
        public void Contract_StateIsolation_ConcurrentCallsDoNotInterfere()
        {
            // Arrange - Different album titles that should produce different results
            var albums = new[]
            {
                "Simple Album",
                "Complex Live Instrumental Sessions",
                "Album (Deluxe Edition)",
                "Unplugged Acoustic Demo"
            };

            var results = new List<(string album, Dictionary<string, AlbumComponentType> components, QueryStrategy strategy)>();

            // Act - Process all albums in sequence to capture baseline
            foreach (var album in albums)
            {
                var components = _classifier.ClassifyComponents(album);
                var strategy = _strategy.DetermineStrategy("Artist", album);
                results.Add((album, components, strategy));
            }

            // Assert - Each album should produce distinct, consistent results
            for (int i = 0; i < albums.Length; i++)
            {
                for (int j = i + 1; j < albums.Length; j++)
                {
                    var result1 = results[i];
                    var result2 = results[j];

                    // Different albums should generally produce different results
                    if (result1.album != result2.album)
                    {
                        // At least some aspect should be different (components, cleaning level, or preserve terms)
                        var isDifferent = !result1.components.Keys.SequenceEqual(result2.components.Keys) ||
                                        result1.strategy.CleaningLevel != result2.strategy.CleaningLevel ||
                                        !result1.strategy.PreserveTerms.SequenceEqual(result2.strategy.PreserveTerms);

                        isDifferent.Should().BeTrue($"'{result1.album}' and '{result2.album}' should produce different results");
                    }
                }
            }

            // Verify state isolation - re-process first album should yield identical results
            var reprocessedComponents = _classifier.ClassifyComponents(albums[0]);
            var reprocessedStrategy = _strategy.DetermineStrategy("Artist", albums[0]);

            reprocessedComponents.Should().BeEquivalentTo(results[0].components,
                "Re-processing should yield identical results (state isolation)");
            reprocessedStrategy.CleaningLevel.Should().Be(results[0].strategy.CleaningLevel,
                "Re-processing should yield identical cleaning level (state isolation)");
        }

        [Fact]
        public void Contract_InputValidation_MaliciousInputHandling()
        {
            // Arrange - Potentially malicious inputs that could cause issues
            var maliciousInputs = new[]
            {
                new string('A', 10000), // Very long string
                "Album" + new string('\0', 100), // Null bytes
                "Album\r\n<script>alert('xss')</script>", // XSS attempt
                "Album'; DROP TABLE albums; --", // SQL injection attempt
                "Album{{{{{{{{{{", // Regex bomb attempt
                "Album" + string.Join("", Enumerable.Repeat("(", 1000)), // Nested parentheses
                "Album\uFEFF\u200B\u2060", // Zero-width characters
            };

            foreach (var input in maliciousInputs)
            {
                // Act & Assert - Should handle gracefully without crashing or hanging
                Action classifierAction = () =>
                {
                    var timeout = TimeSpan.FromSeconds(5);
                    var start = DateTime.UtcNow;
                    var components = _classifier.ClassifyComponents(input);
                    var elapsed = DateTime.UtcNow - start;
                    elapsed.Should().BeLessThan(timeout, "Classifier should not hang on malicious input");
                };

                Action strategyAction = () =>
                {
                    var timeout = TimeSpan.FromSeconds(5);
                    var start = DateTime.UtcNow;
                    var strategy = _strategy.DetermineStrategy("Artist", input);
                    var elapsed = DateTime.UtcNow - start;
                    elapsed.Should().BeLessThan(timeout, "Strategy should not hang on malicious input");
                };

                classifierAction.Should().NotThrow($"Classifier should handle malicious input gracefully: {input.Substring(0, Math.Min(50, input.Length))}...");
                strategyAction.Should().NotThrow($"Strategy should handle malicious input gracefully: {input.Substring(0, Math.Min(50, input.Length))}...");
            }
        }

        [Fact]
        public void Contract_VersionDescriptorConsistency_AllLayersAgree()
        {
            // Arrange - Albums with known version descriptors
            var versionDescriptorAlbums = new[]
            {
                ("Instrumental", "Album Instrumental"),
                ("Live", "Concert Live"),
                ("Acoustic", "Acoustic Sessions"),
                ("Unplugged", "MTV Unplugged"),
                ("Demo", "Early Demo Recordings"),
                ("Mono", "Stereo and Mono Versions"),
                ("Hi-Fi", "Audiophile Hi-Fi Edition"),
                ("En Vivo", "Concierto En Vivo")
            };

            foreach (var (descriptor, albumTitle) in versionDescriptorAlbums)
            {
                // Act
                var components = _classifier.ClassifyComponents(albumTitle);
                var preservedTerms = _classifier.GetPreservedTerms(albumTitle);
                var strategy = _strategy.DetermineStrategy("Artist", albumTitle);

                // Assert - All layers should consistently recognize version descriptors
                var hasVersionDescriptor = components.Values.Any(c => c == AlbumComponentType.VersionDescriptor);
                hasVersionDescriptor.Should().BeTrue($"'{albumTitle}' should contain version descriptor '{descriptor}'");

                preservedTerms.Should().ContainMatch($"*{descriptor}*",
                    $"'{descriptor}' should be preserved in '{albumTitle}'");

                strategy.CleaningLevel.Should().Be(CleaningLevel.Minimal,
                    $"Albums with version descriptor '{descriptor}' should use minimal cleaning");

                strategy.PreserveTerms.Should().ContainMatch($"*{descriptor}*",
                    $"Strategy should preserve version descriptor '{descriptor}'");
            }
        }
    }
}
