using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.TestData;
using Qobuzarr.Tests.Builders;

namespace Qobuzarr.Tests.Unit.TestData
{
    /// <summary>
    /// 🐒💥 CHAOS MONKEY TESTS 💥🐒
    /// 
    /// WARNING: These tests are designed to break things!
    /// They test extreme edge cases, resource exhaustion, and malicious input.
    /// Some tests may be slow, consume significant memory, or trigger exceptions.
    /// 
    /// Use with caution in CI/CD pipelines!
    /// </summary>
    public class ChaosMonkeyTests
    {
        /// <summary>
        /// Verifies that chaos monkey search queries include extreme scenarios
        /// </summary>
        [Fact]
        public void ChaosMonkeySearchQueries_ShouldIncludeExtremeScenarios()
        {
            // Act
            var chaosQueries = EdgeCaseData.ChaosMonkeySearchQueries.ToList();

            // Assert
            chaosQueries.Should().NotBeEmpty("should provide chaos monkey search queries");
            chaosQueries.Should().HaveCountGreaterThan(50, "should have extensive chaos coverage");
            
            var queryStrings = chaosQueries.Select(x => x[0].ToString()).ToList();
            var descriptions = chaosQueries.Select(x => x[1].ToString()).ToList();

            // Memory exhaustion cases
            descriptions.Should().Contain(d => d.Contains("Memory"), "should test memory limits");
            descriptions.Should().Contain(d => d.Contains("Bomb"), "should include bomb scenarios");
            
            // Security attack patterns
            descriptions.Should().Contain(d => d.Contains("Injection"), "should test injection attacks");
            descriptions.Should().Contain(d => d.Contains("HTML") || d.Contains("XSS"), "should test XSS attacks");
            descriptions.Should().Contain(d => d.Contains("SQL"), "should test SQL injection");
            
            // Unicode chaos
            descriptions.Should().Contain(d => d.Contains("Unicode"), "should test Unicode edge cases");
            descriptions.Should().Contain(d => d.Contains("Character"), "should test character chaos");
            
            // Performance degradation
            descriptions.Should().Contain(d => d.Contains("Regex"), "should test regex performance");
            descriptions.Should().Contain(d => d.Contains("Overflow"), "should test overflow scenarios");
        }

        /// <summary>
        /// Verifies that chaos monkey metadata includes extreme track scenarios
        /// </summary>
        [Fact]
        public void ChaosMonkeyMetadata_ShouldIncludeExtremeTrackScenarios()
        {
            // Act
            var chaosMetadata = EdgeCaseData.ChaosMonkeyMetadata.ToList();

            // Assert
            chaosMetadata.Should().NotBeEmpty("should provide chaos monkey metadata");
            chaosMetadata.Should().HaveCountGreaterThan(8, "should have multiple chaos scenarios");
            
            // Should include memory bomb track
            chaosMetadata.Should().Contain(track => track.Id == "memory_bomb", 
                "should include memory exhaustion track");
                
            // Should include tracks with extreme values
            chaosMetadata.Should().Contain(track => track.DurationSeconds < 0, 
                "should include negative duration tracks");
                
            chaosMetadata.Should().Contain(track => track.TrackNumber <= 0, 
                "should include invalid track numbers");
                
            // Should include injection attempts
            chaosMetadata.Should().Contain(track => track.Title.Contains("<script>"), 
                "should include script injection tracks");
        }

