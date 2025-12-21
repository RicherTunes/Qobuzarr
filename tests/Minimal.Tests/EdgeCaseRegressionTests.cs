using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;
using System.Linq;

namespace Minimal.Tests
{
    /// <summary>
    /// Comprehensive edge case tests to prevent regression of search failures
    /// These tests cover the complex real-world album naming patterns that our expert identified
    /// Note: These are functional regression tests, not performance benchmarks
    /// </summary>
    [Trait("Category", "Performance")]
    public class EdgeCaseRegressionTests
    {
        private readonly AlbumComponentClassifier _classifier;
        private readonly SemanticQueryStrategy _strategy;

        public EdgeCaseRegressionTests()
        {
            _classifier = new AlbumComponentClassifier();
            _strategy = new SemanticQueryStrategy();
        }

        [Theory]
        [InlineData("Kind of Blue Mono", "Mono", "Miles Davis mono version must be preserved")]
        [InlineData("Pet Sounds Stereo", "Stereo", "Beach Boys stereo version must be preserved")]
        [InlineData("Dark Side of the Moon Quadraphonic", "Quadraphonic", "Surround format must be preserved")]
        [InlineData("Abbey Road Hi-Fi", "Hi-Fi", "Audio quality descriptor must be preserved")]
        [InlineData("Sgt Pepper 24-Bit", "24-Bit", "Digital quality descriptor must be preserved")]
        public void CriticalBug_AudioFormatDescriptors_MustBePreserved(string albumTitle, string criticalTerm, string reason)
        {
            // Act
            var components = _classifier.ClassifyComponents(albumTitle);
            var preservedTerms = _classifier.GetPreservedTerms(albumTitle);
            var strategy = _strategy.DetermineStrategy("Artist", albumTitle);

            // Assert
            components.Should().ContainKey(criticalTerm, reason);
            components[criticalTerm].Should().Be(AlbumComponentType.VersionDescriptor, 
                $"{criticalTerm} must be classified as version descriptor");
            preservedTerms.Should().Contain(criticalTerm, $"{criticalTerm} must be preserved for search");
            strategy.CleaningLevel.Should().Be(CleaningLevel.Minimal, "Audio format albums need minimal cleaning");
        }

        [Theory]
        [InlineData("Manu Chao - La Radiolina En Vivo", "En Vivo", "Spanish live album")]
        [InlineData("Céline Dion - Live En Direct", "En Direct", "French live album")]  
        [InlineData("Caetano Veloso - Ao Vivo", "Ao Vivo", "Portuguese live album")]
        [InlineData("Rammstein - Live aus Berlin", "Live", "German live album")]
        [InlineData("Andrea Bocelli - Concerto Live", "Concerto", "Italian concert")]
        public void CriticalBug_InternationalLiveAlbums_MustBePreserved(string albumTitle, string criticalTerm, string context)
        {
            // Act - Extract album part after artist
            var album = albumTitle.Contains(" - ") ? albumTitle.Split(" - ").Last() : albumTitle;
            var components = _classifier.ClassifyComponents(album);
            var strategy = _strategy.DetermineStrategy("Artist", album);

            // Assert
            components.Should().ContainKey(criticalTerm, $"{criticalTerm} is critical for {context}");
            components[criticalTerm].Should().Be(AlbumComponentType.VersionDescriptor);
            strategy.CleaningLevel.Should().Be(CleaningLevel.Minimal, "International live albums need preservation");
        }

        [Theory]
        [InlineData("The Complete Studio Sessions", "Studio", "Studio context is part of album identity")]
        [InlineData("Complete Concert Performances", "Concert", "Concert performances are distinct versions")]
        [InlineData("Festival Recordings 1969", "Festival", "Festival recordings are unique versions")]
        [InlineData("The Vegas Residency Shows", "Residency", "Residency shows are specific performances")]
        [InlineData("World Tour Documentary", "Tour", "Tour recordings are distinct from studio")]
        public void CriticalBug_PerformanceContexts_MustBePreserved(string albumTitle, string criticalTerm, string reason)
        {
            // Act
            var components = _classifier.ClassifyComponents(albumTitle);
            var preservedTerms = _classifier.GetPreservedTerms(albumTitle);

            // Assert
            components.Should().ContainKey(criticalTerm, reason);
            components[criticalTerm].Should().Be(AlbumComponentType.VersionDescriptor, 
                $"{criticalTerm} represents performance context");
            preservedTerms.Should().Contain(criticalTerm, "Performance context must be preserved");
        }

