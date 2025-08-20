using System;
using System.Linq;
using Xunit;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using NLog;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Simple unit tests for UnicodeQueryBuilder to validate core functionality
    /// </summary>
    public class UnicodeQueryBuilderSimpleTests
    {
        private readonly UnicodeQueryBuilder _queryBuilder;

        public UnicodeQueryBuilderSimpleTests()
        {
            var logger = LogManager.GetCurrentClassLogger();
            _queryBuilder = new UnicodeQueryBuilder(logger);
        }

        [Fact]
        public void Constructor_InitializesSuccessfully()
        {
            var builder = new UnicodeQueryBuilder(LogManager.GetCurrentClassLogger());
            builder.Should().NotBeNull();
        }

        [Theory]
        [InlineData("Björk", "Homogenic", true)]
        [InlineData("Sigur Rós", "Ágætis byrjun", true)]
        [InlineData("The Beatles", "Abbey Road", false)]
        [InlineData("μ-Ziq", "Lunatic Harness", true)]
        public void RequiresUnicodeHandling_VariousCharacterSets_ReturnsExpected(
            string artist, string album, bool expectedRequiresHandling)
        {
            var query = $"{artist} {album}";
            var result = _queryBuilder.RequiresUnicodeHandling(query);
            result.Should().Be(expectedRequiresHandling);
        }

        [Fact]
        public void GenerateQueryVariants_BjorkHomogenic_GeneratesExpectedVariants()
        {
            var variants = _queryBuilder.GenerateQueryVariants("Björk", "Homogenic");
            
            variants.Should().NotBeEmpty();
            variants.Should().Contain("Björk Homogenic");
            variants.Should().Contain("Bjork Homogenic");
            variants.Should().Contain("Bjork");
            variants.Should().Contain("Homogenic");
        }

        [Fact]
        public void GenerateQueryVariants_CyrillicArtist_GeneratesTransliteration()
        {
            var variants = _queryBuilder.GenerateQueryVariants("Мумий Тролль", "Икра");
            
            variants.Should().NotBeEmpty();
            variants.Should().Contain("Мумий Тролль Икра");
            variants.Should().Contain("Mumiy Troll Ikra");
        }

        [Fact]
        public void GenerateQueryVariants_GreekCharacters_GeneratesTransliteration()
        {
            var variants = _queryBuilder.GenerateQueryVariants("μ-Ziq", "Lunatic Harness");
            
            variants.Should().NotBeEmpty();
            variants.Should().Contain("μ-Ziq Lunatic Harness");
            variants.Should().Contain("m-Ziq Lunatic Harness");
        }

        [Fact]
        public void GenerateQueryVariants_ASCIIOnly_GeneratesLimitedVariants()
        {
            var variants = _queryBuilder.GenerateQueryVariants("The Beatles", "Abbey Road");
            
            variants.Should().NotBeEmpty();
            variants.Should().Contain("The Beatles Abbey Road");
            variants.Should().HaveCountLessOrEqualTo(3);
        }

        [Fact]
        public void RecordVariantResult_UpdatesStatistics()
        {
            _queryBuilder.RecordVariantResult("Björk Homogenic", "Bjork Homogenic", true, 5);
            
            var stats = _queryBuilder.GetPerformanceStatistics();
            stats.TotalQueries.Should().Be(1);
            stats.UnicodeQueries.Should().Be(1);
        }

        [Fact]
        public void GetPerformanceStatistics_InitialState_ReturnsZeroedStats()
        {
            var stats = _queryBuilder.GetPerformanceStatistics();
            
            stats.Should().NotBeNull();
            stats.TotalQueries.Should().Be(0);
            stats.UnicodeQueries.Should().Be(0);
            stats.OverallSuccessRate.Should().Be(0.0);
        }

        [Theory]
        [InlineData("", "")]
        [InlineData(null, null)]
        [InlineData("Artist", "")]
        [InlineData("", "Album")]
        public void GenerateQueryVariants_EmptyInput_ReturnsEmptyList(string artist, string album)
        {
            var variants = _queryBuilder.GenerateQueryVariants(artist, album);
            variants.Should().BeEmpty();
        }
    }
}