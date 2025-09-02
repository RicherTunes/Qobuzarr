using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Xunit;

namespace Qobuzarr.Tests.Unit.Utilities
{
    public class StringSimilarityTests
    {
        [Theory]
        [InlineData("Björk", "Bjork")]
        [InlineData("Mylène Farmer", "Mylene Farmer")]
        [InlineData("Rammstein", "RAMMSTEIN")]
        public void Diacritics_and_case_should_be_high_similarity(string a, string b)
        {
            var sim = StringSimilarity.Calculate(a, b);
            sim.Should().BeGreaterOrEqualTo(0.85, $"'{a}' vs '{b}' should be highly similar");
        }

        [Theory]
        [InlineData("Song - Live!", "Song Live")]
        [InlineData("Hello, World", "Hello World")]
        [InlineData("Rock & Roll", "Rock and Roll")]
        public void Punctuation_and_joiners_should_not_reduce_similarity_much(string a, string b)
        {
            var sim = StringSimilarity.Calculate(a, b);
            sim.Should().BeGreaterOrEqualTo(0.9, $"'{a}' vs '{b}' should be nearly identical after normalization");
        }

        [Theory]
        [InlineData("BLACKPINK", "Black Pink")]
        [InlineData("X-Japan", "X JAPAN")]
        public void Common_spacing_and_hyphenation_variants_should_be_high_similarity(string a, string b)
        {
            var sim = StringSimilarity.Calculate(a, b);
            sim.Should().BeGreaterOrEqualTo(0.85);
        }
    }
}