        [Theory]
        [InlineData("The Complete Rarities Collection", "Rarities", "Rarities collections are distinct releases")]
        [InlineData("Unreleased Studio Outtakes", "Unreleased", "Unreleased material is specifically searched for")]
        [InlineData("The B-Sides Collection", "B-Sides", "B-sides are different from main albums")]
        [InlineData("Alternate Takes and Versions", "Alternate", "Alternate versions are distinct releases")]
        [InlineData("Demo Sessions 1967", "Demo", "Demo sessions are pre-release versions")]
        public void CriticalBug_CollectionTypes_MustBePreserved(string albumTitle, string criticalTerm, string reason)
        {
            // Act
            var components = _classifier.ClassifyComponents(albumTitle);
            
            // Assert
            components.Should().ContainKey(criticalTerm, reason);
            components[criticalTerm].Should().Be(AlbumComponentType.VersionDescriptor, 
                $"{criticalTerm} indicates collection type");
        }

        [Theory]
        [InlineData("Analog Masters Collection", "Analog", "Analog format is distinct from digital")]
        [InlineData("Digital Download Exclusive", "Digital", "Digital exclusive releases are distinct")]
        [InlineData("Vinyl Pressing Original", "Vinyl", "Vinyl pressings have unique mastering")]
        [InlineData("CD Single Release", "CD", "CD singles are different format releases")]
        [InlineData("Rehearsal Room Sessions", "Rehearsal", "Rehearsal recordings are pre-production")]
        public void CriticalBug_FormatAndQualityDescriptors_MustBePreserved(string albumTitle, string criticalTerm, string reason)
        {
            // Act
            var components = _classifier.ClassifyComponents(albumTitle);

            // Assert
            components.Should().ContainKey(criticalTerm, reason);
            components[criticalTerm].Should().Be(AlbumComponentType.VersionDescriptor);
        }

        [Fact]
        public void ComplexCase_MultipleVersionDescriptors_ShouldAllBePreserved()
        {
            // Arrange - Ultra complex real-world case
            var albumTitle = "MTV Unplugged Live Acoustic Sessions";

            // Act
            var components = _classifier.ClassifyComponents(albumTitle);
            var preservedTerms = _classifier.GetPreservedTerms(albumTitle);
            var strategy = _strategy.DetermineStrategy("Artist", albumTitle);

            // Assert - ALL version descriptors must be preserved
            var versionDescriptors = new[] { "Unplugged", "Live", "Acoustic", "Sessions" };
            
            foreach (var descriptor in versionDescriptors)
            {
                components.Should().ContainKey(descriptor, $"{descriptor} is a version descriptor");
                components[descriptor].Should().Be(AlbumComponentType.VersionDescriptor);
                preservedTerms.Should().Contain(descriptor, $"{descriptor} must be preserved");
            }

            strategy.CleaningLevel.Should().Be(CleaningLevel.Minimal, 
                "Albums with multiple version descriptors need minimal cleaning");
        }

        [Fact]
        public void RegressionTest_OriginalInstrumentalBug_MustStayFixed()
        {
            // Arrange - The original bug case that started this investigation
            var artist = "070 Shake";
            var album = "Modus Vivendi Instrumental";

            // Act
            var components = _classifier.ClassifyComponents(album);
            var queries = _strategy.BuildQueriesForBugCase(artist, album);

            // Assert - This must NEVER regress
            components.Should().ContainKey("Instrumental", "Original bug must stay fixed");
            components["Instrumental"].Should().Be(AlbumComponentType.VersionDescriptor);
            queries.Should().OnlyContain(q => q.Contains("Instrumental"), "All queries must preserve Instrumental");
        }