        /// <summary>
        /// Verifies that chaos monkey file paths include problematic scenarios
        /// </summary>
        [Fact]
        public void ChaosMonkeyFilePaths_ShouldIncludeProblematicScenarios()
        {
            // Act
            var chaosFilePaths = EdgeCaseData.ChaosMonkeyFilePaths.ToList();

            // Assert
            chaosFilePaths.Should().NotBeEmpty("should provide chaos monkey file paths");
            chaosFilePaths.Should().HaveCountGreaterThan(20, "should have extensive file path chaos");
            
            var filePaths = chaosFilePaths.Select(x => x[0].ToString()).ToList();
            var descriptions = chaosFilePaths.Select(x => x[1].ToString()).ToList();

            // Windows reserved names
            descriptions.Should().Contain(d => d.Contains("Reserved"), "should test reserved names");
            filePaths.Should().Contain(p => p.StartsWith("CON."), "should include CON reserved name");
            
            // Path traversal attempts
            filePaths.Should().Contain(p => p.Contains("../"), "should test path traversal");
            filePaths.Should().Contain(p => p.Contains("C:\\"), "should test absolute path injection");
            
            // Unicode file names
            descriptions.Should().Contain(d => d.Contains("Unicode") || d.Contains("Emoji"), 
                "should test Unicode file names");
                
            // Control characters
            descriptions.Should().Contain(d => d.Contains("Control") || d.Contains("Null"), 
                "should test control characters in file names");
        }

        /// <summary>
        /// Tests getting different categories of chaos monkey cases
        /// </summary>
        [Fact]
        public void GetChaosMonkeyCases_ShouldReturnAppropriateSubsets()
        {
            // Act
            var generalChaos = EdgeCaseData.GetChaosMonkeyCases(5).ToList();
            var dangerousChaos = EdgeCaseData.GetDangerousChaosMonkeyCases().ToList();
            var securityChaos = EdgeCaseData.GetSecurityChaosMonkeyCases().ToList();
            var unicodeChaos = EdgeCaseData.GetUnicodeChaosMonkeyCases().ToList();
            var performanceChaos = EdgeCaseData.GetPerformanceChaosMonkeyCases().ToList();

            // Assert
            generalChaos.Should().HaveCount(5, "should return requested number of chaos cases");
            
            dangerousChaos.Should().NotBeEmpty("should return dangerous cases");
            dangerousChaos.Should().HaveCountLessOrEqualTo(5, "should limit dangerous cases for safety");
            dangerousChaos.Should().AllSatisfy(chaos => 
            {
                var description = chaos[1].ToString();
                (description.Contains("Memory") || description.Contains("Bomb") || 
                 description.Contains("Overflow") || description.Contains("Exhaustion") ||
                 description.Contains("Massive")).Should().BeTrue("dangerous cases should have appropriate markers");
            });
            
            securityChaos.Should().NotBeEmpty("should return security-focused cases");
            securityChaos.Should().AllSatisfy(chaos => 
            {
                var description = chaos[1].ToString();
                (description.Contains("Injection") || description.Contains("XSS") || description.Contains("HTML") ||
                 description.Contains("SQL") || description.Contains("Attack") ||
                 description.Contains("Traversal")).Should().BeTrue("security cases should have attack markers");
            });
            
            unicodeChaos.Should().NotBeEmpty("should return Unicode chaos cases");
            performanceChaos.Should().NotBeEmpty("should return performance chaos cases");
        }

