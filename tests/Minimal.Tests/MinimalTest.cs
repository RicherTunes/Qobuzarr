using System;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;

namespace Minimal.Tests
{
    public class MinimalTest
    {
        [Fact]
        public void BasicTest_ShouldPass()
        {
            // Simple test to verify the test framework works
            var result = 2 + 2;
            result.Should().Be(4);
        }

        [Fact]
        public void JsonSerialization_ShouldWork()
        {
            // Test that Newtonsoft.Json works
            var obj = new { Name = "Test", Value = 42 };
            var json = JsonConvert.SerializeObject(obj);
            var deserialized = JsonConvert.DeserializeObject<dynamic>(json);

            ((string)deserialized.Name).Should().Be("Test");
            ((int)deserialized.Value).Should().Be(42);
        }

        [Fact]
        public void FluentAssertions_ShouldWork()
        {
            // Test that FluentAssertions works
            var text = "Hello World";
            text.Should().StartWith("Hello");
            text.Should().EndWith("World");
            text.Should().Contain("o W");
        }

        [Theory]
        [InlineData(1, 2, 3)]
        [InlineData(10, 20, 30)]
        [InlineData(-5, 10, 5)]
        public void ParameterizedTest_ShouldWork(int a, int b, int expected)
        {
            var result = a + b;
            result.Should().Be(expected);
        }

        [Fact]
        public void DateTime_ShouldWork()
        {
            var now = DateTime.UtcNow;
            var future = now.AddHours(1);
            var past = now.AddHours(-1);

            future.Should().BeAfter(now);
            past.Should().BeBefore(now);
        }
    }
}
