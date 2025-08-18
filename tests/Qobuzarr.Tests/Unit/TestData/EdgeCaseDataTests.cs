using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.TestData;
using Qobuzarr.Tests.Builders;

namespace Qobuzarr.Tests.Unit.TestData
{
    /// <summary>
    /// Tests for the centralized edge case data collection.
    /// Validates that edge case data is comprehensive and properly structured.
    /// </summary>
    public class EdgeCaseDataTests
    {
        /// <summary>
        /// Verifies that search query edge cases include comprehensive boundary conditions
        /// </summary>
        [Fact]
        public void SearchQueryEdgeCases_ShouldIncludeComprehensiveBoundaryConditions()
        {
            // Act
            var edgeCases = EdgeCaseData.SearchQueryEdgeCases.ToList();

            // Assert
            edgeCases.Should().NotBeEmpty("should provide search query edge cases");
            edgeCases.Should().HaveCountGreaterThan(50, "should have extensive edge case coverage");
            
            // Verify specific categories are covered
            var queryStrings = edgeCases.Select(x => x[0].ToString()).ToList();
            var descriptions = edgeCases.Select(x => x[1].ToString()).ToList();

            // Empty/whitespace cases
            queryStrings.Should().Contain("", "should test empty queries");
            queryStrings.Should().Contain("   ", "should test whitespace queries");
            
            // Unicode cases
            descriptions.Should().Contain(d => d.Contains("Characters") || d.Contains("Script"), 
                "should test international characters");
            
            // Length cases  
            descriptions.Should().Contain(d => d.Contains("Long"), "should test long queries");
            descriptions.Should().Contain(d => d.Contains("Single"), "should test single character queries");
            
            // Special character cases
            descriptions.Should().Contain(d => d.Contains("Punctuation") || d.Contains("Special"), 
                "should test special characters");
        }

        /// <summary>
        /// Verifies that artist name edge cases cover various formatting challenges
        /// </summary>
        [Fact]
        public void ArtistNameEdgeCases_ShouldCoverFormattingChallenges()
        {
            // Act
            var edgeCases = EdgeCaseData.ArtistNameEdgeCases.ToList();

            // Assert
            edgeCases.Should().NotBeEmpty("should provide artist name edge cases");
            edgeCases.Should().HaveCountGreaterThan(10, "should cover various artist name formats");
            
            var descriptions = edgeCases.Select(x => x[1].ToString()).ToList();
            
            // Should cover collaboration patterns
            descriptions.Should().Contain(d => d.Contains("Featuring") || d.Contains("Collaboration"), 
                "should test artist collaborations");
                
            // Should cover name formatting
            descriptions.Should().Contain(d => d.Contains("Name") || d.Contains("Format"), 
                "should test name formatting variations");
        }

        /// <summary>
        /// Verifies that metadata edge cases include diverse track scenarios
        /// </summary>
        [Fact]
        public void MetadataEdgeCases_ShouldIncludeDiverseTrackScenarios()
        {
            // Act
            var edgeCases = EdgeCaseData.MetadataEdgeCases.ToList();

            // Assert
            edgeCases.Should().NotBeEmpty("should provide metadata edge cases");
            edgeCases.Should().HaveCountGreaterThan(5, "should cover various metadata scenarios");
            
            // Should include tracks with minimal metadata
            edgeCases.Should().Contain(track => string.IsNullOrEmpty(track.Version) && 
                track.TrackNumber <= 1, "should include minimal metadata tracks");
                
            // Should include tracks with complex metadata
            edgeCases.Should().Contain(track => !string.IsNullOrEmpty(track.Title) && 
                track.Title.Length > 20, "should include complex metadata tracks");
                
            // Should include different quality levels
            edgeCases.Should().Contain(track => track.MaximumBitDepth > 16, 
                "should include high-quality tracks");
                
            // Should include restricted tracks
            edgeCases.Should().Contain(track => !track.Downloadable || !track.Streamable, 
                "should include restricted tracks");
        }

        /// <summary>
        /// Verifies that quality edge cases cover various audio specifications
        /// </summary>
        [Fact]
        public void AudioQualityEdgeCases_ShouldCoverAudioSpecifications()
        {
            // Act
            var edgeCases = EdgeCaseData.AudioQualityEdgeCases.ToList();

            // Assert
            edgeCases.Should().NotBeEmpty("should provide audio quality edge cases");
            
            var bitDepths = edgeCases.Select(x => (int)x[0]).ToList();
            var sampleRates = edgeCases.Select(x => (double)x[1]).ToList();
            
            // Should include standard CD quality
            bitDepths.Should().Contain(16, "should include CD quality bit depth");
            sampleRates.Should().Contain(44100.0, "should include CD quality sample rate");
            
            // Should include high resolution
            bitDepths.Should().Contain(24, "should include high-res bit depth");
            sampleRates.Should().Contain(r => r > 48000, "should include high-res sample rates");
            
            // Should include edge cases
            bitDepths.Should().Contain(0, "should include invalid bit depth");
            sampleRates.Should().Contain(0.0, "should include invalid sample rate");
        }