        /// <summary>
        /// Tests creating chaos monkey tracks with different chaos types
        /// </summary>
        [Theory]
        [InlineData("MemoryBomb")]
        [InlineData("UnicodeChaos")]
        [InlineData("ControlChaos")]
        [InlineData("QualityChaos")]
        [InlineData("NegativeTrack")]
        [InlineData("ZeroTrack")]
        [InlineData("FormatInjection")]
        [InlineData("PathInjection")]
        [InlineData("ScriptInjection")]
        [InlineData("MathChaos")]
        public void CreateChaosMonkeyTrack_ShouldReturnAppropriateTrack(string chaosType)
        {
            // Act
            var chaosTrack = EdgeCaseData.CreateChaosMonkeyTrack(chaosType);

            // Assert
            chaosTrack.Should().NotBeNull($"should create {chaosType} track");
            chaosTrack.Id.Should().NotBeNullOrEmpty("chaos track should have ID");
            
            // Verify track matches expected chaos type
            switch (chaosType)
            {
                case "MemoryBomb":
                    chaosTrack.Id.Should().Be("memory_bomb");
                    chaosTrack.Title.Should().HaveLength(100000, "memory bomb should have huge title");
                    break;
                    
                case "UnicodeChaos":
                    chaosTrack.Id.Should().Be("unicode_chaos");
                    chaosTrack.Title.Should().Contain("🎵", "unicode chaos should contain emojis");
                    break;
                    
                case "NegativeTrack":
                    chaosTrack.Id.Should().Be("negative_track");
                    chaosTrack.TrackNumber.Should().BeNegative("negative track should have negative track number");
                    break;
                    
                case "ZeroTrack":
                    chaosTrack.Id.Should().Be("zero_track");
                    chaosTrack.TrackNumber.Should().Be(0, "zero track should have zero track number");
                    chaosTrack.DurationSeconds.Should().Be(0, "zero track should have zero duration");
                    break;
                    
                case "ScriptInjection":
                    chaosTrack.Id.Should().Be("script_injection");
                    chaosTrack.Title.Should().Contain("<script>", "script injection should contain script tags");
                    break;
            }
        }

        /// <summary>
        /// Tests concurrent chaos test data generation
        /// </summary>
        [Fact]
        public void GenerateConcurrentChaosTestData_ShouldProvideDiverseData()
        {
            // Act
            var concurrentData = EdgeCaseData.GenerateConcurrentChaosTestData(20).ToList();

            // Assert
            concurrentData.Should().HaveCount(20, "should generate requested amount of test data");
            concurrentData.Should().AllSatisfy(data => 
            {
                data.Should().HaveCount(3, "each data item should have query, description, and index");
                data[0].Should().NotBeNull("query should not be null");
                data[1].Should().NotBeNull("description should not be null");
                data[2].Should().BeOfType<int>("index should be integer");
            });
            
            // Should have diverse queries (not all the same)
            var uniqueQueries = concurrentData.Select(d => d[0].ToString()).Distinct().Count();
            uniqueQueries.Should().BeGreaterThan(1, "should generate diverse chaos queries for concurrent testing");
        }

        /// <summary>
        /// Verifies that all chaos monkey edge case categories are accessible
        /// </summary>
        [Fact]
        public void ChaosMonkeyEdgeCases_ShouldProvideAccessToAllCategories()
        {
            // Act
            var chaosCategories = EdgeCaseData.ChaosMonkeyEdgeCases;

            // Assert
            chaosCategories.Should().NotBeEmpty("should provide access to chaos monkey categories");
            
            var expectedCategories = new[]
            {
                "ChaosSearchQueries", "ChaosFilePaths", "ChaosNetworkConditions", 
                "ChaosDateTimes", "ChaosAudioQualities"
            };
            
            foreach (var category in expectedCategories)
            {
                chaosCategories.Should().ContainKey(category, $"should include {category} category");
                chaosCategories[category].Should().NotBeEmpty($"{category} should not be empty");
            }
        }

        /// <summary>
        /// Verifies that combined edge cases include both normal and chaos scenarios
        /// </summary>
        [Fact]
        public void AllEdgeCasesIncludingChaos_ShouldCombineBothTypes()
        {
            // Act
            var combinedCases = EdgeCaseData.AllEdgeCasesIncludingChaos;

            // Assert
            combinedCases.Should().NotBeEmpty("should provide combined edge cases");
            
            // Should include normal edge cases
            combinedCases.Should().ContainKey("SearchQueries", "should include normal search queries");
            combinedCases.Should().ContainKey("AlbumTitles", "should include normal album titles");
            
            // Should include chaos monkey cases
            combinedCases.Should().ContainKey("ChaosSearchQueries", "should include chaos search queries");
            combinedCases.Should().ContainKey("ChaosFilePaths", "should include chaos file paths");
            
            // Should have more categories than normal edge cases alone
            var normalCount = EdgeCaseData.AllEdgeCases.Count;
            var combinedCount = combinedCases.Count;
            combinedCount.Should().BeGreaterThan(normalCount, "combined should have more categories than normal alone");
        }

