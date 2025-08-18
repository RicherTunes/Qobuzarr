using System;
using System.Linq;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Minimal.Tests
{
    /// <summary>
    /// Unit tests for AlbumComponentClassifier - semantic analysis of album titles
    /// Tests the core logic that determines which parts of album titles to preserve vs. remove
    /// </summary>
    public class AlbumComponentClassifierTests
    {
        private readonly AlbumComponentClassifier _classifier;

        public AlbumComponentClassifierTests()
        {
            _classifier = new AlbumComponentClassifier();
        }

        [Theory]
        [InlineData("Modus Vivendi Instrumental", AlbumComponentType.VersionDescriptor, "Instrumental")]
        [InlineData("MTV Unplugged Live", AlbumComponentType.VersionDescriptor, "Unplugged")]
        [InlineData("MTV Unplugged Live", AlbumComponentType.VersionDescriptor, "Live")]
        [InlineData("Acoustic Sessions", AlbumComponentType.VersionDescriptor, "Acoustic")]
        [InlineData("Acoustic Sessions", AlbumComponentType.VersionDescriptor, "Sessions")]
        [InlineData("Radio Mix", AlbumComponentType.VersionDescriptor, "Radio")]
        [InlineData("Radio Mix", AlbumComponentType.VersionDescriptor, "Mix")]
        [InlineData("In Concert Live", AlbumComponentType.VersionDescriptor, "Live")]
        [InlineData("Demo Recordings", AlbumComponentType.VersionDescriptor, "Demo")]
        [InlineData("Extended Mixes", AlbumComponentType.VersionDescriptor, "Extended")]
        public void ClassifyComponents_VersionDescriptors_ShouldBePreserved(string albumTitle, AlbumComponentType expected, string targetWord)
        {
            // Act
            var components = _classifier.ClassifyComponents(albumTitle);

            // Assert
            components.Should().ContainKey(targetWord);
            components[targetWord].Should().Be(expected, $"'{targetWord}' should be classified as {expected}");
        }

        [Theory]
        [InlineData("1989 (Deluxe Edition)", "Deluxe")]
        [InlineData("Abbey Road (Remastered)", "Remastered")]
        [InlineData("The Wall - 25th Anniversary Edition", "Anniversary")]
        [InlineData("OK Computer (Collector's Edition)", "Collector's")]
        public void ClassifyComponents_EditionMarkersInContext_ShouldBeRemovable(string albumTitle, string targetWord)
        {
            // Act
            var components = _classifier.ClassifyComponents(albumTitle);

            // Assert
            components.Should().ContainKey(targetWord);
            components[targetWord].Should().Be(AlbumComponentType.EditionMarker, $"'{targetWord}' should be classified as EditionMarker when in context");
        }

        [Theory]
        [InlineData("Modus Vivendi", CleaningLevel.Aggressive)]
        [InlineData("Abbey Road", CleaningLevel.Aggressive)]
        [InlineData("The Dark Side of the Moon", CleaningLevel.Aggressive)]
        public void RecommendCleaningLevel_SimpleAlbums_ShouldBeAggressive(string albumTitle, CleaningLevel expected)
        {
            // Act
            var result = _classifier.RecommendCleaningLevel(albumTitle);

            // Assert
            result.Should().Be(expected, $"Simple album '{albumTitle}' should allow aggressive cleaning");
        }

        [Theory]
        [InlineData("Modus Vivendi Instrumental", CleaningLevel.Minimal)]
        [InlineData("MTV Unplugged in New York", CleaningLevel.Minimal)]
        [InlineData("Live at the Apollo", CleaningLevel.Minimal)]
        [InlineData("Acoustic Sessions", CleaningLevel.Minimal)]
        public void RecommendCleaningLevel_AlbumsWithVersionDescriptors_ShouldBeMinimal(string albumTitle, CleaningLevel expected)
        {
            // Act
            var result = _classifier.RecommendCleaningLevel(albumTitle);

            // Assert
            result.Should().Be(expected, $"Album with version descriptors '{albumTitle}' should use minimal cleaning");
        }

        [Theory]
        [InlineData("1989 (Deluxe Edition)", CleaningLevel.Moderate)]
        [InlineData("Abbey Road (Remastered)", CleaningLevel.Moderate)]
        [InlineData("The Wall - 25th Anniversary Edition", CleaningLevel.Moderate)]
        public void RecommendCleaningLevel_AlbumsWithEditionMarkers_ShouldBeModerate(string albumTitle, CleaningLevel expected)
        {
            // Act
            var result = _classifier.RecommendCleaningLevel(albumTitle);

            // Assert
            result.Should().Be(expected, $"Album with edition markers '{albumTitle}' should use moderate cleaning");
        }

        [Theory]
        [InlineData("Modus Vivendi Instrumental", "Instrumental")]
        [InlineData("MTV Unplugged Live", "Unplugged", "Live")]
        [InlineData("Acoustic Sessions", "Acoustic", "Sessions")]
        [InlineData("Symphony Orchestra Live", "Symphony", "Orchestra", "Live")]
        public void GetPreservedTerms_VersionDescriptors_ShouldBeIncluded(string albumTitle, params string[] expectedTerms)
        {
            // Act
            var preservedTerms = _classifier.GetPreservedTerms(albumTitle);

            // Assert
            foreach (var term in expectedTerms)
            {
                preservedTerms.Should().Contain(term, $"'{term}' should be preserved in '{albumTitle}'");
            }
        }

        [Theory]
        [InlineData("Deluxe", "1989 (Deluxe Edition)", true)]
        [InlineData("Remastered", "Abbey Road (Remastered)", true)]
        [InlineData("Anniversary", "The Wall - 25th Anniversary Edition", true)]
        [InlineData("Deluxe", "Deluxe Instrumental Mix", false)] // Deluxe not in edition context
        public void IsRemovableEditionMarker_ContextualAnalysis_ShouldWorkCorrectly(string term, string context, bool expectedRemovable)
        {
            // Act
            var result = _classifier.IsRemovableEditionMarker(term, context);

            // Assert
            result.Should().Be(expectedRemovable, $"'{term}' in '{context}' should {(expectedRemovable ? "" : "not ")}be removable");
        }

        [Fact]
        public void ClassifyComponents_EmptyAlbumTitle_ShouldReturnEmptyDictionary()
        {
            // Act
            var result = _classifier.ClassifyComponents("");

            // Assert
            result.Should().BeEmpty("Empty album title should return empty components");
        }

        [Fact]
        public void ClassifyComponents_NullAlbumTitle_ShouldReturnEmptyDictionary()
        {
            // Act
            var result = _classifier.ClassifyComponents(null);

            // Assert
            result.Should().BeEmpty("Null album title should return empty components");
        }

        [Theory]
        [InlineData("The Beatles", AlbumComponentType.Noise, "The")]
        [InlineData("A Day in the Life", AlbumComponentType.Noise, "A")]
        [InlineData("And Justice for All", AlbumComponentType.Noise, "And")]
        [InlineData("Of Monsters and Men", AlbumComponentType.Noise, "Of")]
        public void ClassifyComponents_NoiseWords_ShouldBeClassifiedAsNoise(string albumTitle, AlbumComponentType expected, string targetWord)
        {
            // Act
            var components = _classifier.ClassifyComponents(albumTitle);

            // Assert
            components.Should().ContainKey(targetWord);
            components[targetWord].Should().Be(expected, $"'{targetWord}' should be classified as noise");
        }

        [Theory]
        [InlineData("Album Title (2023)", "2023")]
        [InlineData("1989 Remaster", "1989")]
        [InlineData("The Wall 1979", "1979")]
        public void ClassifyComponents_Years_ShouldBeClassifiedAsMetadata(string albumTitle, string targetYear)
        {
            // Act
            var components = _classifier.ClassifyComponents(albumTitle);

            // Assert
            components.Should().ContainKey(targetYear);
            components[targetYear].Should().Be(AlbumComponentType.Metadata, $"Year '{targetYear}' should be classified as metadata");
        }

        [Theory]
        [InlineData("Album [Explicit]", "Explicit")]
        [InlineData("Song (Clean Version)", "Clean")]
        [InlineData("Music FLAC", "FLAC")]
        public void ClassifyComponents_MetadataMarkers_ShouldBeClassifiedCorrectly(string albumTitle, string targetMarker)
        {
            // Act
            var components = _classifier.ClassifyComponents(albumTitle);

            // Assert
            components.Should().ContainKey(targetMarker);
            components[targetMarker].Should().Be(AlbumComponentType.Metadata, $"'{targetMarker}' should be classified as metadata");
        }

        [Fact]
        public void GetPreservedTerms_ComplexAlbum_ShouldPreserveCoreAndVersion()
        {
            // Arrange
            var albumTitle = "Modus Vivendi (Instrumental Selections) - Deluxe Edition 2020";

            // Act
            var preservedTerms = _classifier.GetPreservedTerms(albumTitle);

            // Assert
            preservedTerms.Should().Contain("Instrumental", "Instrumental should be preserved as version descriptor");
            preservedTerms.Should().Contain("Modus", "Core title parts should be preserved");
            preservedTerms.Should().Contain("Vivendi", "Core title parts should be preserved");
            preservedTerms.Should().Contain("Selections", "Core title parts should be preserved");
            preservedTerms.Should().NotContain("Deluxe", "Edition marker should not be preserved");
            preservedTerms.Should().NotContain("2020", "Metadata should not be preserved");
        }

        [Theory]
        [InlineData("070 Shake - Modus Vivendi Instrumental")]
        [InlineData("Artist - Album Live")]
        [InlineData("Band - Songs Acoustic")]
        [InlineData("Name - Title Unplugged")]
        public void ClassifyComponents_ArtistAlbumWithVersionDescriptor_ShouldPreserveVersionDescriptor(string fullTitle)
        {
            // Extract just the album part (after the dash)
            var albumPart = fullTitle.Split('-').Last().Trim();

            // Act
            var components = _classifier.ClassifyComponents(albumPart);
            var preservedTerms = _classifier.GetPreservedTerms(albumPart);

            // Assert
            var versionDescriptors = components.Where(c => c.Value == AlbumComponentType.VersionDescriptor).Select(c => c.Key);
            versionDescriptors.Should().NotBeEmpty($"'{albumPart}' should have version descriptors");
            
            foreach (var descriptor in versionDescriptors)
            {
                preservedTerms.Should().Contain(descriptor, $"Version descriptor '{descriptor}' should be preserved");
            }
        }

        [Fact] 
        public void RecommendCleaningLevel_SpecificBugCase_ShouldUseMinimalCleaning()
        {
            // Arrange - This is the exact case from the bug report
            var albumTitle = "Modus Vivendi Instrumental";

            // Act
            var cleaningLevel = _classifier.RecommendCleaningLevel(albumTitle);
            var preservedTerms = _classifier.GetPreservedTerms(albumTitle);

            // Assert
            cleaningLevel.Should().Be(CleaningLevel.Minimal, "Albums with 'Instrumental' should use minimal cleaning");
            preservedTerms.Should().Contain("Instrumental", "'Instrumental' should always be preserved");
            preservedTerms.Should().Contain("Modus", "Core title should be preserved");
            preservedTerms.Should().Contain("Vivendi", "Core title should be preserved");
        }
    }
}