        /// <summary>
        /// Verifies that file path edge cases cover problematic characters
        /// </summary>
        [Fact]
        public void FilePathEdgeCases_ShouldCoverProblematicCharacters()
        {
            // Act
            var edgeCases = EdgeCaseData.FilePathEdgeCases.ToList();

            // Assert
            edgeCases.Should().NotBeEmpty("should provide file path edge cases");
            
            var filePaths = edgeCases.Select(x => x[0].ToString()).ToList();
            
            // Should include paths with spaces
            filePaths.Should().Contain(p => p.Contains(" "), "should test paths with spaces");
            
            // Should include various extensions
            filePaths.Should().Contain(p => p.EndsWith(".flac"), "should test FLAC files");
            filePaths.Should().Contain(p => p.EndsWith(".mp3"), "should test MP3 files");
            
            // Should include Unicode characters
            filePaths.Should().Contain(p => p.Any(char.IsLetter) && 
                p.Any(c => c > 127), "should test Unicode filenames");
        }

        /// <summary>
        /// Tests the random sampling functionality
        /// </summary>
        [Theory]
        [InlineData("SearchQueries", 10)]
        [InlineData("ArtistNames", 5)]
        [InlineData("TrackTitles", 15)]
        public void GetRandomSample_ShouldReturnRequestedSampleSize(string category, int sampleSize)
        {
            // Act
            var sample = EdgeCaseData.GetRandomSample(category, sampleSize).ToList();

            // Assert
            sample.Should().HaveCount(Math.Min(sampleSize, EdgeCaseData.AllEdgeCases[category].Count()), 
                $"should return requested sample size for {category}");
        }

        /// <summary>
        /// Tests the stress test case selection
        /// </summary>
        [Fact]
        public void GetStressTestCases_ShouldReturnComplexQueries()
        {
            // Act
            var stressCases = EdgeCaseData.GetStressTestCases().ToList();

            // Assert
            stressCases.Should().NotBeEmpty("should provide stress test cases");
            stressCases.Should().HaveCountLessOrEqualTo(20, "should limit stress test cases to manageable number");
            
            // Verify these are indeed complex cases
            var descriptions = stressCases.Select(x => x[1].ToString()).ToList();
            descriptions.Should().Contain(d => d.Contains("Long") || d.Contains("Unicode") || d.Contains("Complex"), 
                "should select complex cases for stress testing");
        }

        /// <summary>
        /// Verifies that all edge case categories are accessible
        /// </summary>
        [Fact]
        public void AllEdgeCases_ShouldProvideAccessToAllCategories()
        {
            // Act
            var allCategories = EdgeCaseData.AllEdgeCases;

            // Assert
            allCategories.Should().NotBeEmpty("should provide access to edge case categories");
            
            // Verify expected categories exist
            var expectedCategories = new[] 
            { 
                "SearchQueries", "AlbumTitles", "ArtistNames", "TrackTitles", 
                "AudioQualities", "DateTimes", "FilePaths", "NetworkConditions", "BoundaryValues" 
            };
            
            foreach (var category in expectedCategories)
            {
                allCategories.Should().ContainKey(category, $"should include {category} category");
                allCategories[category].Should().NotBeEmpty($"{category} should not be empty");
            }
        }

        /// <summary>
        /// Verifies that boundary values include important numerical limits
        /// </summary>
        [Fact]
        public void BoundaryValues_ShouldIncludeNumericalLimits()
        {
            // Act
            var boundaryValues = EdgeCaseData.BoundaryValues.ToList();

            // Assert
            boundaryValues.Should().NotBeEmpty("should provide boundary values");
            
            var values = boundaryValues.Select(x => (int)x[0]).ToList();
            
            // Should include zero and extremes
            values.Should().Contain(0, "should include zero");
            values.Should().Contain(1, "should include one");
            values.Should().Contain(-1, "should include negative one");
            
            // Should include common audio-related boundaries
            values.Should().Contain(44100, "should include CD sample rate");
            values.Should().Contain(48000, "should include studio sample rate");
        }

        /// <summary>
        /// Tests edge case data with the builder pattern
        /// </summary>
        [Fact]
        public void EdgeCaseData_ShouldWorkWithBuilders()
        {
            // Arrange & Act - Use edge case data with builders
            var albumEdgeCases = EdgeCaseData.AlbumTitleEdgeCases.Take(5);
            var albums = new List<QobuzAlbum>();

            foreach (var edgeCase in albumEdgeCases)
            {
                var title = edgeCase[0].ToString();
                var description = edgeCase[1].ToString();
                
                if (!string.IsNullOrEmpty(title))
                {
                    var album = QobuzAlbumBuilder.New()
                        .WithTitle(title)
                        .WithArtist("Test Artist")
                        .Build();
                    
                    albums.Add(album);
                }
            }

            // Assert
            albums.Should().NotBeEmpty("should create albums using edge case data");
            albums.Should().AllSatisfy(album => 
            {
                album.Title.Should().NotBeNullOrEmpty("album should have title from edge case data");
                album.Artist.Should().NotBeNull("album should have artist");
            });
        }
    }
}