        /// <summary>
        /// Performance test with chaos monkey data (be careful with this one! 🐒💥)
        /// </summary>
        [Fact]
        public void ChaosMonkeyData_ShouldNotCauseImmediateFailures()
        {
            // This test just verifies we can access the chaos data without immediate crashes
            // It doesn't actually run the chaos scenarios (that would be dangerous!)
            
            // Act & Assert - Just accessing the data should work
            var startTime = DateTime.UtcNow;
            
            Action accessChaosData = () =>
            {
                var chaosQueries = EdgeCaseData.ChaosMonkeySearchQueries.Take(5).ToList();
                var chaosMetadata = EdgeCaseData.ChaosMonkeyMetadata.Take(3).ToList();
                var chaosFilePaths = EdgeCaseData.ChaosMonkeyFilePaths.Take(5).ToList();
                
                chaosQueries.Should().NotBeEmpty();
                chaosMetadata.Should().NotBeEmpty();
                chaosFilePaths.Should().NotBeEmpty();
            };
            
            // Should not throw exceptions just from accessing the data
            accessChaosData.Should().NotThrow("accessing chaos monkey data should not cause immediate failures");
            
            var duration = DateTime.UtcNow - startTime;
            duration.Should().BeLessThan(TimeSpan.FromSeconds(5), "data access should be reasonably fast");
        }

        /// <summary>
        /// Verifies that extreme audio quality chaos cases include mathematical edge cases
        /// </summary>
        [Fact]
        public void ChaosMonkeyAudioQualities_ShouldIncludeMathematicalEdgeCases()
        {
            // Act
            var chaosQualities = EdgeCaseData.ChaosMonkeyAudioQualities.ToList();

            // Assert
            chaosQualities.Should().NotBeEmpty("should provide chaos audio qualities");
            
            var descriptions = chaosQualities.Select(x => x[2].ToString()).ToList();
            
            // Should include infinity cases
            descriptions.Should().Contain(d => d.Contains("Infinite"), "should test infinite values");
            descriptions.Should().Contain(d => d.Contains("NaN"), "should test NaN values");
            
            // Should include extreme values
            descriptions.Should().Contain(d => d.Contains("Max"), "should test maximum values");
            descriptions.Should().Contain(d => d.Contains("Min"), "should test minimum values");
            
            // Should include zero and negative cases
            descriptions.Should().Contain(d => d.Contains("Zero"), "should test zero values");
            descriptions.Should().Contain(d => d.Contains("Negative"), "should test negative values");
        }

        /// <summary>
        /// Test that demonstrates using chaos monkey data with builders (safely!)
        /// </summary>
        [Fact]
        public void ChaosMonkeyData_ShouldWorkWithBuilders()
        {
            // Act - Use a small, safe subset of chaos data
            var safeChaosData = EdgeCaseData.ChaosMonkeySearchQueries
                .Where(x => !x[1].ToString().Contains("Memory") && !x[1].ToString().Contains("Bomb"))
                .Take(3);
                
            var chaosAlbums = new List<QobuzAlbum>();
            
            foreach (var chaosCase in safeChaosData)
            {
                var title = chaosCase[0].ToString();
                if (title.Length < 1000) // Safety check to avoid memory issues
                {
                    var album = QobuzAlbumBuilder.New()
                        .WithTitle(title.Length > 100 ? title.Substring(0, 100) : title) // Truncate long titles
                        .WithArtist("Chaos Monkey Artist")
                        .Build();
                    
                    chaosAlbums.Add(album);
                }
            }

            // Assert
            chaosAlbums.Should().NotBeEmpty("should create albums using safe chaos data");
            chaosAlbums.Should().AllSatisfy(album => 
            {
                album.Title.Should().NotBeNullOrEmpty("album should have title from chaos data");
                album.Artist.Should().NotBeNull("album should have artist");
            });
        }
    }
}