        [Theory]
        [InlineData("Abbey Road", "Simple classic album should use aggressive cleaning")]
        [InlineData("Dark Side of the Moon", "Another classic should use aggressive cleaning")]
        [InlineData("Thriller", "Pop classics should use aggressive cleaning")]
        public void Regression_SimpleAlbums_ShouldStillUseAggressiveCleaning(string albumTitle, string reason)
        {
            // Act
            var strategy = _strategy.DetermineStrategy("Artist", albumTitle);

            // Assert - Simple albums should NOT be affected by our version descriptor expansion
            strategy.CleaningLevel.Should().Be(CleaningLevel.Aggressive, reason);
            strategy.OptimizationLevel.Should().Be(OptimizationLevel.Maximum);
        }

        [Theory]
        [InlineData("Album (Deluxe Edition)", "Deluxe", CleaningLevel.Moderate, "Edition markers still removable")]
        [InlineData("Songs (Remastered)", "Remastered", CleaningLevel.Moderate, "Remastered still removable")]
        [InlineData("Music - Anniversary Edition", "Anniversary", CleaningLevel.Moderate, "Anniversary still removable")]
        public void Regression_EditionMarkers_ShouldStillBeRemovable(string albumTitle, string editionMarker, 
            CleaningLevel expectedLevel, string reason)
        {
            // Act
            var strategy = _strategy.DetermineStrategy("Artist", albumTitle);
            var components = _classifier.ClassifyComponents(albumTitle);

            // Assert - Edition markers should still be handled as before
            strategy.CleaningLevel.Should().Be(expectedLevel, reason);
            
            // Edition markers should NOT be classified as version descriptors
            if (components.ContainsKey(editionMarker))
            {
                components[editionMarker].Should().NotBe(AlbumComponentType.VersionDescriptor, 
                    $"{editionMarker} should remain as edition marker, not version descriptor");
            }
        }

        [Fact]
        public void EdgeCase_MixedLanguageAlbum_ShouldPreserveAllLanguageTerms()
        {
            // Arrange - Real-world multilingual album
            var albumTitle = "Live En Vivo Unplugged Sessions";

            // Act
            var components = _classifier.ClassifyComponents(albumTitle);
            var preservedTerms = _classifier.GetPreservedTerms(albumTitle);

            // Assert
            var expectedTerms = new[] { "Live", "En Vivo", "Unplugged", "Sessions" };
            
            foreach (var term in expectedTerms)
            {
                // Handle multi-word terms like "En Vivo"
                var termInComponents = components.Keys.Any(k => k.Contains(term, System.StringComparison.OrdinalIgnoreCase));
                var termInPreserved = preservedTerms.Any(p => p.Contains(term, System.StringComparison.OrdinalIgnoreCase));
                
                termInComponents.Should().BeTrue($"'{term}' should be recognized in components");
                termInPreserved.Should().BeTrue($"'{term}' should be preserved for search");
            }
        }

        [Fact]
        public void StressTest_UltraComplexRealWorldCase_ShouldHandleCorrectly()
        {
            // Arrange - Worst-case real-world scenario
            var albumTitle = "The Complete Live MTV Unplugged Acoustic Sessions (Mono Audiophile 24-Bit Remaster)";

            // Act
            var components = _classifier.ClassifyComponents(albumTitle);
            var strategy = _strategy.DetermineStrategy("Artist", albumTitle);
            var queries = _strategy.BuildQueriesForStrategy("Artist", albumTitle, strategy);

            // Assert - Should preserve version descriptors but allow edition marker removal
            var versionDescriptors = new[] { "Complete", "Live", "Unplugged", "Acoustic", "Sessions", "Mono", "Audiophile", "24-Bit" };
            var editionMarkers = new[] { "Remaster" }; // This can be removed
            
            foreach (var descriptor in versionDescriptors)
            {
                components.Should().ContainKey(descriptor, $"{descriptor} is critical for this complex album");
            }

            strategy.CleaningLevel.Should().Be(CleaningLevel.Minimal, 
                "Ultra-complex albums with many version descriptors need minimal cleaning");

            queries.Should().OnlyContain(q => versionDescriptors.All(d => q.Contains(d, System.StringComparison.OrdinalIgnoreCase)),
                "All version descriptors must be preserved in final queries");
        }
    }
}